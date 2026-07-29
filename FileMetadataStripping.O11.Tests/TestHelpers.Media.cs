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

    internal static byte[] CreateWav(string? title = null, string? artist = null)
    {
        var wavBytes = BuildMinimalWav();

        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist))
            return wavBytes;

        var ms = new MemoryStream();
        ms.Write(wavBytes, 0, wavBytes.Length);
        ms.Position = 0;

        using var file = TagLib.File.Create(new TagLibStreamAbstraction("test.wav", ms));
        if (!string.IsNullOrEmpty(title))  file.Tag.Title      = title;
        if (!string.IsNullOrEmpty(artist)) file.Tag.Performers = new[] { artist };
        file.Save();

        return ms.ToArray();
    }

    internal static byte[] CreateMp3(string? title = null, string? artist = null)
    {
        var seed = new byte[]
        {
            0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFB, 0x90, 0x00
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

    internal static byte[] CreateFlac() =>
    [
        0x66, 0x4C, 0x61, 0x43,
        0x80,
        0x00, 0x00, 0x22,
        0x10, 0x00,
        0x10, 0x00,
        0x00, 0x00, 0x00,
        0x00, 0x00, 0x00,
        0x0A, 0xC4, 0x40, 0xF0, 0x00, 0x00, 0x00, 0x00,
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

    internal static byte[] CreateOgg() =>
    [
        0x4F, 0x67, 0x67, 0x53,
        0x00,
        0x02,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x01,
        0x1E,
        0x01,
        0x76, 0x6F, 0x72, 0x62, 0x69, 0x73,
        0x00, 0x00, 0x00, 0x00,
        0x01,
        0x44, 0xAC, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0xB8,
        0x01
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
        catch { return seed; }
    }

    /// <summary>
    /// Ogg container carrying an OpusHead identification packet — same OggS magic bytes
    /// as Ogg Vorbis, but TagLibSharp routes it through a different codec-specific parser.
    /// </summary>
    internal static byte[] CreateOpus() =>
    [
        0x4F, 0x67, 0x67, 0x53,
        0x00,
        0x02,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x01,
        0x13,
        // OpusHead (19 bytes)
        0x4F, 0x70, 0x75, 0x73, 0x48, 0x65, 0x61, 0x64,
        0x01,
        0x02,
        0x38, 0x01,
        0x80, 0xBB, 0x00, 0x00,
        0x00, 0x00,
        0x00
    ];

    internal static byte[] CreateMp4() =>
    [
        0x00, 0x00, 0x00, 0x10,
        0x66, 0x74, 0x79, 0x70,
        0x69, 0x73, 0x6F, 0x6D,
        0x00, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x08,
        0x6D, 0x6F, 0x6F, 0x76
    ];

    /// <summary>ISOBMFF ftyp with the "M4A " major brand for iTunes M4A audio.</summary>
    internal static byte[] CreateM4a() =>
    [
        0x00, 0x00, 0x00, 0x10,
        0x66, 0x74, 0x79, 0x70,
        0x4D, 0x34, 0x41, 0x20,
        0x00, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x08,
        0x6D, 0x6F, 0x6F, 0x76
    ];

    /// <summary>ISOBMFF ftyp with the "qt  " major brand for QuickTime MOV.</summary>
    internal static byte[] CreateMov() =>
    [
        0x00, 0x00, 0x00, 0x10,
        0x66, 0x74, 0x79, 0x70,
        0x71, 0x74, 0x20, 0x20,
        0x00, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x08,
        0x6D, 0x6F, 0x6F, 0x76
    ];

    /// <summary>ISOBMFF ftyp with major brand "3gp4" — 3GPP mobile video.</summary>
    internal static byte[] Create3gp() =>
    [
        0x00, 0x00, 0x00, 0x10,
        0x66, 0x74, 0x79, 0x70,
        0x33, 0x67, 0x70, 0x34,
        0x00, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x08,
        0x6D, 0x6F, 0x6F, 0x76
    ];

    /// <summary>ISOBMFF ftyp with major brand "3g2a" — 3GPP2 (CDMA) mobile video.</summary>
    internal static byte[] Create3g2() =>
    [
        0x00, 0x00, 0x00, 0x10,
        0x66, 0x74, 0x79, 0x70,
        0x33, 0x67, 0x32, 0x61,
        0x00, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x08,
        0x6D, 0x6F, 0x6F, 0x76
    ];

    /// <summary>ISOBMFF ftyp with major brand "M4V " — Apple iTunes video.</summary>
    internal static byte[] CreateM4v() =>
    [
        0x00, 0x00, 0x00, 0x10,
        0x66, 0x74, 0x79, 0x70,
        0x4D, 0x34, 0x56, 0x20,
        0x00, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x08,
        0x6D, 0x6F, 0x6F, 0x76
    ];

    /// <summary>ISOBMFF ftyp with major brand "M4B " — Apple iTunes audiobook.</summary>
    internal static byte[] CreateM4b() =>
    [
        0x00, 0x00, 0x00, 0x10,
        0x66, 0x74, 0x79, 0x70,
        0x4D, 0x34, 0x42, 0x20,
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
        catch { return seed; }
    }

    internal static byte[] CreateMkv() =>
    [
        0x1A, 0x45, 0xDF, 0xA3,
        0x84,
        0x42, 0x86,
        0x81,
        0x01
    ];

    /// <summary>EBML header with DocType = "webm" (WebM shares the EBML container with MKV).</summary>
    internal static byte[] CreateWebM() =>
    [
        0x1A, 0x45, 0xDF, 0xA3,
        0x88,
        0x42, 0x82,
        0x84,
        0x77, 0x65, 0x62, 0x6D
    ];

    /// <summary>Minimal ASF stub used for both WMA (audio) and WMV (video) tests.</summary>
    internal static byte[] CreateWma() => BuildMinimalAsf();

    /// <summary>Same minimal ASF stub as WMA (WMA and WMV share the ASF container).</summary>
    internal static byte[] CreateWmv() => BuildMinimalAsf();

    private static byte[] BuildMinimalAsf()
    {
        // ASF_Header_Object GUID: {75B22630-668E-11CF-A6D9-00AA0062CE6C}
        return new byte[]
        {
            0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
            0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C,
            0x1E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x02
        };
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
        catch { return seed; }
    }

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
        const uint avihSize = 56;
        const uint strhSize = 56;
        const uint strfSize = 40;
        const uint strlSize = 4 + (4 + 4 + strhSize) + (4 + 4 + strfSize);
        const uint hdrlSize = 4 + (4 + 4 + avihSize) + (4 + 4 + strlSize);
        const uint moviSize = 4;
        const uint riffSize = 4 + (4 + 4 + hdrlSize) + (4 + 4 + moviSize);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);

        bw.Write(new byte[] { 0x52, 0x49, 0x46, 0x46 }); // "RIFF"
        bw.Write(riffSize);
        bw.Write(new byte[] { 0x41, 0x56, 0x49, 0x20 }); // "AVI "

        bw.Write(new byte[] { 0x4C, 0x49, 0x53, 0x54 }); // "LIST"
        bw.Write(hdrlSize);
        bw.Write(new byte[] { 0x68, 0x64, 0x72, 0x6C }); // "hdrl"

        bw.Write(new byte[] { 0x61, 0x76, 0x69, 0x68 }); // "avih"
        bw.Write(avihSize);
        bw.Write(new byte[avihSize]);

        bw.Write(new byte[] { 0x4C, 0x49, 0x53, 0x54 }); // "LIST"
        bw.Write(strlSize);
        bw.Write(new byte[] { 0x73, 0x74, 0x72, 0x6C }); // "strl"

        bw.Write(new byte[] { 0x73, 0x74, 0x72, 0x68 }); // "strh"
        bw.Write(strhSize);
        bw.Write(new byte[] { 0x76, 0x69, 0x64, 0x73 }); // "vids"
        bw.Write(new byte[52]);

        bw.Write(new byte[] { 0x73, 0x74, 0x72, 0x66 }); // "strf"
        bw.Write(strfSize);
        bw.Write(new byte[strfSize]);

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
        bw.Write(new byte[] { 0x52, 0x49, 0x46, 0x46 });
        bw.Write((uint)36);
        bw.Write(new byte[] { 0x57, 0x41, 0x56, 0x45 });
        bw.Write(new byte[] { 0x66, 0x6D, 0x74, 0x20 });
        bw.Write((uint)16);    bw.Write((ushort)1);
        bw.Write((ushort)1);   bw.Write((uint)44100);
        bw.Write((uint)88200); bw.Write((ushort)2);
        bw.Write((ushort)16);
        bw.Write(new byte[] { 0x64, 0x61, 0x74, 0x61 });
        bw.Write((uint)0);
        bw.Flush();
        return ms.ToArray();
    }

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
