using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Time.Testing;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;
using Parsen.Infrastructure.DynamoDb;

namespace Parsen.Infrastructure.Tests.DynamoDb;

public sealed class DynamoDbExtractionRepositoryTests(DynamoDbFixture fixture) : IClassFixture<DynamoDbFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly DynamoDbExtractionRepository _repository = new(fixture.Client, fixture.Settings);

    [Fact]
    public async Task Save_WritesDetailFieldsAndListItemsInTheDocumentPartition()
    {
        var document = NewDocument();

        await _repository.SaveAsync(document, Result(itemCount: 2), CancellationToken.None);
        var items = await QueryAsync(document);

        Assert.Equal(["FIELD#cpf", "FIELD#itens#001", "FIELD#itens#002", "FIELD#salario_liquido", "META"], items.Keys);

        var detail = items["META"];
        Assert.Equal("holerite.v2", detail["SchemaId"].S);
        Assert.Equal("modelo-de-teste", detail["ModelId"].S);
        Assert.Equal(["CPF não aparece no documento"], detail["Alerts"].L.Select(a => a.S));
        Assert.Equal(document.DossierId.ToString(), detail["DossierId"].S);
        Assert.Equal("4398", detail["Usage"].M["InputTokens"].N);
        Assert.Equal("1036", detail["Usage"].M["OutputTokens"].N);
        Assert.Equal("512", detail["Usage"].M["CacheReadTokens"].N);
    }

    [Fact]
    public async Task Save_KeepsNumbersExactAndAbsentFieldsAsNull()
    {
        var document = NewDocument();

        await _repository.SaveAsync(document, Result(itemCount: 1), CancellationToken.None);
        var items = await QueryAsync(document);

        var liquido = items["FIELD#salario_liquido"];
        Assert.Equal("4870.35", liquido["Value"].N);
        Assert.Equal("0.93", liquido["Confidence"].N);
        Assert.False(liquido["Corrected"].BOOL);
        Assert.Equal("1", liquido["Evidence"].M["Page"].N);
        Assert.Equal("4.870,35", liquido["Evidence"].M["Text"].S);
        Assert.Equal(["0.6", "0.8", "0.1", "0.02"], liquido["Evidence"].M["Bbox"].L.Select(n => n.N));

        var cpf = items["FIELD#cpf"];
        Assert.True(cpf["Value"].NULL);
        Assert.False(cpf.ContainsKey("Evidence"));
    }

    [Fact]
    public async Task Save_ListItemsKeepOrderAndAttributes()
    {
        var document = NewDocument();

        await _repository.SaveAsync(document, Result(itemCount: 2), CancellationToken.None);
        var items = await QueryAsync(document);

        var second = items["FIELD#itens#002"];
        Assert.Equal("itens", second["List"].S);
        Assert.Equal("2", second["Order"].N);
        Assert.Equal("RUBRICA 2", second["Attributes"].M["descricao"].S);
        Assert.Equal(2.00m, decimal.Parse(second["Attributes"].M["valor"].N, System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(second["Attributes"].M["codigo"].NULL);
    }

    [Fact]
    public async Task SaveAgain_ReplacesAndRemovesFieldsThatDidNotComeBack()
    {
        var document = NewDocument();
        await _repository.SaveAsync(document, Result(itemCount: 3), CancellationToken.None);

        await _repository.SaveAsync(document, Result(itemCount: 1), CancellationToken.None);
        var items = await QueryAsync(document);

        Assert.Equal(["FIELD#cpf", "FIELD#itens#001", "FIELD#salario_liquido", "META"], items.Keys);
    }

    [Fact]
    public async Task Save_ManyItems_SplitsIntoBatchesOf25()
    {
        var document = NewDocument();

        await _repository.SaveAsync(document, Result(itemCount: 60), CancellationToken.None);
        var items = await QueryAsync(document);

        Assert.Equal(60, items.Keys.Count(k => k.StartsWith("FIELD#itens#", StringComparison.Ordinal)));
    }

    private static Document NewDocument() =>
        Document.Create(TenantId.From($"ten_{Guid.NewGuid():N}"), DossierId.New(), DocumentContentType.Pdf, 1_000, new FakeTimeProvider(Now));

    private static ExtractionResult Result(int itemCount) => new(
        "holerite",
        "holerite.v2",
        "modelo-de-teste",
        [
            new ExtractedField("salario_liquido", FieldValue.Number("4870.35"), new Evidence(1, "4.870,35", [0.6, 0.8, 0.1, 0.02]), 0.93),
            new ExtractedField("cpf", null, null, null),
        ],
        new Dictionary<string, IReadOnlyList<ExtractedItem>>
        {
            ["itens"] = [.. Enumerable.Range(1, itemCount).Select(i => new ExtractedItem(
                new Dictionary<string, FieldValue?>
                {
                    ["codigo"] = null,
                    ["descricao"] = FieldValue.Text($"RUBRICA {i}"),
                    ["valor"] = FieldValue.Number($"{i}.00"),
                },
                new Evidence(1, $"{i},00", null),
                0.5))],
        },
        ["CPF não aparece no documento"],
        new ModelUsage(4398, 1036, 512, 0),
        Now);

    private async Task<SortedDictionary<string, Dictionary<string, AttributeValue>>> QueryAsync(Document document)
    {
        var response = await fixture.Client.QueryAsync(new QueryRequest
        {
            TableName = fixture.Settings.TableName,
            KeyConditionExpression = "PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new AttributeValue($"TENANT#{document.TenantId}#DOC#{document.Id}"),
            },
            ConsistentRead = true,
        });

        return new SortedDictionary<string, Dictionary<string, AttributeValue>>(
            response.Items.ToDictionary(i => i["SK"].S, i => i), StringComparer.Ordinal);
    }
}
