using System.Globalization;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;

namespace Parsen.Eval.Synthetic;

/// <summary>
/// Arquivos sintéticos com vários documentos em sequência, para avaliar o split e a classificação.
/// Holerites vêm do HoleriteGenerator; extrato, comprovante, matrícula e "outro" têm só cabeçalho e linhas
/// típicas (o que a classificação lê). Continuações sem cabeçalho e documentos do mesmo tipo seguidos
/// testam as regras de separação. Determinístico pela semente.
/// </summary>
public sealed class BundleGenerator(int seed)
{
    private static readonly string[] Banks = ["BANCO EXEMPLO S.A.", "BANCO FICTICIO DO BRASIL S.A.", "COOPERATIVA DE CREDITO MODELO", "INSTITUICAO DE PAGAMENTO TESTE S.A."];
    private static readonly string[] Utilities =
    [
        "COMPANHIA ENERGETICA EXEMPLO|CONTA DE ENERGIA ELÉTRICA",
        "SANEAMENTO MODELO S.A.|CONTA DE ÁGUA E ESGOTO",
        "FIBRA TESTE TELECOMUNICACOES LTDA|FATURA DE SERVIÇOS DE INTERNET",
    ];
    private static readonly string[] Others =
    [
        "CONTRATO DE PRESTAÇÃO DE SERVIÇOS|Pelo presente instrumento particular, as partes abaixo qualificadas",
        "DECLARAÇÃO|Declaro, para os devidos fins, que conheço e aceito as condições",
        "RECIBO|Recebi a importância referente ao serviço prestado nesta data",
    ];
    private static readonly string[] Names = ["ANA LIMA SANTOS", "BRUNO DIAS GOMES", "CARLA MARTINS VIEIRA", "DIEGO RIBEIRO ALMEIDA"];

    private readonly Random _random = new(seed);
    private readonly HoleriteGenerator _holerites = new(seed + 1);

    public (OcrDocument Ocr, Gabarito Gabarito) Generate()
    {
        var pages = new List<OcrPage>();
        var parts = new List<(DocumentType Type, int First, int Last)>();
        var count = _random.Next(1, 5);

        for (var i = 0; i < count; i++)
        {
            var type = PickType();
            var documentPages = type switch
            {
                DocumentType.Holerite => [_holerites.Generate().Ocr.Pages[0].Lines],
                DocumentType.ExtratoBancario => Extrato(),
                DocumentType.ComprovanteResidencia => [Comprovante()],
                DocumentType.MatriculaImovel => Matricula(),
                _ => [Outro()],
            };

            var first = pages.Count + 1;
            pages.AddRange(documentPages.Select((lines, index) => new OcrPage(first + index, lines)));
            parts.Add((type, first, pages.Count));
        }

        var gabarito = new Gabarito(
            "classificacao",
            new Dictionary<string, ExpectedValue>(),
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, ExpectedValue>>>
            {
                ["documentos"] = [.. parts.Select(p => (IReadOnlyDictionary<string, ExpectedValue>)new Dictionary<string, ExpectedValue>
                {
                    ["tipo"] = new(null, DocumentTypeCodes.ToCode(p.Type)),
                    ["pagina_inicial"] = new(p.First, null),
                    ["pagina_final"] = new(p.Last, null),
                })],
            });

        return (new OcrDocument(pages), gabarito);
    }

    private DocumentType PickType() => _random.Next(10) switch
    {
        < 3 => DocumentType.Holerite,
        < 5 => DocumentType.ExtratoBancario,
        < 7 => DocumentType.ComprovanteResidencia,
        < 9 => DocumentType.MatriculaImovel,
        _ => DocumentType.Outro,
    };

    private List<IReadOnlyList<OcrLine>> Extrato()
    {
        var bank = Pick(Banks);
        var month = _random.Next(1, 13);
        var account = $"Agência {_random.Next(1000, 9999)}  Conta {_random.Next(10000, 99999)}-{_random.Next(10)}";
        var pageCount = _random.Next(1, 4);
        var repeatHeader = _random.NextDouble() < 0.5;
        var pages = new List<IReadOnlyList<OcrLine>>();

        for (var p = 1; p <= pageCount; p++)
        {
            var rows = new List<string>();
            if (p == 1 || repeatHeader)
            {
                rows.AddRange([bank, "EXTRATO DE CONTA CORRENTE", Pick(Names), account, $"Período: 01/{month:00}/2026 a 28/{month:00}/2026", $"Página {p}/{pageCount}"]);
                rows.Add("Data  Histórico  Documento  Valor  Saldo");
            }

            for (var t = 0; t < 14; t++)
            {
                var credit = _random.NextDouble() < 0.4;
                rows.Add($"{_random.Next(1, 29):00}/{month:00}  {(credit ? "PIX RECEBIDO" : "COMPRA CARTAO DEBITO")}  {_random.Next(100000, 999999)}  {(credit ? "" : "-")}{Money()}  {Money()}");
            }

            pages.Add(Lines(rows));
        }

        return pages;
    }

