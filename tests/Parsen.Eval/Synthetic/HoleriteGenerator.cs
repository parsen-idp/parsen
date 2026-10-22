using System.Globalization;
using Parsen.Application.Pipeline;
using Parsen.Domain.Identifiers;

namespace Parsen.Eval.Synthetic;

/// <summary>
/// Holerites sintéticos já no formato do OCR (textract.json), com o gabarito correspondente. Imitam o layout
/// em colunas dos holerites reais: rótulo e valor em linhas diferentes, uma ou duas vias, bases de cálculo
/// que não são rubricas. Nenhum dado real: nomes inventados e CPF/CNPJ gerados (dígitos válidos).
/// Determinístico: a mesma semente gera os mesmos documentos.
/// </summary>
public sealed class HoleriteGenerator(int seed)
{
    private static readonly string[] FirstNames = ["ANA", "BRUNO", "CARLA", "DIEGO", "ELISA", "FABIO", "GABRIELA", "HUGO", "ISABELA", "JOAO", "LARISSA", "MARCOS", "NATALIA", "OTAVIO", "PAULA", "RAFAEL"];
    private static readonly string[] LastNames = ["ALMEIDA", "BARBOSA", "CARDOSO", "DIAS", "FERREIRA", "GOMES", "LIMA", "MARTINS", "NOGUEIRA", "OLIVEIRA", "PEREIRA", "RIBEIRO", "SANTOS", "TEIXEIRA", "VIEIRA"];
    private static readonly string[] Companies = ["COMERCIAL ALFA LTDA", "BETA SERVICOS DE TI LTDA", "GAMA INDUSTRIA E COMERCIO S.A.", "DELTA LOGISTICA EIRELI", "EPSILON CONSULTORIA LTDA", "ZETA ALIMENTOS LTDA"];
    private static readonly string[] Roles = ["ANALISTA ADMINISTRATIVO", "ASSISTENTE FINANCEIRO", "DESENVOLVEDOR DE SISTEMAS", "AUXILIAR DE LOGISTICA", "VENDEDOR", "SUPERVISOR DE PRODUCAO"];
    private static readonly string[] Months = ["JANEIRO", "FEVEREIRO", "MARCO", "ABRIL", "MAIO", "JUNHO", "JULHO", "AGOSTO", "SETEMBRO", "OUTUBRO", "NOVEMBRO", "DEZEMBRO"];

    // Largura média de um caractere e altura de uma linha, em frações da página
    private const double CharWidth = 0.0065;
    private const double LineHeight = 0.012;
    private const double RowStep = 0.019;

    private readonly Random _random = new(seed);

    public (OcrDocument Ocr, Gabarito Gabarito) Generate()
    {
        var holerite = NewHolerite();
        var twoCopies = _random.NextDouble() < 0.5;

        // Duas vias na mesma página (empregador e funcionário): a segunda começa no meio da folha
        var lines = Render(holerite, top: 0.03);
        if (twoCopies)
        {
            lines.AddRange(Render(holerite, top: 0.52));
        }

        return (new OcrDocument([new OcrPage(1, lines)]), holerite.ToGabarito());
    }

