using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for ORF (Olympus RAW) files — synthetic TIFF bytes.
/// ORF is TIFF-based with Olympus-specific IFDs. The helper returns a valid TIFF;
/// MagickImageInfo detects the bytes as TIFF and routes them to the image strip path.</summary>
public class OrfImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_OrfAsTiffBytes_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticOrfBytes(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_OrfAsTiffBytes_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticOrfBytes(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_OrfAsTiffBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticOrfBytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_CorruptOrfBytes_DoesNotThrow()
    {
        var corrupt = new byte[] { 0x49, 0x49, 0x2A, 0x00, 0xFF, 0xFF, 0xFF, 0xFF };
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }
}