    private List<OcrLine> Comprovante()
    {
        var utility = Pick(Utilities).Split('|');
        return Lines(
        [
            utility[0],
            utility[1],
            $"Nota fiscal / conta nº {_random.Next(100000, 999999)}",
            Pick(Names),
            $"RUA DAS FLORES, {_random.Next(1, 999)} - APTO {_random.Next(1, 200)}",
            $"CEP {_random.Next(10000, 99999)}-{_random.Next(100, 999)}  EXEMPLÓPOLIS - SP",
            $"Referência {_random.Next(1, 13):00}/2026   Vencimento {_random.Next(1, 29):00}/{_random.Next(1, 13):00}/2026",
            $"Total a pagar R$ {Money()}",
            "Leitura anterior  Leitura atual  Consumo",
        ]);
    }

    private List<IReadOnlyList<OcrLine>> Matricula()
    {
        var number = $"{_random.Next(10, 99)}.{_random.Next(100, 999)}";
        var pageCount = _random.Next(1, 4);
        var pages = new List<IReadOnlyList<OcrLine>>
        {
            Lines(
            [
                $"REGISTRO DE IMÓVEIS DA {_random.Next(1, 9)}ª CIRCUNSCRIÇÃO",
                "COMARCA DE EXEMPLÓPOLIS - SP",
                "LIVRO Nº 2 - REGISTRO GERAL",
                $"MATRÍCULA Nº {number}    FICHA 01",
                $"IMÓVEL: Apartamento nº {_random.Next(1, 300)}, localizado no {_random.Next(1, 20)}º andar do Edifício Modelo,",
                "com área privativa de 65,40 m² e área comum de 30,12 m².",
                $"PROPRIETÁRIO: {Pick(Names)}, brasileiro, solteiro.",
                $"R.1/{number} - COMPRA E VENDA. Por escritura pública lavrada em notas do 1º Tabelião,",
            ]),
        };

        for (var p = 2; p <= pageCount; p++)
        {
            // Metade das fichas seguintes repete o número da matrícula; a outra metade é só texto corrido
            var rows = _random.NextDouble() < 0.5 ? new List<string> { $"MATRÍCULA Nº {number}    FICHA {p:00}" } : [];
            rows.AddRange(
            [
                $"Av.{p}/{number} - Procede-se a esta averbação para constar o cancelamento da hipoteca",
                "registrada sob o R.2, por autorização do credor, nos termos do instrumento particular.",
                $"R.{p + 1}/{number} - ALIENAÇÃO FIDUCIÁRIA em favor do BANCO EXEMPLO S.A.",
                "Valor da dívida: R$ 250.000,00. Prazo: 360 meses.",
            ]);
            pages.Add(Lines(rows));
        }

        return pages;
    }

    private List<OcrLine> Outro()
    {
        var other = Pick(Others).Split('|');
        return Lines([other[0], other[1], Pick(Names), "EXEMPLÓPOLIS, 10 DE SETEMBRO DE 2026", "Assinatura"]);
    }

    private string Pick(string[] values) => values[_random.Next(values.Length)];

    private string Money() => (_random.Next(1_000, 900_000) / 100m).ToString("#,##0.00", CultureInfo.InvariantCulture)
        .Replace(",", "_", StringComparison.Ordinal).Replace(".", ",", StringComparison.Ordinal).Replace("_", ".", StringComparison.Ordinal);

    // OCR limpo de linhas inteiras: a classificação só lê o texto, então a posição é aproximada
    private static List<OcrLine> Lines(IEnumerable<string> rows) =>
        [.. rows.Select((text, i) =>
        {
            var y = Math.Round(0.04 + (i * 0.022), 4);
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => new OcrWord(w, 99, [0.05, y, 0.05, 0.012])).ToList();
            return new OcrLine(text, 99, [0.05, y, Math.Min(0.9, text.Length * 0.0065), 0.012], words);
        })];
}
