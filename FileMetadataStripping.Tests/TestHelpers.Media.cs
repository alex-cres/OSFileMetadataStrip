using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using System.IO.Packaging;
using System.Xml.Linq;
using TagLib;
namespace FileMetadataStripping.Tests;

internal static partial class TestHelpers
{
    // Audio/video test-data helpers (TagLibSharp-backed strip pipeline).

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

    internal static byte[] CreateFlacWithMetadata(string? title = null, string? artist = null)
    {
        var seed = CreateFlac();
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist)) return seed;
        var ms = new MemoryStream();
        ms.Write(seed, 0, seed.Length);
        ms.Position = 0;
        using var file = TagLib.File.Create(new TagLibStreamAbstraction("test.flac", ms));
        if (!string.IsNullOrEmpty(title))  file.Tag.Title      = title;
        if (!string.IsNullOrEmpty(artist)) file.Tag.Performers = new[] { artist };
        file.Save();
        return ms.ToArray();
    }

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

    internal static byte[] CreateOggWithMetadata(string? title = null, string? artist = null)
    {
        var seed = CreateOgg();
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist)) return seed;
        try
        {
            var ms = new MemoryStream();
            ms.Write(seed, 0, seed.Length);
            ms.Position = 0;
            using var file = TagLib.File.Create(new TagLibStreamAbstraction("test.ogg", ms));
            if (!string.IsNullOrEmpty(title))  file.Tag.Title      = title;
            if (!string.IsNullOrEmpty(artist)) file.Tag.Performers = new[] { artist };
            file.Save();
            return ms.ToArray();
        }
        catch { return seed; } // minimal OGG cannot accept metadata writes
    }

    /// <summary>
    /// Creates a minimal Ogg Opus file — Ogg container carrying an OpusHead identification
    /// packet instead of a Vorbis identification packet. The Ogg magic bytes (OggS) are the
    /// same as for Ogg Vorbis, but the payload identifies Opus and TagLibSharp routes it
    /// through a different codec-specific parser branch.
    /// </summary>
    internal static byte[] CreateOpus() =>
    [
        // Ogg page header
        0x4F, 0x67, 0x67, 0x53,              // "OggS"
        0x00,                                  // stream structure version
        0x02,                                  // header_type: beginning of stream
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // granule_position
        0x01, 0x00, 0x00, 0x00,              // bitstream_serial_number
        0x00, 0x00, 0x00, 0x00,              // page_sequence_number
        0x00, 0x00, 0x00, 0x00,              // CRC (zero)
        0x01,                                  // number_page_segments = 1
        0x13,                                  // segment_table[0] = 19 bytes (OpusHead size)
        // OpusHead identification packet (19 bytes)
        0x4F, 0x70, 0x75, 0x73, 0x48, 0x65, 0x61, 0x64, // "OpusHead"
        0x01,                                  // version
        0x02,                                  // channel count (stereo)
        0x38, 0x01,                            // pre-skip (312, little-endian)
        0x80, 0xBB, 0x00, 0x00,              // input sample rate = 48000 (LE)
        0x00, 0x00,                            // output gain (Q7.8)
        0x00                                   // channel mapping family = 0
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
    /// Creates a minimal M4A file — ISOBMFF ftyp with the "M4A " major brand so
    /// TagLibSharp's format detector selects the iTunes M4A parser.
    /// </summary>
    internal static byte[] CreateM4a() =>
    [
        0x00, 0x00, 0x00, 0x10,              // size = 16
        0x66, 0x74, 0x79, 0x70,              // "ftyp"
        0x4D, 0x34, 0x41, 0x20,              // major_brand = "M4A "
        0x00, 0x00, 0x02, 0x00,              // minor_version
        0x00, 0x00, 0x00, 0x08,              // moov size = 8
        0x6D, 0x6F, 0x6F, 0x76               // "moov"
    ];

    /// <summary>
    /// Creates a minimal MOV (QuickTime) file — ISOBMFF ftyp with the "qt  " major brand.
    /// </summary>
    internal static byte[] CreateMov() =>
    [
        0x00, 0x00, 0x00, 0x10,              // size = 16
        0x66, 0x74, 0x79, 0x70,              // "ftyp"
        0x71, 0x74, 0x20, 0x20,              // major_brand = "qt  "
        0x00, 0x00, 0x02, 0x00,              // minor_version
        0x00, 0x00, 0x00, 0x08,              // moov size = 8
        0x6D, 0x6F, 0x6F, 0x76               // "moov"
    ];

    /// <summary>ISOBMFF ftyp with major brand "3gp4" — 3GPP mobile video.</summary>
    internal static byte[] Create3gp() =>
    [
        0x00, 0x00, 0x00, 0x10,
        0x66, 0x74, 0x79, 0x70,
        0x33, 0x67, 0x70, 0x34,              // major_brand = "3gp4"
        0x00, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x08,
        0x6D, 0x6F, 0x6F, 0x76
    ];

    /// <summary>ISOBMFF ftyp with major brand "3g2a" — 3GPP2 (CDMA) mobile video.</summary>
    internal static byte[] Create3g2() =>
    [
        0x00, 0x00, 0x00, 0x10,
        0x66, 0x74, 0x79, 0x70,
        0x33, 0x67, 0x32, 0x61,              // major_brand = "3g2a"
        0x00, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x08,
        0x6D, 0x6F, 0x6F, 0x76
    ];

    /// <summary>ISOBMFF ftyp with major brand "M4V " — Apple iTunes video.</summary>
    internal static byte[] CreateM4v() =>
    [
        0x00, 0x00, 0x00, 0x10,
        0x66, 0x74, 0x79, 0x70,
        0x4D, 0x34, 0x56, 0x20,              // major_brand = "M4V "
        0x00, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x08,
        0x6D, 0x6F, 0x6F, 0x76
    ];

    /// <summary>ISOBMFF ftyp with major brand "M4B " — Apple iTunes audiobook.</summary>
    internal static byte[] CreateM4b() =>
    [
        0x00, 0x00, 0x00, 0x10,
        0x66, 0x74, 0x79, 0x70,
        0x4D, 0x34, 0x42, 0x20,              // major_brand = "M4B "
        0x00, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x08,
        0x6D, 0x6F, 0x6F, 0x76
    ];

    internal static byte[] CreateMp4WithMetadata(string? title = null)
    {
        var seed = CreateMp4();
        if (string.IsNullOrEmpty(title)) return seed;
        try
        {
            var ms = new MemoryStream();
            ms.Write(seed, 0, seed.Length);
            ms.Position = 0;
            using var file = TagLib.File.Create(new TagLibStreamAbstraction("test.mp4", ms));
            file.Tag.Title = title;
            file.Save();
            return ms.ToArray();
        }
        catch { return seed; } // minimal MP4 lacks mvhd and cannot accept metadata writes
    }

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
    /// Creates a minimal WebM file — EBML header with DocType = "webm". EBML is the same
    /// container used for MKV; WebM is distinguished only by the DocType element.
    /// </summary>
    internal static byte[] CreateWebM() =>
    [
        0x1A, 0x45, 0xDF, 0xA3,              // EBML element ID
        0x88,                                  // data size = 8 bytes
        0x42, 0x82,                            // DocType element ID
        0x84,                                  // DocType data size = 4
        0x77, 0x65, 0x62, 0x6D               // "webm"
    ];

    /// <summary>
    /// Creates a minimal ASF (Advanced Systems Format) file — the container used by both
    /// WMA (audio) and WMV (video). Starts with the ASF Header Object GUID; the extension
    /// hint returned by <c>GetMediaExtensionHint</c> for ASF magic is <c>.wma</c>.
    /// </summary>
    internal static byte[] CreateWma() => BuildMinimalAsf();

    /// <summary>Creates the same minimal ASF stub used for WMA (WMA and WMV share the ASF container).</summary>
    internal static byte[] CreateWmv() => BuildMinimalAsf();

    /// <summary>
    /// Builds a minimal ASF header — the 16-byte ASF Header Object GUID followed by a
    /// header-object length field and a zero child-count. Not a complete ASF stream;
    /// TagLibSharp cannot fully parse it, so the strip pipeline returns the original
    /// bytes with a <c>processingError</c> note. This is enough to verify that the
    /// format is detected and routed to the media pipeline.
    /// </summary>
    private static byte[] BuildMinimalAsf()
    {
        // ASF_Header_Object GUID (little-endian):
        // {75B22630-668E-11CF-A6D9-00AA0062CE6C}
        var bytes = new byte[]
        {
            0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
            0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C,
            // Object size (uint64 little-endian) — 30 (whole header object)
            0x1E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            // Number of header objects (uint32) — 0 (no children)
            0x00, 0x00, 0x00, 0x00,
            // Reserved bytes (as required by spec)
            0x01, 0x02
        };
        return bytes;
    }

    internal static byte[] CreateMkvWithMetadata(string? title = null)
    {
        var seed = CreateMkv();
        if (string.IsNullOrEmpty(title)) return seed;
        try
        {
            var ms = new MemoryStream();
            ms.Write(seed, 0, seed.Length);
            ms.Position = 0;
            using var file = TagLib.File.Create(new TagLibStreamAbstraction("test.mkv", ms));
            file.Tag.Title = title;
            file.Save();
            return ms.ToArray();
        }
        catch { return seed; } // minimal MKV lacks Segment element and cannot accept metadata writes
    }

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
        // A minimal but structurally valid AVI that TagLib can parse and write metadata to.
        // Structure: RIFF/AVI  > LIST/hdrl > avih + LIST/strl > strh + strf
        //                      > LIST/movi
        const uint avihSize = 56;  // AVI main header
        const uint strhSize = 56;  // stream header ("vids" type + 52 zeros)
        const uint strfSize = 40;  // BITMAPINFOHEADER
        const uint strlSize = 4 + (4 + 4 + strhSize) + (4 + 4 + strfSize); // "strl" + strh chunk + strf chunk
        const uint hdrlSize = 4 + (4 + 4 + avihSize) + (4 + 4 + strlSize); // "hdrl" + avih chunk + LIST strl
        const uint moviSize = 4;   // just the "movi" form type
        const uint riffSize = 4 + (4 + 4 + hdrlSize) + (4 + 4 + moviSize); // "AVI " + LIST hdrl + LIST movi

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);

        // RIFF header
        bw.Write(new byte[] { 0x52, 0x49, 0x46, 0x46 }); // "RIFF"
        bw.Write(riffSize);
        bw.Write(new byte[] { 0x41, 0x56, 0x49, 0x20 }); // "AVI "

        // LIST hdrl
        bw.Write(new byte[] { 0x4C, 0x49, 0x53, 0x54 }); // "LIST"
        bw.Write(hdrlSize);
        bw.Write(new byte[] { 0x68, 0x64, 0x72, 0x6C }); // "hdrl"

        // avih chunk (AVI main header — all zeros)
        bw.Write(new byte[] { 0x61, 0x76, 0x69, 0x68 }); // "avih"
        bw.Write(avihSize);
        bw.Write(new byte[avihSize]);

        // LIST strl
        bw.Write(new byte[] { 0x4C, 0x49, 0x53, 0x54 }); // "LIST"
        bw.Write(strlSize);
        bw.Write(new byte[] { 0x73, 0x74, 0x72, 0x6C }); // "strl"

        // strh chunk (stream header — "vids" type + 52 zeros)
        bw.Write(new byte[] { 0x73, 0x74, 0x72, 0x68 }); // "strh"
        bw.Write(strhSize);
        bw.Write(new byte[] { 0x76, 0x69, 0x64, 0x73 }); // "vids"
        bw.Write(new byte[52]);

        // strf chunk (BITMAPINFOHEADER — all zeros)
        bw.Write(new byte[] { 0x73, 0x74, 0x72, 0x66 }); // "strf"
        bw.Write(strfSize);
        bw.Write(new byte[strfSize]);

        // LIST movi (empty movie data)
        bw.Write(new byte[] { 0x4C, 0x49, 0x53, 0x54 }); // "LIST"
        bw.Write(moviSize);
        bw.Write(new byte[] { 0x6D, 0x6F, 0x76, 0x69 }); // "movi"

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
