using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for FITS (Flexible Image Transport System) image files.
/// FITS is a fully supported RW format in Magick.NET; metadata stored as header keywords.</summary>
public class FitsImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_FitsInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateFits(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_FitsInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateFits(), false);
        Assert.NotNull(result.CleanFile);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_FitsInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateFits(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_FitsInput_DimensionsArePreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateFits(), false);
        var info = new MagickImageInfo(result.CleanFile);
        Assert.Equal(10u, info.Width);
        Assert.Equal(10u, info.Height);
    }

    [Fact]
    public void StripFileMetadata_CleanFits_RemovedEntryCountIsZero()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateFits(), false);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_FitsWithComment_CodecDoesNotPreserveCommentInRoundTrip()
    {
        // The Magick.NET FITS codec does not write image.Comment to the format's binary
        // fields. When the file is decoded for processing, the comment is absent. Both
        // ExtractedMetadata and RemovedEntryCount stay at their default values.
        // Security note: the comment is also absent from the output file — the codec never
        // wrote it, so it cannot appear in the CleanFile output.
        var input = TestHelpers.CreateFits(img => img.Comment = "FITS injection payload");
        var result = _sut.StripFileMetadata(input, false);
        Assert.Equal("[]", result.ExtractedMetadata);
        Assert.Equal(0, result.RemovedEntryCount);
    }

    [Fact]
    public void StripFileMetadata_FitsWithComment_CommentIsRemovedFromCleanFile()
    {
        var input = TestHelpers.CreateFits(img => img.Comment = "FITS injection payload");
        var result = _sut.StripFileMetadata(input, false);
        using var clean = new MagickImage(result.CleanFile);
        Assert.True(string.IsNullOrEmpty(clean.Comment));
    }

    [Fact]
    public void StripFileMetadata_CorruptFitsInput_DoesNotThrow()
    {
        // FITS magic: "SIMPLE  =" (8 chars, ASCII)
        var corrupt = System.Text.Encoding.ASCII.GetBytes("SIMPLE  =");
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }
}
