using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for XPM (X PixMap) image files.
/// XPM is a fully supported RW format in Magick.NET with comment field stripping.</summary>
public class XpmImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_XpmInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateXpm(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_XpmInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateXpm(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_XpmInput_DimensionsArePreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateXpm(), false);
        var info = new MagickImageInfo(result.CleanFile);
        Assert.Equal(10u, info.Width);
        Assert.Equal(10u, info.Height);
    }

    [Fact]
    public void StripFileMetadata_CleanXpm_RemovedEntryCountIsZero()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateXpm(), false);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_XpmWithComment_CodecDoesNotPreserveCommentInRoundTrip()
    {
        // The Magick.NET XPM codec does not write image.Comment to the format's binary
        // fields. When the file is decoded for processing, the comment is absent. Both
        // ExtractedMetadata and RemovedEntryCount stay at their default values.
        // Security note: the comment is also absent from the output file — the codec never
        // wrote it, so it cannot appear in the CleanFile output.
        var input = TestHelpers.CreateXpm(img => img.Comment = "XPM injection payload");
        var result = _sut.StripFileMetadata(input, false);
        Assert.Equal("[]", result.ExtractedMetadata);
        Assert.Equal(0, result.RemovedEntryCount);
    }

    [Fact]
    public void StripFileMetadata_XpmWithComment_CommentIsRemovedFromCleanFile()
    {
        var input = TestHelpers.CreateXpm(img => img.Comment = "XPM injection payload");
        var result = _sut.StripFileMetadata(input, false);
        using var clean = new MagickImage(result.CleanFile);
        Assert.True(string.IsNullOrEmpty(clean.Comment));
    }

    [Fact]
    public void StripFileMetadata_CorruptXpmInput_DoesNotThrow()
    {
        // XPM is ASCII text format starting with "/* XPM */"
        var corrupt = System.Text.Encoding.ASCII.GetBytes("/* XPM */\nstatic char *bad[]");
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }
}
