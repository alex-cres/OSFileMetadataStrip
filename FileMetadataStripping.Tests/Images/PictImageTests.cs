using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for PICT (Apple QuickDraw) files — synthetic 512-byte header + minimal opcodes.
/// PICT decode on Linux (ODC) will fail; tests verify graceful handling.</summary>
public class PictImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_PictSyntheticBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticPictBytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_PictSyntheticBytes_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticPictBytes(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_PictSyntheticBytes_RemovedEntryCountIsNonNegative()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticPictBytes(), false);
        Assert.True(result.RemovedEntryCount >= 0);
    }
}
