using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for NEF (Nikon RAW) files — synthetic TIFF bytes.
/// NEF is TIFF-based; MagickImageInfo detects the bytes as TIFF and routes them
/// to the image strip path.</summary>
public class NefImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_NefAsTiffBytes_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticNefBytes(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_NefAsTiffBytes_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticNefBytes(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_NefAsTiffBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticNefBytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_CorruptNefBytes_DoesNotThrow()
    {
        var corrupt = new byte[] { 0x49, 0x49, 0x2A, 0x00, 0xFF, 0xFF, 0xFF, 0xFF };
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }
}
