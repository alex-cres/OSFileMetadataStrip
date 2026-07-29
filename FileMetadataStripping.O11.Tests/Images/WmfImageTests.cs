using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for WMF (Windows Metafile) files — synthetic magic bytes.
/// WMF decode on Linux (ODC) will fail since libwmf is typically not bundled.
/// Tests verify graceful handling and no exceptions in either case.</summary>
public class WmfImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_WmfSyntheticBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticWmfBytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_WmfSyntheticBytes_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticWmfBytes(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_WmfSyntheticBytes_RemovedEntryCountIsNonNegative()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticWmfBytes(), false);
        Assert.True(result.RemovedEntryCount >= 0);
    }
}
