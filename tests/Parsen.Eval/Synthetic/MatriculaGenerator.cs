using System.Globalization;
using Parsen.Application.Pipeline;
using Parsen.Domain.Identifiers;

namespace Parsen.Eval.Synthetic;

/// <summary>
/// Certidões de matrícula sintéticas no formato do OCR, com gabarito (cabeçalho, atos e partes). Variam o que muda
/// nas matrículas reais: apartamento ou terreno, proprietário citado na abertura (fora de "partes"), compra e venda,
/// alienação fiduciária com ou sem cancelamento, penhora, construção, numeração "R.1/45.678" ou "AV-3/45.678", datas
/// por extenso ou numéricas e fichas seguintes com ou sem cabeçalho. Cada parte fica numa linha só, para que o nome
/// seja localizável. Nenhum dado real. Determinístico pela semente.
/// </summary>
public sealed class MatriculaGenerator(int seed)
{
    private static readonly string[] Names = ["ANA LIMA SANTOS", "BRUNO DIAS GOMES", "CARLA MARTINS VIEIRA", "DIEGO RIBEIRO ALMEIDA", "ELISA NOGUEIRA PEREIRA", "FABIO TEIXEIRA LIMA"];
    private static readonly string[] Banks = ["BANCO EXEMPLO S.A.", "BANCO FICTICIO DO BRASIL S.A."];
    private static readonly string[] Streets = ["RUA DAS FLORES", "AVENIDA BRASIL", "RUA SETE DE SETEMBRO", "ALAMEDA DOS ANJOS"];
    private static readonly string[] Neighborhoods = ["JARDIM EXEMPLO", "CENTRO", "VILA NOVA"];
    private static readonly string[] Months = ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];

    private readonly Random _random = new(seed);
    private int _generated;

    public (OcrDocument Ocr, Gabarito Gabarito) Generate()
    {
        var apartment = _generated++ % 2 == 0;
        var number = $"{_random.Next(10, 200)}.{_random.Next(100, 999)}";
        var circ = _random.Next(1, 9);
        var cartorio = $"REGISTRO DE IMÓVEIS DA {circ}ª CIRCUNSCRIÇÃO DE EXEMPLÓPOLIS";
        var street = Pick(Streets);
        var streetNumber = _random.Next(10, 2000).ToString(CultureInfo.InvariantCulture);
        var neighborhood = Pick(Neighborhoods);
        var unit = apartment ? $"APTO {_random.Next(11, 180)}" : null;
        var privateArea = apartment ? _random.Next(40_00, 140_00) / 100m : (decimal?)null;
        var totalArea = apartment ? privateArea!.Value + (_random.Next(10_00, 40_00) / 100m) : _random.Next(200_00, 600_00) / 100m;
        var registration = $"{_random.Next(100, 999)}.{_random.Next(100, 999)}.{_random.Next(1000, 9999)}";
        var avStyle = _random.NextDouble() < 0.5;
        var writtenDates = _random.NextDouble() < 0.5;

        var rows = new List<string>
        {
            cartorio,
            "COMARCA DE EXEMPLÓPOLIS - SP",
            "LIVRO Nº 2 - REGISTRO GERAL",
            $"MATRÍCULA {number}    FICHA 01",
            apartment
                ? $"IMÓVEL: APARTAMENTO {unit}, localizado no {_random.Next(1, 20)}º andar do Edifício Modelo, situado na"
                : "IMÓVEL: TERRENO sem benfeitorias, constituído pelo lote 12 da quadra B, situado na",
            $"{street}, nº {streetNumber}, bairro {neighborhood}, nesta cidade de EXEMPLÓPOLIS/SP,",
            apartment
                ? $"com área privativa de {SyntheticOcr.Pt(privateArea!.Value)} m² e área total de {SyntheticOcr.Pt(totalArea)} m²."
                : $"com área total de {SyntheticOcr.Pt(totalArea)} m².",
            $"Inscrição municipal {registration}.",
        };

        // Proprietário da abertura: não pertence a nenhum ato (fica fora de "partes"), reaparece como transmitente no R.1
        var opener = Pick(Names);
        var openerCpf = Cpf();
        rows.Add($"PROPRIETÁRIO: {opener}, CPF {FormatCpf(openerCpf)}.");
        rows.Add($"REGISTRO ANTERIOR: matrícula {_random.Next(1000, 9999)} deste cartório.");

        var acts = new List<(string Number, string Type, DateOnly Date, string? Cancels, decimal? Value)>();
        var parties = new List<(string Act, string Role, string Name, string? TaxId)>();
        var date = new DateOnly(_random.Next(2005, 2015), _random.Next(1, 13), _random.Next(1, 28));
        var owners = new List<(string Name, string Cpf)> { (opener, openerCpf) };
        var seq = 0;

        string Act(string kind) => $"{kind}.{++seq}";

        // R.1: compra e venda (sempre)
        var buyers = Enumerable.Range(0, _random.Next(1, 3)).Select(_ => (Name: Pick(Names), Cpf: Cpf())).DistinctBy(b => b.Name).Where(b => b.Name != opener).ToList();
        if (buyers.Count == 0)
        {
            buyers = [("GABRIELA FERREIRA DIAS", Cpf())];
        }

        AddTransfer(Act("R"), "compra_venda", "COMPRA E VENDA", owners, buyers);
        owners = buyers;

        // R.n: alienação fiduciária, cancelada ou não por uma averbação seguinte
        if (_random.NextDouble() < 0.7)
        {
            var bank = Pick(Banks);
            var bankCnpjBase = Digits(8) + "0001";
            var bankCnpj = bankCnpjBase + Cnpj.CheckDigits(bankCnpjBase);
            var lien = Act("R");
            date = date.AddDays(_random.Next(5, 60));
            var debt = _random.Next(100_000, 600_000);
            acts.Add((lien, "alienacao_fiduciaria", date, null, debt));
            rows.Add($"{ActLabel(lien)} - {DateText(date)}. ALIENAÇÃO FIDUCIÁRIA. Por instrumento particular, os proprietários");
            foreach (var owner in owners)
            {
                rows.Add($"{owner.Name}, CPF {FormatCpf(owner.Cpf)},");
                parties.Add((lien, "devedor", owner.Name, owner.Cpf));
            }

            rows.Add("alienaram fiduciariamente o imóvel ao credor");
            rows.Add($"{bank}, CNPJ {FormatCnpj(bankCnpj)},");
            parties.Add((lien, "credor", bank, bankCnpj));
            rows.Add($"em garantia da dívida de R$ {SyntheticOcr.Pt(debt)}.");

            if (_random.NextDouble() < 0.6)
            {
                var cancel = Act("Av");
                date = date.AddDays(_random.Next(365, 2000));
                acts.Add((cancel, "cancelamento", date, lien, null));
                rows.Add($"{ActLabel(cancel)} - {DateText(date)}. CANCELAMENTO. Fica cancelada a alienação fiduciária registrada sob o");
                rows.Add($"{ActLabel(lien).Split('/')[0].Replace("-", ".", StringComparison.Ordinal)}, por autorização do credor, nos termos do instrumento particular.");
            }
        }

        // Av.n: construção (só no terreno)
        if (!apartment && _random.NextDouble() < 0.6)
        {
            var build = Act("Av");
            date = date.AddDays(_random.Next(100, 800));
            acts.Add((build, "construcao", date, null, null));
            rows.Add($"{ActLabel(build)} - {DateText(date)}. CONSTRUÇÃO. Averba-se a construção de uma casa residencial com");
            rows.Add($"{SyntheticOcr.Pt(_random.Next(80_00, 200_00) / 100m)} m², conforme habite-se da prefeitura.");
        }

        // R.n: penhora
        if (_random.NextDouble() < 0.4)
        {
            var levy = Act("R");
            date = date.AddDays(_random.Next(30, 400));
            var amount = _random.Next(10_000, 90_000);
            acts.Add((levy, "penhora", date, null, amount));
            rows.Add($"{ActLabel(levy)} - {DateText(date)}. PENHORA. Por mandado judicial extraído de execução movida por");
            rows.Add("CONDOMÍNIO EDIFÍCIO MODELO,");
            parties.Add((levy, "credor", "CONDOMÍNIO EDIFÍCIO MODELO", null));
            rows.Add("contra");
            var debtor = owners[0];
            rows.Add($"{debtor.Name}, CPF {FormatCpf(debtor.Cpf)},");
            parties.Add((levy, "devedor", debtor.Name, debtor.Cpf));
            rows.Add($"penhorou-se o imóvel para garantia do valor de R$ {SyntheticOcr.Pt(amount)}.");
        }

        // R.n: nova compra e venda
        if (_random.NextDouble() < 0.4)
        {
            var newBuyers = new List<(string Name, string Cpf)> { (Names.First(n => n != opener && owners.All(o => o.Name != n)), Cpf()) };
            AddTransfer(Act("R"), "compra_venda", "COMPRA E VENDA", owners, newBuyers);
        }

        var issued = new DateOnly(2026, _random.Next(1, 10), _random.Next(1, 28));
        rows.Add($"CERTIFICO que a presente é reprodução autêntica da matrícula {number}.");
        rows.Add($"EXEMPLÓPOLIS, {issued.Day} de {Months[issued.Month - 1]} de {issued.Year}.");

        var pages = Paginate(rows, number);
        var gabarito = new Gabarito(
            "matricula_imovel",
            new Dictionary<string, ExpectedValue>
            {
                ["numero_matricula"] = Text(number),
                ["cartorio"] = Text(cartorio),
                ["comarca"] = Text("EXEMPLÓPOLIS"),
                ["data_certidao"] = Text(Iso(issued)),
                ["inscricao_municipal"] = Text(registration),
                ["tipo_imovel"] = Text(apartment ? "APARTAMENTO" : "TERRENO"),
                ["logradouro"] = Text(street),
                ["numero"] = Text(streetNumber),
                ["complemento"] = unit is null ? ExpectedValue.Absent : Text(unit),
                ["bairro"] = Text(neighborhood),
                ["cidade"] = Text("EXEMPLÓPOLIS"),
                ["uf"] = Text("SP"),
                ["area_privativa"] = privateArea is { } area ? new(area, null) : ExpectedValue.Absent,
                ["area_total"] = new(totalArea, null),
            },
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, ExpectedValue>>>
            {
                ["atos"] = [.. acts.Select(a => (IReadOnlyDictionary<string, ExpectedValue>)new Dictionary<string, ExpectedValue>
                {
                    ["numero"] = Text(a.Number),
                    ["tipo"] = Text(a.Type),
                    ["data"] = Text(Iso(a.Date)),
                    ["cancela"] = a.Cancels is null ? ExpectedValue.Absent : Text(a.Cancels),
                    ["valor"] = a.Value is { } value ? new(value, null) : ExpectedValue.Absent,
                })],
                ["partes"] = [.. parties.Select(p => (IReadOnlyDictionary<string, ExpectedValue>)new Dictionary<string, ExpectedValue>
                {
                    ["ato"] = Text(p.Act),
                    ["papel"] = Text(p.Role),
                    ["nome"] = Text(p.Name),
                    ["cpf_cnpj"] = p.TaxId is null ? ExpectedValue.Absent : Text(p.TaxId),
                })],
            });

        return (new OcrDocument(pages), gabarito);

        void AddTransfer(string act, string type, string title, List<(string Name, string Cpf)> from, List<(string Name, string Cpf)> to)
        {
            date = date.AddDays(_random.Next(30, 900));
            var price = _random.Next(150_000, 900_000);
            acts.Add((act, type, date, null, price));
            rows.Add($"{ActLabel(act)} - {DateText(date)}. {title}. Por escritura pública lavrada no 1º Tabelião de Notas local,");
            rows.Add(from.Count > 1 ? "os proprietários" : "o proprietário");
            foreach (var seller in from)
            {
                rows.Add($"{seller.Name}, CPF {FormatCpf(seller.Cpf)},");
                parties.Add((act, "transmitente", seller.Name, seller.Cpf));
            }

            rows.Add(from.Count > 1 ? "venderam o imóvel a" : "vendeu o imóvel a");
            foreach (var buyer in to)
            {
                rows.Add($"{buyer.Name}, CPF {FormatCpf(buyer.Cpf)},");
                parties.Add((act, "adquirente", buyer.Name, buyer.Cpf));
            }

            rows.Add($"pelo valor de R$ {SyntheticOcr.Pt(price)}.");
        }

        string ActLabel(string act) =>
            act.StartsWith("Av", StringComparison.Ordinal) && avStyle ? $"AV-{act[3..]}/{number}" : $"{act}/{number}";

        string DateText(DateOnly d) => writtenDates
            ? $"Em {d.Day} de {Months[d.Month - 1]} de {d.Year}"
            : $"Data: {d:dd/MM/yyyy}";
    }

    // Fichas de ~22 linhas; metade das seguintes repete o número da matrícula no topo
    private List<OcrPage> Paginate(List<string> rows, string number)
    {
        var pages = new List<OcrPage>();
        var repeatHeader = _random.NextDouble() < 0.5;
        for (var start = 0; start < rows.Count; start += 22)
        {
            var chunk = rows.Skip(start).Take(22).ToList();
            if (start > 0 && repeatHeader)
            {
                chunk.Insert(0, $"MATRÍCULA {number}    FICHA {pages.Count + 1:00}");
            }

            pages.Add(new OcrPage(pages.Count + 1, SyntheticOcr.Lines(chunk)));
        }

        return pages;
    }

    private static ExpectedValue Text(string value) => new(null, value);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private string Cpf()
    {
        var digits = Digits(9);
        return digits + Parsen.Domain.Identifiers.Cpf.CheckDigits(digits);
    }

    private static string FormatCpf(string cpf) => $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}";

    private static string FormatCnpj(string cnpj) => $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}";

    private string Pick(string[] values) => values[_random.Next(values.Length)];

    private string Digits(int count) => string.Concat(Enumerable.Range(0, count).Select(_ => (char)('0' + _random.Next(10))));
}
