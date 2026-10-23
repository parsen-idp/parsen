using Amazon;
using Amazon.BedrockRuntime;
using Parsen.Application.Pipeline;
using Parsen.Eval;
using Parsen.Eval.Synthetic;
using Parsen.Infrastructure.Extraction;
using Parsen.Infrastructure.Ocr;
using Parsen.Schemas;

// Uso:
//   dotnet run --project tests/Parsen.Eval -- generate [--kind holerite|comprovante_residencia|extrato_bancario|matricula_imovel|classificacao] [--count 10] [--seed 42] [--out <pasta>]
//   dotnet run --project tests/Parsen.Eval -- run [--dataset <pasta>]... [--only <prefixo>] [--repeat N] [--classificacao vN] [--model <id>] [--report <arquivo.md>] [--yes]
//
// "run" sem --yes só lista os casos e o custo estimado: chamar o modelo tem custo real (CLAUDE.md).
// --dataset aponta para documentos próprios, fora do repositório; o relatório deles não mostra valores.

const string DefaultFixtures = "tests/Parsen.Eval/fixtures";
const string DefaultModel = "global.anthropic.claude-haiku-4-5-20251001-v1:0";

var options = Options.Parse(args);
return options.Command switch
{
    "generate" => Generate(options),
    "run" => await RunAsync(options),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("Comandos: generate | run. Veja o comentário no início de tests/Parsen.Eval/Program.cs.");
    return 1;
}

static int Generate(Options options)
{
    var count = int.Parse(options.Get("count") ?? "10", System.Globalization.CultureInfo.InvariantCulture);
    var seed = int.Parse(options.Get("seed") ?? "42", System.Globalization.CultureInfo.InvariantCulture);
    var kind = options.Get("kind") ?? "holerite";
    var output = options.Get("out") ?? Path.Combine(DefaultFixtures, kind);
    Directory.CreateDirectory(output);

    Func<(OcrDocument, Gabarito)> generate = kind switch
    {
        "holerite" => new HoleriteGenerator(seed).Generate,
        "comprovante_residencia" => new ComprovanteGenerator(seed).Generate,
        "extrato_bancario" => new ExtratoGenerator(seed).Generate,
        "matricula_imovel" => new MatriculaGenerator(seed).Generate,
        "classificacao" => new BundleGenerator(seed).Generate,
        _ => throw new ArgumentException($"Tipo de fixture desconhecido: {kind} (use holerite, comprovante_residencia, extrato_bancario, matricula_imovel ou classificacao)."),
    };

    for (var i = 1; i <= count; i++)
    {
        var (ocr, gabarito) = generate();
        var name = $"{kind}-sintetico-{i:000}";
        File.WriteAllBytes(Path.Combine(output, $"{name}.ocr.json"), OcrJson.Serialize(ocr));
        File.WriteAllText(Path.Combine(output, $"{name}.gabarito.json"), gabarito.ToJson());
    }

    Console.WriteLine($"{count} casos sintéticos ({kind}) gerados em {output} (semente {seed}).");
    return 0;
}

