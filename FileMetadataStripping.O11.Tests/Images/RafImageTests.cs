using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for RAF (Fuji RAW) files — synthetic "FUJIFILMCCD-RAW " magic bytes.
/// Magick.NET may detect the bytes as RAF and attempt decode → fail → processingError,
/// or may not recognise the bytes at all → passthrough.
/// Tests verify graceful handling and no exceptions in either case.</summary>
public class RafImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_RafSyntheticBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticRafBytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_RafSyntheticBytes_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticRafBytes(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_RafSyntheticBytes_RemovedEntryCountIsNonNegative()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticRafBytes(), false);
        Assert.True(result.RemovedEntryCount >= 0);
    }
}
