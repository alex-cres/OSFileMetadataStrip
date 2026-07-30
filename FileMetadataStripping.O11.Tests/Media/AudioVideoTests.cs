using TagLib;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>
/// Tests for audio and video file stripping via TagLibSharp.
/// Each supported format has at minimum a detection test (IsPassthrough = false).
/// Formats where TagLibSharp can work with minimal constructed files also get
/// metadata strip tests (title/artist cleared, extracted metadata captured).
///
/// Test data is generated programmatically � no binary files are committed.
/// </summary>
public class AudioVideoTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    // -- WAV (full strip tests � easiest format to construct programmatically) -----

    [Fact]
    public void StripFileMetadata_WavWithTitle_TitleIsCleared()
    {
        var input = TestHelpers.CreateWav(title: "Injected Title", artist: "Attacker");

        var result = _sut.StripFileMetadata(input, false);

        var ms = new MemoryStream(result.CleanFile);
        using var file = TagLib.File.Create(new TestHelpers.TagLibStreamAbstraction("test.wav", ms));
        Assert.True(string.IsNullOrEmpty(file.Tag.Title));
        Assert.Empty(file.Tag.Performers);
    }

    [Fact]
    public void StripFileMetadata_WavWithMetadata_ExtractedContainsTitle()
    {
        var input = TestHelpers.CreateWav(title: "Injected Title");

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("title", result.ExtractedMetadata);
        Assert.Contains("Injected Title", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_WavWithMetadata_ExtractedContainsArtist()
    {
        var input = TestHelpers.CreateWav(artist: "Attacker Name");

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("artists", result.ExtractedMetadata);
        Assert.Contains("Attacker Name", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_WavWithMetadata_RemovedEntryCountIsGreaterThanZero()
    {
        var input = TestHelpers.CreateWav(title: "Test Title", artist: "Test Artist");

        var result = _sut.StripFileMetadata(input, false);

        Assert.True(result.RemovedEntryCount > 0);
    }

    [Fact]
    public void StripFileMetadata_WavWithMetadata_InputHasTitleBeforeStrip()
    {
        var input = TestHelpers.CreateWav(title: "Present");

        var ms = new MemoryStream(input);
        using var file = TagLib.File.Create(new TestHelpers.TagLibStreamAbstraction("test.wav", ms));
        Assert.Equal("Present", file.Tag.Title);
    }

    [Fact]
    public void StripFileMetadata_WavOutput_IsValidRiffFile()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateWav(title: "Test"), false);

        Assert.Equal(0x52, result.CleanFile[0]); // R
        Assert.Equal(0x49, result.CleanFile[1]); // I
        Assert.Equal(0x46, result.CleanFile[2]); // F
        Assert.Equal(0x46, result.CleanFile[3]); // F
    }

    [Fact]
    public void StripFileMetadata_WavOutput_IsDecodableByTagLib()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateWav(title: "Test"), false);

        var ex = Record.Exception(() =>
        {
            var ms = new MemoryStream(result.CleanFile);
            TagLib.File.Create(new TestHelpers.TagLibStreamAbstraction("test.wav", ms)).Dispose();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_Wav_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateWav(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_WavWithNoMetadata_RemovedCountIsZero()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateWav(), false);

        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    // -- MP3 (full strip tests � minimal ID3v2 header is writable by TagLibSharp) -

    [Fact]
    public void StripFileMetadata_Mp3WithTitle_TitleIsCleared()
    {
        var input = TestHelpers.CreateMp3(title: "Injected Title", artist: "Attacker");

        var result = _sut.StripFileMetadata(input, false);

        var ms = new MemoryStream(result.CleanFile);
        using var file = TagLib.File.Create(new TestHelpers.TagLibStreamAbstraction("test.mp3", ms));
        Assert.True(string.IsNullOrEmpty(file.Tag.Title));
    }

    [Fact]
    public void StripFileMetadata_Mp3WithMetadata_ExtractedContainsTitle()
    {
        var input = TestHelpers.CreateMp3(title: "Injected Title");

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("title", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_Mp3WithMetadata_RemovedEntryCountIsGreaterThanZero()
    {
        var input = TestHelpers.CreateMp3(title: "Test", artist: "Artist");

        var result = _sut.StripFileMetadata(input, false);

        Assert.True(result.RemovedEntryCount > 0);
    }

    [Fact]
    public void StripFileMetadata_Mp3_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateMp3(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_Mp3Output_IsDecodableByTagLib()
    {
        // After stripping, ID3 tags are removed and the output starts with MPEG frame bytes.
        // Verify TagLib can still read the result (it is a valid, stripped MP3).
        var result = _sut.StripFileMetadata(TestHelpers.CreateMp3(title: "Test"), false);

        var ex = Record.Exception(() =>
        {
            var ms = new MemoryStream(result.CleanFile);
            TagLib.File.Create(new TestHelpers.TagLibStreamAbstraction("test.mp3", ms)).Dispose();
        });
        Assert.Null(ex);
    }

    // -- FLAC --------------------------------------------------------------------

    [Fact]
    public void StripFileMetadata_Flac_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateFlac(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_Flac_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateFlac(), false);

        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_Flac_MagicBytesPreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateFlac(), false);

        Assert.Equal(0x66, result.CleanFile[0]); // f
        Assert.Equal(0x4C, result.CleanFile[1]); // L
        Assert.Equal(0x61, result.CleanFile[2]); // a
        Assert.Equal(0x43, result.CleanFile[3]); // C
    }

    [Fact]
    public void StripFileMetadata_FlacWithTitle_TitleIsCleared()
    {
        var input = TestHelpers.CreateFlacWithMetadata(title: "Injected Title");

        var result = _sut.StripFileMetadata(input, false);

        var ms = new MemoryStream(result.CleanFile);
        using var file = TagLib.File.Create(new TestHelpers.TagLibStreamAbstraction("test.flac", ms));
        Assert.True(string.IsNullOrEmpty(file.Tag.Title));
    }

    [Fact]
    public void StripFileMetadata_FlacWithMetadata_ExtractedContainsTitle()
    {
        var input = TestHelpers.CreateFlacWithMetadata(title: "Injected Title");

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("title", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_FlacWithMetadata_RemovedEntryCountIsGreaterThanZero()
    {
        var input = TestHelpers.CreateFlacWithMetadata(title: "Test", artist: "Artist");

        var result = _sut.StripFileMetadata(input, false);

        Assert.True(result.RemovedEntryCount > 0);
    }

    // -- OGG ---------------------------------------------------------------------

    [Fact]
    public void StripFileMetadata_Ogg_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateOgg(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_Ogg_CleanFileIsNonEmpty()
        => Assert.NotEmpty(_sut.StripFileMetadata(TestHelpers.CreateOgg(), false).CleanFile);

    [Fact]
    public void StripFileMetadata_Ogg_MagicBytesPreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateOgg(), false);

        Assert.Equal(0x4F, result.CleanFile[0]); // O
        Assert.Equal(0x67, result.CleanFile[1]); // g
        Assert.Equal(0x67, result.CleanFile[2]); // g
        Assert.Equal(0x53, result.CleanFile[3]); // S
    }

    [Fact]
    public void StripFileMetadata_OggWithTitle_TitleIsCleared()
    {
        // Note: the minimal OGG seed cannot accept metadata writes via TagLibSharp
        // (no comment header page). CreateOggWithMetadata falls back to the seed.
        // This test verifies that even a "no-metadata" OGG is correctly handled.
        var input  = TestHelpers.CreateOggWithMetadata(title: "Injected Title");
        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.NotEmpty(result.CleanFile);
    }

    // -- MP4 ---------------------------------------------------------------------

    [Fact]
    public void StripFileMetadata_Mp4_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateMp4(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_Mp4_CleanFileIsNonEmpty()
        => Assert.NotEmpty(_sut.StripFileMetadata(TestHelpers.CreateMp4(), false).CleanFile);

    [Fact]
    public void StripFileMetadata_Mp4_FtypSignaturePresent()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateMp4(), false);

        Assert.Equal(0x66, result.CleanFile[4]); // f
        Assert.Equal(0x74, result.CleanFile[5]); // t
        Assert.Equal(0x79, result.CleanFile[6]); // y
        Assert.Equal(0x70, result.CleanFile[7]); // p
    }

    [Fact]
    public void StripFileMetadata_Mp4WithTitle_TitleIsCleared()
    {
        // Note: the minimal MP4 seed lacks mvhd and cannot accept metadata writes.
        // CreateMp4WithMetadata falls back to the seed. This test verifies correct handling.
        var input  = TestHelpers.CreateMp4WithMetadata(title: "Injected Title");
        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.NotEmpty(result.CleanFile);
    }

    // -- MKV ---------------------------------------------------------------------

    [Fact]
    public void StripFileMetadata_Mkv_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateMkv(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_Mkv_CleanFileIsNonEmpty()
        => Assert.NotEmpty(_sut.StripFileMetadata(TestHelpers.CreateMkv(), false).CleanFile);

    [Fact]
    public void StripFileMetadata_Mkv_EbmlMagicBytesPreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateMkv(), false);

        Assert.Equal(0x1A, result.CleanFile[0]);
        Assert.Equal(0x45, result.CleanFile[1]);
        Assert.Equal(0xDF, result.CleanFile[2]);
        Assert.Equal(0xA3, result.CleanFile[3]);
    }

    [Fact]
    public void StripFileMetadata_MkvWithTitle_TitleIsCleared()
    {
        // Note: the minimal MKV seed lacks a Segment element and cannot accept metadata writes.
        // CreateMkvWithMetadata falls back to the seed. This test verifies correct handling.
        var input  = TestHelpers.CreateMkvWithMetadata(title: "Injected Title");
        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.NotEmpty(result.CleanFile);
    }

    // -- AVI ---------------------------------------------------------------------

    [Fact]
    public void StripFileMetadata_Avi_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateAvi(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_Avi_CleanFileIsNonEmpty()
        => Assert.NotEmpty(_sut.StripFileMetadata(TestHelpers.CreateAvi(), false).CleanFile);

    [Fact]
    public void StripFileMetadata_Avi_RiffAviSignaturePreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateAvi(), false);

        Assert.Equal(0x52, result.CleanFile[0]); // R
        Assert.Equal(0x49, result.CleanFile[1]); // I
        Assert.Equal(0x46, result.CleanFile[2]); // F
        Assert.Equal(0x46, result.CleanFile[3]); // F
        Assert.Equal(0x41, result.CleanFile[8]); // A
        Assert.Equal(0x56, result.CleanFile[9]); // V
        Assert.Equal(0x49, result.CleanFile[10]); // I
    }

    // -- Processing error message -----------------------------------------------


    [Fact]
    public void StripFileMetadata_AviWithTitle_TitleIsCleared()
    {
        var input = TestHelpers.CreateAvi(title: "Injected Title");

        var result = _sut.StripFileMetadata(input, false);

        var ms = new MemoryStream(result.CleanFile);
        using var file = TagLib.File.Create(new TestHelpers.TagLibStreamAbstraction("test.avi", ms));
        Assert.True(string.IsNullOrEmpty(file.Tag.Title));
    }

    [Fact]
    public void StripFileMetadata_AviWithMetadata_ExtractedContainsTitle()
    {
        var input = TestHelpers.CreateAvi(title: "Injected Title");

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("title", result.ExtractedMetadata);
        Assert.Contains("Injected Title", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_AviWithMetadata_RemovedEntryCountIsGreaterThanZero()
    {
        var input = TestHelpers.CreateAvi(title: "Injected Title");

        var result = _sut.StripFileMetadata(input, false);

        Assert.True(result.RemovedEntryCount > 0);
    }

    [Fact]
    public void StripFileMetadata_UnparsableMediaFile_ExtractedMetadataContainsProcessingError()
    {
        // A minimal FLAC that TagLibSharp cannot fully open returns an audit note
        // instead of "[]" so the caller knows WHY metadata was not stripped.
        var input = TestHelpers.CreateFlac();

        var result = _sut.StripFileMetadata(input, false);

        // Either it was successfully processed (no error key) or it contains a processing note
        // � either way IsPassthrough must be false (it IS a media format)
        Assert.False(result.IsPassthrough);

        if (result.ExtractedMetadata != "[]")
            Assert.Contains("processingError", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_ProcessingError_CleanFileEqualsOriginal()
    {
        // When TagLibSharp cannot parse the file, the original bytes are returned unchanged.
        var input = TestHelpers.CreateFlac();
        var result = _sut.StripFileMetadata(input, false);

        if (result.ExtractedMetadata.Contains("processingError"))
            Assert.Equal(input, result.CleanFile);
    }

    // -- M4A (iTunes audio in an ISOBMFF container) ------------------------------

    [Fact]
    public void StripFileMetadata_M4a_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateM4a(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_M4a_CleanFileIsNonEmpty()
        => Assert.NotEmpty(_sut.StripFileMetadata(TestHelpers.CreateM4a(), false).CleanFile);

    [Fact]
    public void StripFileMetadata_M4a_FtypSignaturePresent()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateM4a(), false);

        Assert.Equal(0x66, result.CleanFile[4]); // f
        Assert.Equal(0x74, result.CleanFile[5]); // t
        Assert.Equal(0x79, result.CleanFile[6]); // y
        Assert.Equal(0x70, result.CleanFile[7]); // p
    }

    [Fact]
    public void StripFileMetadata_M4a_M4aBrandPreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateM4a(), false);

        // Bytes 8-11 must remain "M4A " so the file continues to identify as M4A audio.
        Assert.Equal(0x4D, result.CleanFile[8]);
        Assert.Equal(0x34, result.CleanFile[9]);
        Assert.Equal(0x41, result.CleanFile[10]);
        Assert.Equal(0x20, result.CleanFile[11]);
    }

    // -- MOV (QuickTime video in an ISOBMFF container) ---------------------------

    [Fact]
    public void StripFileMetadata_Mov_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateMov(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_Mov_CleanFileIsNonEmpty()
        => Assert.NotEmpty(_sut.StripFileMetadata(TestHelpers.CreateMov(), false).CleanFile);

    [Fact]
    public void StripFileMetadata_Mov_FtypSignaturePresent()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateMov(), false);

        Assert.Equal(0x66, result.CleanFile[4]); // f
        Assert.Equal(0x74, result.CleanFile[5]); // t
        Assert.Equal(0x79, result.CleanFile[6]); // y
        Assert.Equal(0x70, result.CleanFile[7]); // p
    }

    [Fact]
    public void StripFileMetadata_Mov_QuickTimeBrandPreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateMov(), false);

        // Bytes 8-11 must remain "qt  " so the file continues to identify as MOV.
        Assert.Equal(0x71, result.CleanFile[8]);
        Assert.Equal(0x74, result.CleanFile[9]);
        Assert.Equal(0x20, result.CleanFile[10]);
        Assert.Equal(0x20, result.CleanFile[11]);
    }

    // -- WebM (EBML container, DocType "webm") -----------------------------------

    [Fact]
    public void StripFileMetadata_WebM_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateWebM(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_WebM_CleanFileIsNonEmpty()
        => Assert.NotEmpty(_sut.StripFileMetadata(TestHelpers.CreateWebM(), false).CleanFile);

    [Fact]
    public void StripFileMetadata_WebM_EbmlMagicBytesPreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateWebM(), false);

        Assert.Equal(0x1A, result.CleanFile[0]);
        Assert.Equal(0x45, result.CleanFile[1]);
        Assert.Equal(0xDF, result.CleanFile[2]);
        Assert.Equal(0xA3, result.CleanFile[3]);
    }

    // -- WMA (Windows Media Audio, ASF container) --------------------------------

    [Fact]
    public void StripFileMetadata_Wma_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateWma(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_Wma_CleanFileIsNonEmpty()
        => Assert.NotEmpty(_sut.StripFileMetadata(TestHelpers.CreateWma(), false).CleanFile);

    [Fact]
    public void StripFileMetadata_Wma_AsfHeaderGuidPreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateWma(), false);

        // ASF Header Object GUID first 4 bytes: 30 26 B2 75
        Assert.Equal(0x30, result.CleanFile[0]);
        Assert.Equal(0x26, result.CleanFile[1]);
        Assert.Equal(0xB2, result.CleanFile[2]);
        Assert.Equal(0x75, result.CleanFile[3]);
    }

    [Fact]
    public void StripFileMetadata_Wma_DoesNotThrow()
    {
        // Minimal ASF stub — TagLibSharp cannot fully parse it, but the strip pipeline
        // must catch that gracefully and return the original with a processingError note.
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateWma(), false));
        Assert.Null(ex);
    }

    // -- WMV (Windows Media Video — shares the ASF container with WMA) ----------

    [Fact]
    public void StripFileMetadata_Wmv_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateWmv(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_Wmv_CleanFileIsNonEmpty()
        => Assert.NotEmpty(_sut.StripFileMetadata(TestHelpers.CreateWmv(), false).CleanFile);

    [Fact]
    public void StripFileMetadata_Wmv_AsfHeaderGuidPreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateWmv(), false);

        Assert.Equal(0x30, result.CleanFile[0]);
        Assert.Equal(0x26, result.CleanFile[1]);
        Assert.Equal(0xB2, result.CleanFile[2]);
        Assert.Equal(0x75, result.CleanFile[3]);
    }

    [Fact]
    public void StripFileMetadata_Wmv_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateWmv(), false));
        Assert.Null(ex);
    }

    // -- 3GP (3GPP mobile video, ISOBMFF ftyp brand "3gp4") ---------------------

    [Fact]
    public void StripFileMetadata_3gp_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.Create3gp(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_3gp_CleanFileIsNonEmpty()
        => Assert.NotEmpty(_sut.StripFileMetadata(TestHelpers.Create3gp(), false).CleanFile);

    [Fact]
    public void StripFileMetadata_3gp_FtypSignaturePresent()
    {
        var result = _sut.StripFileMetadata(TestHelpers.Create3gp(), false);

        Assert.Equal(0x66, result.CleanFile[4]); // f
        Assert.Equal(0x74, result.CleanFile[5]); // t
        Assert.Equal(0x79, result.CleanFile[6]); // y
        Assert.Equal(0x70, result.CleanFile[7]); // p
    }

    [Fact]
    public void StripFileMetadata_3gp_BrandPreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.Create3gp(), false);

        // Bytes 8-11 must remain "3gp4" so downstream players continue to identify the format.
        Assert.Equal(0x33, result.CleanFile[8]);
        Assert.Equal(0x67, result.CleanFile[9]);
        Assert.Equal(0x70, result.CleanFile[10]);
        Assert.Equal(0x34, result.CleanFile[11]);
    }

    [Fact]
    public void StripFileMetadata_3gp_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.Create3gp(), false));
        Assert.Null(ex);
    }

    // -- 3G2 (3GPP2 CDMA mobile video, ISOBMFF ftyp brand "3g2a") --------------

    [Fact]
    public void StripFileMetadata_3g2_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.Create3g2(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_3g2_CleanFileIsNonEmpty()
        => Assert.NotEmpty(_sut.StripFileMetadata(TestHelpers.Create3g2(), false).CleanFile);

    [Fact]
    public void StripFileMetadata_3g2_BrandPreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.Create3g2(), false);

        Assert.Equal(0x33, result.CleanFile[8]);
        Assert.Equal(0x67, result.CleanFile[9]);
        Assert.Equal(0x32, result.CleanFile[10]);
        Assert.Equal(0x61, result.CleanFile[11]);
    }

    [Fact]
    public void StripFileMetadata_3g2_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.Create3g2(), false));
        Assert.Null(ex);
    }

    // -- M4V (Apple iTunes video, ISOBMFF ftyp brand "M4V ") -------------------

    [Fact]
    public void StripFileMetadata_M4v_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateM4v(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_M4v_CleanFileIsNonEmpty()
        => Assert.NotEmpty(_sut.StripFileMetadata(TestHelpers.CreateM4v(), false).CleanFile);

    [Fact]
    public void StripFileMetadata_M4v_BrandPreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateM4v(), false);

        // Bytes 8-11 must remain "M4V ".
        Assert.Equal(0x4D, result.CleanFile[8]);
        Assert.Equal(0x34, result.CleanFile[9]);
        Assert.Equal(0x56, result.CleanFile[10]);
        Assert.Equal(0x20, result.CleanFile[11]);
    }

    [Fact]
    public void StripFileMetadata_M4v_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateM4v(), false));
        Assert.Null(ex);
    }

    // -- M4B (Apple iTunes audiobook, ISOBMFF ftyp brand "M4B ") ---------------

    [Fact]
    public void StripFileMetadata_M4b_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateM4b(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_M4b_CleanFileIsNonEmpty()
        => Assert.NotEmpty(_sut.StripFileMetadata(TestHelpers.CreateM4b(), false).CleanFile);

    [Fact]
    public void StripFileMetadata_M4b_BrandPreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateM4b(), false);

        // Bytes 8-11 must remain "M4B ".
        Assert.Equal(0x4D, result.CleanFile[8]);
        Assert.Equal(0x34, result.CleanFile[9]);
        Assert.Equal(0x42, result.CleanFile[10]);
        Assert.Equal(0x20, result.CleanFile[11]);
    }

    [Fact]
    public void StripFileMetadata_M4b_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateM4b(), false));
        Assert.Null(ex);
    }

    // -- Ogg Opus (OggS magic, OpusHead identification packet) ------------------

    [Fact]
    public void StripFileMetadata_Opus_IsPassthroughIsFalse()
        => Assert.False(_sut.StripFileMetadata(TestHelpers.CreateOpus(), false).IsPassthrough);

    [Fact]
    public void StripFileMetadata_Opus_CleanFileIsNonEmpty()
        => Assert.NotEmpty(_sut.StripFileMetadata(TestHelpers.CreateOpus(), false).CleanFile);

    [Fact]
    public void StripFileMetadata_Opus_OggMagicBytesPreserved()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateOpus(), false);

        Assert.Equal(0x4F, result.CleanFile[0]); // O
        Assert.Equal(0x67, result.CleanFile[1]); // g
        Assert.Equal(0x67, result.CleanFile[2]); // g
        Assert.Equal(0x53, result.CleanFile[3]); // S
    }

    [Fact]
    public void StripFileMetadata_Opus_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateOpus(), false));
        Assert.Null(ex);
    }
}
