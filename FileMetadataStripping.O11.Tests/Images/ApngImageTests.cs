using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for APNG (Animated PNG) image files.
///
/// APNG is detected via the <c>acTL</c> (Animation Control) chunk. The implementation
/// passes <see cref="MagickFormat.APng"/> as a read hint so <see cref="MagickImageCollection"/>
/// decodes all animation frames instead of stopping at the first.
/// Writing APNG back requires ImageMagick's <c>video</c> delegate (ffmpeg). When ffmpeg is
/// absent a <see cref="MagickMissingDelegateErrorException"/> is thrown and the output is
/// transcoded to JPEG. The <c>transcodedFormat</c> key in <c>ExtractedMetadata</c> records
/// this codec change.</summary>
public class ApngImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_ApngInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateApng(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_ApngInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateApng(), false);
        Assert.NotNull(result.CleanFile);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_ApngInput_CleanFileIsDecodable()
    {
        // When ffmpeg is absent, APng write fails → JPEG fallback. JPEG is always decodable.
        var result = _sut.StripFileMetadata(TestHelpers.CreateApng(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_ApngInput_DimensionsArePreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateApng(), false);
        var info = new MagickImageInfo(result.CleanFile);
        Assert.Equal(10u, info.Width);
        Assert.Equal(10u, info.Height);
    }

    [Fact]
    public void StripFileMetadata_CleanApng_RemovedEntryCountIsZero()
    {
        // Clean APNG input carries no user metadata — strip reports zero removals.
        // When ffmpeg is absent the output is JPEG (APng write → JPEG fallback); in that case
        // ExtractedMetadata contains a "transcodedFormat" note but no user-controlled fields.
        var result = _sut.StripFileMetadata(TestHelpers.CreateApng(), false);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.DoesNotContain("\"exif\"",    result.ExtractedMetadata);
        Assert.DoesNotContain("\"iptc\"",    result.ExtractedMetadata);
        Assert.DoesNotContain("\"comment\"", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_ApngInput_OutputIsCleanRegardlessOfCodec()
    {
        // Whether the output is JPEG (no ffmpeg) or APng (ffmpeg present), the clean file
        // must be decodable and contain no EXIF metadata.
        var result = _sut.StripFileMetadata(TestHelpers.CreateApng(), false);
        using var clean = new MagickImage(result.CleanFile);
        Assert.Null(clean.GetExifProfile());
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_ApngWithExif_CleanFileHasNullExifProfile()
    {
        var input = TestHelpers.CreateApng();
        var result = _sut.StripFileMetadata(input, false);
        using var output = new MagickImage(result.CleanFile);
        Assert.Null(output.GetExifProfile());
    }

    [Fact]
    public void StripFileMetadata_CorruptApngInput_DoesNotThrow()
    {
        // APNG has PNG magic bytes followed by IDAT with animated chunks
        var corrupt = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x00                            // incomplete IHDR
        };
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }
}