    private Holerite NewHolerite()
    {
        var month = _random.Next(1, 13);
        var year = 2026;
        var baseSalary = Money(_random.Next(1_800_00, 12_000_00));

        var items = new List<Item> { new("001", "SALARIO", "30,00", "vencimento", baseSalary) };
        if (_random.NextDouble() < 0.4)
        {
            var hours = _random.Next(2, 21);
            items.Add(new("050", "HORAS EXTRAS 50%", $"{hours},00", "vencimento", Money(baseSalary / 220m * 1.5m * hours)));
        }

        if (_random.NextDouble() < 0.2)
        {
            items.Add(new("060", "ADICIONAL NOTURNO", "20,00%", "vencimento", Money(baseSalary * 0.2m)));
        }

        var gross = items.Sum(i => i.Value);
        var inssRate = gross switch { <= 1_518m => 7.5m, <= 2_793.88m => 9m, <= 4_190.83m => 12m, _ => 14m };
        var inss = Money(Math.Min(gross * inssRate / 100m, 951.63m));
        items.Add(new("901", "INSS", Pt(inssRate) + "%", "desconto", inss));

        if (gross > 3_500m)
        {
            items.Add(new("902", "IRRF", "15,00%", "desconto", Money(Math.Max((gross - inss) * 0.15m - 394.16m, 10m))));
        }

        if (_random.NextDouble() < 0.5)
        {
            items.Add(new("910", "VALE TRANSPORTE", "6,00%", "desconto", Money(baseSalary * 0.06m)));
        }

        if (_random.NextDouble() < 0.4)
        {
            items.Add(new("920", "PLANO DE SAUDE", null, "desconto", Money(_random.Next(80_00, 450_00))));
        }

        var cnpjBase = _random.NextDouble() < 0.3 ? AlphanumericBase() : Digits(8) + "0001";
        var cpfBase = Digits(9);

        return new Holerite(
            Name: $"{Pick(FirstNames)} {Pick(LastNames)} {Pick(LastNames)}",
            Cpf: _random.NextDouble() < 0.6 ? cpfBase + Cpf.CheckDigits(cpfBase) : null,
            Company: Pick(Companies),
            Cnpj: cnpjBase + Cnpj.CheckDigits(cnpjBase),
            Month: month,
            Year: year,
            MonthText: _random.Next(3) switch
            {
                0 => $"{Months[month - 1]}/{year}",
                1 => $"{month:00}/{year}",
                _ => $"{Months[month - 1][..3]}/{year}",
            },
            Role: _random.NextDouble() < 0.8 ? Pick(Roles) : null,
            Admission: _random.NextDouble() < 0.7 ? new DateOnly(_random.Next(2012, 2026), _random.Next(1, 13), _random.Next(1, 29)) : null,
            EmployeeCode: _random.Next(100, 9999).ToString(CultureInfo.InvariantCulture),
            BaseSalary: baseSalary,
            Items: items);
    }

    /// <summary>Linhas do OCR de uma via, de cima para baixo e da esquerda para a direita, como o Textract lê.</summary>
    private List<OcrLine> Render(Holerite h, double top)
    {
        // Cada célula vira uma linha do OCR: colunas distantes não se juntam numa linha só no Textract
        var rows = new List<(double X, string Text)[]>
        {
            Row((0.05, "DEMONSTRATIVO DE PAGAMENTO DE SALARIO"), (0.72, "Referência")),
            Row((0.05, h.Company), (0.72, h.MonthText)),
            Row((0.05, $"CNPJ: {FormatCnpj(h.Cnpj)}")),
            Row((0.05, "Código"), (0.13, "Nome do Funcionário"), (0.55, "CBO"), (0.65, "Função")),
            Row((0.05, h.EmployeeCode), (0.13, h.Name), (0.55, "411010"), (0.65, h.Role ?? "")),
        };

        var idRow = new List<(double, string)>();
        if (h.Cpf is not null)
        {
            idRow.Add((0.05, $"CPF: {FormatCpf(h.Cpf)}"));
        }

        if (h.Admission is { } admission)
        {
            idRow.Add((0.55, $"Admissão: {admission:dd/MM/yyyy}"));
        }

        if (idRow.Count > 0)
        {
            rows.Add([.. idRow]);
        }

        rows.Add([(0.05, "Cód."), (0.12, "Descrição"), (0.45, "Referência"), (0.62, "Vencimentos"), (0.82, "Descontos")]);
        foreach (var item in h.Items)
        {
            var cells = new List<(double, string)> { (0.05, item.Code), (0.12, item.Description) };
            if (item.Reference is not null)
            {
                cells.Add((0.45, item.Reference));
            }

            cells.Add((item.Type == "vencimento" ? 0.62 : 0.82, Pt(item.Value)));
            rows.Add([.. cells]);
        }

        rows.Add([(0.62, "Total de Vencimentos"), (0.82, "Total de Descontos")]);
        rows.Add([(0.62, Pt(h.TotalEarnings)), (0.82, Pt(h.TotalDeductions))]);
        rows.Add([(0.62, "Valor Líquido ->"), (0.82, Pt(h.Net))]);
        rows.Add([(0.05, "Salário Base"), (0.2, "Sal. Contr. INSS"), (0.36, "Base Cálc. FGTS"), (0.52, "F.G.T.S do Mês"), (0.68, "Base Cálc. IRRF")]);
        rows.Add([(0.05, Pt(h.BaseSalary)), (0.2, Pt(h.TotalEarnings)), (0.36, Pt(h.TotalEarnings)), (0.52, Pt(Money(h.TotalEarnings * 0.08m))), (0.68, Pt(h.TotalEarnings - h.Inss))]);
        rows.Add([(0.05, "Declaro ter recebido a importância líquida discriminada neste recibo"), (0.7, "Assinatura do Funcionário")]);

        var lines = new List<OcrLine>();
        for (var r = 0; r < rows.Count; r++)
        {
            var y = Math.Round(top + (r * RowStep), 4);
            lines.AddRange(rows[r].Where(c => c.Text.Length > 0).Select(c => Line(c.Text, c.X, y)));
        }

        return lines;
    }

