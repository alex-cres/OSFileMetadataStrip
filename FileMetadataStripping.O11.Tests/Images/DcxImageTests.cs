using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for DCX (ZSoft multi-page Paintbrush).
///
/// DCX is a container format that wraps one or more PCX images. It starts with a
/// 4-byte magic signature:
///
///   <c>0xB1 0x68 0xDE 0x3A</c>  (little-endian 32-bit value <c>0x3ADE68B1</c>)
///
/// followed by up to 1023 uint32 offsets that point at the individual PCX pages.
/// Because DCX uses the PCX decoder for each embedded page, it shares the same
/// metadata surface — PCX comments and image attributes are stripped through the
/// standard image pipeline.
/// </summary>
public class DcxImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    private const string InjectedComment = "DCX injection payload";

    [Fact]
    public void StripFileMetadata_DcxInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDcx(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_DcxInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDcx(), false);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_DcxInput_CleanFilePreservesDcxMagic()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDcx(), false);

        Assert.True(result.CleanFile.Length >= 4);
        // DCX magic: 0xB1 0x68 0xDE 0x3A
        Assert.Equal(0xB1, result.CleanFile[0]);
        Assert.Equal(0x68, result.CleanFile[1]);
        Assert.Equal(0xDE, result.CleanFile[2]);
        Assert.Equal(0x3A, result.CleanFile[3]);
    }

    [Fact]
    public void StripFileMetadata_DcxInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDcx(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_DcxInput_AllFramesArePreserved()
    {
        // The input was constructed with three frames; the round-trip must
        // preserve every one of them.
        var input  = TestHelpers.CreateDcx(frameCount: 3);
        var result = _sut.StripFileMetadata(input, false);

        using var frames = new MagickImageCollection(result.CleanFile);
        Assert.Equal(3, frames.Count);
    }

    [Fact]
    public void StripFileMetadata_CorruptDcxInput_DoesNotThrow()
    {
        // DCX magic in the first four bytes but nothing else — the strip
        // pipeline must catch any decoder failure gracefully.
        var corrupt = new byte[] { 0xB1, 0x68, 0xDE, 0x3A, 0x00, 0x00, 0x00, 0x00 };
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }
}
