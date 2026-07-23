using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ImageMagick;
using PdfSharp.Pdf;
using System.IO.Packaging;
using TagLib;

namespace FileMetadataStripping.Tests;

// ── O11 adapter types ─────────────────────────────────────────────────────────
// These mirror the ODC FileMetadataResult / IFileMetadataStripping surface so
// that all five test files are byte-for-byte identical to the ODC test project.

internal struct FileMetadataResult
{
    public byte[]  CleanFile          { get; set; }
    public string  ExtractedMetadata  { get; set; }
    public int     RemovedEntryCount  { get; set; }
    public bool    IsPassthrough      { get; set; }
}

internal interface IFileMetadataStripping
{
    FileMetadataResult StripFileMetadata(byte[] rawFile);
}

internal sealed class FileMetadataStripping : IFileMetadataStripping
{
    private readonly OutSystems.NssFileMetadataStripping.CssFileMetadataStripping _inner = new();

    public FileMetadataResult StripFileMetadata(byte[] rawFile)
    {
        _inner.MssStripFileMetadata(rawFile, out var r);
        return new FileMetadataResult
        {
            CleanFile         = r.ssCleanFile,
            ExtractedMetadata = r.ssExtractedMetadata,
            RemovedEntryCount = r.ssRemovedEntryCount,
            IsPassthrough     = r.ssIsPassthrough
        };
    }
}

// ── Test data helpers ─────────────────────────────────────────────────────────

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

    internal static byte[] CreateAnimatedGif(int frameCount = 3)
    {
        using var images = new MagickImageCollection();
        var colors = new[] { MagickColors.Red, MagickColors.LimeGreen, MagickColors.RoyalBlue };
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new MagickImage(colors[i % colors.Length], 10, 10);
            frame.Format = MagickFormat.Gif;
            frame.AnimationDelay = 10;
            images.Add(frame);
        }
        using var ms = new MemoryStream();
        images.Write(ms, MagickFormat.Gif);
        return ms.ToArray();
    }

    internal static byte[] CreateMultiFrameTiff(int frameCount = 3)
    {
        using var images = new MagickImageCollection();
        var colors = new[] { MagickColors.Red, MagickColors.LimeGreen, MagickColors.RoyalBlue };
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new MagickImage(colors[i % colors.Length], 10, 10);
            frame.Format = MagickFormat.Tiff;
            images.Add(frame);
        }
        using var ms = new MemoryStream();
        images.Write(ms, MagickFormat.Tiff);
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

    internal static byte[] CreatePdfWithXmp()
    {
        var doc = new PdfDocument();
        doc.AddPage();
        // Inject /Metadata as a simple string entry on the catalog.
        // Using a direct stream object is invalid PDF (streams must be indirect),
        // so a PdfString gives a well-formed document that PdfSharp can fully parse
        // in Modify mode — ensuring Elements.Remove() is correctly tracked.
        doc.Internals.Catalog.Elements["/Metadata"] =
            new PdfString("<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'></rdf:RDF></x:xmpmeta>");
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
    /// Creates a byte array with PDF magic bytes followed by invalid content.
    /// PdfSharp will throw PdfReaderException when trying to open this file,
    /// simulating a corrupted or password-protected PDF.
    /// </summary>
    internal static byte[] CreateCorruptedPdf()
    {
        var bytes = new byte[64];
        bytes[0] = 0x25; // %
        bytes[1] = 0x50; // P
        bytes[2] = 0x44; // D
        bytes[3] = 0x46; // F
        // Remaining bytes are 0x00 — no valid cross-reference table or trailer.
        return bytes;
    }

    /// <summary>
    /// Creates a byte array with ZIP/PK magic bytes followed by invalid content.
    /// System.IO.Packaging.Package.Open will throw when trying to parse this file,
    /// simulating a corrupted or password-protected OOXML file.
    /// </summary>
    internal static byte[] CreateCorruptedDocx()
    {
        var bytes = new byte[64];
        bytes[0] = 0x50; // P
        bytes[1] = 0x4B; // K
        bytes[2] = 0x03;
        bytes[3] = 0x04;
        // Remaining bytes are 0x00 — not a valid ZIP local file header.
        return bytes;
    }

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

    internal static byte[] CreateMp4() =>
    [
        0x00, 0x00, 0x00, 0x10,
        0x66, 0x74, 0x79, 0x70,
        0x69, 0x73, 0x6F, 0x6D,
        0x00, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x08,
        0x6D, 0x6F, 0x6F, 0x76
    ];

    internal static byte[] CreateMkv() =>
    [
        0x1A, 0x45, 0xDF, 0xA3,
        0x84,
        0x42, 0x86,
        0x81,
        0x01
    ];

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
        bw.Write(new byte[] { 0x52, 0x49, 0x46, 0x46 });
        bw.Write((uint)4);
        bw.Write(new byte[] { 0x41, 0x56, 0x49, 0x20 });
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
