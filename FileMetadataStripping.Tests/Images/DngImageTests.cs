using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for DNG (Adobe Digital Negative) files.
/// DNG is TIFF-based; MagickImageInfo detects the bytes as TIFF and routes them
/// to the image strip path. Tests document this TIFF-routing behaviour.</summary>
public class DngImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_DngAsTiffBytes_IsPassthroughIsFalse()
    {
        // DNG is TIFF-based; MagickImageInfo detects the bytes as TIFF and processes them.
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticDngBytes(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_DngAsTiffBytes_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticDngBytes(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_DngAsTiffBytes_DimensionsArePreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticDngBytes(), false);
        var info = new MagickImageInfo(result.CleanFile);
        Assert.Equal(10u, info.Width);
        Assert.Equal(10u, info.Height);
    }

    [Fact]
    public void StripFileMetadata_CorruptDngBytes_DoesNotThrow()
    {
        // Minimal TIFF header (little-endian) followed by invalid IFD offset.
        var corrupt = new byte[] { 0x49, 0x49, 0x2A, 0x00, 0xFF, 0xFF, 0xFF, 0xFF };
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }
}
