using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>
/// Tests for the passthrough behaviour for file formats that have no supported metadata containers
/// (plain text, CSV, JSON, XML, Markdown, and any other unrecognised format).
///
/// Passthrough contract:
/// - CleanFile   = original bytes unchanged
/// - RemovedEntryCount = 0
/// - ExtractedMetadata = "[]"
/// - IsPassthrough = true
///
/// Also verifies that IsPassthrough = false for all actively processed formats (image, PDF, OOXML).
/// </summary>
public class PassthroughTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    // â”€â”€ Plain text passthrough â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void StripFileMetadata_PlainText_IsPassthroughIsTrue()
    {
        var input = "Hello world â€” plain text file"u8.ToArray();

        var result = _sut.StripFileMetadata(input, false);

        Assert.True(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_PlainText_CleanFileEqualsInput()
    {
        var input = "Creator: attacker\nContent: some data"u8.ToArray();

        var result = _sut.StripFileMetadata(input, false);

        Assert.Equal(input, result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_PlainText_NothingRemovedAndMetadataIsEmpty()
    {
        var input = "col1,col2,col3\nval1,val2,val3"u8.ToArray(); // CSV-like

        var result = _sut.StripFileMetadata(input, false);

        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    // â”€â”€ IsPassthrough = false for active formats â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void StripFileMetadata_Image_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateJpeg(), false);

        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_Pdf_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePdf(), false);

        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_Docx_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDocx(), false);

        Assert.False(result.IsPassthrough);
    }
}
