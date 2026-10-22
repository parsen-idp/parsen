using System.Globalization;
using Parsen.Application.Pipeline;
using Parsen.Domain.Identifiers;

namespace Parsen.Eval.Synthetic;

/// <summary>
/// Contas de consumo sintéticas (energia, água, gás e telecom) no formato do OCR, com gabarito. Variam o que
/// muda nas contas reais: rótulo na mesma linha ou na de cima, complemento, CPF completo, mascarado ou ausente,
/// endereço de entrega separado e formato do mês de referência. Nenhum dado real. Determinístico pela semente.
/// </summary>
public sealed class ComprovanteGenerator(int seed)
{
    private static readonly Dictionary<string, (string[] Emitters, string Title, string Consumption)> Services = new()
    {
        ["energia"] = (["ENERGIA EXEMPLO DISTRIBUIDORA S.A.", "COMPANHIA DE LUZ MODELO"], "CONTA DE ENERGIA ELÉTRICA", "Consumo (kWh)"),
        ["agua"] = (["SANEAMENTO MODELO S.A.", "COMPANHIA DE ÁGUAS EXEMPLO"], "CONTA DE ÁGUA E ESGOTO", "Consumo (m³)"),
        ["gas"] = (["GÁS CANALIZADO EXEMPLO S.A."], "FATURA DE GÁS NATURAL", "Volume (m³)"),
        ["telecom"] = (["FIBRA TESTE TELECOMUNICAÇÕES LTDA", "OPERADORA MODELO S.A."], "FATURA DE SERVIÇOS DE TELECOMUNICAÇÕES", "Plano contratado"),
    };

    private static readonly string[] Names = ["ANA LIMA SANTOS", "BRUNO DIAS GOMES", "CARLA MARTINS VIEIRA", "DIEGO RIBEIRO ALMEIDA", "ELISA NOGUEIRA PEREIRA"];
    private static readonly string[] Streets = ["RUA DAS FLORES", "AVENIDA BRASIL", "RUA SETE DE SETEMBRO", "ALAMEDA DOS ANJOS", "TRAVESSA DO COMÉRCIO"];
    private static readonly string[] Neighborhoods = ["JARDIM EXEMPLO", "CENTRO", "VILA NOVA", "BELA VISTA"];
    private static readonly (string City, string Uf)[] Cities = [("EXEMPLÓPOLIS", "SP"), ("VILA MODELO", "MG"), ("PORTO FICTÍCIO", "RS"), ("SANTA TESTE", "PR")];
    private static readonly string[] Complements = ["APTO 45", "CASA 2", "BLOCO B APTO 12"];
    private static readonly string[] Months = ["JAN", "FEV", "MAR", "ABR", "MAI", "JUN", "JUL", "AGO", "SET", "OUT", "NOV", "DEZ"];

    private readonly Random _random = new(seed);
    private int _generated;

