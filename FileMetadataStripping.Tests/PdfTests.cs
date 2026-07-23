using PdfSharp.Pdf.IO;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>
/// Tests for PDF file stripping.
/// Covers: /Info fields cleared, metadata captured for audit, valid PDF output, IsPassthrough flag.
/// </summary>
public class PdfTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_PdfWithMetadata_AuthorAndTitleAreCleared()
    {
        var input = TestHelpers.CreatePdf(author: "Attacker Name", title: "Injected Title");

        var result = _sut.StripFileMetadata(input);

        using var ms = new MemoryStream(result.CleanFile);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        Assert.Equal(string.Empty, doc.Info.Author);
        Assert.Equal(string.Empty, doc.Info.Title);
    }

    [Fact]
    public void StripFileMetadata_PdfWithMetadata_ExtractedMetadataContainsAuthor()
    {
        var input = TestHelpers.CreatePdf(author: "Attacker Name");

        var result = _sut.StripFileMetadata(input);

        Assert.Contains("author", result.ExtractedMetadata);
        Assert.Contains("Attacker Name", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_PdfWithMetadata_RemovedEntryCountIsGreaterThanZero()
    {
        var input = TestHelpers.CreatePdf(author: "Attacker", title: "Injected");

        var result = _sut.StripFileMetadata(input);

        Assert.True(result.RemovedEntryCount > 0);
    }

    [Fact]
    public void StripFileMetadata_PdfWithNoUserMetadata_NoAuthorOrTitleInExtracted()
    {
        var input = TestHelpers.CreatePdf(); // no explicit author or title

        var result = _sut.StripFileMetadata(input);

        // PdfSharp auto-sets Creator/Producer; user-injected fields must be absent
        Assert.DoesNotContain("\"author\"", result.ExtractedMetadata);
        Assert.DoesNotContain("\"title\"", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_PdfOutput_IsValidPdf()
    {
        var input = TestHelpers.CreatePdf(author: "Test Author");

        var result = _sut.StripFileMetadata(input);

        // PDF magic bytes: %PDF
        Assert.Equal(0x25, result.CleanFile[0]);
        Assert.Equal(0x50, result.CleanFile[1]);
        Assert.Equal(0x44, result.CleanFile[2]);
        Assert.Equal(0x46, result.CleanFile[3]);
    }

    [Fact]
    public void StripFileMetadata_Pdf_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePdf());

        Assert.False(result.IsPassthrough);
    }

    // ── XMP catalog stream ──────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_PdfWithXmp_CatalogMetadataEntryIsRemoved()
    {
        var input = TestHelpers.CreatePdfWithXmp();

        var result = _sut.StripFileMetadata(input);

        using var ms  = new MemoryStream(result.CleanFile);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        Assert.False(doc.Internals.Catalog.Elements.ContainsKey("/Metadata"));
    }

    [Fact]
    public void StripFileMetadata_PdfWithXmp_ExtractedMetadataContainsXmpKey()
    {
        var input = TestHelpers.CreatePdfWithXmp();

        var result = _sut.StripFileMetadata(input);

        Assert.Contains("\"xmp\"", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_PdfWithXmp_RemovedEntryCountIncludesXmp()
    {
        var input = TestHelpers.CreatePdfWithXmp();

        var result = _sut.StripFileMetadata(input);

        Assert.True(result.RemovedEntryCount >= 1);
    }

    [Fact]
    public void StripFileMetadata_PdfWithXmp_OutputIsValidPdf()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePdfWithXmp());

        Assert.Equal(0x25, result.CleanFile[0]); // %PDF magic bytes
        Assert.Equal(0x50, result.CleanFile[1]);
        Assert.Equal(0x44, result.CleanFile[2]);
        Assert.Equal(0x46, result.CleanFile[3]);
    }

    [Fact]
    public void StripFileMetadata_PdfWithXmp_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePdfWithXmp());

        Assert.False(result.IsPassthrough);
    }

    // ── Encrypted / unreadable PDF ────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_EncryptedPdf_DoesNotThrow()
    {
        var input = TestHelpers.CreateCorruptedPdf();

        var ex = Record.Exception(() => _sut.StripFileMetadata(input));

        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_EncryptedPdf_ReturnsOriginalFileUnchanged()
    {
        var input = TestHelpers.CreateCorruptedPdf();

        var result = _sut.StripFileMetadata(input);

        Assert.Equal(input, result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_EncryptedPdf_ExtractedMetadataContainsProcessingError()
    {
        var input = TestHelpers.CreateCorruptedPdf();

        var result = _sut.StripFileMetadata(input);

        Assert.Contains("processingError", result.ExtractedMetadata);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.False(result.IsPassthrough);
    }
}
