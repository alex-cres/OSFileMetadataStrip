using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for extended-audio detection: AIFF / AIFC, APE (Monkey's Audio),
/// WavPack (.wv), and MPC (Musepack SV7 + SV8).
///
/// TagLibSharp handles the full parse+strip round-trip for real-world files, so
/// these tests focus on the detection contract:
/// <list type="bullet">
///   <item>The file is routed to the media pipeline (<c>IsPassthrough = false</c>).</item>
///   <item>The strip path never throws, even on synthetic input that TagLibSharp
///         may reject as unsupported / corrupt.</item>
///   <item>When TagLibSharp cannot parse the synthetic bytes, the strip path
///         returns the original file with a <c>processingError</c> audit note
///         (verified by the existing exception-handling contract on <c>StripMediaMetadata</c>).</item>
/// </list></summary>
public class ExtendedAudioDetectionTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    // ── AIFF (Audio Interchange File Format) ─────────────────────────────────

    [Fact]
    public void StripFileMetadata_AiffInput_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateAiff(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_AiffInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateAiff(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_AiffInput_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateAiff(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_AifcInput_IsPassthroughIsFalse()
    {
        // AIFC (compressed AIFF) shares the FORM container; detection must accept both trailers.
        var result = _sut.StripFileMetadata(TestHelpers.CreateAifc(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_AifcInput_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateAifc(), false));
        Assert.Null(ex);
    }

    // ── APE (Monkey's Audio) ─────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_ApeInput_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateApe(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_ApeInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateApe(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_ApeInput_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateApe(), false);
        Assert.NotNull(result.CleanFile);
    }

    // ── WavPack (.wv) ────────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_WavPackInput_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateWavPack(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_WavPackInput_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateWavPack(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_WavPackInput_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateWavPack(), false);
        Assert.NotNull(result.CleanFile);
    }

    // ── MPC (Musepack SV7 + SV8) ─────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_MpcSv8Input_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateMpcSv8(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_MpcSv8Input_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateMpcSv8(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_MpcSv7Input_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateMpcSv7(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_MpcSv7Input_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateMpcSv7(), false);
        Assert.False(result.IsPassthrough);
    }

    // ── Detection false-positive guards ──────────────────────────────────────

    [Fact]
    public void StripFileMetadata_MpPlusWithoutSv7Marker_TreatedAsPassthrough()
    {
        // "MP+" prefix without the SV7 stream-version marker (low nibble == 0x07)
        // must NOT be routed to the media pipeline — otherwise arbitrary binary
        // data starting with those three bytes would be mis-routed.
        var input = new byte[] { 0x4D, 0x50, 0x2B, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var result = _sut.StripFileMetadata(input, false);
        Assert.True(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_FormWithoutAiffTrailer_TreatedAsPassthrough()
    {
        // "FORM" without "AIFF"/"AIFC" at bytes 8-11 is an IFF-family file
        // (e.g. ILBM image, ANIM animation) that we do not currently support —
        // it must fall through to passthrough, not the media pipeline.
        var input = new byte[16];
        input[0] = 0x46; input[1] = 0x4F; input[2] = 0x52; input[3] = 0x4D; // "FORM"
        // Bytes 8-11: "ILBM" (Interleaved Bitmap — a legacy Amiga image format).
        input[8] = 0x49; input[9] = 0x4C; input[10] = 0x42; input[11] = 0x4D;
        var result = _sut.StripFileMetadata(input, false);
        Assert.True(result.IsPassthrough);
    }
}
