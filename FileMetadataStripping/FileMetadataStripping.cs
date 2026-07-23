using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO.Packaging;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using TagLib;

namespace FileMetadataStripping;

public class FileMetadataStripping : IFileMetadataStripping
{
    private enum FileCategory { Image, Pdf, OpenXml, Odf, Media, Passthrough }

    public FileMetadataResult StripFileMetadata(byte[] rawFile, bool stripBodyAuthors)
    {
        return DetectCategory(rawFile) switch
        {
            FileCategory.Image       => StripImageMetadata(rawFile),
            FileCategory.Pdf         => StripPdfMetadata(rawFile),
            FileCategory.OpenXml     => StripOpenXmlMetadata(rawFile, stripBodyAuthors),
            FileCategory.Odf         => StripOdfMetadata(rawFile),
            FileCategory.Media       => StripMediaMetadata(rawFile),
            FileCategory.Passthrough => Passthrough(rawFile),
            _                        => Passthrough(rawFile)
        };
    }

    // ── Detection ─────────────────────────────────────────────────────────────

    private static FileCategory DetectCategory(byte[] rawFile)
    {
        // PDF: %PDF magic bytes (0x25 0x50 0x44 0x46) — check before image (PDF can have JPEG previews)
        if (rawFile.Length >= 4
            && rawFile[0] == 0x25 && rawFile[1] == 0x50
            && rawFile[2] == 0x44 && rawFile[3] == 0x46)
            return FileCategory.Pdf;

        // Office Open XML (DOCX/XLSX/PPTX) or ODF (ODT/ODS/ODP): ZIP PK signature (0x50 0x4B 0x03 0x04)
        if (rawFile.Length >= 4
            && rawFile[0] == 0x50 && rawFile[1] == 0x4B
            && rawFile[2] == 0x03 && rawFile[3] == 0x04)
            return IsOdfFormat(rawFile) ? FileCategory.Odf : FileCategory.OpenXml;

        // Images: JPEG, PNG, GIF, BMP, TIFF, WebP, TGA, and 100+ more — detected by Magick.NET
        try
        {
            var info = new MagickImageInfo(rawFile);
            if (info.Format != MagickFormat.Unknown)
                return FileCategory.Image;
        }
        catch (MagickException) { }

        // Audio/video — detected by magic bytes (TagLibSharp handles these formats)
        // MP3: ID3 header
        if (rawFile.Length >= 3 && rawFile[0] == 0x49 && rawFile[1] == 0x44 && rawFile[2] == 0x33)
            return FileCategory.Media;
        // FLAC: fLaC
        if (rawFile.Length >= 4 && rawFile[0] == 0x66 && rawFile[1] == 0x4C && rawFile[2] == 0x61 && rawFile[3] == 0x43)
            return FileCategory.Media;
        // OGG: OggS
        if (rawFile.Length >= 4 && rawFile[0] == 0x4F && rawFile[1] == 0x67 && rawFile[2] == 0x67 && rawFile[3] == 0x53)
            return FileCategory.Media;
        // RIFF container: WAV (WAVE) or AVI (AVI )
        if (rawFile.Length >= 12 && rawFile[0] == 0x52 && rawFile[1] == 0x49 && rawFile[2] == 0x46 && rawFile[3] == 0x46
            && ((rawFile[8] == 0x57 && rawFile[9] == 0x41 && rawFile[10] == 0x56 && rawFile[11] == 0x45)
             || (rawFile[8] == 0x41 && rawFile[9] == 0x56 && rawFile[10] == 0x49 && rawFile[11] == 0x20)))
            return FileCategory.Media;
        // ISO Base Media (MP4, M4A, M4V, MOV): "ftyp" at bytes 4–7
        if (rawFile.Length >= 8 && rawFile[4] == 0x66 && rawFile[5] == 0x74 && rawFile[6] == 0x79 && rawFile[7] == 0x70)
            return FileCategory.Media;
        // Matroska / WebM: EBML header
        if (rawFile.Length >= 4 && rawFile[0] == 0x1A && rawFile[1] == 0x45 && rawFile[2] == 0xDF && rawFile[3] == 0xA3)
            return FileCategory.Media;
        // WMA / ASF
        if (rawFile.Length >= 4 && rawFile[0] == 0x30 && rawFile[1] == 0x26 && rawFile[2] == 0xB2 && rawFile[3] == 0x75)
            return FileCategory.Media;

        // No known metadata format (plain text, CSV, JSON, XML, etc.) — passthrough
        return FileCategory.Passthrough;
    }

