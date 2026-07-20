using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ImageMagick;
using PdfSharp.Pdf;
using System.IO.Packaging;
using TagLib;

namespace FileMetadataStripping.Tests;

/// <summary>
/// Shared test data helpers. All helpers generate data programmatically — no binary files are committed.
/// </summary>
internal static class TestHelpers
{
    internal static byte[] CreateJpeg(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Jpeg;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreatePng(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Png;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateGif(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Gif;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateBmp(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Bmp;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateTiff(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Tiff;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateWebP(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.WebP;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreatePdf(string? author = null, string? title = null)
    {
        var doc = new PdfDocument();
        if (!string.IsNullOrEmpty(author)) doc.Info.Author = author;
        if (!string.IsNullOrEmpty(title))  doc.Info.Title  = title;
        doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateDocx(string? creator = null, string? title = null)
    {
        using var ms = new MemoryStream();
        using var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document);
        doc.AddMainDocumentPart().Document = new Document(new Body(new Paragraph()));
        if (!string.IsNullOrEmpty(creator)) doc.PackageProperties.Creator = creator;
        if (!string.IsNullOrEmpty(title))   doc.PackageProperties.Title   = title;
        doc.Save();
        return ms.ToArray();
    }

    /// <summary>
    /// Creates a minimal WAV file (empty audio, valid RIFF/WAVE structure) and optionally tags it
    /// using TagLibSharp so the resulting bytes represent a real tagged WAV.
    /// </summary>
    internal static byte[] CreateWav(string? title = null, string? artist = null)
    {
        // Build a minimal silent WAV (RIFF header + fmt chunk + empty data chunk)
        var wavBytes = BuildMinimalWav();

        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist))
            return wavBytes;

        // Embed metadata using TagLibSharp
        var ms = new MemoryStream();
        ms.Write(wavBytes, 0, wavBytes.Length);
        ms.Position = 0;

        using var file = TagLib.File.Create(new TagLibStreamAbstraction("test.wav", ms));
        if (!string.IsNullOrEmpty(title))  file.Tag.Title      = title;
        if (!string.IsNullOrEmpty(artist)) file.Tag.Performers = new[] { artist };
        file.Save();

        return ms.ToArray();
    }

    /// <summary>
    /// Creates a minimal MP3 file (ID3v2.4 header) and optionally embeds metadata.
    /// </summary>
    internal static byte[] CreateMp3(string? title = null, string? artist = null)
    {
        // Minimal MP3: empty ID3v2.4 header (10 bytes) + one valid MPEG sync frame header (4 bytes)
        // TagLibSharp requires at least one MPEG frame header to recognise the file as MP3.
        var seed = new byte[]
        {
            0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // ID3v2.4 header
            0xFF, 0xFB, 0x90, 0x00                                        // MPEG1 L3, 128kbps, 44100Hz
        };

        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist))
            return seed;

        var ms = new MemoryStream();
        ms.Write(seed, 0, seed.Length);
        ms.Position = 0;
        using var file = TagLib.File.Create(new TagLibStreamAbstraction("test.mp3", ms));
        if (!string.IsNullOrEmpty(title))  file.Tag.Title      = title;
        if (!string.IsNullOrEmpty(artist)) file.Tag.Performers = new[] { artist };
        file.Save();
        return ms.ToArray();
    }

    /// <summary>
    /// Creates a minimal FLAC file (fLaC + valid STREAMINFO metadata block).
    /// </summary>
    internal static byte[] CreateFlac() =>
    [
        0x66, 0x4C, 0x61, 0x43,              // "fLaC"
        0x80,                                  // last block (1) + type 0 (STREAMINFO)
        0x00, 0x00, 0x22,                      // length = 34 bytes
        // STREAMINFO (34 bytes):
        0x10, 0x00,                            // min blocksize = 4096
        0x10, 0x00,                            // max blocksize = 4096
        0x00, 0x00, 0x00,                      // min framesize = 0 (unknown)
        0x00, 0x00, 0x00,                      // max framesize = 0 (unknown)
        // sr(20)=44100, ch(3)=0(mono), bps(5)=15(16-bit), total_samples(36)=0
        0x0A, 0xC4, 0x40, 0xF0, 0x00, 0x00, 0x00, 0x00,
        // MD5 signature (16 bytes, all zeros for empty stream)
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    /// <summary>
    /// Creates a minimal OGG Vorbis file (one page with Vorbis identification header).
    /// </summary>
    internal static byte[] CreateOgg() =>
    [
        // Ogg page header
        0x4F, 0x67, 0x67, 0x53,              // "OggS" capture pattern
        0x00,                                  // stream structure version
        0x02,                                  // header_type: beginning of stream
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // granule_position = 0
        0x01, 0x00, 0x00, 0x00,              // bitstream_serial_number = 1
        0x00, 0x00, 0x00, 0x00,              // page_sequence_number = 0
        0x00, 0x00, 0x00, 0x00,              // CRC (zero — some readers are lenient)
        0x01,                                  // number_page_segments = 1
        0x1E,                                  // segment_table[0] = 30 bytes
        // Vorbis identification header (30 bytes):
        0x01,                                  // packet_type = 1 (identification)
        0x76, 0x6F, 0x72, 0x62, 0x69, 0x73, // "vorbis"
        0x00, 0x00, 0x00, 0x00,              // vorbis_version = 0
        0x01,                                  // audio_channels = 1
        0x44, 0xAC, 0x00, 0x00,              // audio_sample_rate = 44100 (LE)
        0x00, 0x00, 0x00, 0x00,              // bitrate_maximum = 0
        0x00, 0x00, 0x00, 0x00,              // bitrate_nominal = 0
        0x00, 0x00, 0x00, 0x00,              // bitrate_minimum = 0
        0xB8,                                  // blocksize_0=8 (256), blocksize_1=11 (2048)
        0x01                                   // framing_bit = 1
    ];

    /// <summary>
    /// Creates a minimal MP4 file (ftyp + empty moov box).
    /// </summary>
    internal static byte[] CreateMp4() =>
    [
        // ftyp box (16 bytes)
        0x00, 0x00, 0x00, 0x10,              // size = 16
        0x66, 0x74, 0x79, 0x70,              // "ftyp"
        0x69, 0x73, 0x6F, 0x6D,              // major_brand = "isom"
        0x00, 0x00, 0x02, 0x00,              // minor_version
        // moov box (8 bytes, empty container)
        0x00, 0x00, 0x00, 0x08,              // size = 8
        0x6D, 0x6F, 0x6F, 0x76               // "moov"
    ];

    /// <summary>
    /// Creates a minimal MKV/EBML file (EBML header with EBMLVersion element).
    /// </summary>
    internal static byte[] CreateMkv() =>
    [
        0x1A, 0x45, 0xDF, 0xA3,              // EBML element ID
        0x84,                                  // data size = 4 bytes
        0x42, 0x86,                            // EBMLVersion element ID
        0x81,                                  // size = 1 byte
        0x01                                   // EBMLVersion = 1
    ];

    /// <summary>
    /// Creates a minimal AVI file (RIFF + AVI  header) and optionally embeds a title tag.
    /// </summary>
    internal static byte[] CreateAvi(string? title = null)
    {
        var seed = BuildMinimalAvi();

        if (string.IsNullOrEmpty(title))
            return seed;

        var ms = new MemoryStream();
        ms.Write(seed, 0, seed.Length);
        ms.Position = 0;
        using var file = TagLib.File.Create(new TagLibStreamAbstraction("test.avi", ms));
        file.Tag.Title = title;
        file.Save();
        return ms.ToArray();
    }

    private static byte[] BuildMinimalAvi()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);
        bw.Write(new byte[] { 0x52, 0x49, 0x46, 0x46 }); // "RIFF"
        bw.Write((uint)4);
        bw.Write(new byte[] { 0x41, 0x56, 0x49, 0x20 }); // "AVI "
        bw.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildMinimalWav()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);

        bw.Write(new byte[] { 0x52, 0x49, 0x46, 0x46 }); // "RIFF"
        bw.Write((uint)36);                                // bytes to follow
        bw.Write(new byte[] { 0x57, 0x41, 0x56, 0x45 }); // "WAVE"

        bw.Write(new byte[] { 0x66, 0x6D, 0x74, 0x20 }); // "fmt "
        bw.Write((uint)16);    bw.Write((ushort)1);       // PCM
        bw.Write((ushort)1);   bw.Write((uint)44100);     // mono, 44 100 Hz
        bw.Write((uint)88200); bw.Write((ushort)2);       // byte rate, block align
        bw.Write((ushort)16);                              // 16-bit depth

        bw.Write(new byte[] { 0x64, 0x61, 0x74, 0x61 }); // "data"
        bw.Write((uint)0);                                 // no audio samples

        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>In-memory IFileAbstraction for TagLibSharp — usable in tests without file I/O.</summary>
    internal sealed class TagLibStreamAbstraction : TagLib.File.IFileAbstraction
    {
        private readonly Stream _stream;
        internal TagLibStreamAbstraction(string name, Stream stream) { Name = name; _stream = stream; }
        public string Name { get; }
        public Stream ReadStream  => _stream;
        public Stream WriteStream => _stream;
        public void CloseStream(Stream stream) { }
    }
}
