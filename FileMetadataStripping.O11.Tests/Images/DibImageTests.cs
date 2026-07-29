using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for DIB (Windows Device Independent Bitmap).
///
/// DIB is BMP without the 14-byte <c>BITMAPFILEHEADER</c> (no "BM" prefix). The
/// file begins with a <c>BITMAPINFOHEADER</c> whose first four bytes encode the
/// header size — typically <c>0x28 0x00 0x00 0x00</c> for the standard 40-byte
/// header.
///
/// Because DIB carries no metadata containers — no EXIF, IPTC, XMP, or comment
/// chunk — it is <b>routed to passthrough</b> by <c>IsDibFile()</c> alongside
/// BMP, WBMP, XBM, and XPM. Passthrough returns the input bytes verbatim
/// (<c>CleanFile</c> is bit-for-bit identical to <c>RawFile</c>) with
/// <c>IsPassthrough = true</c>, <c>RemovedEntryCount = 0</c>, and
/// <c>ExtractedMetadata = "[]"</c>. This avoids a wasteful re-encode round-trip
/// through Magick.NET that would provide no security benefit.
/// </summary>
public class DibImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    // ── Baseline: strip pipeline must not throw ───────────────────────────────

    [Fact]
    public void StripFileMetadata_DibInput_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateDib(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_DibInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDib(), false);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_DibInput_CleanFileIsDecodableAsImage()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDib(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    // ── Passthrough contract ──────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_DibInput_IsPassthroughIsTrue()
    {
        // IsDibFile() routes DIB to FileCategory.Passthrough — the file has no
        // metadata containers so a re-encode round-trip would be a pure no-op.
        var result = _sut.StripFileMetadata(TestHelpers.CreateDib(), false);
        Assert.True(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_DibInput_CleanFileEqualsInput()
    {
        // Passthrough returns bytes bit-for-bit identical to the input.
        var input  = TestHelpers.CreateDib();
        var result = _sut.StripFileMetadata(input, false);
        Assert.Equal(input, result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_DibInput_ExtractedMetadataIsEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDib(), false);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_DibInput_RemovedEntryCountIsZero()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDib(), false);
        Assert.Equal(0, result.RemovedEntryCount);
    }

    // ── Detection heuristic ───────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_DibInput_DimensionsArePreservedInOutput()
    {
        // Passthrough preserves the entire byte stream, so the embedded
        // BITMAPINFOHEADER (which encodes width and height) is intact.
        var result = _sut.StripFileMetadata(TestHelpers.CreateDib(), false);
        var info = new MagickImageInfo(result.CleanFile);
        Assert.Equal(10u, info.Width);
        Assert.Equal(10u, info.Height);
    }

    [Fact]
    public void StripFileMetadata_ByteStringResemblingDibHeaderSize_IsNotFalsePositive()
    {
        // 40 bytes whose first 4 bytes are 0x28 0x00 0x00 0x00 (matches the DIB
        // header-size field) but whose planes and bit-count fields are invalid.
        // The IsDibFile heuristic must reject this — the strip pipeline must not
        // throw regardless of which branch handles the (bogus) bytes.
        var bogus = new byte[40];
        bogus[0] = 0x28; // header size = 40
        bogus[12] = 0x02; // planes = 2 (invalid — must be 1)
        bogus[14] = 0x00; // bit count = 0 (invalid)
        var ex = Record.Exception(() => _sut.StripFileMetadata(bogus, false));
        Assert.Null(ex);
    }

    // ── Injected profiles are discarded before the file is even written ──────

    [Fact]
    public void StripFileMetadata_DibWithExif_CleanFileHasNoExifProfile()
    {
        // Even if the caller believes they attached an EXIF profile, the DIB
        // encoder discards it — the output must not contain an EXIF profile.
        var input = TestHelpers.CreateDib(img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, "DIB injection payload");
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        using var clean = new MagickImage(result.CleanFile);
        Assert.Null(clean.GetExifProfile());
    }

    [Fact]
    public void StripFileMetadata_DibWithComment_CleanFileHasNoComment()
    {
        var input = TestHelpers.CreateDib(img => img.Comment = "DIB comment injection");
        var result = _sut.StripFileMetadata(input, false);

        using var clean = new MagickImage(result.CleanFile);
        Assert.True(string.IsNullOrEmpty(clean.Comment));
    }
}
