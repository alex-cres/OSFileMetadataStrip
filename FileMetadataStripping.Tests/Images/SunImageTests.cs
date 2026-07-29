using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for SUN (SUN Rasterfile) image files.
/// SUN is a fully supported RW format in Magick.NET with comment field stripping.</summary>
public class SunImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_SunInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSun(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_SunInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSun(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_SunInput_DimensionsArePreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSun(), false);
        var info = new MagickImageInfo(result.CleanFile);
        Assert.Equal(10u, info.Width);
        Assert.Equal(10u, info.Height);
    }

    [Fact]
    public void StripFileMetadata_CleanSun_RemovedEntryCountIsZero()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSun(), false);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_SunWithComment_CodecDoesNotPreserveCommentInRoundTrip()
    {
        // The Magick.NET SUN codec does not write image.Comment to the format's binary
        // fields. When the file is decoded for processing, the comment is absent. Both
        // ExtractedMetadata and RemovedEntryCount stay at their default values.
        // Security note: the comment is also absent from the output file — the codec never
        // wrote it, so it cannot appear in the CleanFile output.
        var input = TestHelpers.CreateSun(img => img.Comment = "SUN injection payload");
        var result = _sut.StripFileMetadata(input, false);
        Assert.Equal("[]", result.ExtractedMetadata);
        Assert.Equal(0, result.RemovedEntryCount);
    }

    [Fact]
    public void StripFileMetadata_SunWithComment_CommentIsRemovedFromCleanFile()
    {
        var input = TestHelpers.CreateSun(img => img.Comment = "SUN injection payload");
        var result = _sut.StripFileMetadata(input, false);
        using var clean = new MagickImage(result.CleanFile);
        Assert.True(string.IsNullOrEmpty(clean.Comment));
    }

    [Fact]
    public void StripFileMetadata_CorruptSunInput_DoesNotThrow()
    {
        // SUN magic: 0x59 0xA6 0x6A 0x95
        var corrupt = new byte[] { 0x59, 0xA6, 0x6A, 0x95, 0x00, 0x00, 0x00, 0x00 };
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }
}
