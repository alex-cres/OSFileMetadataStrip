using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for JPEG-2000 code-stream variants — J2C and J2K (round-trip),
/// and JPT (synthetic-bytes graceful-failure).
///
/// A JPEG-2000 code stream begins with the <c>SOC</c> marker <c>0xFF 0x4F</c>
/// followed by the <c>SIZ</c> marker <c>0xFF 0x51</c>, distinguishing it from
/// the JP2 file-format wrapper (which begins with the 12-byte JP2 signature box
/// <c>00 00 00 0C 6A 50 20 20 0D 0A 87 0A</c>).
///
/// The J2C, J2K, and JPT variants share the same decoder in ImageMagick.
/// Magick.NET-Q8's OpenJPEG build:
///   - decodes all three variants and identifies them via the SOC marker,
///   - re-encodes to the <b>JP2 file-format wrapper</b> on write (not back to a
///     raw code stream — the wrapper is the more portable output format),
///   - does <b>not</b> compile in a JPT encoder — attempting to write JPT throws
///     <c>MagickDelegateErrorException</c>. JPT is therefore covered by a
///     synthetic-byte test that verifies the pipeline's graceful failure path.
///
/// For J2C / J2K each variant is verified for:
///   1. Detection — the input is not treated as passthrough.
///   2. Non-empty output — the strip pipeline produces bytes.
///   3. Output identifies as a JPEG-2000 family image on re-decode.
///   4. Security invariant — no EXIF profile in the round-tripped output.
///
/// For JPT: the strip pipeline must handle a truncated code stream without
/// throwing (the decoder failure is caught as a <c>MagickException</c> and the
/// original bytes are returned with a <c>processingError</c> note).
/// </summary>
public class Jp2CodeStreamTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    // ── J2C ───────────────────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_J2cInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateJp2CodeStream(MagickFormat.J2c), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_J2cInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateJp2CodeStream(MagickFormat.J2c), false);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_J2cInput_CleanFileIsJpeg2000Family()
    {
        // Magick.NET re-encodes J2C to the JP2 file-format wrapper on write.
        // Whether the output starts with the SOC marker (0xFF 0x4F) or the JP2
        // signature box (0x00 0x00 0x00 0x0C 'jP  '), MagickImageInfo must still
        // identify it as a JPEG-2000 family image (Jp2 / J2c / J2k / Jpt).
        var result = _sut.StripFileMetadata(TestHelpers.CreateJp2CodeStream(MagickFormat.J2c), false);
        var info = new MagickImageInfo(result.CleanFile);
        Assert.True(info.Format is MagickFormat.Jp2 or MagickFormat.J2c
                              or MagickFormat.J2k or MagickFormat.Jpt,
            $"Expected JPEG-2000 family format but got {info.Format}");
    }

    [Fact]
    public void StripFileMetadata_J2cInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateJp2CodeStream(MagickFormat.J2c), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_J2cWithExif_CleanFileHasNoExifProfile()
    {
        // Security invariant: even when EXIF is attached to the source, the
        // round-tripped clean file must contain no EXIF profile.
        var input = TestHelpers.CreateJp2CodeStream(MagickFormat.J2c, img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, "J2C injection payload");
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        using var clean = new MagickImage(result.CleanFile);
        Assert.Null(clean.GetExifProfile());
    }

    // ── J2K ───────────────────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_J2kInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateJp2CodeStream(MagickFormat.J2k), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_J2kInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateJp2CodeStream(MagickFormat.J2k), false);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_J2kInput_CleanFileIsJpeg2000Family()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateJp2CodeStream(MagickFormat.J2k), false);
        var info = new MagickImageInfo(result.CleanFile);
        Assert.True(info.Format is MagickFormat.Jp2 or MagickFormat.J2c
                              or MagickFormat.J2k or MagickFormat.Jpt,
            $"Expected JPEG-2000 family format but got {info.Format}");
    }

    [Fact]
    public void StripFileMetadata_J2kInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateJp2CodeStream(MagickFormat.J2k), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_J2kWithExif_CleanFileHasNoExifProfile()
    {
        var input = TestHelpers.CreateJp2CodeStream(MagickFormat.J2k, img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, "J2K injection payload");
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        using var clean = new MagickImage(result.CleanFile);
        Assert.Null(clean.GetExifProfile());
    }

    // ── JPT (synthetic bytes — Magick.NET-Q8 lacks the JPT encoder) ───────────

    [Fact]
    public void StripFileMetadata_SyntheticJptBytes_DoesNotThrow()
    {
        // JPT starts with the same SOC marker as J2C / J2K (0xFF 0x4F 0xFF 0x51).
        // Magick.NET-Q8 cannot encode JPT (WriteJP2Image fails with
        // MagickDelegateErrorException) so a real round-trip is not possible; we
        // verify that the strip pipeline handles the truncated code stream
        // gracefully, catching MagickException and returning a processingError.
        var syntheticJpt = new byte[]
        {
            0xFF, 0x4F,             // SOC — Start of Codestream
            0xFF, 0x51,             // SIZ — image and tile size marker
            0x00, 0x00, 0x00, 0x00, // (truncated payload — decoder will fail)
        };
        var ex = Record.Exception(() => _sut.StripFileMetadata(syntheticJpt, false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_SyntheticJptBytes_CleanFileIsNonEmpty()
    {
        var syntheticJpt = new byte[]
        {
            0xFF, 0x4F, 0xFF, 0x51, 0x00, 0x00, 0x00, 0x00,
        };
        var result = _sut.StripFileMetadata(syntheticJpt, false);
        Assert.NotEmpty(result.CleanFile);
    }
}
