using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for JXR/WDP (JPEG XR / HD Photo) files — synthetic magic bytes 0x49 0x49 0xBC.
/// JXR is a Microsoft format; decode on Linux (ODC) may fail. Tests verify graceful handling.</summary>
public class JxrWdpImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_JxrSyntheticBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticJxrBytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_JxrSyntheticBytes_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticJxrBytes(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_JxrSyntheticBytes_RemovedEntryCountIsNonNegative()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticJxrBytes(), false);
        Assert.True(result.RemovedEntryCount >= 0);
    }
}
