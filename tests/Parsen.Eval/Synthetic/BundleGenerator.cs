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
    private static readonly string[] Others =
    [
        "CONTRATO DE PRESTAÇÃO DE SERVIÇOS|Pelo presente instrumento particular, as partes abaixo qualificadas",
        "DECLARAÇÃO|Declaro, para os devidos fins, que conheço e aceito as condições",
        "RECIBO|Recebi a importância referente ao serviço prestado nesta data",
    ];
    private static readonly string[] Names = ["ANA LIMA SANTOS", "BRUNO DIAS GOMES", "CARLA MARTINS VIEIRA", "DIEGO RIBEIRO ALMEIDA"];

    private readonly Random _random = new(seed);
    private readonly HoleriteGenerator _holerites = new(seed + 1);
    private readonly ComprovanteGenerator _comprovantes = new(seed + 2);
    private int _bundles;
    private int _negatives;

    public (OcrDocument Ocr, Gabarito Gabarito) Generate()
    {
        var pages = new List<OcrPage>();
        var parts = new List<(DocumentType Type, int First, int Last)>();
        var count = _random.Next(1, 5);

        // A cada dois arquivos, um documento negativo do comprovante (IPTU, cartão ou condomínio, em rodízio)
        // numa posição sorteada: a classificação precisa mandá-lo para "outro"
        var negativeAt = _bundles++ % 2 == 0 ? _random.Next(count) : -1;

        for (var i = 0; i < count; i++)
        {
            var type = i == negativeAt ? DocumentType.Outro : PickType();
            var documentPages = type switch
            {
                DocumentType.Holerite => [Holerite()],
                DocumentType.ExtratoBancario => Extrato(),
                DocumentType.ComprovanteResidencia => [Comprovante()],
                DocumentType.MatriculaImovel => Matricula(),
                _ => [i == negativeAt ? Negative(_negatives++ % 3) : Outro()],
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

    /// <summary>
    /// Metade dos holerites sem título e com cabeçalho mais longo (endereço, folha, matrícula), como os reais:
    /// num holerite de verdade sem "Demonstrativo" as colunas Vencimentos e Descontos só aparecem depois da linha 20.
    /// </summary>
    private IReadOnlyList<OcrLine> Holerite()
    {
        var lines = _holerites.Generate().Ocr.Pages[0].Lines;
        if (_random.NextDouble() < 0.5)
        {
            return lines;
        }

        var headerRows = Lines(
        [
            "Folha Mensal",
            $"RUA DO COMERCIO, {_random.Next(10, 999)} - CENTRO",
            "EXEMPLÓPOLIS - SP",
            $"Matrícula {_random.Next(1000, 9999)}",
            "Depto: ADMINISTRATIVO",
        ]);

        var withoutTitle = lines.Where(l => !l.Text.StartsWith("DEMONSTRATIVO", StringComparison.Ordinal)).ToList();
        return [.. withoutTitle.Take(3), .. headerRows, .. withoutTitle.Skip(3)];
    }

    private List<IReadOnlyList<OcrLine>> Extrato()
    {
        var bank = Pick(Banks);
        var month = _random.Next(1, 13);
        // Titular, conta e período são do extrato inteiro: mudar um deles entre páginas indicaria outro documento
        var holder = Pick(Names);
        var account = $"Agência {_random.Next(1000, 9999)}  Conta {_random.Next(10000, 99999)}-{_random.Next(10)}";
        var pageCount = _random.Next(1, 4);
        var repeatHeader = _random.NextDouble() < 0.5;
        var pages = new List<IReadOnlyList<OcrLine>>();

        for (var p = 1; p <= pageCount; p++)
        {
            var rows = new List<string>();
            if (p == 1 || repeatHeader)
            {
                rows.AddRange([bank, "EXTRATO DE CONTA CORRENTE", holder, account, $"Período: 01/{month:00}/2026 a 28/{month:00}/2026", $"Página {p}/{pageCount}"]);
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

    private IReadOnlyList<OcrLine> Comprovante() => _comprovantes.Generate().Ocr.Pages[0].Lines;

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

    /// <summary>
    /// "Outro": documentos comuns e os casos negativos do comprovante (IPTU, fatura de cartão e boleto de
    /// condomínio), que no MVP não são aceitos como comprovante de residência e precisam ser classificados como outro.
    /// </summary>
    private List<OcrLine> Outro() => _random.Next(6) is var kind && kind < 3 ? Negative(kind) : OtherDocument();

    private List<OcrLine> Negative(int kind) => kind switch
    {
        0 => Lines(
        [
            "PREFEITURA MUNICIPAL DE EXEMPLÓPOLIS",
            "SECRETARIA DA FAZENDA",
            "IPTU 2026 - IMPOSTO PREDIAL E TERRITORIAL URBANO",
            $"Contribuinte: {Pick(Names)}",
            $"Inscrição imobiliária {_random.Next(100, 999)}.{_random.Next(100, 999)}.{_random.Next(1000, 9999)}",
            $"Endereço do imóvel: RUA DAS FLORES, {_random.Next(1, 999)} - CENTRO",
            "Endereço de entrega: o mesmo do imóvel",
            "Valor venal do imóvel  Área construída  Área do terreno",
            $"Parcela 01/10   Vencimento 10/03/2026   Valor R$ {Money()}",
        ]),
        1 => Lines(
        [
            "BANCO EXEMPLO S.A. - CARTÃO MODELO PLATINUM",
            "FATURA DO CARTÃO DE CRÉDITO",
            Pick(Names),
            $"RUA SETE DE SETEMBRO, {_random.Next(1, 999)}",
            $"CEP {_random.Next(10000, 99999)}-{_random.Next(100, 999)}  EXEMPLÓPOLIS - SP",
            $"Vencimento 15/09/2026   Total da fatura R$ {Money()}",
            "Pagamento mínimo   Limite disponível",
            "Lançamentos nacionais",
            $"02/08  SUPERMERCADO EXEMPLO  {Money()}",
            $"05/08  FARMACIA MODELO  {Money()}",
        ]),
        2 => Lines(
        [
            "CONDOMÍNIO EDIFÍCIO MODELO",
            "BOLETO DE COBRANÇA - TAXA CONDOMINIAL",
            $"Beneficiário: CONDOMÍNIO EDIFÍCIO MODELO   CNPJ {_random.Next(10, 99)}.{_random.Next(100, 999)}.{_random.Next(100, 999)}/0001-{_random.Next(10, 99)}",
            $"Pagador: {Pick(Names)}",
            $"Unidade: BLOCO A APTO {_random.Next(11, 140)}",
            $"Vencimento 10/09/2026   Valor do documento R$ {Money()}",
            "Taxa condominial  Fundo de reserva  Rateio de água",
            $"{_random.Next(10000, 99999)}.{_random.Next(10000, 99999)} {_random.Next(10000, 99999)}.{_random.Next(100000, 999999)}",
        ]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private List<OcrLine> OtherDocument()
    {
        var other = Pick(Others).Split('|');
        return Lines([other[0], other[1], Pick(Names), "EXEMPLÓPOLIS, 10 DE SETEMBRO DE 2026", "Assinatura"]);
    }

    private string Pick(string[] values) => values[_random.Next(values.Length)];

    private string Money() => SyntheticOcr.Pt(_random.Next(1_000, 900_000) / 100m);

    private static List<OcrLine> Lines(IEnumerable<string> rows) => SyntheticOcr.Lines(rows);
}
