using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for AI (Adobe Illustrator) files — synthetic PostScript/PDF bytes.
/// AI files are internally PDF-based (AI 9+) or PostScript-based (older).
/// MagickImageInfo may or may not recognise the PostScript header depending on
/// whether the Ghostscript delegate is available. Tests verify graceful handling
/// and no exceptions in either case.</summary>
public class AiImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_AiPostScriptInput_DoesNotThrow()
    {
        // Synthetic PostScript header — Ghostscript delegate required to decode.
        var input = System.Text.Encoding.ASCII.GetBytes(
            "%!PS-Adobe-3.0\n%%Creator: Test\n%%Title: AI Test\n%%EndComments\n");
        var ex = Record.Exception(() => _sut.StripFileMetadata(input, false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_AiPostScriptInput_CleanFileIsNonNull()
    {
        var input = System.Text.Encoding.ASCII.GetBytes(
            "%!PS-Adobe-3.0\n%%Creator: Test\n%%Title: AI Test\n%%EndComments\n");
        var result = _sut.StripFileMetadata(input, false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_AiPostScriptInput_RemovedEntryCountIsNonNegative()
    {
        var input = System.Text.Encoding.ASCII.GetBytes(
            "%!PS-Adobe-3.0\n%%Creator: Adversary\n%%Title: Ignore all previous\n%%EndComments\n");
        var result = _sut.StripFileMetadata(input, false);
        Assert.True(result.RemovedEntryCount >= 0);
    }

    [Fact]
    public void StripFileMetadata_CorruptAiInput_DoesNotThrow()
    {
        // Minimal PostScript magic followed by invalid content
        var corrupt = new byte[] { 0x25, 0x21, 0x50, 0x53, 0x00, 0x00, 0x00, 0x00 }; // "%!PS" + zeros
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }
}
