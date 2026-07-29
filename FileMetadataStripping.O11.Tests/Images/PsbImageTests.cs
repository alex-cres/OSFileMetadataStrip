using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for PSB (Adobe Large Document Format).
///
/// PSB shares the Photoshop <c>8BPS</c> file header with PSD, and the version
/// field distinguishes the two:
///   - PSD: <c>0x38 0x42 0x50 0x53 0x00 0x01</c> ("8BPS" + version 1)
///   - PSB: <c>0x38 0x42 0x50 0x53 0x00 0x02</c> ("8BPS" + version 2)
///
/// PSB is designed for documents whose canvas exceeds the 30 000-pixel PSD limit.
/// It uses the same decoder / encoder as PSD, so the strip pipeline routes both
/// formats identically through Magick.NET.
///
/// Note: Magick.NET's Photoshop encoder writes <b>version 1 (PSD)</b> for images
/// small enough to fit that format, even when the caller requests
/// <c>MagickFormat.Psb</c>. This is the documented ImageMagick behaviour — PSB
/// is only necessary above the PSD size limit. The tests therefore verify only
/// that the "8BPS" prefix is preserved (bytes 0–3), not the specific version byte.
/// </summary>
public class PsbImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_PsbInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePsb(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_PsbInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePsb(), false);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_PsbInput_CleanFilePreserves8BpsPrefix()
    {
        // The "8BPS" prefix marks both PSD and PSB; the version byte at
        // offset 5 may differ from the input because Magick.NET writes
        // version 1 (PSD) for small canvases.
        var result = _sut.StripFileMetadata(TestHelpers.CreatePsb(), false);

        Assert.True(result.CleanFile.Length >= 4);
        Assert.Equal(0x38, result.CleanFile[0]); // '8'
        Assert.Equal(0x42, result.CleanFile[1]); // 'B'
        Assert.Equal(0x50, result.CleanFile[2]); // 'P'
        Assert.Equal(0x53, result.CleanFile[3]); // 'S'
    }

    [Fact]
    public void StripFileMetadata_PsbInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePsb(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_PsbInput_DimensionsArePreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePsb(), false);
        var info = new MagickImageInfo(result.CleanFile);
        Assert.Equal(10u, info.Width);
        Assert.Equal(10u, info.Height);
    }

    [Fact]
    public void StripFileMetadata_PsbWithExif_CleanFileHasNoExifProfile()
    {
        var input = TestHelpers.CreatePsb(img =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, "PSB injection payload");
            img.SetProfile(exif);
        });
        var result = _sut.StripFileMetadata(input, false);

        using var clean = new MagickImage(result.CleanFile);
        Assert.Null(clean.GetExifProfile());
    }
}
