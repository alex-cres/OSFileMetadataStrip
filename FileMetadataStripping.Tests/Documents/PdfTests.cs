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

        var result = _sut.StripFileMetadata(input, false);

        using var ms = new MemoryStream(result.CleanFile);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        Assert.Equal(string.Empty, doc.Info.Author);
        Assert.Equal(string.Empty, doc.Info.Title);
    }

    [Fact]
    public void StripFileMetadata_PdfWithMetadata_ExtractedMetadataContainsAuthor()
    {
        var input = TestHelpers.CreatePdf(author: "Attacker Name");

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("author", result.ExtractedMetadata);
        Assert.Contains("Attacker Name", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_PdfWithMetadata_RemovedEntryCountIsGreaterThanZero()
    {
        var input = TestHelpers.CreatePdf(author: "Attacker", title: "Injected");

        var result = _sut.StripFileMetadata(input, false);

        Assert.True(result.RemovedEntryCount > 0);
    }

    [Fact]
    public void StripFileMetadata_PdfWithNoUserMetadata_NoAuthorOrTitleInExtracted()
    {
        var input = TestHelpers.CreatePdf(); // no explicit author or title

        var result = _sut.StripFileMetadata(input, false);

        // PdfSharp auto-sets Creator/Producer; user-injected fields must be absent
        Assert.DoesNotContain("\"author\"", result.ExtractedMetadata);
        Assert.DoesNotContain("\"title\"", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_PdfOutput_IsValidPdf()
    {
        var input = TestHelpers.CreatePdf(author: "Test Author");

        var result = _sut.StripFileMetadata(input, false);

        // PDF magic bytes: %PDF
        Assert.Equal(0x25, result.CleanFile[0]);
        Assert.Equal(0x50, result.CleanFile[1]);
        Assert.Equal(0x44, result.CleanFile[2]);
        Assert.Equal(0x46, result.CleanFile[3]);
    }

    [Fact]
    public void StripFileMetadata_Pdf_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePdf(), false);

        Assert.False(result.IsPassthrough);
    }

    // ── XMP catalog stream ──────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_PdfWithXmp_CatalogMetadataEntryIsRemoved()
    {
        var input = TestHelpers.CreatePdfWithXmp();

        var result = _sut.StripFileMetadata(input, false);

        using var ms  = new MemoryStream(result.CleanFile);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        Assert.False(doc.Internals.Catalog.Elements.ContainsKey("/Metadata"));
    }

    [Fact]
    public void StripFileMetadata_PdfWithXmp_ExtractedMetadataContainsXmpKey()
    {
        var input = TestHelpers.CreatePdfWithXmp();

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("\"xmp\"", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_PdfWithXmp_RemovedEntryCountIncludesXmp()
    {
        var input = TestHelpers.CreatePdfWithXmp();

        var result = _sut.StripFileMetadata(input, false);

        Assert.True(result.RemovedEntryCount >= 1);
    }

    [Fact]
    public void StripFileMetadata_PdfWithXmp_OutputIsValidPdf()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePdfWithXmp(), false);

        Assert.Equal(0x25, result.CleanFile[0]); // %PDF magic bytes
        Assert.Equal(0x50, result.CleanFile[1]);
        Assert.Equal(0x44, result.CleanFile[2]);
        Assert.Equal(0x46, result.CleanFile[3]);
    }

    [Fact]
    public void StripFileMetadata_PdfWithXmp_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePdfWithXmp(), false);

        Assert.False(result.IsPassthrough);
    }

    // ── Encrypted / unreadable PDF ────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_EncryptedPdf_DoesNotThrow()
    {
        var input = TestHelpers.CreateCorruptedPdf();

        var ex = Record.Exception(() => _sut.StripFileMetadata(input, false));

        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_EncryptedPdf_ReturnsOriginalFileUnchanged()
    {
        var input = TestHelpers.CreateCorruptedPdf();

        var result = _sut.StripFileMetadata(input, false);

        Assert.Equal(input, result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_EncryptedPdf_ExtractedMetadataContainsProcessingError()
    {
        var input = TestHelpers.CreateCorruptedPdf();

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("processingError", result.ExtractedMetadata);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.False(result.IsPassthrough);
    }

    // ── Annotations (────────────────────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_PdfWithAnnotation_AuthorIsRemoved()
    {
        var input = TestHelpers.CreatePdfWithAnnotation(authorName: "John Doe");

        var result = _sut.StripFileMetadata(input, false);

        // Author name must not appear anywhere in the clean output bytes.
        var text = System.Text.Encoding.GetEncoding("ISO-8859-1").GetString(result.CleanFile);
        Assert.DoesNotContain("John Doe", text);
    }

    [Fact]
    public void StripFileMetadata_PdfWithAnnotation_ExtractedMetadataContainsAnnotationAuthor()
    {
        var input = TestHelpers.CreatePdfWithAnnotation(authorName: "John Doe");

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("annotationAuthors", result.ExtractedMetadata);
        Assert.Contains("John Doe", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_PdfWithAnnotation_RemovedEntryCountIncludesAnnotation()
    {
        var input = TestHelpers.CreatePdfWithAnnotation(authorName: "John Doe");

        var result = _sut.StripFileMetadata(input, false);

        Assert.True(result.RemovedEntryCount > 0);
    }

    [Fact]
    public void StripFileMetadata_PdfWithAnnotation_OutputIsValidPdf()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePdfWithAnnotation(authorName: "John Doe"), false);

        Assert.Equal(0x25, result.CleanFile[0]); // %
        Assert.Equal(0x50, result.CleanFile[1]); // P
        Assert.Equal(0x44, result.CleanFile[2]); // D
        Assert.Equal(0x46, result.CleanFile[3]); // F
    }
}
