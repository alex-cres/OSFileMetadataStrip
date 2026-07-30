using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for JBIG image files — synthetic magic bytes (0x97 0x4A 0x42 0x32...).
/// JBIG decode requires the jbigkit delegate which may not be bundled with Magick.NET.
/// Tests verify graceful handling and no exceptions whether or not the delegate is available.</summary>
public class JbigImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_JbigSyntheticBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticJbigBytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_JbigSyntheticBytes_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticJbigBytes(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_JbigSyntheticBytes_RemovedEntryCountIsNonNegative()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticJbigBytes(), false);
        Assert.True(result.RemovedEntryCount >= 0);
    }
}
