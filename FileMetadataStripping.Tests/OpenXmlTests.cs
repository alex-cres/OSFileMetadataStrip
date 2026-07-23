using System.IO.Packaging;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>
/// Tests for Office Open XML stripping (DOCX, XLSX, PPTX).
/// Covers: core properties cleared, metadata captured for audit, valid OOXML output, IsPassthrough flag.
/// </summary>
public class OpenXmlTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_DocxWithMetadata_CreatorIsCleared()
    {
        var input = TestHelpers.CreateDocx(creator: "Attacker", title: "Injected Title");

        var result = _sut.StripFileMetadata(input);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        Assert.Null(package.PackageProperties.Creator);
        Assert.Null(package.PackageProperties.Title);
    }

    [Fact]
    public void StripFileMetadata_DocxWithMetadata_ExtractedMetadataContainsCreator()
    {
        var input = TestHelpers.CreateDocx(creator: "Attacker Name");

        var result = _sut.StripFileMetadata(input);

        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Attacker Name", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_DocxWithMetadata_RemovedEntryCountIsGreaterThanZero()
    {
        var input = TestHelpers.CreateDocx(creator: "Attacker", title: "Injected");

        var result = _sut.StripFileMetadata(input);

        Assert.True(result.RemovedEntryCount > 0);
    }

    [Fact]
    public void StripFileMetadata_DocxOutput_IsValidOoxml()
    {
        var input = TestHelpers.CreateDocx(creator: "Test Creator");

        var result = _sut.StripFileMetadata(input);

        // OOXML is a ZIP: PK signature
        Assert.Equal(0x50, result.CleanFile[0]);
        Assert.Equal(0x4B, result.CleanFile[1]);
    }

    [Fact]
    public void StripFileMetadata_Docx_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDocx());

        Assert.False(result.IsPassthrough);
    }

    // ── Encrypted / unreadable OOXML ──────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_EncryptedDocx_DoesNotThrow()
    {
        var input = TestHelpers.CreateCorruptedDocx();

        var ex = Record.Exception(() => _sut.StripFileMetadata(input));

        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_EncryptedDocx_ReturnsOriginalFileUnchanged()
    {
        var input = TestHelpers.CreateCorruptedDocx();

        var result = _sut.StripFileMetadata(input);

        Assert.Equal(input, result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_EncryptedDocx_ExtractedMetadataContainsProcessingError()
    {
        var input = TestHelpers.CreateCorruptedDocx();

        var result = _sut.StripFileMetadata(input);

        Assert.Contains("processingError", result.ExtractedMetadata);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.False(result.IsPassthrough);
    }
}
