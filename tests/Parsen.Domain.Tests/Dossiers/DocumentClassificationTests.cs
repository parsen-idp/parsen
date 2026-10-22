using Microsoft.Extensions.Time.Testing;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Domain.Tests.Dossiers;

/// <summary>Classificação e split (spec, seções 5 e 14): um arquivo vira um documento ou vários filhos.</summary>
public class DocumentClassificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);

    [Fact]
    public void NewDocument_IsPendingWithoutTypeOrPages()
    {
        var document = NewDocument();

        Assert.Equal(DocumentStatus.Pending, document.Status);
        Assert.Null(document.Type);
        Assert.Null(document.Pages);
        Assert.Null(document.SourceDocumentId);
    }

    [Fact]
    public void Classify_SingleDocument_KeepsTheUploadId()
    {
        var document = NewDocument();
        var id = document.Id;

        document.Classify(DocumentType.Holerite, new PageRange(1, 2));

        Assert.Equal(id, document.Id);
        Assert.Equal(DocumentStatus.Classified, document.Status);
        Assert.Equal(DocumentType.Holerite, document.Type);
        Assert.Equal(new PageRange(1, 2), document.Pages);
    }

    [Fact]
    public void Classify_Twice_IsRefused()
    {
        var document = NewDocument();
        document.Classify(DocumentType.Holerite, new PageRange(1, 1));

        Assert.Throws<InvalidDocumentTransitionException>(() => document.Classify(DocumentType.Outro, new PageRange(1, 1)));
    }

    [Fact]
    public void Split_CreatesClassifiedChildrenPointingToTheSourceFile()
    {
        var source = NewDocument();

        var children = source.Split(
            [(DocumentType.Holerite, new PageRange(1, 1)), (DocumentType.ExtratoBancario, new PageRange(2, 4))],
            _clock);

        Assert.Equal(DocumentStatus.Split, source.Status);
        Assert.Null(source.Type);
        Assert.Equal(2, children.Count);
        Assert.All(children, c =>
        {
            Assert.NotEqual(source.Id, c.Id);
            Assert.Equal(source.Id, c.SourceDocumentId);
            Assert.Equal(source.TenantId, c.TenantId);
            Assert.Equal(source.DossierId, c.DossierId);
            Assert.Equal(source.ContentType, c.ContentType);
            Assert.Equal(DocumentStatus.Classified, c.Status);
            Assert.Equal(Now, c.CreatedAt);
        });
        Assert.Equal(DocumentType.ExtratoBancario, children[1].Type);
        Assert.Equal(new PageRange(2, 4), children[1].Pages);
    }

    [Fact]
    public void Split_WithASinglePart_IsRefused()
    {
        var source = NewDocument();

        Assert.Throws<ArgumentException>(() => source.Split([(DocumentType.Holerite, new PageRange(1, 3))], _clock));
    }

    [Fact]
    public void Split_WithOverlappingOrUnorderedParts_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => NewDocument().Split(
            [(DocumentType.Holerite, new PageRange(1, 2)), (DocumentType.ExtratoBancario, new PageRange(2, 3))], _clock));
        Assert.Throws<ArgumentException>(() => NewDocument().Split(
            [(DocumentType.Holerite, new PageRange(3, 4)), (DocumentType.ExtratoBancario, new PageRange(1, 2))], _clock));
    }

    [Fact]
    public void Split_AfterClassify_IsRefused()
    {
        var document = NewDocument();
        document.Classify(DocumentType.Holerite, new PageRange(1, 1));

        Assert.Throws<InvalidDocumentTransitionException>(() => document.Split(
            [(DocumentType.Holerite, new PageRange(1, 1)), (DocumentType.Outro, new PageRange(2, 2))], _clock));
    }

    [Fact]
    public void MarkExtracted_FromClassified()
    {
        var document = NewDocument();
        document.Classify(DocumentType.Holerite, new PageRange(1, 1));

        document.MarkExtracted();

        Assert.Equal(DocumentStatus.Extracted, document.Status);
    }

    [Fact]
    public void MarkExtracted_BeforeClassify_IsRefused()
    {
        Assert.Throws<InvalidDocumentTransitionException>(() => NewDocument().MarkExtracted());
    }

    // "outro" é guardado e sinalizado na revisão, sem extração (spec, seção 5)
    [Fact]
    public void MarkExtracted_OnUnsupportedType_IsRefused()
    {
        var document = NewDocument();
        document.Classify(DocumentType.Outro, new PageRange(1, 1));

        Assert.False(document.IsExtractable);
        Assert.Throws<InvalidDocumentTransitionException>(() => document.MarkExtracted());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    public void PageRange_Invalid_IsRefused(int first, int last)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageRange(first, last));
    }

    [Fact]
    public void PageRange_CountsAndContainsPages()
    {
        var range = new PageRange(2, 4);

        Assert.Equal(3, range.Count);
        Assert.True(range.Contains(2));
        Assert.True(range.Contains(4));
        Assert.False(range.Contains(5));
    }

    private Document NewDocument() =>
        Document.Create(TenantId.From("ten_teste"), DossierId.New(), DocumentContentType.Pdf, 1_000, _clock);
}
