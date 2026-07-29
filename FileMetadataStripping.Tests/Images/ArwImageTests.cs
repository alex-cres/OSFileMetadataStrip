using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for ARW (Sony RAW) files — synthetic TIFF bytes.
/// ARW is TIFF-based; MagickImageInfo detects the bytes as TIFF and routes them
/// to the image strip path.</summary>
public class ArwImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_ArwAsTiffBytes_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticArwBytes(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_ArwAsTiffBytes_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticArwBytes(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_ArwAsTiffBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticArwBytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_CorruptArwBytes_DoesNotThrow()
    {
        var corrupt = new byte[] { 0x49, 0x49, 0x2A, 0x00, 0xFF, 0xFF, 0xFF, 0xFF };
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }
}
