using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for UHDR (Ultra HDR) image files.
/// UHDR is JPEG-based with an embedded XMP gainmap extension.
/// MagickImageInfo detects the file as JPEG and routes it to the full JPEG strip path.
/// Tests exercise the JPEG strip path with gainmap XMP metadata.</summary>
public class UhdrImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_UhdrInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateUhdr(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_UhdrInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateUhdr(), false);
        Assert.NotNull(result.CleanFile);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_UhdrInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateUhdr(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_UhdrInput_DimensionsArePreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateUhdr(), false);
        var info = new MagickImageInfo(result.CleanFile);
        Assert.Equal(10u, info.Width);
        Assert.Equal(10u, info.Height);
    }

    [Fact]
    public void StripFileMetadata_CleanUhdrJpeg_RemovedEntryCountIsZero()
    {
        // A plain JPEG (base UHDR without XMP gainmap) should strip no metadata.
        var result = _sut.StripFileMetadata(TestHelpers.CreateJpeg(), false);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_UhdrWithXmpGainmap_XmpIsRemoved()
    {
        // UHDR embeds XMP gainmap metadata; the strip path should remove it.
        var result = _sut.StripFileMetadata(TestHelpers.CreateUhdr(), false);
        using var output = new MagickImage(result.CleanFile);
        Assert.Null(output.GetXmpProfile());
    }

    [Fact]
    public void StripFileMetadata_UhdrWithXmpGainmap_RemovedEntryCountIsGreaterThanZero()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateUhdr(), false);
        Assert.True(result.RemovedEntryCount > 0);
    }
}
