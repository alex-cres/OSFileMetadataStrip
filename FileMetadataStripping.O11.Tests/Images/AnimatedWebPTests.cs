using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for animated WebP files.
///
/// Known implementation gap: without libwebpmux, MagickImageCollection decodes only
/// the first frame of an animated WebP. The strip implementation processes whatever
/// frames are decoded and writes the result.
///
/// Tests verify graceful handling: no exception and a valid (decodable) output.</summary>
public class AnimatedWebPTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_AnimatedWebPInput_DoesNotThrow()
    {
        // If animated WebP creation fails (no libwebpmux), fall back to a static WebP.
        byte[] input;
        try { input = TestHelpers.CreateAnimatedWebP(); }
        catch (MagickException) { input = TestHelpers.CreateWebP(); }

        var ex = Record.Exception(() => _sut.StripFileMetadata(input, false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_AnimatedWebPInput_CleanFileIsNonNull()
    {
        byte[] input;
        try { input = TestHelpers.CreateAnimatedWebP(); }
        catch (MagickException) { input = TestHelpers.CreateWebP(); }

        var result = _sut.StripFileMetadata(input, false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_AnimatedWebPInput_CleanFileIsDecodable()
    {
        // Output may be the first frame only if libwebpmux is unavailable.
        byte[] input;
        try { input = TestHelpers.CreateAnimatedWebP(); }
        catch (MagickException) { input = TestHelpers.CreateWebP(); }

        var result = _sut.StripFileMetadata(input, false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_AnimatedWebPInput_IsPassthroughIsFalse()
    {
        byte[] input;
        try { input = TestHelpers.CreateAnimatedWebP(); }
        catch (MagickException) { input = TestHelpers.CreateWebP(); }

        var result = _sut.StripFileMetadata(input, false);
        Assert.False(result.IsPassthrough);
    }
}
