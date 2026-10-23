using System.Globalization;
using Parsen.Application.Pipeline;
using Parsen.Domain.Identifiers;

namespace Parsen.Eval.Synthetic;

/// <summary>
/// Extratos sintéticos de um mês no formato do OCR, com gabarito (cabeçalho, saldos e só os créditos). Variam o que
/// muda nos extratos reais: valor com sinal ("-45,90") ou com sufixo ("45,90 D"), linhas de saldo do dia que não são
/// lançamentos, várias páginas com ou sem cabeçalho repetido, créditos de várias origens e créditos iguais no mesmo
/// dia. Colunas separadas de entradas e saídas, sem sinal, ficam de fora: só pelo texto do OCR não dá para distingui-las.
/// Nenhum dado real. Determinístico pela semente.
/// </summary>
public sealed class ExtratoGenerator(int seed)
{
    private static readonly string[] Banks = ["BANCO EXEMPLO S.A.", "BANCO FICTICIO DO BRASIL S.A.", "INSTITUICAO DE PAGAMENTO TESTE S.A."];
    private static readonly string[] Names = ["ANA LIMA SANTOS", "BRUNO DIAS GOMES", "CARLA MARTINS VIEIRA", "DIEGO RIBEIRO ALMEIDA", "ELISA NOGUEIRA PEREIRA"];
    private static readonly string[] Companies = ["COMERCIAL ALFA LTDA", "BETA SERVICOS LTDA", "GAMA CONSULTORIA"];
    private static readonly string[] Stores = ["SUPERMERCADO EXEMPLO", "FARMACIA MODELO", "POSTO TESTE", "PADARIA CENTRAL"];

    private readonly Random _random = new(seed);
    private int _generated;

    public (OcrDocument Ocr, Gabarito Gabarito) Generate()
    {
        // Formato em rodízio: com poucas amostras, o sorteio podia deixar um de fora
        var suffixFormat = _generated++ % 2 == 1;
        var bank = Pick(Banks);
        var cnpjBase = Digits(8) + "0001";
        var cnpj = cnpjBase + Cnpj.CheckDigits(cnpjBase);
        var holder = Pick(Names);
        var agency = Digits(4);
        var account = $"{Digits(5)}-{Digits(1)}";
        var month = _random.Next(1, 13);
        var lastDay = DateTime.DaysInMonth(2026, month);
        var pageCount = _random.Next(1, 4);
        var repeatHeader = _random.NextDouble() < 0.5;

        var balance = _random.Next(-300_00, 5_000_00) / 100m;
        var openingBalance = balance;
        var credits = new List<(DateOnly Date, string Description, decimal Value)>();
        var pages = new List<OcrPage>();
        var day = 1;

        for (var p = 1; p <= pageCount; p++)
        {
            var rows = new List<string>();
            if (p == 1 || repeatHeader)
            {
                rows.AddRange(
                [
                    bank,
                    $"CNPJ {cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}",
                    "EXTRATO DE CONTA CORRENTE",
                    holder,
                    $"Agência {agency}   Conta {account}",
                    $"Período: 01/{month:00}/2026 a {lastDay}/{month:00}/2026",
                    $"Página {p}/{pageCount}",
                    "Data  Lançamento  Valor (R$)",
                ]);
            }

            if (p == 1)
            {
                rows.Add($"SALDO ANTERIOR  {Amount(openingBalance, suffixFormat, isBalance: true)}");
            }

            var entries = _random.Next(10, 16);
            for (var e = 0; e < entries; e++)
            {
                var date = new DateOnly(2026, month, day);
                var isCredit = _random.NextDouble() < 0.4;
                var value = _random.Next(5_00, isCredit ? 3_500_00 : 800_00) / 100m;
                var description = isCredit ? CreditDescription() : DebitDescription();
                rows.Add($"{date:dd/MM}  {description}  {Amount(isCredit ? value : -value, suffixFormat, isBalance: false)}");
                balance += isCredit ? value : -value;
                if (isCredit)
                {
                    credits.Add((date, description, value));

                    // Crédito repetido no mesmo dia: são dois lançamentos, não um
                    if (_random.NextDouble() < 0.1)
                    {
                        rows.Add($"{date:dd/MM}  {description}  {Amount(value, suffixFormat, isBalance: false)}");
                        balance += value;
                        credits.Add((date, description, value));
                    }
                }

                if (_random.NextDouble() < 0.4)
                {
                    rows.Add($"SALDO DO DIA {date:dd/MM}  {Amount(balance, suffixFormat, isBalance: true)}");
                    day = Math.Min(lastDay, day + _random.Next(1, 4));
                }
            }

            if (p == pageCount)
            {
                rows.Add($"SALDO FINAL  {Amount(balance, suffixFormat, isBalance: true)}");
            }

            pages.Add(new OcrPage(p, SyntheticOcr.Lines(rows)));
        }

        var gabarito = new Gabarito(
            "extrato_bancario",
            new Dictionary<string, ExpectedValue>
            {
                ["instituicao"] = Text(bank),
                ["cnpj_instituicao"] = Text(cnpj),
                ["titular"] = Text(holder),
                ["cpf_titular"] = ExpectedValue.Absent,
                ["agencia"] = Text(agency),
                ["conta"] = Text(account),
                ["periodo_inicio"] = Text($"2026-{month:00}-01"),
                ["periodo_fim"] = Text($"2026-{month:00}-{lastDay}"),
                ["saldo_inicial"] = new(openingBalance, null),
                ["saldo_final"] = new(balance, null),
            },
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, ExpectedValue>>>
            {
                ["creditos"] = [.. credits.Select(c => (IReadOnlyDictionary<string, ExpectedValue>)new Dictionary<string, ExpectedValue>
                {
                    ["data"] = Text(c.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    ["descricao"] = Text(c.Description),
                    ["valor"] = new(c.Value, null),
                })],
            });

        return (new OcrDocument(pages), gabarito);
    }

    // Lançamento: "1.250,00" / "-45,90" (com sinal) ou "1.250,00 C" / "45,90 D" (com sufixo); saldo negativo idem
    private static string Amount(decimal value, bool suffixFormat, bool isBalance)
    {
        var text = SyntheticOcr.Pt(Math.Abs(value));
        if (suffixFormat)
        {
            return $"{text} {(value < 0 ? "D" : "C")}";
        }

        return value < 0 ? $"-{text}" : text;
    }

    private string CreditDescription() => _random.Next(7) switch
    {
        0 => $"PIX RECEBIDO {Pick(Names)}",
        1 => $"TED RECEBIDA {Pick(Companies)}",
        2 => "DEPOSITO EM DINHEIRO",
        3 => $"SALARIO {Pick(Companies)}",
        4 => "RENDIMENTO POUPANCA",
        5 => "ESTORNO COMPRA",
        _ => "RESGATE CDB",
    };

    private string DebitDescription() => _random.Next(5) switch
    {
        0 => $"COMPRA CARTAO DEBITO {Pick(Stores)}",
        1 => $"PIX ENVIADO {Pick(Names)}",
        2 => "PAGAMENTO DE BOLETO",
        3 => "TARIFA PACOTE DE SERVICOS",
        _ => "SAQUE 24H",
    };

    private static ExpectedValue Text(string value) => new(null, value);

    private string Pick(string[] values) => values[_random.Next(values.Length)];

    private string Digits(int count) => string.Concat(Enumerable.Range(0, count).Select(_ => (char)('0' + _random.Next(10))));
}
