using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for ICO (Microsoft Icon) files — synthetic magic bytes 0x00 0x00 0x01 0x00.
/// ICO files may produce a JPEG fallback or processingError when decoded by Magick.NET.
/// Tests verify graceful handling and no exceptions.</summary>
public class IcoImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_IcoSyntheticBytes_IsPassthroughIsFalse()
    {
        // ICO magic is recognised; the file is routed to the image path.
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticIcoBytes(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_IcoSyntheticBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticIcoBytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_IcoSyntheticBytes_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticIcoBytes(), false);
        Assert.NotNull(result.CleanFile);
    }
}
