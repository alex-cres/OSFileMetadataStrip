using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for MPO (Multi-picture Object) files.
/// MPO is JPEG-based (JPEG magic bytes with APP2 MPOE extension).
/// MagickImageInfo detects it as JPEG and routes it to the full JPEG strip path.</summary>
public class MpoImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_MpoAsJpegBytes_IsPassthroughIsFalse()
    {
        // MPO is JPEG-based; detected as JPEG and processed through the image strip path.
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticMpoBytes(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_MpoAsJpegBytes_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticMpoBytes(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_MpoAsJpegBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticMpoBytes(), false));
        Assert.Null(ex);
    }
}
