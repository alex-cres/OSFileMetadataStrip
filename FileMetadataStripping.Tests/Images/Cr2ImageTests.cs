using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for CR2/CRW (Canon RAW) files — synthetic TIFF bytes.
/// CR2 is TIFF-based; the helper returns a valid TIFF since MagickImageInfo
/// detects the bytes as TIFF and routes them to the image strip path.
/// The full Canon CR2 decode path requires the actual Canon codec.</summary>
public class Cr2ImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_Cr2AsTiffBytes_IsPassthroughIsFalse()
    {
        // TIFF-based bytes are detected as image format → processed, not passed through.
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticCr2Bytes(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_Cr2AsTiffBytes_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticCr2Bytes(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_Cr2AsTiffBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticCr2Bytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_CorruptCr2Bytes_DoesNotThrow()
    {
        // Minimal TIFF (LE) header with invalid IFD.
        var corrupt = new byte[] { 0x49, 0x49, 0x2A, 0x00, 0xFF, 0xFF, 0xFF, 0xFF };
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }
}
