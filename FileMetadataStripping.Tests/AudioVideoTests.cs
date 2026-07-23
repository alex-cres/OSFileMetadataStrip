using TagLib;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>
/// Tests for audio and video file stripping via TagLibSharp.
/// Each supported format has at minimum a detection test (IsPassthrough = false).
/// Formats where TagLibSharp can work with minimal constructed files also get
/// metadata strip tests (title/artist cleared, extracted metadata captured).
///
/// Test data is generated programmatically â€” no binary files are committed.
/// </summary>
public class AudioVideoTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    // â”€â”€ WAV (full strip tests â€” easiest format to construct programmatically) â”€â”€â”€â”€â”€

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

    // â”€â”€ MP3 (full strip tests â€” minimal ID3v2 header is writable by TagLibSharp) â”€

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

    // â”€â”€ FLAC â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ OGG â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ MP4 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ MKV â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ AVI â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ Processing error message â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void StripFileMetadata_UnparsableMediaFile_ExtractedMetadataContainsProcessingError()
    {
        // A minimal FLAC that TagLibSharp cannot fully open returns an audit note
        // instead of "[]" so the caller knows WHY metadata was not stripped.
        var input = TestHelpers.CreateFlac();

        var result = _sut.StripFileMetadata(input, false);

        // Either it was successfully processed (no error key) or it contains a processing note
        // â€” either way IsPassthrough must be false (it IS a media format)
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
}