    // ── Image ─────────────────────────────────────────────────────────────────

    private static FileMetadataResult StripImageMetadata(byte[] rawFile)
    {
        using var images = new MagickImageCollection(rawFile);

        // Extract metadata from the first frame; file-level profiles live there.
        var (extractedMetadata, removedEntryCount) = ExtractImageMetadata((MagickImage)images[0]);

        // Strip every frame — preserves animated GIFs and multi-frame TIFFs in full.
        foreach (var frame in images)
            frame.Strip(); // removes EXIF, IPTC, XMP, ICC profiles, and comments

        using var output = new MemoryStream();
        images.Write(output); // preserves original format and all frames automatically

        return new FileMetadataResult
        {
            CleanFile         = output.ToArray(),
            ExtractedMetadata = extractedMetadata,
            RemovedEntryCount = removedEntryCount,
            IsPassthrough     = false
        };
    }

    private static (string json, int count) ExtractImageMetadata(MagickImage image)
    {
        var root  = new JsonObject();
        var count = 0;

        var exifProfile = image.GetExifProfile();
        if (exifProfile != null)
        {
            var exifNode = new JsonObject();
            foreach (var v in exifProfile.Values)
            {
                exifNode[v.Tag.ToString()] = JsonValue.Create(v.GetValue()?.ToString());
                count++;
            }
            root["exif"] = exifNode;
        }

        var iptcProfile = image.GetIptcProfile();
        if (iptcProfile != null)
        {
            var iptcArray = new JsonArray();
            foreach (var v in iptcProfile.Values)
            {
                iptcArray.Add(new JsonObject
                {
                    ["tag"]   = JsonValue.Create(v.Tag.ToString()),
                    ["value"] = JsonValue.Create(v.Value)
                });
                count++;
            }
            root["iptc"] = iptcArray;
        }

        var xmpProfile = image.GetXmpProfile();
        if (xmpProfile != null)
        {
            root["xmp"] = "present";
            count++;
        }

        return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
    }

    // ── PDF ───────────────────────────────────────────────────────────────────

