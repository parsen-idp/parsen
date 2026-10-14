using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Domain.Tests.Dossiers;

public class IdentifierTests
{
    [Fact]
    public void DossierId_New_HasPrefixAndUlid()
    {
        var id = DossierId.New();

        Assert.StartsWith("dos_", id.ToString(), StringComparison.Ordinal);
        Assert.Equal(4 + 26, id.ToString().Length);
    }

    [Fact]
    public void DossierId_New_IsUnique()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => DossierId.New()).ToHashSet();

        Assert.Equal(1000, ids.Count);
    }

    [Fact]
    public void DossierId_ParseRoundTrip_ReturnsSameId()
    {
        var id = DossierId.New();

        var parsed = DossierId.Parse(id.ToString());

        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("dos_")]
    [InlineData("dos_nao-e-um-ulid")]
    [InlineData("doc_01JABCDEFGHJKMNPQRSTVWXYZ0")]
    public void DossierId_TryParse_RejectsInvalidValues(string value)
    {
        Assert.False(DossierId.TryParse(value, out _));
    }

    [Fact]
    public void DocumentId_New_HasDocPrefix()
    {
        Assert.StartsWith("doc_", DocumentId.New().ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DossierId_IdsCreatedLater_SortAfterEarlierOnes()
    {
        var first = DossierId.New();
        Thread.Sleep(2);
        var second = DossierId.New();

        Assert.True(string.CompareOrdinal(first.ToString(), second.ToString()) < 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TenantId_From_RejectsBlank(string value)
    {
        Assert.Throws<ArgumentException>(() => TenantId.From(value));
    }

    [Fact]
    public void TenantId_From_KeepsValue()
    {
        Assert.Equal("ten_123", TenantId.From("ten_123").Value);
    }
}
