using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for XCF (GIMP native format) files — synthetic "gimp xcf " magic bytes.
/// Magick.NET may detect the bytes as XCF and attempt decode → fail → processingError,
/// or pass through as an unknown format.</summary>
public class XcfImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_XcfSyntheticBytes_IsPassthroughIsFalse()
    {
        // XCF is a recognised image format; it should be routed to the image path.
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticXcfBytes(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_XcfSyntheticBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticXcfBytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_XcfSyntheticBytes_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticXcfBytes(), false);
        Assert.NotNull(result.CleanFile);
    }
}
