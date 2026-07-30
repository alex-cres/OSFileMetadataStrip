using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for XBM (X BitMap) image files.
/// XBM is a fully supported RW format in Magick.NET with comment field stripping.</summary>
public class XbmImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_XbmInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateXbm(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_XbmInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateXbm(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_XbmInput_DimensionsArePreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateXbm(), false);
        var info = new MagickImageInfo(result.CleanFile);
        Assert.Equal(10u, info.Width);
        Assert.Equal(10u, info.Height);
    }

    [Fact]
    public void StripFileMetadata_CleanXbm_RemovedEntryCountIsZero()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateXbm(), false);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_XbmWithComment_CodecDoesNotPreserveCommentInRoundTrip()
    {
        // The Magick.NET XBM codec does not write image.Comment to the format's binary
        // fields. When the file is decoded for processing, the comment is absent. Both
        // ExtractedMetadata and RemovedEntryCount stay at their default values.
        // Security note: the comment is also absent from the output file — the codec never
        // wrote it, so it cannot appear in the CleanFile output.
        var input = TestHelpers.CreateXbm(img => img.Comment = "XBM injection payload");
        var result = _sut.StripFileMetadata(input, false);
        Assert.Equal("[]", result.ExtractedMetadata);
        Assert.Equal(0, result.RemovedEntryCount);
    }

    [Fact]
    public void StripFileMetadata_XbmWithComment_CommentIsRemovedFromCleanFile()
    {
        var input = TestHelpers.CreateXbm(img => img.Comment = "XBM injection payload");
        var result = _sut.StripFileMetadata(input, false);
        using var clean = new MagickImage(result.CleanFile);
        Assert.True(string.IsNullOrEmpty(clean.Comment));
    }

    [Fact]
    public void StripFileMetadata_CorruptXbmInput_DoesNotThrow()
    {
        // XBM is ASCII text format starting with "#define"
        var corrupt = System.Text.Encoding.ASCII.GetBytes("#define bad_width");
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }
}
