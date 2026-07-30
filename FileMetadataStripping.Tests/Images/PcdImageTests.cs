using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for PCD/PCDS (Kodak Photo CD) files — synthetic bytes with "PCD_OPA" at offset 0x800.
/// PCD decode requires specific delegate support. Tests verify graceful handling.</summary>
public class PcdImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_PcdSyntheticBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticPcdBytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_PcdSyntheticBytes_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticPcdBytes(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_PcdSyntheticBytes_RemovedEntryCountIsNonNegative()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticPcdBytes(), false);
        Assert.True(result.RemovedEntryCount >= 0);
    }
}
