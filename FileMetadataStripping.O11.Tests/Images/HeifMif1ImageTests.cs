using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for HEIF files with "mif1" major brand (HEIF Base Profile).
///
/// HEIF containers with the "mif1" brand trigger the IsHeifOrAvifBrand check
/// and are routed to the image strip path. Since no HEVC encode delegate is
/// available in Magick.NET-Q16, the strip attempt results in a processingError
/// (same pattern as synthetic HEIC bytes in AppleImageTests).</summary>
public class HeifMif1ImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_HeifMif1Input_IsPassthroughIsFalse()
    {
        // mif1 brand is detected as a HEIF variant → routed to image path, not passthrough.
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticHeifMif1Bytes(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_HeifMif1Input_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticHeifMif1Bytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_HeifMif1Input_ReturnsProcessingError()
    {
        // Without a valid HEVC bitstream, decode fails; the result carries a processingError note.
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticHeifMif1Bytes(), false);
        Assert.Contains("processingError", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_HeifMif1Input_OriginalFileReturnedUnchanged()
    {
        var input = TestHelpers.CreateSyntheticHeifMif1Bytes();
        var result = _sut.StripFileMetadata(input, false);
        Assert.Equal(input, result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_HeifMif1Input_RemovedEntryCountIsZero()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticHeifMif1Bytes(), false);
        Assert.Equal(0, result.RemovedEntryCount);
    }
}