    private static FileMetadataResult StripPdfMetadata(byte[] rawFile)
    {
        using var input = new MemoryStream(rawFile);
        PdfDocument source;
        try
        {
            source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        }
        catch (Exception ex) when (
            ex is PdfReaderException        ||
            ex is InvalidOperationException ||
            ex is NotSupportedException)
        {
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the PDF could not be opened (it may be encrypted or password-protected). " +
                    $"Original file returned unchanged. Reason: {ex.GetType().Name}: {ex.Message}")
            };
            return new FileMetadataResult
            {
                CleanFile         = rawFile,
                ExtractedMetadata = note.ToJsonString(),
                RemovedEntryCount = 0,
                IsPassthrough     = false
            };
        }

        using (source)
        {
            // Extract metadata from the source document before creating the clean copy.
            var (extractedMetadata, removedEntryCount) = ExtractPdfMetadata(source);

            // Create a fresh document and copy all pages.
            // A new document has neither /Info metadata nor a catalog /Metadata entry.
            using var dest   = new PdfDocument();
            using var output = new MemoryStream();

            // Copy pages and clear annotation /Author entries on each destination page.
            var annotAuthors  = new HashSet<string>(StringComparer.Ordinal);
            int annotEntries  = 0;
            for (int i = 0; i < source.PageCount; i++)
            {
                var destPage = dest.AddPage(source.Pages[i]);
                var (cleared, authors) = ClearPageAnnotationAuthors(destPage);
                annotEntries += cleared;
                foreach (var a in authors) annotAuthors.Add(a);
            }

            if (annotEntries > 0)
            {
                var metaNode = extractedMetadata == "[]"
                    ? new JsonObject()
                    : JsonNode.Parse(extractedMetadata)!.AsObject();
                var arr = new JsonArray();
                foreach (var a in annotAuthors.OrderBy(x => x)) arr.Add(JsonValue.Create(a));
                metaNode["annotationAuthors"] = arr;
                extractedMetadata = metaNode.ToJsonString();
                removedEntryCount += annotEntries;
            }

            // Explicitly blank /Info fields so read-back returns string.Empty rather than null.
            dest.Info.Title    = string.Empty;
            dest.Info.Author   = string.Empty;
            dest.Info.Subject  = string.Empty;
            dest.Info.Keywords = string.Empty;
            dest.Info.Creator  = string.Empty;

            dest.Save(output);

            // PdfSharp 6.x's PdfCatalog.PrepareForSave() re-adds /Metadata to the
            // catalog during Save, even when removed from Elements beforehand.
            // Post-process the output bytes: replace every /Metadata indirect-reference
            // token with an equal-length whitespace run.  Because no bytes are added or
            // removed, all XRef byte offsets stay valid and the file remains well-formed.
            var cleanBytes = EraseCatalogXmpKey(output.ToArray());

            return new FileMetadataResult
            {
                CleanFile         = cleanBytes,
                ExtractedMetadata = extractedMetadata,
                RemovedEntryCount = removedEntryCount,
                IsPassthrough     = false
            };
        }
    }

    /// <summary>
    /// Replaces every /Metadata indirect-reference token in a PDF file with an
    /// equal-length run of spaces.  This neutralises the catalog XMP entry that
    /// PdfSharp 6.x writes unconditionally during PrepareForSave, without altering
    /// any byte positions (so all XRef offsets remain valid).
    /// </summary>
    private static byte[] EraseCatalogXmpKey(byte[] pdfBytes)
    {
        // PDF uses Latin-1 (ISO 8859-1) for its syntactic structure; converting to
        // a Latin-1 string and back is a lossless round-trip for every byte value.
        var text = System.Text.Encoding.Latin1.GetString(pdfBytes);

        // In PdfSharp 6.x output the catalog /Metadata value is always an indirect
        // object reference:  /Metadata N M R  (e.g. /Metadata 6 0 R)
        // Replacing with the same number of ASCII spaces preserves byte positions.
        var patched = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"/Metadata\s+\d+\s+\d+\s+R",
            m => new string(' ', m.Length));

        // Safety net for the edge case where PdfSharp preserved a direct PdfString.
        patched = System.Text.RegularExpressions.Regex.Replace(
            patched,
            @"/Metadata\s*\([^)]*\)",
            m => new string(' ', m.Length));

        return System.Text.Encoding.Latin1.GetBytes(patched);
    }

    private static (string json, int count) ExtractPdfMetadata(PdfDocument document)
    {
        var root  = new JsonObject();
        var count = 0;
        var info  = document.Info;

        void Capture(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value)) { root[key] = JsonValue.Create(value); count++; }
        }

        Capture("title",    info.Title);
        Capture("author",   info.Author);
        Capture("subject",  info.Subject);
        Capture("keywords", info.Keywords);
        Capture("creator",  info.Creator);
        Capture("producer", info.Producer);

        if (document.Internals.Catalog.Elements.ContainsKey("/Metadata"))
        {
            root["xmp"] = "present";
            count++;
        }

        return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
    }

    // ── Office Open XML (DOCX / XLSX / PPTX) ─────────────────────────────────

    private static FileMetadataResult StripOpenXmlMetadata(byte[] rawFile, bool stripBodyAuthors)
    {
        try
        {
            var ms = new MemoryStream();
            ms.Write(rawFile, 0, rawFile.Length);
            ms.Position = 0;

            var root       = new JsonObject();
            var count      = 0;
            // Collects modified XDocuments keyed by ZIP entry name (e.g. "docProps/app.xml").
            var partWrites = new Dictionary<string, XDocument>();

            using (var package = Package.Open(ms, FileMode.Open, FileAccess.ReadWrite))
            {
                // Core properties (docProps/core.xml) — clear via PackageProperties API.
                ExtractOpenXmlCoreMetadata(package.PackageProperties, root, ref count);
                var coreProps = package.PackageProperties;
                coreProps.Creator        = null;
                coreProps.LastModifiedBy = null;
                coreProps.Created        = null;
                coreProps.Modified       = null;
                coreProps.Title          = null;
                coreProps.Subject        = null;
                coreProps.Description    = null;
                coreProps.Keywords       = null;
                coreProps.Category       = null;
                coreProps.ContentStatus  = null;
                coreProps.Revision       = null;
                coreProps.LastPrinted    = null;
                coreProps.Identifier     = null;
                coreProps.Version        = null;

                // App / custom / body — READ and stage XML modifications for later write.
                ExtractAndClearAppProperties(package, root, ref count, partWrites);
                ExtractAndClearCustomProperties(package, root, ref count, partWrites);
                if (stripBodyAuthors)
                    StripOoxmlAuthorNames(package, root, ref count, partWrites);
            }
            // Package is now closed; core property changes have been flushed to ms.

            // Apply staged XML part modifications using ZipArchive — avoids PackagePart
            // write-back issues and works reliably on all target platforms.
            if (partWrites.Count > 0)
            {
                // Copy to a fresh expandable stream so ZipArchive.Update starts from a
                // consistent baseline without any tail bytes left by Package.Close().
                var packageBytes = ms.ToArray();
                using var zipMs = new MemoryStream();
                zipMs.Write(packageBytes, 0, packageBytes.Length);
                zipMs.Position = 0;
                using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Update, leaveOpen: true))
                {
                    foreach (var kvp in partWrites)
                    {
                        zip.GetEntry(kvp.Key)?.Delete();
                        using var s = zip.CreateEntry(kvp.Key).Open();
                        kvp.Value.Save(s);
                    }
                }
                return new FileMetadataResult
                {
                    CleanFile         = zipMs.ToArray(),
                    ExtractedMetadata = count > 0 ? root.ToJsonString() : "[]",
                    RemovedEntryCount = count,
                    IsPassthrough     = false
                };
            }

            return new FileMetadataResult
            {
                CleanFile         = ms.ToArray(),
                ExtractedMetadata = count > 0 ? root.ToJsonString() : "[]",
                RemovedEntryCount = count,
                IsPassthrough     = false
            };
        }
        catch (Exception ex) when (
            ex is FileFormatException  ||
            ex is InvalidDataException ||
            ex is NotSupportedException)
        {
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the OOXML file could not be opened (it may be encrypted or password-protected). " +
                    $"Original file returned unchanged. Reason: {ex.GetType().Name}: {ex.Message}")
            };
            return new FileMetadataResult
            {
                CleanFile         = rawFile,
                ExtractedMetadata = note.ToJsonString(),
                RemovedEntryCount = 0,
                IsPassthrough     = false
            };
        }
    }

    private static void ExtractOpenXmlCoreMetadata(PackageProperties props, JsonObject root, ref int count)
    {
        int localCount = 0;
        void Capture(string key, object? value)
        {
            var str = value?.ToString();
            if (!string.IsNullOrEmpty(str)) { root[key] = JsonValue.Create(str); localCount++; }
        }

        Capture("creator",        props.Creator);
        Capture("lastModifiedBy", props.LastModifiedBy);
        Capture("created",        props.Created);
        Capture("modified",       props.Modified);
        Capture("title",          props.Title);
        Capture("subject",        props.Subject);
        Capture("description",    props.Description);
        Capture("keywords",       props.Keywords);
        Capture("category",       props.Category);
        Capture("contentStatus",  props.ContentStatus);
        Capture("revision",       props.Revision);
        Capture("lastPrinted",    props.LastPrinted);
        Capture("identifier",     props.Identifier);
        Capture("version",        props.Version);

        count += localCount;
    }

    private static void ExtractAndClearAppProperties(Package package, JsonObject root, ref int count,
        Dictionary<string, XDocument> partWrites)
    {
        var appUri = PackUriHelper.CreatePartUri(new Uri("/docProps/app.xml", UriKind.Relative));
        if (!package.PartExists(appUri)) return;

        XDocument xdoc;
        using (var stream = package.GetPart(appUri).GetStream(FileMode.Open, FileAccess.Read))
            xdoc = XDocument.Load(stream);

        XNamespace ep = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
        bool modified = false;
        var fields = new[]
        {
            ("Application",   "appApplication"),
            ("Company",       "appCompany"),
            ("Manager",       "appManager"),
            ("AppVersion",    "appVersion"),
            ("Template",      "appTemplate"),
            ("HyperlinkBase", "appHyperlinkBase")
        };
        foreach (var (xmlField, jsonKey) in fields)
        {
            var el = xdoc.Root?.Element(ep + xmlField);
            if (el != null && !string.IsNullOrEmpty(el.Value))
            {
                root[jsonKey] = JsonValue.Create(el.Value);
                count++;
                el.Value = string.Empty;
                modified = true;
            }
        }

        if (modified)
            partWrites["docProps/app.xml"] = xdoc;
    }

    private static void ExtractAndClearCustomProperties(Package package, JsonObject root, ref int count,
        Dictionary<string, XDocument> partWrites)
    {
        var customUri = PackUriHelper.CreatePartUri(new Uri("/docProps/custom.xml", UriKind.Relative));
        if (!package.PartExists(customUri)) return;

        XDocument xdoc;
        using (var stream = package.GetPart(customUri).GetStream(FileMode.Open, FileAccess.Read))
            xdoc = XDocument.Load(stream);

        XNamespace cp = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
        var customProps = new JsonObject();
        foreach (var prop in xdoc.Root?.Elements(cp + "property") ?? Enumerable.Empty<XElement>())
        {
            var name  = prop.Attribute("name")?.Value;
            var value = prop.Elements().FirstOrDefault()?.Value;
            if (!string.IsNullOrEmpty(name))
            {
                customProps[name] = JsonValue.Create(value ?? string.Empty);
                count++;
            }
        }

        if (customProps.Count > 0)
        {
            root["customProperties"] = customProps;
            xdoc.Root?.RemoveNodes();
            partWrites["docProps/custom.xml"] = xdoc;
        }
    }

    private static readonly HashSet<string> _wordAuthorContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml"
    };

    private static void StripOoxmlAuthorNames(Package package, JsonObject root, ref int count,
        Dictionary<string, XDocument> partWrites)
    {
        var authorNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in package.GetParts())
        {
            var ct = part.ContentType;
            XDocument? modified;
            if (_wordAuthorContentTypes.Contains(ct))
                modified = StripWordAuthorAttributes(part, authorNames);
            else if (ct == "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml")
                modified = StripExcelCommentAuthors(part, authorNames);
            else if (ct == "application/vnd.ms-excel.person+xml")
                modified = StripExcelPersonAuthors(part, authorNames);
            else if (ct == "application/vnd.openxmlformats-officedocument.presentationml.commentAuthors+xml")
                modified = StripPptCommentAuthors(part, authorNames);
            else
                continue;

            if (modified != null)
                partWrites[part.Uri.ToString().TrimStart('/')] = modified;
        }

        if (authorNames.Count > 0)
        {
            var arr = new JsonArray();
            foreach (var name in authorNames.OrderBy(x => x))
                arr.Add(JsonValue.Create(name));
            root["strippedAuthors"] = arr;
            count += authorNames.Count;
        }
    }

    /// <summary>
    /// Blanks <c>w:author</c> and <c>w:initials</c> on every element in a Word part.
    /// Returns the modified <see cref="XDocument"/> when changes were made, otherwise <see langword="null"/>.
    /// </summary>
    private static XDocument? StripWordAuthorAttributes(PackagePart part, HashSet<string> authorNames)
    {
        XDocument xdoc;
        using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
            xdoc = XDocument.Load(stream);

        XNamespace w       = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XName authorAttr   = w + "author";
        XName initialsAttr = w + "initials";
        bool modified      = false;

        foreach (var el in xdoc.Descendants())
        {
            var author = el.Attribute(authorAttr);
            if (author != null && !string.IsNullOrEmpty(author.Value))
            {
                authorNames.Add(author.Value);
                author.Value = string.Empty;
                modified = true;
            }
            var initials = el.Attribute(initialsAttr);
            if (initials != null && !string.IsNullOrEmpty(initials.Value))
            {
                initials.Value = string.Empty;
                modified = true;
            }
        }

        return modified ? xdoc : null;
    }

    /// <summary>
    /// Blanks author text elements in an Excel worksheet comments part.
    /// Returns the modified <see cref="XDocument"/> when changes were made, otherwise <see langword="null"/>.
    /// </summary>
    private static XDocument? StripExcelCommentAuthors(PackagePart part, HashSet<string> authorNames)
    {
        XDocument xdoc;
        using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
            xdoc = XDocument.Load(stream);

        XNamespace xl = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        bool modified = false;

        foreach (var el in xdoc.Descendants(xl + "author"))
        {
            if (!string.IsNullOrEmpty(el.Value))
            {
                authorNames.Add(el.Value);
                el.Value = string.Empty;
                modified = true;
            }
        }

        return modified ? xdoc : null;
    }

    /// <summary>
    /// Blanks <c>name</c> and <c>initials</c> attributes in a PowerPoint comment authors part.
    /// Returns the modified <see cref="XDocument"/> when changes were made, otherwise <see langword="null"/>.
    /// </summary>
    private static XDocument? StripPptCommentAuthors(PackagePart part, HashSet<string> authorNames)
    {
        XDocument xdoc;
        using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
            xdoc = XDocument.Load(stream);

        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        bool modified = false;

        foreach (var el in xdoc.Descendants(p + "cmAuthor"))
        {
            var name = el.Attribute("name");
            if (name != null && !string.IsNullOrEmpty(name.Value))
            {
                authorNames.Add(name.Value);
                name.Value = string.Empty;
                modified = true;
            }
            var initials = el.Attribute("initials");
            if (initials != null && !string.IsNullOrEmpty(initials.Value))
            {
                initials.Value = string.Empty;
                modified = true;
            }
        }

        return modified ? xdoc : null;
    }

    /// <summary>
    /// Blanks <c>displayName</c> and <c>userId</c> on Excel modern person entries
    /// (<c>xl/persons/person.xml</c>, Microsoft 365 threaded comments).
    /// </summary>
    private static XDocument? StripExcelPersonAuthors(PackagePart part, HashSet<string> authorNames)
    {
        XDocument xdoc;
        using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
            xdoc = XDocument.Load(stream);

        XNamespace ns = "http://schemas.microsoft.com/office/spreadsheetml/2017/11/persons";
        bool modified = false;

        foreach (var el in xdoc.Descendants(ns + "Person"))
        {
            var displayName = el.Attribute("displayName");
            if (displayName != null && !string.IsNullOrEmpty(displayName.Value))
            {
                authorNames.Add(displayName.Value);
                displayName.Value = string.Empty;
                modified = true;
            }
            var userId = el.Attribute("userId");
            if (userId != null && !string.IsNullOrEmpty(userId.Value))
            {
                userId.Value = string.Empty;
                modified = true;
            }
        }

        return modified ? xdoc : null;
    }

    /// <summary>
    /// Removes the <c>/Author</c> entry from every annotation on a PDF page.
    /// Returns the number of entries cleared and the set of distinct author names found.
    /// </summary>
    private static (int cleared, string[] authors) ClearPageAnnotationAuthors(PdfPage page)
    {
        if (!page.Elements.ContainsKey("/Annots")) return (0, Array.Empty<string>());

        var annotsObj = page.Elements["/Annots"];
        var annots    = annotsObj as PdfArray;
        if (annots == null && annotsObj is PdfReference ar) annots = ar.Value as PdfArray;
        if (annots == null || annots.Elements.Count == 0) return (0, Array.Empty<string>());

        var authors = new HashSet<string>(StringComparer.Ordinal);
        int cleared = 0;

        for (int j = 0; j < annots.Elements.Count; j++)
        {
            var item      = annots.Elements[j];
            var annotDict = item as PdfDictionary;
            if (annotDict == null && item is PdfReference annotRef)
                annotDict = annotRef.Value as PdfDictionary;
            if (annotDict == null) continue;

            if (annotDict.Elements.ContainsKey("/Author"))
            {
                var author = annotDict.Elements.GetString("/Author");
                if (!string.IsNullOrEmpty(author)) authors.Add(author);
                annotDict.Elements.Remove("/Author");
                cleared++;
            }
        }

        return (cleared, authors.ToArray());
    }

    // ── ODF (LibreOffice ODT / ODS / ODP) ─────────────────────────────────────

    private static bool IsOdfFormat(byte[] rawFile)
    {
        try
        {
            using var ms  = new MemoryStream(rawFile, writable: false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
            var entry = zip.GetEntry("mimetype");
            if (entry == null) return false;
            using var stream = entry.Open();
            using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false, bufferSize: 64, leaveOpen: false);
            var mime = reader.ReadToEnd().Trim();
            return mime.StartsWith("application/vnd.oasis.opendocument.",
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static FileMetadataResult StripOdfMetadata(byte[] rawFile)
    {
        try
        {
            var zipMs = new MemoryStream();
            zipMs.Write(rawFile, 0, rawFile.Length);
            zipMs.Position = 0;

            int    count            = 0;
            string extractedMetadata = "[]";

            using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Update, leaveOpen: true))
            {
                var metaEntry = zip.GetEntry("meta.xml");
                if (metaEntry != null)
                {
                    XDocument xdoc;
                    using (var s = metaEntry.Open()) xdoc = XDocument.Load(s);

                    var (json, n) = ExtractAndClearOdfMetadata(xdoc);
                    if (n > 0)
                    {
                        extractedMetadata = json;
                        count             = n;
                        metaEntry.Delete();
                        using var ws = zip.CreateEntry("meta.xml").Open();
                        xdoc.Save(ws);
                    }
                }
            }

            return new FileMetadataResult
            {
                CleanFile         = zipMs.ToArray(),
                ExtractedMetadata = extractedMetadata,
                RemovedEntryCount = count,
                IsPassthrough     = false
            };
        }
        catch (Exception ex) when (
            ex is InvalidDataException    ||
            ex is NotSupportedException   ||
            ex is System.Xml.XmlException)
        {
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the ODF file could not be opened. " +
                    $"Original file returned unchanged. Reason: {ex.GetType().Name}: {ex.Message}")
            };
            return new FileMetadataResult
            {
                CleanFile         = rawFile,
                ExtractedMetadata = note.ToJsonString(),
                RemovedEntryCount = 0,
                IsPassthrough     = false
            };
        }
    }

    private static (string json, int count) ExtractAndClearOdfMetadata(XDocument metaDoc)
    {
        XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        XNamespace dc     = "http://purl.org/dc/elements/1.1/";
        XNamespace meta   = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";

        var officeMeta = metaDoc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "meta"
                              && e.Name.Namespace == office);
        if (officeMeta == null) return ("[]", 0);

        var root  = new JsonObject();
        var count = 0;

        void Capture(string key, XElement? el)
        {
            if (el != null && !string.IsNullOrEmpty(el.Value))
            {
                root[key] = JsonValue.Create(el.Value);
                count++;
                el.Value = string.Empty;
            }
        }

        Capture("title",           officeMeta.Element(dc    + "title"));
        Capture("creator",         officeMeta.Element(dc    + "creator"));
        Capture("description",     officeMeta.Element(dc    + "description"));
        Capture("subject",         officeMeta.Element(dc    + "subject"));
        Capture("initialCreator",  officeMeta.Element(meta  + "initial-creator"));
        Capture("generator",       officeMeta.Element(meta  + "generator"));
        Capture("editingCycles",   officeMeta.Element(meta  + "editing-cycles"));
        Capture("editingDuration", officeMeta.Element(meta  + "editing-duration"));

        var userDefined = officeMeta.Elements(meta + "user-defined").ToList();
        if (userDefined.Count > 0)
        {
            var customProps = new JsonObject();
            foreach (var el in userDefined)
            {
                var name = el.Attribute(meta + "name")?.Value;
                if (!string.IsNullOrEmpty(name))
                {
                    customProps[name] = JsonValue.Create(el.Value);
                    count++;
                }
            }
            if (customProps.Count > 0) root["userDefinedProperties"] = customProps;
            foreach (var el in userDefined) el.Remove();
        }

        return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
    }

    // ── Audio / Video (TagLibSharp) ───────────────────────────────────────────

    /// <summary>Allows TagLibSharp to read/write from an in-memory stream.</summary>
    private sealed class MemoryStreamAbstraction : TagLib.File.IFileAbstraction
    {
        private readonly Stream _stream;
        internal MemoryStreamAbstraction(string name, Stream stream) { Name = name; _stream = stream; }
        public string Name { get; }
        public Stream ReadStream  => _stream;
        public Stream WriteStream => _stream;
        public void CloseStream(Stream stream) { /* lifecycle managed by caller */ }
    }

    private static FileMetadataResult StripMediaMetadata(byte[] rawFile)
    {
        var hint = GetMediaExtensionHint(rawFile);

        var ms = new MemoryStream();
        ms.Write(rawFile, 0, rawFile.Length);
        ms.Position = 0;

        try
        {
            using var file = TagLib.File.Create(new MemoryStreamAbstraction("file" + hint, ms));

            var (extractedMetadata, removedEntryCount) = ExtractMediaMetadata(file.Tag);

            file.RemoveTags(TagTypes.AllTags);
            file.Save();

            return new FileMetadataResult
            {
                CleanFile         = ms.ToArray(),
                ExtractedMetadata = extractedMetadata,
                RemovedEntryCount = removedEntryCount,
                IsPassthrough     = false
            };
        }
        catch (Exception ex) when (
            ex is TagLib.UnsupportedFormatException ||
            ex is TagLib.CorruptFileException       ||
            ex is ArgumentOutOfRangeException       ||
            ex is InvalidOperationException)
        {
            // File has the correct magic bytes but TagLibSharp could not fully parse it.
            // The original file is returned unchanged and the audit note explains why.
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the file could not be parsed by the audio/video engine. " +
                    $"Original file returned unchanged. Reason: {ex.GetType().Name}: {ex.Message}")
            };
            return new FileMetadataResult
            {
                CleanFile         = rawFile,
                ExtractedMetadata = note.ToJsonString(),
                RemovedEntryCount = 0,
                IsPassthrough     = false
            };
        }
    }

    private static string GetMediaExtensionHint(byte[] rawFile)
    {
        if (rawFile.Length >= 3 && rawFile[0] == 0x49 && rawFile[1] == 0x44 && rawFile[2] == 0x33)
            return ".mp3";  // MP3 ID3 header
        if (rawFile.Length >= 4 && rawFile[0] == 0x66 && rawFile[1] == 0x4C && rawFile[2] == 0x61 && rawFile[3] == 0x43)
            return ".flac"; // fLaC
        if (rawFile.Length >= 4 && rawFile[0] == 0x4F && rawFile[1] == 0x67 && rawFile[2] == 0x67 && rawFile[3] == 0x53)
            return ".ogg";  // OggS
        if (rawFile.Length >= 12 && rawFile[0] == 0x52 && rawFile[1] == 0x49 && rawFile[2] == 0x46 && rawFile[3] == 0x46)
        {
            if (rawFile[8] == 0x57 && rawFile[9] == 0x41 && rawFile[10] == 0x56 && rawFile[11] == 0x45)
                return ".wav"; // RIFF WAVE
            if (rawFile[8] == 0x41 && rawFile[9] == 0x56 && rawFile[10] == 0x49 && rawFile[11] == 0x20)
                return ".avi"; // RIFF AVI
        }
        if (rawFile.Length >= 8 && rawFile[4] == 0x66 && rawFile[5] == 0x74 && rawFile[6] == 0x79 && rawFile[7] == 0x70)
            return ".mp4"; // ISO Base Media (MP4/M4A/MOV)
        if (rawFile.Length >= 4 && rawFile[0] == 0x1A && rawFile[1] == 0x45 && rawFile[2] == 0xDF && rawFile[3] == 0xA3)
            return ".mkv"; // Matroska / WebM
        if (rawFile.Length >= 4 && rawFile[0] == 0x30 && rawFile[1] == 0x26 && rawFile[2] == 0xB2 && rawFile[3] == 0x75)
            return ".wma"; // WMA / ASF
        return ".mp3"; // fallback
    }

    private static (string json, int count) ExtractMediaMetadata(Tag tag)
    {
        var root  = new JsonObject();
        var count = 0;

        void Capture(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value)) { root[key] = JsonValue.Create(value); count++; }
        }

        void CaptureArray(string key, string[]? values)
        {
            if (values?.Length > 0)
            {
                var nonEmpty = values.Where(v => !string.IsNullOrEmpty(v)).ToArray();
                if (nonEmpty.Length > 0)
                {
                    root[key] = new JsonArray(nonEmpty.Select(v => JsonValue.Create(v)).ToArray<JsonNode?>());
                    count += nonEmpty.Length;
                }
            }
        }

        Capture("title",           tag.Title);
        CaptureArray("artists",    tag.Performers);
        Capture("album",           tag.Album);
        Capture("comment",         tag.Comment);
        CaptureArray("genres",     tag.Genres);
        Capture("copyright",       tag.Copyright);
        CaptureArray("composers",  tag.Composers);
        Capture("conductor",       tag.Conductor);

        return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
    }

    // ── Passthrough (plain text, CSV, JSON, XML, MD, etc.) ────────────────────

    private static FileMetadataResult Passthrough(byte[] rawFile) =>
        new FileMetadataResult
        {
            CleanFile         = rawFile,
            ExtractedMetadata = "[]",
            RemovedEntryCount = 0,
            IsPassthrough     = true
        };

}

