using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Parsen.Eval;

/// <summary>Valor esperado de um campo: número (decimal exato), texto ou ausente (null).</summary>
public sealed record ExpectedValue(decimal? Number, string? Text)
{
    public static readonly ExpectedValue Absent = new(null, null);

    public bool IsAbsent => Number is null && Text is null;

    public override string ToString() => Number?.ToString(CultureInfo.InvariantCulture) ?? Text ?? "null";

    public static ExpectedValue From(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => Absent,
        JsonValueKind.Number => new ExpectedValue(value.GetDecimal(), null),
        JsonValueKind.String => new ExpectedValue(null, value.GetString()),
        _ => throw new InvalidDataException($"Valor de gabarito inválido: {value.ValueKind}."),
    };
}

/// <summary>
/// Valores corretos de um documento. Formato do arquivo *.gabarito.json:
/// { "document_type": "holerite", "fields": { "campo": valor | null }, "lists": { "itens": [ { "atributo": valor } ] } }
/// </summary>
public sealed record Gabarito(
    string DocumentType,
    IReadOnlyDictionary<string, ExpectedValue> Fields,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, ExpectedValue>>> Lists)
{
    public static Gabarito Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var fields = root.GetProperty("fields").EnumerateObject()
            .ToDictionary(p => p.Name, p => ExpectedValue.From(p.Value));

        var lists = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, ExpectedValue>>>();
        if (root.TryGetProperty("lists", out var listsElement))
        {
            foreach (var list in listsElement.EnumerateObject())
            {
                lists[list.Name] = [.. list.Value.EnumerateArray().Select(item =>
                    (IReadOnlyDictionary<string, ExpectedValue>)item.EnumerateObject().ToDictionary(p => p.Name, p => ExpectedValue.From(p.Value)))];
            }
        }

        return new Gabarito(root.GetProperty("document_type").GetString()!, fields, lists);
    }

    public string ToJson()
    {
        var fields = new JsonObject();
        foreach (var (name, value) in Fields)
        {
            fields[name] = ToNode(value);
        }

        var lists = new JsonObject();
        foreach (var (name, items) in Lists)
        {
            var array = new JsonArray();
            foreach (var item in items)
            {
                var obj = new JsonObject();
                foreach (var (attribute, value) in item)
                {
                    obj[attribute] = ToNode(value);
                }

                array.Add(obj);
            }

            lists[name] = array;
        }

        var root = new JsonObject { ["document_type"] = DocumentType, ["fields"] = fields, ["lists"] = lists };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static JsonValue? ToNode(ExpectedValue value) =>
        value.Number is { } number ? JsonValue.Create(number) : value.Text is { } text ? JsonValue.Create(text) : null;
}