    private static (double X, string Text)[] Row(params (double X, string Text)[] cells) => cells;

    private OcrLine Line(string text, double x, double y)
    {
        var words = new List<OcrWord>();
        var cursor = x;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var width = Math.Round(word.Length * CharWidth, 4);
            words.Add(new OcrWord(word, Confidence(), [Math.Round(cursor, 4), y, width, LineHeight]));
            cursor += width + CharWidth;
        }

        var lineWidth = Math.Round(cursor - CharWidth - x, 4);
        return new OcrLine(text, Math.Round(words.Average(w => w.Confidence), 2), [x, y, lineWidth, LineHeight], words);
    }

    // OCR limpo, com uma palavra ocasional lida com menos certeza
    private double Confidence() => Math.Round(_random.NextDouble() < 0.05 ? 80 + (_random.NextDouble() * 15) : 97 + (_random.NextDouble() * 2.9), 2);

    private string Pick(string[] values) => values[_random.Next(values.Length)];

    private string Digits(int count) => string.Concat(Enumerable.Range(0, count).Select(_ => (char)('0' + _random.Next(10))));

    // CNPJ alfanumérico (julho de 2026): letras e dígitos na raiz, dígitos na ordem do estabelecimento
    private string AlphanumericBase() =>
        string.Concat(Enumerable.Range(0, 8).Select(_ => _random.NextDouble() < 0.5 ? (char)('A' + _random.Next(26)) : (char)('0' + _random.Next(10)))) + "0001";

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal Money(int cents) => cents / 100m;

    /// <summary>Formato brasileiro: 1.234,56.</summary>
    private static string Pt(decimal value) =>
        value.ToString("#,##0.00", CultureInfo.InvariantCulture).Replace(",", "_", StringComparison.Ordinal).Replace(".", ",", StringComparison.Ordinal).Replace("_", ".", StringComparison.Ordinal);

    private static string FormatCpf(string cpf) => $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}";

    private static string FormatCnpj(string cnpj) => $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}";

    private sealed record Item(string Code, string Description, string? Reference, string Type, decimal Value);

    private sealed record Holerite(
        string Name, string? Cpf, string Company, string Cnpj, int Month, int Year, string MonthText, string? Role,
        DateOnly? Admission, string EmployeeCode, decimal BaseSalary, List<Item> Items)
    {
        public decimal TotalEarnings => Items.Where(i => i.Type == "vencimento").Sum(i => i.Value);

        public decimal TotalDeductions => Items.Where(i => i.Type == "desconto").Sum(i => i.Value);

        public decimal Net => TotalEarnings - TotalDeductions;

        public decimal Inss => Items.Single(i => i.Description == "INSS").Value;

        public Gabarito ToGabarito() => new(
            "holerite",
            new Dictionary<string, ExpectedValue>
            {
                ["nome_funcionario"] = new(null, Name),
                ["cpf"] = Cpf is null ? ExpectedValue.Absent : new(null, Cpf),
                ["empregador"] = new(null, Company),
                ["cnpj_empregador"] = new(null, Cnpj),
                ["competencia"] = new(null, $"{Year}-{Month:00}"),
                ["cargo"] = Role is null ? ExpectedValue.Absent : new(null, Role),
                ["data_admissao"] = Admission is { } date ? new(null, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : ExpectedValue.Absent,
                ["salario_base"] = new(BaseSalary, null),
                ["total_vencimentos"] = new(TotalEarnings, null),
                ["total_descontos"] = new(TotalDeductions, null),
                ["salario_liquido"] = new(Net, null),
            },
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, ExpectedValue>>>
            {
                ["itens"] = [.. Items.Select(i => (IReadOnlyDictionary<string, ExpectedValue>)new Dictionary<string, ExpectedValue>
                {
                    ["codigo"] = new(null, i.Code),
                    ["descricao"] = new(null, i.Description),
                    ["referencia"] = i.Reference is null ? ExpectedValue.Absent : new(null, i.Reference),
                    ["tipo"] = new(null, i.Type),
                    ["valor"] = new(i.Value, null),
                })],
            });
    }
}
