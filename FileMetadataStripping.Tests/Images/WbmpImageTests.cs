using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for WBMP (Wireless Bitmap) image files.
/// WBMP has no metadata spec; tests are limited to clean baseline, decodability, and IsPassthrough.</summary>
public class WbmpImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_WbmpInput_IsPassthroughIsTrue()
    {
        // WBMP has no start-of-file magic bytes so it is treated as an unknown format (passthrough).
        var result = _sut.StripFileMetadata(TestHelpers.CreateWbmp(), false);
        Assert.True(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_WbmpInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateWbmp(), false);
        Assert.NotNull(result.CleanFile);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_WbmpInput_CleanFileIsByteForByteEqualToInput()
    {
        var input = TestHelpers.CreateWbmp();
        var result = _sut.StripFileMetadata(input, false);
        Assert.Equal(input, result.CleanFile);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_CleanWbmp_RemovedEntryCountIsZero()
    {
        // WBMP has no metadata containers; a clean WBMP file has nothing to strip.
        var result = _sut.StripFileMetadata(TestHelpers.CreateWbmp(), false);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }
}
