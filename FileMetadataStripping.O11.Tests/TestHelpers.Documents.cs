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
    // Document test-data helpers (PDF, Office Open XML, ODF, EPUB, ORA, legacy binary Office).

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

    /// <summary>Creates an ORA (Open Raster) ZIP with mimetype = image/openraster.
    /// Routed to StripOraMetadata via the ZIP mimetype dispatcher.</summary>
    internal static byte[] CreateOra()
    {
        return CreateOra("root", "Background");
    }

    /// <summary>Creates an ORA archive with caller-specified stack and layer names.</summary>
    internal static byte[] CreateOra(string stackName, string layerName)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var mimetypeEntry = zip.CreateEntry("mimetype",
                System.IO.Compression.CompressionLevel.NoCompression);
            using (var s = mimetypeEntry.Open())
            {
                var b = System.Text.Encoding.ASCII.GetBytes("image/openraster");
                s.Write(b, 0, b.Length);
            }
            var stackEntry = zip.CreateEntry("stack.xml");
            using (var s = stackEntry.Open())
            {
                var xml = System.Text.Encoding.UTF8.GetBytes(
                    "<?xml version='1.0'?><image w='10' h='10'>" +
                    $"<stack opacity='1' name='{stackName}'>" +
                    $"<layer name='{layerName}' src='data/layer0.png'/>" +
                    "</stack></image>");
                s.Write(xml, 0, xml.Length);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Creates an EPUB ZIP with mimetype = application/epub+zip.
    /// Contains only container.xml — no OPF — so the strip path exits cleanly with 0 removals.</summary>
    internal static byte[] CreateEpub()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var mimetypeEntry = zip.CreateEntry("mimetype",
                System.IO.Compression.CompressionLevel.NoCompression);
            using (var s = mimetypeEntry.Open())
            {
                var b = System.Text.Encoding.ASCII.GetBytes("application/epub+zip");
                s.Write(b, 0, b.Length);
            }
            var containerEntry = zip.CreateEntry("META-INF/container.xml");
            using (var s = containerEntry.Open())
            {
                var xml = System.Text.Encoding.UTF8.GetBytes(
                    "<?xml version='1.0'?>" +
                    "<container version='1.0' xmlns='urn:oasis:names:tc:opendocument:xmlns:container'>" +
                    "<rootfiles><rootfile full-path='OEBPS/content.opf' " +
                    "media-type='application/oebps-package+xml'/></rootfiles></container>");
                s.Write(xml, 0, xml.Length);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Creates an EPUB with a full OPF package document carrying Dublin Core metadata.</summary>
    internal static byte[] CreateEpubWithOpf(string creator, string title, string? description = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var mimetypeEntry = zip.CreateEntry("mimetype",
                System.IO.Compression.CompressionLevel.NoCompression);
            using (var s = mimetypeEntry.Open())
            {
                var b = System.Text.Encoding.ASCII.GetBytes("application/epub+zip");
                s.Write(b, 0, b.Length);
            }
            var containerEntry = zip.CreateEntry("META-INF/container.xml");
            using (var s = containerEntry.Open())
            {
                var xml = System.Text.Encoding.UTF8.GetBytes(
                    "<?xml version='1.0'?>" +
                    "<container version='1.0' xmlns='urn:oasis:names:tc:opendocument:xmlns:container'>" +
                    "<rootfiles><rootfile full-path='OEBPS/content.opf' " +
                    "media-type='application/oebps-package+xml'/></rootfiles></container>");
                s.Write(xml, 0, xml.Length);
            }
            var opfEntry = zip.CreateEntry("OEBPS/content.opf");
            using (var s = opfEntry.Open())
            {
                var descLine = description != null
                    ? $"<dc:description>{description}</dc:description>" : string.Empty;
                var xml = System.Text.Encoding.UTF8.GetBytes(
                    "<?xml version='1.0' encoding='UTF-8'?>" +
                    "<package xmlns='http://www.idpf.org/2007/opf' version='3.0' unique-identifier='pub-id'>" +
                    "<metadata xmlns:dc='http://purl.org/dc/elements/1.1/'>" +
                    $"<dc:creator>{creator}</dc:creator>" +
                    $"<dc:title>{title}</dc:title>" +
                    descLine +
                    "</metadata>" +
                    "<manifest/><spine/></package>");
                s.Write(xml, 0, xml.Length);
            }
        }
        return ms.ToArray();
    }

    internal static byte[] CreateCfbf(
        string?    title       = "Confidential Report",
        string?    subject     = null,
        string?    author      = "Alice Attacker",
        string?    keywords    = null,
        string?    comments    = null,
        string?    lastSavedBy = null,
        string?    application = "Microsoft Office Word",
        string?    company     = "Acme Corp",
        string?    manager     = null,
        string?    category    = null,
        Dictionary<string, string>? customProperties = null)
    {
        var ms = new MemoryStream();
        using (var root = OpenMcdf.RootStorage.Create(
                   ms, OpenMcdf.Version.V3, OpenMcdf.StorageModeFlags.LeaveOpen))
        {
            var summary = new OpenMcdf.Ole.OlePropertiesContainer(
                1252, OpenMcdf.Ole.ContainerType.SummaryInfo);
            AddCodePage(summary, 1252);
            AddLpstr(summary, 0x02, title);
            AddLpstr(summary, 0x03, subject);
            AddLpstr(summary, 0x04, author);
            AddLpstr(summary, 0x05, keywords);
            AddLpstr(summary, 0x06, comments);
            AddLpstr(summary, 0x08, lastSavedBy);
            AddLpstr(summary, 0x12, application);
            using (var summaryStream = root.CreateStream("\u0005SummaryInformation"))
                summary.Save(summaryStream);

            var docSummary = new OpenMcdf.Ole.OlePropertiesContainer(
                1252, OpenMcdf.Ole.ContainerType.DocumentSummaryInfo);
            AddCodePage(docSummary, 1252);
            AddLpstr(docSummary, 0x02, category);
            AddLpstr(docSummary, 0x0E, manager);
            AddLpstr(docSummary, 0x0F, company);

            if (customProperties != null && customProperties.Count > 0)
            {
                var udp = docSummary.CreateUserDefinedProperties(1252);
                foreach (var kv in customProperties)
                {
                    var p = udp.AddUserDefinedProperty(
                        OpenMcdf.Ole.VTPropertyType.VT_LPSTR, kv.Key);
                    p.Value = kv.Value;
                }
            }

            using (var docSummaryStream = root.CreateStream(
                       "\u0005DocumentSummaryInformation"))
                docSummary.Save(docSummaryStream);

            root.Flush();
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a CFBF file with NO property-set streams. Used to verify the
    /// "clean baseline" contract: a valid CFBF that is detected (IsPassthrough
    /// = false) but leaves RemovedEntryCount = 0.
    /// </summary>
    internal static byte[] CreateCfbfWithoutMetadata()
    {
        var ms = new MemoryStream();
        using (var root = OpenMcdf.RootStorage.Create(
                   ms, OpenMcdf.Version.V3, OpenMcdf.StorageModeFlags.LeaveOpen))
        {
            using var body = root.CreateStream("WordDocument");
            var placeholder = new byte[16];
            body.Write(placeholder, 0, placeholder.Length);
            root.Flush();
        }
        return ms.ToArray();
    }

    private static void AddLpstr(
        OpenMcdf.Ole.OlePropertiesContainer container, uint id, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var prop = container.CreateProperty(
            OpenMcdf.Ole.VTPropertyType.VT_LPSTR, id);
        prop.Value = value;
        container.Add(prop);
    }

    private static void AddCodePage(
        OpenMcdf.Ole.OlePropertiesContainer container, int codePage)
    {
        var prop = container.CreateProperty(
            OpenMcdf.Ole.VTPropertyType.VT_I2, 0x01);
        prop.Value = (short)codePage;
        container.Add(prop);
    }

}
