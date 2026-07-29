using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for DPX (SMPTE 268M) and CIN (Kodak Cineon) image files.
///
/// <c>dpx:*</c> and <c>cin:*</c> per-image attributes are stripped explicitly after
/// the raster Strip() call, so no production metadata (file.filename, film.id,
/// origination.device, …) reaches the output.</summary>
public class DpxCinImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    // ── DPX ───────────────────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_DpxInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDpx(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_DpxInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDpx(), false);
        Assert.NotNull(result.CleanFile);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_DpxInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDpx(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_DpxInput_DimensionsArePreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDpx(), false);
        var info = new MagickImageInfo(result.CleanFile);
        Assert.Equal(10u, info.Width);
        Assert.Equal(10u, info.Height);
    }

    [Fact]
    public void StripFileMetadata_CleanDpx_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateDpx(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_CorruptDpxInput_DoesNotThrow()
    {
        // DPX magic: 0x53 0x44 0x50 0x58 (big-endian) or 0x58 0x50 0x44 0x53 (little-endian)
        var corrupt = new byte[] { 0x53, 0x44, 0x50, 0x58, 0x00, 0x00, 0x00, 0x00 };
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }

    // ── CIN ───────────────────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_CinInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateCin(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_CinInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateCin(), false);
        Assert.NotNull(result.CleanFile);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_CinInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateCin(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_CinInput_DimensionsArePreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateCin(), false);
        var info = new MagickImageInfo(result.CleanFile);
        Assert.Equal(10u, info.Width);
        Assert.Equal(10u, info.Height);
    }

    [Fact]
    public void StripFileMetadata_CleanCin_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateCin(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_CorruptCinInput_DoesNotThrow()
    {
        // CIN magic: 0x80 0x2A 0x5F 0xD7
        var corrupt = new byte[] { 0x80, 0x2A, 0x5F, 0xD7, 0x00, 0x00, 0x00, 0x00 };
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }

    // ── Namespaced-attribute strip (dpx:*, cin:*) ─────────────────────────────

    [Fact]
    public void StripFileMetadata_DpxWithFilenameAttribute_AttributeIsStrippedFromOutput()
    {
        // A dpx:* attribute set on the source image must not survive to the output.
        var input = TestHelpers.CreateDpx(img =>
            img.SetAttribute("dpx:file.filename", "/private/user1/secret.dpx"));
        var result = _sut.StripFileMetadata(input, false);

        using var clean = new MagickImage(result.CleanFile);
        var value = clean.GetAttribute("dpx:file.filename");
        Assert.True(string.IsNullOrEmpty(value));
    }

    [Fact]
    public void StripFileMetadata_DpxWithFilenameAttribute_ValueIsRecordedInExtractedMetadata()
    {
        var input = TestHelpers.CreateDpx(img =>
            img.SetAttribute("dpx:file.filename", "/private/user1/secret.dpx"));
        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("secret.dpx", result.ExtractedMetadata);
        Assert.True(result.RemovedEntryCount >= 1);
    }

    [Fact]
    public void StripFileMetadata_CinWithFilenameAttribute_AttributeIsStrippedFromOutput()
    {
        // ImageMagick's CIN decoder exposes header metadata under the dpx:* prefix
        // (a shared production-metadata namespace between DPX and CIN), so the strip
        // path — which removes both prefixes — must clear it on the output.
        var input = TestHelpers.CreateCin(img =>
            img.SetAttribute("dpx:file.filename", "/private/user2/master.cin"));
        var result = _sut.StripFileMetadata(input, false);

        using var clean = new MagickImage(result.CleanFile);
        var value = clean.GetAttribute("dpx:file.filename");
        Assert.True(string.IsNullOrEmpty(value));
    }

    [Fact]
    public void StripFileMetadata_CinWithFilenameAttribute_ValueIsRecordedInExtractedMetadata()
    {
        var input = TestHelpers.CreateCin(img =>
            img.SetAttribute("dpx:file.filename", "/private/user2/master.cin"));
        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("master.cin", result.ExtractedMetadata);
        Assert.True(result.RemovedEntryCount >= 1);
    }
}
