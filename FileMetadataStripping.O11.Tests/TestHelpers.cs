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
    FileMetadataResult StripFileMetadata(byte[] rawFile, bool stripBodyAuthors);
}

internal sealed class FileMetadataStripping : IFileMetadataStripping
{
    private readonly OutSystems.NssFileMetadataStripping.CssFileMetadataStripping _inner = new();

    public FileMetadataResult StripFileMetadata(byte[] rawFile, bool stripBodyAuthors)
    {
        _inner.MssStripFileMetadata(rawFile, stripBodyAuthors, out var r);
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

    /// <summary>
    /// Creates a TIFF that carries EXIF as native IFD tags (the real-world camera scenario).
    /// Magick.NET's TIFF encoder silently drops a directly-set ExifProfile, but when a JPEG
    /// with EXIF is re-encoded to TIFF the EXIF APP1 is translated to native TIFF IFD tags.
    /// </summary>
    internal static byte[] CreateTiffFromJpegWithExif(string imageDescription = "test exif in tiff")
    {
        using var img = new MagickImage(MagickColors.White, 10, 10);
        img.Format = MagickFormat.Jpeg;
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.ImageDescription, imageDescription);
        img.SetProfile(exif);
        using var jpegMs = new MemoryStream();
        img.Write(jpegMs);

        using var readBack = new MagickImage(jpegMs.ToArray());

        // Capture EXIF:* attributes from the JPEG before the TIFF encoder drops them
        var exifAttrs = readBack.AttributeNames
            .Where(n => n.StartsWith("EXIF:", StringComparison.OrdinalIgnoreCase))
            .Select(n => (name: n, value: readBack.GetAttribute(n)))
            .ToList();

        // Re-encode as TIFF and re-apply the captured attributes
        readBack.Format = MagickFormat.Tiff;
        foreach (var (name, value) in exifAttrs)
            if (value is not null)
                readBack.SetAttribute(name, value);

        using var tiffMs = new MemoryStream();
        readBack.Write(tiffMs);
        return tiffMs.ToArray();
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

    internal static byte[] CreateXlsx(string? creator = null)
    {
        using var ms = new MemoryStream();
        using (var package = Package.Open(ms, FileMode.Create, FileAccess.ReadWrite))
        {
            var uri = PackUriHelper.CreatePartUri(new Uri("/xl/workbook.xml", UriKind.Relative));
            package.CreatePart(uri, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
            package.CreateRelationship(uri, TargetMode.Internal,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
            if (!string.IsNullOrEmpty(creator)) package.PackageProperties.Creator = creator;
        } // Dispose flushes the ZIP data to the stream before ToArray().
        return ms.ToArray();
    }

    internal static byte[] CreatePptx(string? creator = null)
    {
        using var ms = new MemoryStream();
        using (var package = Package.Open(ms, FileMode.Create, FileAccess.ReadWrite))
        {
            var uri = PackUriHelper.CreatePartUri(new Uri("/ppt/presentation.xml", UriKind.Relative));
            package.CreatePart(uri, "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml");
            package.CreateRelationship(uri, TargetMode.Internal,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
            if (!string.IsNullOrEmpty(creator)) package.PackageProperties.Creator = creator;
        } // Dispose flushes the ZIP data to the stream before ToArray().
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

    // ── Task 8: App and custom properties ─────────────────────────────────────

    internal static byte[] CreateDocxWithAppProperties(string? company = null, string? manager = null)
    {
        // Use Package.Open to inject docProps/app.xml; avoids SDK part creation which
        // can produce inconsistent compressed-data entries on .NET Framework 4.8.
        var rawDocx = CreateDocx();
        var ms = new MemoryStream();
        ms.Write(rawDocx, 0, rawDocx.Length);
        ms.Position = 0;

        using (var package = Package.Open(ms, FileMode.Open, FileAccess.ReadWrite))
        {
            var appUri = PackUriHelper.CreatePartUri(new Uri("/docProps/app.xml", UriKind.Relative));
            var appPart = package.CreatePart(appUri,
                "application/vnd.openxmlformats-officedocument.extended-properties+xml",
                CompressionOption.Normal);
            package.CreateRelationship(appUri, TargetMode.Internal,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties");

            XNamespace ep = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
            var xdoc = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(ep + "Properties",
                    company != null ? new XElement(ep + "Company", company) : null,
                    manager  != null ? new XElement(ep + "Manager",  manager)  : null));
            using var stream = appPart.GetStream(FileMode.Create, FileAccess.Write);
            xdoc.Save(stream);
        }

        return ms.ToArray();
    }

    internal static byte[] CreateDocxWithCustomProperties(Dictionary<string, string> properties)
    {
        var rawDocx = CreateDocx();
        var ms = new MemoryStream();
        ms.Write(rawDocx, 0, rawDocx.Length);
        ms.Position = 0;

        using (var package = Package.Open(ms, FileMode.Open, FileAccess.ReadWrite))
        {
            var customUri = PackUriHelper.CreatePartUri(new Uri("/docProps/custom.xml", UriKind.Relative));
            var customPart = package.CreatePart(customUri,
                "application/vnd.openxmlformats-officedocument.custom-properties+xml",
                CompressionOption.Normal);
            package.CreateRelationship(customUri, TargetMode.Internal,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties");

            XNamespace cp = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
            XNamespace vt = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
            var propsEl = new XElement(cp + "Properties",
                new XAttribute(XNamespace.Xmlns + "vt", vt));
            int pid = 2;
            foreach (var kvp in properties)
            {
                propsEl.Add(new XElement(cp + "property",
                    new XAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"),
                    new XAttribute("pid", pid++),
                    new XAttribute("name", kvp.Key),
                    new XElement(vt + "lpwstr", kvp.Value)));
            }
            var xdoc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), propsEl);
            using var stream = customPart.GetStream(FileMode.Create, FileAccess.Write);
            xdoc.Save(stream);
        }

        return ms.ToArray();
    }

    // ── Task 10: Tracked changes and comment authors ───────────────────────────

    internal static byte[] CreateDocxWithTrackedChanges(string authorName)
    {
        // Start from a valid DOCX, then inject a w:ins element directly into document.xml.
        var rawDocx = CreateDocx();
        var ms = new MemoryStream();
        ms.Write(rawDocx, 0, rawDocx.Length);
        ms.Position = 0;

        using var package = Package.Open(ms, FileMode.Open, FileAccess.ReadWrite);
        var docUri = PackUriHelper.CreatePartUri(new Uri("/word/document.xml", UriKind.Relative));
        var part = package.GetPart(docUri);
        XDocument xdoc;
        using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
            xdoc = XDocument.Load(stream);

        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var body = xdoc.Descendants(w + "body").First();
        body.AddFirst(new XElement(w + "p",
            new XElement(w + "ins",
                new XAttribute(w + "id", "1"),
                new XAttribute(w + "author", authorName),
                new XAttribute(w + "date", "2024-01-01T00:00:00Z"),
                new XElement(w + "r", new XElement(w + "t", "tracked")))));

        using (var stream = part.GetStream(FileMode.Create, FileAccess.Write))
            xdoc.Save(stream);

        package.Close();
        return ms.ToArray();
    }

    internal static byte[] CreateDocxWithComment(string authorName)
    {
        // Inject word/comments.xml via Package.Open to avoid SDK part-creation issues on net48.
        var rawDocx = CreateDocx();
        var ms = new MemoryStream();
        ms.Write(rawDocx, 0, rawDocx.Length);
        ms.Position = 0;

        using (var package = Package.Open(ms, FileMode.Open, FileAccess.ReadWrite))
        {
            var commentsUri = PackUriHelper.CreatePartUri(new Uri("/word/comments.xml", UriKind.Relative));
            var commentsPart = package.CreatePart(commentsUri,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml",
                CompressionOption.Normal);

            // Relationship: word/document.xml → word/comments.xml (relative target)
            var docUri = PackUriHelper.CreatePartUri(new Uri("/word/document.xml", UriKind.Relative));
            package.GetPart(docUri).CreateRelationship(
                new Uri("comments.xml", UriKind.Relative),
                TargetMode.Internal,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments");

            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var xdoc = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(w + "comments",
                    new XElement(w + "comment",
                        new XAttribute(w + "id", "1"),
                        new XAttribute(w + "author", authorName),
                        new XAttribute(w + "initials", "JS"),
                        new XAttribute(w + "date", "2024-01-01T00:00:00Z"),
                        new XElement(w + "p",
                            new XElement(w + "r",
                                new XElement(w + "t", "comment text"))))));
            using var stream = commentsPart.GetStream(FileMode.Create, FileAccess.Write);
            xdoc.Save(stream);
        }

        return ms.ToArray();
    }

    internal static byte[] CreateXlsxWithComments(string authorName)
    {
        using var ms = new MemoryStream();
        using (var package = Package.Open(ms, FileMode.Create, FileAccess.ReadWrite))
        {
            var commentsUri = PackUriHelper.CreatePartUri(new Uri("/xl/comments1.xml", UriKind.Relative));
            var part = package.CreatePart(commentsUri,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml");
            XNamespace xl = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var xdoc = new XDocument(
                new XElement(xl + "comments",
                    new XElement(xl + "authors",
                        new XElement(xl + "author", authorName)),
                    new XElement(xl + "commentList")));
            using var stream = part.GetStream(FileMode.Create);
            xdoc.Save(stream);
        }
        return ms.ToArray();
    }

    internal static byte[] CreatePptxWithCommentAuthors(string authorName)
    {
        using var ms = new MemoryStream();
        using (var package = Package.Open(ms, FileMode.Create, FileAccess.ReadWrite))
        {
            var authorsUri = PackUriHelper.CreatePartUri(new Uri("/ppt/commentAuthors.xml", UriKind.Relative));
            var part = package.CreatePart(authorsUri,
                "application/vnd.openxmlformats-officedocument.presentationml.commentAuthors+xml");
            XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
            var xdoc = new XDocument(
                new XElement(p + "cmAuthorLst",
                    new XElement(p + "cmAuthor",
                        new XAttribute("id", "0"),
                        new XAttribute("name", authorName),
                        new XAttribute("initials", "PX"),
                        new XAttribute("clrIdx", "0"),
                        new XAttribute("lastIdx", "0"))));
            using var stream = part.GetStream(FileMode.Create);
            xdoc.Save(stream);
        }
        return ms.ToArray();
    }

    // ── PDF annotation ───────────────────────────────────────────────────────

    internal static byte[] CreatePdfWithAnnotation(string authorName)
    {
        var doc  = new PdfDocument();
        var page = doc.AddPage();
        var annotDict = new PdfDictionary(doc);
        annotDict.Elements.SetName("/Type",    "/Annot");
        annotDict.Elements.SetName("/Subtype", "/Text");
        annotDict.Elements.SetString("/Author",   authorName);
        annotDict.Elements.SetString("/Contents", "Comment text");
        var rectArray = new PdfArray(doc);
        rectArray.Elements.Add(new PdfInteger(50));
        rectArray.Elements.Add(new PdfInteger(700));
        rectArray.Elements.Add(new PdfInteger(150));
        rectArray.Elements.Add(new PdfInteger(750));
        annotDict.Elements["/Rect"] = rectArray;
        doc.Internals.AddObject(annotDict);
        var annotsArray = new PdfArray(doc);
        annotsArray.Elements.Add(annotDict.Reference!);
        page.Elements["/Annots"] = annotsArray;
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    // ── ODF (LibreOffice ODT/ODS/ODP) ────────────────────────────────────────

    internal static byte[] CreateOdt(string? creator = null, string? title = null,
        Dictionary<string, string>? userDefined = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var mimetypeEntry = zip.CreateEntry("mimetype",
                System.IO.Compression.CompressionLevel.NoCompression);
            using (var s = mimetypeEntry.Open())
            {
                var bytes = System.Text.Encoding.ASCII.GetBytes("application/vnd.oasis.opendocument.text");
                s.Write(bytes, 0, bytes.Length);
            }

            var metaEntry = zip.CreateEntry("meta.xml");
            XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
            XNamespace dc     = "http://purl.org/dc/elements/1.1/";
            XNamespace meta   = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
            var officeMeta = new XElement(office + "meta");
            if (creator     != null) officeMeta.Add(new XElement(dc   + "creator", creator));
            if (title       != null) officeMeta.Add(new XElement(dc   + "title",   title));
            if (userDefined != null)
                foreach (var kvp in userDefined)
                    officeMeta.Add(new XElement(meta + "user-defined",
                        new XAttribute(meta + "name", kvp.Key), kvp.Value));
            var metaDoc = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(office + "document-meta",
                    new XAttribute(XNamespace.Xmlns + "office", office),
                    new XAttribute(XNamespace.Xmlns + "dc",     dc),
                    new XAttribute(XNamespace.Xmlns + "meta",   meta),
                    officeMeta));
            using (var s = metaEntry.Open()) metaDoc.Save(s);

            var manifestEntry = zip.CreateEntry("META-INF/manifest.xml");
            XNamespace mf = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
            var manifestDoc = new XDocument(
                new XElement(mf + "manifest",
                    new XAttribute(XNamespace.Xmlns + "manifest", mf),
                    new XElement(mf + "file-entry",
                        new XAttribute(mf + "full-path",  "/"),
                        new XAttribute(mf + "media-type", "application/vnd.oasis.opendocument.text")),
                    new XElement(mf + "file-entry",
                        new XAttribute(mf + "full-path",  "meta.xml"),
                        new XAttribute(mf + "media-type", "text/xml"))));
            using (var s = manifestEntry.Open()) manifestDoc.Save(s);
        }
        return ms.ToArray();
    }

    // ── OOXML LastPrinted / Identifier ────────────────────────────────────────

    internal static byte[] CreateDocxWithLastPrinted()
    {
        var rawDocx = CreateDocx();
        var ms = new MemoryStream();
        ms.Write(rawDocx, 0, rawDocx.Length);
        ms.Position = 0;
        using (var package = Package.Open(ms, FileMode.Open, FileAccess.ReadWrite))
        {
            package.PackageProperties.LastPrinted = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            package.PackageProperties.Identifier  = "urn:uuid:test-identifier-12345";
        }
        return ms.ToArray();
    }

    // ── Excel xl/persons (Microsoft 365 threaded comments) ───────────────────

    internal static byte[] CreateXlsxWithPersons(string displayName)
    {
        using var ms = new MemoryStream();
        using (var package = Package.Open(ms, FileMode.Create, FileAccess.ReadWrite))
        {
            var personsUri = PackUriHelper.CreatePartUri(
                new Uri("/xl/persons/person.xml", UriKind.Relative));
            var part = package.CreatePart(personsUri,
                "application/vnd.ms-excel.person+xml", CompressionOption.Normal);
            XNamespace ns = "http://schemas.microsoft.com/office/spreadsheetml/2017/11/persons";
            var xdoc = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(ns + "Persons",
                    new XElement(ns + "Person",
                        new XAttribute("id", "{12345678-1234-1234-1234-123456789012}"),
                        new XAttribute("displayName", displayName),
                        new XAttribute("userId",      "user@example.com"),
                        new XAttribute("providerId",  "AD"))));
            using var stream = part.GetStream(FileMode.Create, FileAccess.Write);
            xdoc.Save(stream);
        }
        return ms.ToArray();
    }
}