static async Task<int> RunAsync(Options options)
{
    var model = options.Get("model") ?? DefaultModel;
    var price = ModelPrice.For(model);

    var privateDatasets = options.GetAll("dataset");
    var datasets = new List<(string Path, bool Synthetic)> { (DefaultFixtures, true) };
    datasets.AddRange(privateDatasets.Select(d => (d, false)));

    var cases = datasets
        .Where(d => Directory.Exists(d.Path))
        .SelectMany(d => Directory.EnumerateFiles(d.Path, "*.ocr.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(ocr => (Ocr: ocr, Gabarito: ocr.Replace(".ocr.json", ".gabarito.json", StringComparison.Ordinal), d.Synthetic)))
        .Where(c => File.Exists(c.Gabarito))
        .ToList();

    // --only <prefixo>: avalia só um tipo (ex.: classificacao), sem pagar de novo pelos outros
    if (options.Get("only") is { } only)
    {
        cases = [.. cases.Where(c => Path.GetFileName(c.Ocr).StartsWith(only, StringComparison.Ordinal))];
    }

    // Gabarito gerado a partir do próprio modelo e ainda não conferido com o documento: medir contra ele seria medir o modelo contra si mesmo
    var unreviewed = cases.Where(c => File.ReadAllText(c.Gabarito).Contains("\"_revisar\"", StringComparison.Ordinal)).ToList();
    foreach (var c in unreviewed)
    {
        Console.WriteLine($"Ignorado (gabarito aguardando revisão): {Path.GetFileName(c.Gabarito)}");
    }

    cases = [.. cases.Except(unreviewed)];

    // Estimativa: holerite medido na Fase 1 (~4.400 tokens de entrada e ~1.100 de saída); classificação, só o
    // começo das páginas (~1.500 e ~150)
    var classificationCases = cases.Count(c => Path.GetFileName(c.Ocr).StartsWith("classificacao", StringComparison.Ordinal));
    var repetitions = int.Parse(options.Get("repeat") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
    var estimate = repetitions * (((cases.Count - classificationCases) * price.Cost(new ModelUsage(4_400, 1_100, 0, 0)))
        + (classificationCases * price.Cost(new ModelUsage(3_000, 200, 0, 0))));
    Console.WriteLine($"{cases.Count} casos ({cases.Count(c => c.Synthetic)} sintéticos) x {repetitions} rodada(s), modelo {model}: custo estimado US$ {estimate:0.00}.");

    if (!options.Has("yes"))
    {
        Console.WriteLine("Nada foi enviado ao modelo. Rode de novo com --yes para avaliar (custo real).");
        return 0;
    }

    AWSConfigs.AWSProfileName = options.Get("profile") ?? "parsen";
    using var bedrock = new AmazonBedrockRuntimeClient(new AmazonBedrockRuntimeConfig
    {
        RegionEndpoint = RegionEndpoint.GetBySystemName(options.Get("region") ?? "sa-east-1"),
        MaxErrorRetry = 2,
    });
    var engine = new BedrockExtractionEngine(bedrock, new BedrockExtractionSettings(model));
    // --classificacao vN: avalia outra versão do prompt de classificação (comparar antes do merge, regra 8)
    var classificationDefinition = options.Get("classificacao") is { } version ? ExtractionSchemas.Load("classificacao", version) : null;
    var classifier = new BedrockDocumentClassifier(bedrock, new BedrockExtractionSettings(model, MaxTokens: 1024), classificationDefinition);

    // Um relatório por tipo de documento: misturar holerite e comprovante juntaria campos diferentes na mesma tabela
    var resultsByType = new SortedDictionary<string, (string SchemaId, List<CaseResult> Results)>(StringComparer.Ordinal);
    var classifications = new List<CaseResult>();
    var classificationSchemaId = "";

    // --repeat N: cada caso N vezes, para medir a instabilidade do modelo (a mesma entrada nem sempre dá a mesma resposta)
    var repeat = int.Parse(options.Get("repeat") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
    var runs = cases.SelectMany(c => Enumerable.Range(1, repeat).Select(r => (c.Ocr, c.Gabarito, c.Synthetic, Run: r))).ToList();

    // Um caso por vez: a cota da conta é de 10 requisições por minuto no Haiku 4.5
    foreach (var (ocrPath, gabaritoPath, synthetic, run) in runs)
    {
        var name = Path.GetFileName(ocrPath).Replace(".ocr.json", "", StringComparison.Ordinal) + (repeat > 1 ? $" #{run}" : "");
        var gabarito = Gabarito.Parse(await File.ReadAllTextAsync(gabaritoPath));
        var ocr = OcrJson.Deserialize(await File.ReadAllBytesAsync(ocrPath));

        if (gabarito.DocumentType == "classificacao")
        {
            try
            {
                var classification = await WithThrottlingRetryAsync(() => classifier.ClassifyAsync(ocr, CancellationToken.None));
                classificationSchemaId = classification.SchemaId;
                classifications.Add(new CaseResult(name, synthetic, null, classification.Usage, price.Cost(classification.Usage), null,
                    ClassificationComparer.Compare(gabarito, classification)));
                Console.WriteLine($"  {name}: ok");
            }
#pragma warning disable CA1031 // um caso com erro não interrompe a avaliação dos outros
            catch (Exception ex)
#pragma warning restore CA1031
            {
                classifications.Add(new CaseResult(name, synthetic, null, ModelUsage.None, 0, ex.GetType().Name));
                Console.WriteLine($"  {name}: erro {ex.GetType().Name}");
            }

            continue;
        }

        try
        {
            var output = await WithThrottlingRetryAsync(() => engine.ExtractAsync(gabarito.DocumentType, ocr, CancellationToken.None));
            var extraction = ExtractionAssembler.Assemble(gabarito.DocumentType, output, ocr, DateTimeOffset.UtcNow);
            var group = resultsByType.TryGetValue(gabarito.DocumentType, out var existing) ? existing.Results : [];
            group.Add(new CaseResult(name, synthetic, Comparer.Compare(gabarito, extraction), output.Usage, price.Cost(output.Usage), null));
            resultsByType[gabarito.DocumentType] = (extraction.SchemaId, group);
            Console.WriteLine($"  {name}: ok");
        }
#pragma warning disable CA1031 // um caso com erro não interrompe a avaliação dos outros
        catch (Exception ex)
#pragma warning restore CA1031
        {
            var group = resultsByType.TryGetValue(gabarito.DocumentType, out var existing) ? existing.Results : [];
            group.Add(new CaseResult(name, synthetic, null, ModelUsage.None, 0, ex.GetType().Name));
            resultsByType[gabarito.DocumentType] = (existing.SchemaId ?? gabarito.DocumentType, group);
            Console.WriteLine($"  {name}: erro {ex.GetType().Name}");
        }
    }

    var sections = new List<string>();
    foreach (var (_, (typeSchemaId, typeResults)) in resultsByType)
    {
        sections.Add(Report.Render(typeSchemaId, model, typeResults));
    }

    if (classifications.Count > 0)
    {
        sections.Add(Report.RenderClassification(classificationSchemaId, model, classifications));
    }

    var report = string.Join(Environment.NewLine, sections);
    Console.WriteLine();
    Console.WriteLine(report);

    if (options.Get("report") is { } reportPath)
    {
        await File.WriteAllTextAsync(reportPath, report);
        Console.WriteLine($"Relatório gravado em {reportPath}.");
    }

    return 0;
}

/// <summary>
/// A conta tem cota de 10 chamadas por minuto no Haiku 4.5: quando a AWS limita, espera e tenta de novo (até 3 vezes).
/// Chamada recusada por limite não é cobrada.
/// </summary>
internal static partial class Program
{
    private static async Task<T> WithThrottlingRetryAsync<T>(Func<Task<T>> call)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (Amazon.BedrockRuntime.Model.ThrottlingException) when (attempt <= 3)
            {
                Console.WriteLine($"    limite de chamadas por minuto: aguardando 20 s (tentativa {attempt} de 3)");
                await Task.Delay(TimeSpan.FromSeconds(20));
            }
        }
    }
}

/// <summary>Argumentos: o primeiro é o comando; depois, --nome valor (repetível) e --flag.</summary>
internal sealed class Options
{
    private readonly List<(string Name, string? Value)> _values = [];

    public string Command { get; private set; } = "";

    public static Options Parse(string[] args)
    {
        var options = new Options { Command = args.Length > 0 ? args[0] : "" };
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            options._values.Add((args[i][2..], hasValue ? args[++i] : null));
        }

        return options;
    }

    public string? Get(string name) => _values.LastOrDefault(v => v.Name == name).Value;

    public IEnumerable<string> GetAll(string name) => _values.Where(v => v.Name == name && v.Value is not null).Select(v => v.Value!);

    public bool Has(string name) => _values.Any(v => v.Name == name);
}
