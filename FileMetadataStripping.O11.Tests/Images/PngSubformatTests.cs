using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for the PNG subformats — PNG8, PNG24, PNG32, PNG48, PNG64, and PNG00.
///
/// All PNG subformats share the same libpng decoder, but each requests a distinct
/// bit-depth / colour-type combination on write:
///   - PNG8   8-bit indexed with optional binary transparency
///   - PNG24  Opaque or binary-transparent 24-bit RGB
///   - PNG32  Opaque or transparent 32-bit RGBA
///   - PNG48  Opaque or binary-transparent 48-bit RGB (16 bits per channel)
///   - PNG64  Opaque or transparent 64-bit RGBA (16 bits per channel)
///   - PNG00  Inherit subformat from the source image if possible
///
/// The Q8 build of Magick.NET clamps 16-bit-per-channel writes to 8-bit samples,
/// so PNG48 / PNG64 output is functionally 24-bit / 32-bit but still a well-formed
/// PNG.  Each test verifies:
///   1. Detection — the input is not treated as passthrough.
///   2. Non-empty output — the strip pipeline produces bytes.
///   3. PNG signature preserved — the first 8 bytes remain the PNG magic.
///   4. Metadata stripping — an EXIF ImageDescription added to the source is not
///      present in the round-tripped clean output, and the injected value is
///      captured in <c>ExtractedMetadata</c>.
/// </summary>
public class PngSubformatTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    private const string InjectedDescription = "PNG subformat injection payload";

    private static void AssertPngSignature(byte[] bytes)
    {
        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        Assert.True(bytes.Length >= 8);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal(0x50, bytes[1]);
        Assert.Equal(0x4E, bytes[2]);
        Assert.Equal(0x47, bytes[3]);
        Assert.Equal(0x0D, bytes[4]);
        Assert.Equal(0x0A, bytes[5]);
        Assert.Equal(0x1A, bytes[6]);
        Assert.Equal(0x0A, bytes[7]);
    }

    // ── PNG8 (8-bit indexed) ──────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_Png8Input_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png8), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_Png8Input_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png8), false);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_Png8Input_CleanFilePreservesPngSignature()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png8), false);
        AssertPngSignature(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_Png8WithExif_CleanFileHasNoImageDescription()
    {
        var input = TestHelpers.CreatePngSubformat(MagickFormat.Png8, img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, InjectedDescription);
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        using var clean = new MagickImage(result.CleanFile);
        Assert.Null(clean.GetExifProfile());
    }

    [Fact]
    public void StripFileMetadata_Png8WithExif_ExtractedMetadataContainsInjectedValue()
    {
        var input = TestHelpers.CreatePngSubformat(MagickFormat.Png8, img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, InjectedDescription);
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains(InjectedDescription, result.ExtractedMetadata);
    }

    // ── PNG24 (24-bit RGB) ────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_Png24Input_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png24), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_Png24Input_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png24), false);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_Png24Input_CleanFilePreservesPngSignature()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png24), false);
        AssertPngSignature(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_Png24WithExif_CleanFileHasNoImageDescription()
    {
        var input = TestHelpers.CreatePngSubformat(MagickFormat.Png24, img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, InjectedDescription);
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        using var clean = new MagickImage(result.CleanFile);
        Assert.Null(clean.GetExifProfile());
    }

    [Fact]
    public void StripFileMetadata_Png24WithExif_ExtractedMetadataContainsInjectedValue()
    {
        var input = TestHelpers.CreatePngSubformat(MagickFormat.Png24, img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, InjectedDescription);
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains(InjectedDescription, result.ExtractedMetadata);
    }

    // ── PNG32 (32-bit RGBA) ───────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_Png32Input_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png32), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_Png32Input_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png32), false);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_Png32Input_CleanFilePreservesPngSignature()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png32), false);
        AssertPngSignature(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_Png32WithExif_CleanFileHasNoImageDescription()
    {
        var input = TestHelpers.CreatePngSubformat(MagickFormat.Png32, img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, InjectedDescription);
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        using var clean = new MagickImage(result.CleanFile);
        Assert.Null(clean.GetExifProfile());
    }

    [Fact]
    public void StripFileMetadata_Png32WithExif_ExtractedMetadataContainsInjectedValue()
    {
        var input = TestHelpers.CreatePngSubformat(MagickFormat.Png32, img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, InjectedDescription);
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains(InjectedDescription, result.ExtractedMetadata);
    }

    // ── PNG48 (48-bit RGB, 16 bits per channel) ───────────────────────────────

    [Fact]
    public void StripFileMetadata_Png48Input_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png48), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_Png48Input_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png48), false);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_Png48Input_CleanFilePreservesPngSignature()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png48), false);
        AssertPngSignature(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_Png48WithExif_CleanFileHasNoImageDescription()
    {
        var input = TestHelpers.CreatePngSubformat(MagickFormat.Png48, img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, InjectedDescription);
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        using var clean = new MagickImage(result.CleanFile);
        Assert.Null(clean.GetExifProfile());
    }

    [Fact]
    public void StripFileMetadata_Png48WithExif_ExtractedMetadataContainsInjectedValue()
    {
        var input = TestHelpers.CreatePngSubformat(MagickFormat.Png48, img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, InjectedDescription);
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains(InjectedDescription, result.ExtractedMetadata);
    }

    // ── PNG64 (64-bit RGBA, 16 bits per channel) ──────────────────────────────

    [Fact]
    public void StripFileMetadata_Png64Input_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png64), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_Png64Input_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png64), false);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_Png64Input_CleanFilePreservesPngSignature()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png64), false);
        AssertPngSignature(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_Png64WithExif_CleanFileHasNoImageDescription()
    {
        var input = TestHelpers.CreatePngSubformat(MagickFormat.Png64, img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, InjectedDescription);
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        using var clean = new MagickImage(result.CleanFile);
        Assert.Null(clean.GetExifProfile());
    }

    [Fact]
    public void StripFileMetadata_Png64WithExif_ExtractedMetadataContainsInjectedValue()
    {
        var input = TestHelpers.CreatePngSubformat(MagickFormat.Png64, img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, InjectedDescription);
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains(InjectedDescription, result.ExtractedMetadata);
    }

    // ── PNG00 (inherit subformat from original) ───────────────────────────────

    [Fact]
    public void StripFileMetadata_Png00Input_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png00), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_Png00Input_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png00), false);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_Png00Input_CleanFilePreservesPngSignature()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePngSubformat(MagickFormat.Png00), false);
        AssertPngSignature(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_Png00WithExif_CleanFileHasNoImageDescription()
    {
        var input = TestHelpers.CreatePngSubformat(MagickFormat.Png00, img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, InjectedDescription);
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        using var clean = new MagickImage(result.CleanFile);
        Assert.Null(clean.GetExifProfile());
    }

    [Fact]
    public void StripFileMetadata_Png00WithExif_ExtractedMetadataContainsInjectedValue()
    {
        var input = TestHelpers.CreatePngSubformat(MagickFormat.Png00, img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, InjectedDescription);
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains(InjectedDescription, result.ExtractedMetadata);
    }
}
