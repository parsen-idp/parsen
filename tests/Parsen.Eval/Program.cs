using Amazon;
using Amazon.BedrockRuntime;
using Parsen.Application.Pipeline;
using Parsen.Eval;
using Parsen.Eval.Synthetic;
using Parsen.Infrastructure.Extraction;
using Parsen.Infrastructure.Ocr;

// Uso:
//   dotnet run --project tests/Parsen.Eval -- generate [--count 10] [--seed 42] [--out tests/Parsen.Eval/fixtures/holerite]
//   dotnet run --project tests/Parsen.Eval -- run [--dataset <pasta>]... [--model <id>] [--report <arquivo.md>] [--yes]
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
    var output = options.Get("out") ?? Path.Combine(DefaultFixtures, "holerite");
    Directory.CreateDirectory(output);

    var generator = new HoleriteGenerator(seed);
    for (var i = 1; i <= count; i++)
    {
        var (ocr, gabarito) = generator.Generate();
        var name = $"holerite-sintetico-{i:000}";
        File.WriteAllBytes(Path.Combine(output, $"{name}.ocr.json"), OcrJson.Serialize(ocr));
        File.WriteAllText(Path.Combine(output, $"{name}.gabarito.json"), gabarito.ToJson());
    }

    Console.WriteLine($"{count} holerites sintéticos gerados em {output} (semente {seed}).");
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

    // Gabarito gerado a partir do próprio modelo e ainda não conferido com o documento: medir contra ele seria medir o modelo contra si mesmo
    var unreviewed = cases.Where(c => File.ReadAllText(c.Gabarito).Contains("\"_revisar\"", StringComparison.Ordinal)).ToList();
    foreach (var c in unreviewed)
    {
        Console.WriteLine($"Ignorado (gabarito aguardando revisão): {Path.GetFileName(c.Gabarito)}");
    }

    cases = [.. cases.Except(unreviewed)];

    // Estimativa pelo holerite medido na Fase 1 (~4.400 tokens de entrada e ~1.100 de saída)
    var estimate = cases.Count * price.Cost(new ModelUsage(4_400, 1_100, 0, 0));
    Console.WriteLine($"{cases.Count} casos ({cases.Count(c => c.Synthetic)} sintéticos), modelo {model}: custo estimado US$ {estimate:0.00}.");

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

    var results = new List<CaseResult>();
    var schemaId = "";

    // Um caso por vez: a cota da conta é de 10 requisições por minuto no Haiku 4.5
    foreach (var (ocrPath, gabaritoPath, synthetic) in cases)
    {
        var name = Path.GetFileName(ocrPath).Replace(".ocr.json", "", StringComparison.Ordinal);
        var gabarito = Gabarito.Parse(await File.ReadAllTextAsync(gabaritoPath));
        var ocr = OcrJson.Deserialize(await File.ReadAllBytesAsync(ocrPath));

        try
        {
            var output = await engine.ExtractAsync(gabarito.DocumentType, ocr, CancellationToken.None);
            var extraction = ExtractionAssembler.Assemble(gabarito.DocumentType, output, ocr, DateTimeOffset.UtcNow);
            schemaId = extraction.SchemaId;
            results.Add(new CaseResult(name, synthetic, Comparer.Compare(gabarito, extraction), output.Usage, price.Cost(output.Usage), null));
            Console.WriteLine($"  {name}: ok");
        }
#pragma warning disable CA1031 // um caso com erro não interrompe a avaliação dos outros
        catch (Exception ex)
#pragma warning restore CA1031
        {
            results.Add(new CaseResult(name, synthetic, null, ModelUsage.None, 0, ex.GetType().Name));
            Console.WriteLine($"  {name}: erro {ex.GetType().Name}");
        }
    }

    var report = Report.Render(schemaId, model, results);
    Console.WriteLine();
    Console.WriteLine(report);

    if (options.Get("report") is { } reportPath)
    {
        await File.WriteAllTextAsync(reportPath, report);
        Console.WriteLine($"Relatório gravado em {reportPath}.");
    }

    return 0;
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