    public (OcrDocument Ocr, Gabarito Gabarito) Generate()
    {
        // Subtipos em rodízio: com poucas amostras, o sorteio deixava algum serviço de fora
        var subtype = Services.Keys.ElementAt(_generated++ % Services.Count);
        var service = Services[subtype];
        var emitter = Pick(service.Emitters);
        var cnpjBase = Digits(8) + "0001";
        var cnpj = cnpjBase + Cnpj.CheckDigits(cnpjBase);
        var holder = Pick(Names);
        var street = Pick(Streets);
        var number = _random.Next(10, 2000).ToString(CultureInfo.InvariantCulture);
        string? complement = _random.NextDouble() < 0.5 ? Pick(Complements) : null;
        var neighborhood = Pick(Neighborhoods);
        var (city, uf) = Cities[_random.Next(Cities.Length)];
        var cep = Digits(8);
        var month = _random.Next(1, 13);
        var issued = new DateOnly(2026, month, _random.Next(1, 10));
        var due = issued.AddDays(_random.Next(8, 20));
        var total = _random.Next(45_00, 650_00) / 100m;
        var labelsAbove = _random.NextDouble() < 0.5;

        // CPF: 40% completo, 30% mascarado (gabarito null), 30% ausente
        var cpfDraw = _random.NextDouble();
        var cpfBase = Digits(9);
        var cpf = cpfBase + Cpf.CheckDigits(cpfBase);
        var cpfLine = cpfDraw < 0.4 ? $"CPF: {cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}" : cpfDraw < 0.7 ? $"CPF: ***.{cpf[3..6]}.{cpf[6..9]}-**" : null;
        string? delivery = _random.NextDouble() < 0.25
            ? $"AVENIDA CENTRAL, {_random.Next(100, 999)} SALA {_random.Next(1, 30)} - CENTRO - VILA MODELO/MG - CEP {Digits(5)}-{Digits(3)}"
            : null;

        var reference = _random.NextDouble() < 0.5 ? $"{Months[month - 1]}/2026" : $"{month:00}/2026";
        var rows = new List<string>
        {
            emitter,
            $"CNPJ {cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}",
            service.Title,
            $"Código do cliente {Digits(7)}   Instalação {Digits(9)}",
        };

        rows.AddRange(labelsAbove ? ["Cliente", holder] : [$"Cliente: {holder}"]);
        if (cpfLine is not null)
        {
            rows.Add(cpfLine);
        }

        rows.Add(labelsAbove ? "Endereço da unidade consumidora" : "Endereço:");
        rows.Add($"{street}, {number}{(complement is null ? "" : $" {complement}")}");
        rows.Add(neighborhood);
        rows.Add($"{city} - {uf}");
        rows.Add($"CEP {cep[..5]}-{cep[5..]}");
        if (delivery is not null)
        {
            rows.Add("Endereço para entrega da fatura");
            rows.Add(delivery);
        }

        rows.AddRange(labelsAbove
            ? ["Data de emissão", issued.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), "Referência", reference, "Vencimento", due.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)]
            : [$"Emissão: {issued:dd/MM/yyyy}   Referência: {reference}", $"Vencimento: {due:dd/MM/yyyy}"]);
        rows.Add(subtype == "telecom" ? $"{service.Consumption}  FIBRA {Pick(["300", "500", "700"])} MEGA" : $"{service.Consumption}  {_random.Next(5, 600)}");
        if (subtype != "telecom")
        {
            rows.Add($"Leitura anterior {_random.Next(1000, 9999)}   Leitura atual {_random.Next(1000, 9999)}");
        }

        rows.Add("Total a pagar");
        rows.Add($"R$ {SyntheticOcr.Pt(total)}");
        rows.Add($"{Digits(11)} {Digits(11)} {Digits(11)} {Digits(11)}");

        Gabarito gabarito = new(
            "comprovante_residencia",
            new Dictionary<string, ExpectedValue>
            {
                ["subtipo"] = Text(subtype),
                ["emissor"] = Text(emitter),
                ["cnpj_emissor"] = Text(cnpj),
                ["titular"] = Text(holder),
                ["cpf_titular"] = cpfDraw < 0.4 ? Text(cpf) : ExpectedValue.Absent,
                ["logradouro"] = Text(street),
                ["numero"] = Text(number),
                ["complemento"] = complement is null ? ExpectedValue.Absent : Text(complement),
                ["bairro"] = Text(neighborhood),
                ["cidade"] = Text(city),
                ["uf"] = Text(uf),
                ["cep"] = Text(cep),
                ["endereco_entrega"] = delivery is null ? ExpectedValue.Absent : Text(delivery),
                ["data_emissao"] = Text(issued.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ["data_vencimento"] = Text(due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ["mes_referencia"] = Text($"2026-{month:00}"),
                ["valor_total"] = new(total, null),
            },
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, ExpectedValue>>>());

        return (new OcrDocument([new OcrPage(1, SyntheticOcr.Lines(rows))]), gabarito);
    }

    private static ExpectedValue Text(string value) => new(null, value);

    private string Pick(string[] values) => values[_random.Next(values.Length)];

    private string Digits(int count) => string.Concat(Enumerable.Range(0, count).Select(_ => (char)('0' + _random.Next(10))));
}
