using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for X3F (Sigma RAW) files — synthetic "FOVb" magic bytes.
/// X3F has a proprietary format; Magick.NET may detect the bytes as X3F and attempt
/// decode → fail → processingError, or pass through as unknown bytes.</summary>
public class X3fImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_X3fSyntheticBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticX3fBytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_X3fSyntheticBytes_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticX3fBytes(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_X3fSyntheticBytes_RemovedEntryCountIsNonNegative()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticX3fBytes(), false);
        Assert.True(result.RemovedEntryCount >= 0);
    }
}
