using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for QOI (Quite OK Image Format) image files.
/// QOI has a minimal metadata spec (no EXIF/IPTC/XMP in the format itself).
/// Tests cover decodability, clean baseline, IsPassthrough, dimensions, and corrupt input.</summary>
public class QoiImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_QoiInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateQoi(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_QoiInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateQoi(), false);
        Assert.NotNull(result.CleanFile);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_QoiInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateQoi(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_QoiInput_DimensionsArePreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateQoi(), false);
        var info = new MagickImageInfo(result.CleanFile);
        Assert.Equal(10u, info.Width);
        Assert.Equal(10u, info.Height);
    }

    [Fact]
    public void StripFileMetadata_CleanQoi_RemovedEntryCountIsZero()
    {
        // QOI has no standard metadata containers; a clean QOI has nothing to strip.
        var result = _sut.StripFileMetadata(TestHelpers.CreateQoi(), false);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_CorruptQoiInput_DoesNotThrow()
    {
        // QOI magic: "qoif" (0x71 0x6F 0x69 0x66)
        var corrupt = new byte[] { 0x71, 0x6F, 0x69, 0x66, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00, 0x0A };
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }
}
