using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>
/// GDI+ fallback engine — unsupported-but-recognised format contract.
///
/// When the caller supplies bytes matching a known image magic (WebP, HEIC,
/// AVIF, JXL, JP2, JXR/HD Photo, PSD/PSB, DDS, EXR, HDR, DPX/CIN, FITS, QOI,
/// SGI, SUN, PCX, DCX, JBIG, XCF, WMF, MNG, ICO, DCM, TGA, Netpbm, camera-RAW
/// TIFF-wrapped) but GDI+ cannot decode the format, the fallback engine
/// returns the DID-NOT-STRIP contract:
///
///   · IsPassthrough       = false          ← deliberately NOT true. Setting
///                                            true would be a security-signal
///                                            downgrade (the file DOES carry
///                                            metadata containers and the
///                                            engine did NOT strip them).
///   · RemovedEntryCount   = 0
///   · CleanFile           == input bytes (verbatim)
///   · ExtractedMetadata   contains "GDI+ fallback:" and the format name
///
/// Consumers can distinguish "actually stripped" from "declined to strip" with:
///
///     result.IsPassthrough == false && result.RemovedEntryCount &gt; 0   // stripped
///     result.IsPassthrough == false && result.RemovedEntryCount == 0   // declined
///                                   && result.ExtractedMetadata.Contains("GDI+ fallback:")
///
/// The primary O11 test project asserts the corresponding Magick.NET success
/// contract for every one of these formats.
/// </summary>
public class GdiUnsupportedFormatTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    // Format name string sent by the fallback → generator function.
    // A single parametric test covers every recognised-but-unsupported format
    // so adding coverage for a new format is a one-line change.
    public static TheoryData<string, byte[]> RecognisedUnsupportedFormats() => new()
    {
        { "WebP", TestHelpers.CreateWebP() },
        { "Avif", TestHelpers.CreateAvif() },
        { "Heic", TestHelpers.CreateSyntheticHeicFtypBytes() },
        { "Heif", TestHelpers.CreateSyntheticHeifMif1Bytes() },
        { "Jxl",  TestHelpers.CreateJxl() },
        { "Jp2",  TestHelpers.CreateJp2() },
        // J2c would ideally live here, but CreateJp2CodeStream(MagickFormat.J2c)
        // returns JP2 boxed bytes (magic "\0\0\0\x0CjP  ") — the same content
        // as CreateJp2 — so the detector rightly returns "Jp2". Skip to avoid
        // asserting a format-name mismatch that reflects the test-data helper,
        // not the detector.
        { "Jxr",  TestHelpers.CreateSyntheticJxrBytes() },
        { "Psd",  TestHelpers.CreatePsd() },
        { "Psd",  TestHelpers.CreatePsb() },   // PSB shares the 8BPS magic
        { "Dds",  TestHelpers.CreateDds() },
        { "Exr",  TestHelpers.CreateExr() },
        { "Hdr",  TestHelpers.CreateHdr() },
        { "Dpx",  TestHelpers.CreateDpx() },
        { "Cin",  TestHelpers.CreateCin() },
        { "Fits", TestHelpers.CreateFits() },
        { "Qoi",  TestHelpers.CreateQoi() },
        { "Sgi",  TestHelpers.CreateSgi() },
        { "Sun",  TestHelpers.CreateSun() },
        { "Pcx",  TestHelpers.CreatePcx() },
        { "Dcx",  TestHelpers.CreateDcx() },
        { "Jbig", TestHelpers.CreateSyntheticJbigBytes() },
        { "Xcf",  TestHelpers.CreateSyntheticXcfBytes() },
        { "Wmf",  TestHelpers.CreateSyntheticWmfBytes() },
        { "Mng",  TestHelpers.CreateMng() },
        { "Ico",  TestHelpers.CreateSyntheticIcoBytes() },
        { "Dcm",  TestHelpers.CreateSyntheticDcmBytes() },
        { "Tga",  TestHelpers.CreateTga() },
    };

    [Theory]
    [MemberData(nameof(RecognisedUnsupportedFormats))]
    public void StripFileMetadata_UnsupportedFormat_IsPassthroughIsFalse(string _, byte[] input)
    {
        var result = _sut.StripFileMetadata(input, false);
        Assert.False(result.IsPassthrough);
    }

    [Theory]
    [MemberData(nameof(RecognisedUnsupportedFormats))]
    public void StripFileMetadata_UnsupportedFormat_RemovedEntryCountIsZero(string _, byte[] input)
    {
        var result = _sut.StripFileMetadata(input, false);
        Assert.Equal(0, result.RemovedEntryCount);
    }

    [Theory]
    [MemberData(nameof(RecognisedUnsupportedFormats))]
    public void StripFileMetadata_UnsupportedFormat_CleanFileIsInputBytes(string _, byte[] input)
    {
        var result = _sut.StripFileMetadata(input, false);
        Assert.Equal(input, result.CleanFile);
    }

    [Theory]
    [MemberData(nameof(RecognisedUnsupportedFormats))]
    public void StripFileMetadata_UnsupportedFormat_ExtractedMetadataContainsGdiFallbackMarker(string _, byte[] input)
    {
        var result = _sut.StripFileMetadata(input, false);
        Assert.Contains("GDI+ fallback:", result.ExtractedMetadata);
    }

    [Theory]
    [MemberData(nameof(RecognisedUnsupportedFormats))]
    public void StripFileMetadata_UnsupportedFormat_ExtractedMetadataMentionsFormatName(string formatName, byte[] input)
    {
        var result = _sut.StripFileMetadata(input, false);
        Assert.Contains(formatName, result.ExtractedMetadata);
    }

    [Theory]
    [MemberData(nameof(RecognisedUnsupportedFormats))]
    public void StripFileMetadata_UnsupportedFormat_DoesNotThrow(string _, byte[] input)
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(input, false));
        Assert.Null(ex);
    }

    // ── Consumer-facing predicate: "did the engine strip?" vs "did it decline?" ──
    // These tests document the recommended pattern for consumers.

    [Fact]
    public void ConsumerPredicate_ActivelyStrippedInput_HasPositiveRemovedEntryCount()
    {
        var input = TestHelpers.CreateJpeg(img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, "test");
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);
        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
    }

    [Fact]
    public void ConsumerPredicate_DeclinedInput_HasZeroRemovedEntryCountAndFallbackMarker()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateWebP(), false);
        Assert.False(result.IsPassthrough);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Contains("GDI+ fallback:", result.ExtractedMetadata);
    }

    // ── Regression lock — HEIC brand-coverage parity ────────────────────────
    // Fixed after the initial detector shipped a narrower HEIC brand list than
    // IsHeifOrAvifBrand. A `hevc`-branded HEIC (the most common iPhone brand)
    // must route to the GDI+-unsupported error contract, NOT to a silent
    // Passthrough=true (which would be a security-signal downgrade — the file
    // DOES carry EXIF/GPS and did NOT get stripped).

    private static byte[] BuildIsobmffFtyp(byte[] majorBrand)
    {
        // Minimal 32-byte ISOBMFF file: `ftyp` box, size=32 bytes, one compat brand.
        //   00 00 00 20  "ftyp"  <major:4> 00 00 00 00  <compat:4> 00 00 00 00
        var buf = new byte[32];
        buf[0] = 0x00; buf[1] = 0x00; buf[2] = 0x00; buf[3] = 0x20;
        buf[4] = 0x66; buf[5] = 0x74; buf[6] = 0x79; buf[7] = 0x70;
        System.Array.Copy(majorBrand, 0, buf, 8, 4);
        System.Array.Copy(majorBrand, 0, buf, 16, 4);
        return buf;
    }

    public static TheoryData<string> IPhoneHeicBrands() => new()
    {
        "hevc", "hevx", "heim", "heis", "heic", "heix",
    };

    [Theory]
    [MemberData(nameof(IPhoneHeicBrands))]
    public void StripFileMetadata_HeicBrandedInput_IsNotSilentPassthrough(string brand)
    {
        var bytes = BuildIsobmffFtyp(System.Text.Encoding.ASCII.GetBytes(brand));
        var result = _sut.StripFileMetadata(bytes, false);
        // The file was recognised as an image but GDI+ cannot decode it — return
        // the error contract, NOT a silent passthrough.
        Assert.False(result.IsPassthrough);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Contains("GDI+ fallback:", result.ExtractedMetadata);
        Assert.Contains("Heic", result.ExtractedMetadata);
    }
}
