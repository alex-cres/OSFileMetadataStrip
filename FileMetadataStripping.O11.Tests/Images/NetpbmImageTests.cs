using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for Netpbm image formats: PBM (Portable Bitmap), PGM (Portable Graymap),
/// PPM (Portable Pixmap), and PNM (Portable Anymap).
/// All support a comment field (# lines in the ASCII header).</summary>
public class NetpbmImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    // ── PBM ───────────────────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_PbmInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePbm(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_PbmInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePbm(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_CleanPbm_RemovedEntryCountIsZero()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePbm(), false);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_PbmWithComment_CommentIsExtracted()
    {
        var input = TestHelpers.CreatePbm(img => img.Comment = "PBM injection payload");
        var result = _sut.StripFileMetadata(input, false);
        Assert.Contains("comment", result.ExtractedMetadata);
        Assert.Contains("PBM injection payload", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_PbmWithComment_CommentIsRemovedFromCleanFile()
    {
        var input = TestHelpers.CreatePbm(img => img.Comment = "PBM injection payload");
        var result = _sut.StripFileMetadata(input, false);
        using var clean = new MagickImage(result.CleanFile);
        Assert.True(string.IsNullOrEmpty(clean.Comment));
    }

    [Fact]
    public void StripFileMetadata_PbmWithComment_RemovedEntryCountIsGreaterThanZero()
    {
        var input = TestHelpers.CreatePbm(img => img.Comment = "PBM comment metadata");
        var result = _sut.StripFileMetadata(input, false);
        Assert.True(result.RemovedEntryCount > 0);
    }

    [Fact]
    public void StripFileMetadata_CorruptPbmInput_DoesNotThrow()
    {
        var corrupt = new byte[] { 0x50, 0x34, 0x0A, 0x00 }; // PBM magic "P4" + invalid
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }

    // ── PGM ───────────────────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_PgmInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePgm(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_PgmInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePgm(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_CleanPgm_RemovedEntryCountIsZero()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePgm(), false);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_PgmWithComment_CommentIsExtracted()
    {
        var input = TestHelpers.CreatePgm(img => img.Comment = "PGM injection payload");
        var result = _sut.StripFileMetadata(input, false);
        Assert.Contains("comment", result.ExtractedMetadata);
        Assert.Contains("PGM injection payload", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_PgmWithComment_CommentIsRemovedFromCleanFile()
    {
        var input = TestHelpers.CreatePgm(img => img.Comment = "PGM injection payload");
        var result = _sut.StripFileMetadata(input, false);
        using var clean = new MagickImage(result.CleanFile);
        Assert.True(string.IsNullOrEmpty(clean.Comment));
    }

    [Fact]
    public void StripFileMetadata_PgmWithComment_RemovedEntryCountIsGreaterThanZero()
    {
        var input = TestHelpers.CreatePgm(img => img.Comment = "PGM comment metadata");
        var result = _sut.StripFileMetadata(input, false);
        Assert.True(result.RemovedEntryCount > 0);
    }

    [Fact]
    public void StripFileMetadata_CorruptPgmInput_DoesNotThrow()
    {
        var corrupt = new byte[] { 0x50, 0x35, 0x0A, 0x00 }; // PGM magic "P5" + invalid
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }

    // ── PPM ───────────────────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_PpmInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePpm(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_PpmInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePpm(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_CleanPpm_RemovedEntryCountIsZero()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePpm(), false);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_PpmWithComment_CommentIsExtracted()
    {
        var input = TestHelpers.CreatePpm(img => img.Comment = "PPM injection payload");
        var result = _sut.StripFileMetadata(input, false);
        Assert.Contains("comment", result.ExtractedMetadata);
        Assert.Contains("PPM injection payload", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_PpmWithComment_CommentIsRemovedFromCleanFile()
    {
        var input = TestHelpers.CreatePpm(img => img.Comment = "PPM injection payload");
        var result = _sut.StripFileMetadata(input, false);
        using var clean = new MagickImage(result.CleanFile);
        Assert.True(string.IsNullOrEmpty(clean.Comment));
    }

    [Fact]
    public void StripFileMetadata_PpmWithComment_RemovedEntryCountIsGreaterThanZero()
    {
        var input = TestHelpers.CreatePpm(img => img.Comment = "PPM comment metadata");
        var result = _sut.StripFileMetadata(input, false);
        Assert.True(result.RemovedEntryCount > 0);
    }

    [Fact]
    public void StripFileMetadata_CorruptPpmInput_DoesNotThrow()
    {
        var corrupt = new byte[] { 0x50, 0x36, 0x0A, 0x00 }; // PPM magic "P6" + invalid
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }

    // ── PNM ───────────────────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_PnmInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePnm(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_PnmInput_CleanFileIsDecodable()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePnm(), false);
        var ex = Record.Exception(() => new MagickImage(result.CleanFile).Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_CleanPnm_RemovedEntryCountIsZero()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreatePnm(), false);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_CorruptPnmInput_DoesNotThrow()
    {
        var corrupt = new byte[] { 0x50, 0x37, 0x0A, 0x00 }; // PNM magic "P7" + invalid
        var ex = Record.Exception(() => _sut.StripFileMetadata(corrupt, false));
        Assert.Null(ex);
    }
}
