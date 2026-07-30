using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.IO.Packaging;
using System.Xml.Linq;

namespace FileMetadataStripping.Tests;

internal static partial class TestHelpers
{
    // Office Open XML (DOCX / XLSX / PPTX) and Word 2003 XML test-data helpers.

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

    /// <summary>Creates a DOCX with an embedded thumbnail (docProps/thumbnail.jpeg)
    /// and the corresponding _rels/.rels relationship. The thumbnail image is a
    /// programmatically-generated JPEG so no binary asset is committed.</summary>
    internal static byte[] CreateDocxWithThumbnail(byte[]? thumbnailJpegBytes = null)
    {
        var rawDocx = CreateDocx();
        var ms = new MemoryStream();
        ms.Write(rawDocx, 0, rawDocx.Length);
        ms.Position = 0;

        // Build a small JPEG on the fly if none was provided.
        thumbnailJpegBytes ??= CreateJpeg();

        using (var package = Package.Open(ms, FileMode.Open, FileAccess.ReadWrite))
        {
            var thumbUri = PackUriHelper.CreatePartUri(new Uri("/docProps/thumbnail.jpeg", UriKind.Relative));
            var thumbPart = package.CreatePart(thumbUri, "image/jpeg", CompressionOption.NotCompressed);
            package.CreateRelationship(thumbUri, TargetMode.Internal,
                "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail");
            using var stream = thumbPart.GetStream(FileMode.Create, FileAccess.Write);
            stream.Write(thumbnailJpegBytes, 0, thumbnailJpegBytes.Length);
        }

        return ms.ToArray();
    }

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

    // ── Word 2003 XML (WordProcessingML) test helper ─────────────────────────
    //
    // Builds a Word 2003 XML document (.xml) whose root is <w:wordDocument> in
    // the Microsoft 2003 WordML namespace. Standard properties live in
    // <o:DocumentProperties>; caller-supplied user-defined values go under
    // <o:CustomDocumentProperties>. A single tracked-change insertion with a
    // configurable w:author is emitted when trackedChangeAuthor is provided,
    // used to exercise the stripBodyAuthors flag.

    internal static byte[] CreateWordMl(
        string? author       = null,
        string? lastAuthor   = null,
        string? company      = null,
        string? manager      = null,
        string? title        = null,
        string? subject      = null,
        string? keywords     = null,
        string? description  = null,
        string? category     = null,
        string? template     = null,
        string? hyperlinkBase = null,
        Dictionary<string, string>? customProperties = null,
        string? trackedChangeAuthor = null)
    {
        XNamespace w   = "http://schemas.microsoft.com/office/word/2003/wordml";
        XNamespace o   = "urn:schemas-microsoft-com:office:office";
        XNamespace aml = "http://schemas.microsoft.com/aml/2001/core";

        var docProps = new XElement(o + "DocumentProperties");
        void Add(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
                docProps.Add(new XElement(o + name, value));
        }
        Add("Author",        author);
        Add("LastAuthor",    lastAuthor);
        Add("Company",       company);
        Add("Manager",       manager);
        Add("Title",         title);
        Add("Subject",       subject);
        Add("Keywords",      keywords);
        Add("Description",   description);
        Add("Category",      category);
        Add("Template",      template);
        Add("HyperlinkBase", hyperlinkBase);

        var root = new XElement(w + "wordDocument",
            new XAttribute(XNamespace.Xmlns + "w", w),
            new XAttribute(XNamespace.Xmlns + "o", o),
            new XAttribute(XNamespace.Xmlns + "aml", aml));

        if (docProps.HasElements) root.Add(docProps);

        if (customProperties != null && customProperties.Count > 0)
        {
            var custom = new XElement(o + "CustomDocumentProperties");
            foreach (var kvp in customProperties)
                custom.Add(new XElement(o + kvp.Key, kvp.Value));
            root.Add(custom);
        }

        // Body — one paragraph, optionally wrapped in a tracked-change insertion
        // whose w:author attribute exercises the stripBodyAuthors flag.
        var run = new XElement(w + "r",
            new XElement(w + "t", "Hello world."));
        XElement paragraph;
        if (trackedChangeAuthor != null)
        {
            paragraph = new XElement(w + "p",
                new XElement(w + "ins",
                    new XAttribute(w + "id",     "1"),
                    new XAttribute(w + "author", trackedChangeAuthor),
                    new XAttribute(w + "date",   "2024-01-01T00:00:00Z"),
                    run));
        }
        else
        {
            paragraph = new XElement(w + "p", run);
        }
        root.Add(new XElement(w + "body", paragraph));

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XProcessingInstruction("mso-application", "progid=\"Word.Document\""),
            root);

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }
}
