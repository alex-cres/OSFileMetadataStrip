using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Packaging;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using TagLib;
namespace OutSystems.NssFileMetadataStripping;

public partial class CssFileMetadataStripping
{
    // Structured-document strip pipelines (PDF, Office Open XML, ODF, EPUB, ORA, legacy binary Office / CFBF).

    private static RecFileMetadataResult StripPdfMetadata(byte[] rawFile)
    {
        using var input = new MemoryStream(rawFile);
        PdfDocument document;
        try
        {
            document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
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
            return new RecFileMetadataResult
            {
                ssCleanFile         = rawFile,
                ssExtractedMetadata = note.ToJsonString(),
                ssRemovedEntryCount = 0,
                ssIsPassthrough     = false
            };
        }

        using (document)
        {
            var (extractedMetadata, removedEntryCount) = ExtractPdfMetadata(document);

            // Clear annotation /Author entries on every page.
            var annotAuthors = new HashSet<string>(StringComparer.Ordinal);
            int annotEntries = 0;
            for (int i = 0; i < document.PageCount; i++)
            {
                var (cleared, authors) = ClearPageAnnotationAuthors(document.Pages[i]);
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

            document.Info.Title    = string.Empty;
            document.Info.Author   = string.Empty;
            document.Info.Subject  = string.Empty;
            document.Info.Keywords = string.Empty;
            document.Info.Creator  = string.Empty;

            // Strip catalog /Metadata XMP stream
            var catalog = document.Internals.Catalog;
            if (catalog.Elements.ContainsKey("/Metadata"))
                catalog.Elements.Remove("/Metadata");

            using var output = new MemoryStream();
            document.Save(output);

            return new RecFileMetadataResult
            {
                ssCleanFile         = output.ToArray(),
                ssExtractedMetadata = extractedMetadata,
                ssRemovedEntryCount = removedEntryCount,
                ssIsPassthrough     = false
            };
        }
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
        Capture("keywords",  info.Keywords);
        Capture("creator",  info.Creator);
        Capture("producer", info.Producer);

        if (document.Internals.Catalog.Elements.ContainsKey("/Metadata"))
        {
            root["xmp"] = "present";
            count++;
        }

        return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
    }

    private static RecFileMetadataResult StripOpenXmlMetadata(byte[] rawFile, bool stripBodyAuthors)
    {
        try
        {
            var ms = new MemoryStream();
            ms.Write(rawFile, 0, rawFile.Length);
            ms.Position = 0;

            var root       = new JsonObject();
            var count      = 0;
            var partWrites = new Dictionary<string, XDocument>();
            var partDeletes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? thumbnailPath = null;

            using (var package = Package.Open(ms, FileMode.Open, FileAccess.ReadWrite))
            {
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

                ExtractAndClearAppProperties(package, root, ref count, partWrites);
                ExtractAndClearCustomProperties(package, root, ref count, partWrites);
                if (stripBodyAuthors)
                    StripOoxmlAuthorNames(package, root, ref count, partWrites);

                // Embedded thumbnail (docProps/thumbnail.{jpeg,png,emf,wmf,gif,tiff}).
                thumbnailPath = FindOoxmlThumbnailPath(package);
                if (thumbnailPath != null)
                {
                    partDeletes.Add(thumbnailPath);
                    root["thumbnail"] = JsonValue.Create("removed");
                    count++;
                }
            }

            if (partWrites.Count > 0 || partDeletes.Count > 0)
            {
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
                    foreach (var entryName in partDeletes)
                        zip.GetEntry(entryName)?.Delete();
                    if (thumbnailPath != null)
                        RemoveOoxmlThumbnailRelationship(zip, thumbnailPath);
                }
                return new RecFileMetadataResult
                {
                    ssCleanFile         = zipMs.ToArray(),
                    ssExtractedMetadata = count > 0 ? root.ToJsonString() : "[]",
                    ssRemovedEntryCount = count,
                    ssIsPassthrough     = false
                };
            }

            return new RecFileMetadataResult
            {
                ssCleanFile         = ms.ToArray(),
                ssExtractedMetadata = count > 0 ? root.ToJsonString() : "[]",
                ssRemovedEntryCount = count,
                ssIsPassthrough     = false
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
            return new RecFileMetadataResult
            {
                ssCleanFile         = rawFile,
                ssExtractedMetadata = note.ToJsonString(),
                ssRemovedEntryCount = 0,
                ssIsPassthrough     = false
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

    /// <summary>Returns the ZIP entry path of the OOXML embedded thumbnail image, or null if none.</summary>
    private static string? FindOoxmlThumbnailPath(Package package)
    {
        const string ThumbRelType =
            "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail";
        var rel = package.GetRelationshipsByType(ThumbRelType).FirstOrDefault();
        if (rel == null) return null;
        var target = rel.TargetUri.OriginalString;
        return target.TrimStart('/');
    }

    /// <summary>Removes the thumbnail relationship from _rels/.rels after the thumbnail entry is deleted.</summary>
    private static void RemoveOoxmlThumbnailRelationship(ZipArchive zip, string thumbnailPath)
    {
        var relsEntry = zip.GetEntry("_rels/.rels");
        if (relsEntry == null) return;
        XDocument rels;
        try
        {
            using var s = relsEntry.Open();
            rels = XDocument.Load(s);
        }
        catch (System.Xml.XmlException) { return; }

        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
        var toRemove = rels.Descendants(ns + "Relationship")
            .Where(r =>
            {
                var type   = r.Attribute("Type")?.Value ?? string.Empty;
                var target = (r.Attribute("Target")?.Value ?? string.Empty).TrimStart('/');
                return type.EndsWith("/metadata/thumbnail", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(target, thumbnailPath, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        if (toRemove.Count == 0) return;

        foreach (var r in toRemove) r.Remove();
        relsEntry.Delete();
        using var ws = zip.CreateEntry("_rels/.rels").Open();
        rels.Save(ws);
    }

    private static readonly HashSet<string> _wordAuthorContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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

    private static (int cleared, string[] authors) ClearPageAnnotationAuthors(PdfPage page)
    {
        if (!page.Elements.ContainsKey("/Annots")) return (0, new string[0]);

        var annotsObj = page.Elements["/Annots"];
        var annots    = annotsObj as PdfArray;
        if (annots == null && annotsObj is PdfReference ar) annots = ar.Value as PdfArray;
        if (annots == null || annots.Elements.Count == 0) return (0, new string[0]);

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

    /// <summary>Strips Dublin Core metadata (dc:*) and OPF meta refinements from the OPF package document.</summary>
    private static RecFileMetadataResult StripEpubMetadata(byte[] rawFile)
    {
        try
        {
            var zipMs = new MemoryStream();
            zipMs.Write(rawFile, 0, rawFile.Length);
            zipMs.Position = 0;

            int    count             = 0;
            string extractedMetadata = "[]";

            using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Update, leaveOpen: true))
            {
                var opfPath = ReadEpubOpfPath(zip);
                if (opfPath != null)
                {
                    var opfEntry = zip.GetEntry(opfPath);
                    if (opfEntry != null)
                    {
                        XDocument xdoc;
                        using (var s = opfEntry.Open()) xdoc = XDocument.Load(s);

                        var (json, n) = ExtractAndClearEpubMetadata(xdoc);
                        if (n > 0)
                        {
                            extractedMetadata = json;
                            count             = n;
                            opfEntry.Delete();
                            using var ws = zip.CreateEntry(opfPath).Open();
                            xdoc.Save(ws);
                        }
                    }
                }
            }

            return new RecFileMetadataResult
            {
                ssCleanFile         = zipMs.ToArray(),
                ssExtractedMetadata = extractedMetadata,
                ssRemovedEntryCount = count,
                ssIsPassthrough     = false
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
                    "Metadata stripping was skipped — the EPUB file could not be opened. " +
                    "Original file returned unchanged. Reason: " + ex.GetType().Name + ": " + ex.Message)
            };
            return new RecFileMetadataResult
            {
                ssCleanFile         = rawFile,
                ssExtractedMetadata = note.ToJsonString(),
                ssRemovedEntryCount = 0,
                ssIsPassthrough     = false
            };
        }
    }

    private static string? ReadEpubOpfPath(ZipArchive zip)
    {
        var container = zip.GetEntry("META-INF/container.xml");
        if (container == null) return null;
        try
        {
            XDocument xdoc;
            using (var s = container.Open()) xdoc = XDocument.Load(s);
            XNamespace ns = "urn:oasis:names:tc:opendocument:xmlns:container";
            var raw = xdoc.Descendants(ns + "rootfile").FirstOrDefault()
                    ?.Attribute("full-path")?.Value
                ?? xdoc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "rootfile")
                    ?.Attribute("full-path")?.Value;
            return SanitiseEpubOpfPath(raw);
        }
        catch { return null; }
    }

    /// <summary>Rejects OPF paths containing traversal segments so the output archive
    /// cannot be turned into a Zip Slip payload by a crafted EPUB.</summary>
    private static string? SanitiseEpubOpfPath(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var path = raw!.Replace('\\', '/').Trim();
        if (path.Length == 0) return null;
        if (path.StartsWith("/") || path.Length >= 2 && path[1] == ':') return null;
        foreach (var segment in path.Split('/'))
        {
            if (segment == "..") return null;
        }
        return path;
    }

    private static (string json, int count) ExtractAndClearEpubMetadata(XDocument opfDoc)
    {
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        var metadataEl = opfDoc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "metadata");
        if (metadataEl == null) return ("[]", 0);

        var root  = new JsonObject();
        int count = 0;

        var accum = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var el in metadataEl.Elements()
                     .Where(e => e.Name.Namespace == dc).ToList())
        {
            var value = el.Value?.Trim();
            if (!string.IsNullOrEmpty(value))
            {
                var key = el.Name.LocalName;
                if (!accum.TryGetValue(key, out var list))
                    accum[key] = list = new List<string>();
                list.Add(value!);
                el.Value = string.Empty;
                count++;
            }
        }
        foreach (var kvp in accum)
        {
            if (kvp.Value.Count == 1)
            {
                root[kvp.Key] = JsonValue.Create(kvp.Value[0]);
            }
            else
            {
                var arr = new JsonArray();
                foreach (var v in kvp.Value) arr.Add(JsonValue.Create(v));
                root[kvp.Key] = arr;
            }
        }

        foreach (var el in metadataEl.Elements()
                     .Where(e => e.Name.LocalName == "meta").ToList())
        {
            var property = el.Attribute("property")?.Value;
            var name     = el.Attribute("name")?.Value;
            var content  = el.Attribute("content")?.Value;
            var text     = el.Value?.Trim();

            if (!string.IsNullOrEmpty(property) && !string.IsNullOrEmpty(text))
            {
                root["meta_" + property!.Replace(':', '_')] = JsonValue.Create(text);
                el.Value = string.Empty;
                count++;
            }
            else if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(content))
            {
                root["meta_" + name!] = JsonValue.Create(content);
                el.SetAttributeValue("content", string.Empty);
                count++;
            }
        }

        return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
    }

    /// <summary>Strips user-controlled name/description attributes from stack.xml.</summary>
    private static RecFileMetadataResult StripOraMetadata(byte[] rawFile)
    {
        try
        {
            var zipMs = new MemoryStream();
            zipMs.Write(rawFile, 0, rawFile.Length);
            zipMs.Position = 0;

            int    count             = 0;
            string extractedMetadata = "[]";

            using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Update, leaveOpen: true))
            {
                var stackEntry = zip.GetEntry("stack.xml");
                if (stackEntry != null)
                {
                    XDocument xdoc;
                    using (var s = stackEntry.Open()) xdoc = XDocument.Load(s);

                    var (json, n) = ExtractAndClearOraMetadata(xdoc);
                    if (n > 0)
                    {
                        extractedMetadata = json;
                        count             = n;
                        stackEntry.Delete();
                        using var ws = zip.CreateEntry("stack.xml").Open();
                        xdoc.Save(ws);
                    }
                }
            }

            return new RecFileMetadataResult
            {
                ssCleanFile         = zipMs.ToArray(),
                ssExtractedMetadata = extractedMetadata,
                ssRemovedEntryCount = count,
                ssIsPassthrough     = false
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
                    "Metadata stripping was skipped — the ORA file could not be opened. " +
                    "Original file returned unchanged. Reason: " + ex.GetType().Name + ": " + ex.Message)
            };
            return new RecFileMetadataResult
            {
                ssCleanFile         = rawFile,
                ssExtractedMetadata = note.ToJsonString(),
                ssRemovedEntryCount = 0,
                ssIsPassthrough     = false
            };
        }
    }

    private static (string json, int count) ExtractAndClearOraMetadata(XDocument stackDoc)
    {
        var root         = new JsonObject();
        var names        = new JsonArray();
        var descriptions = new JsonArray();
        int count        = 0;

        foreach (var el in stackDoc.Descendants().ToList())
        {
            var nameAttr = el.Attribute("name");
            if (nameAttr != null && !string.IsNullOrEmpty(nameAttr.Value))
            {
                names.Add(JsonValue.Create(nameAttr.Value));
                nameAttr.Value = string.Empty;
                count++;
            }
            var descAttr = el.Attribute("description");
            if (descAttr != null && !string.IsNullOrEmpty(descAttr.Value))
            {
                descriptions.Add(JsonValue.Create(descAttr.Value));
                descAttr.Value = string.Empty;
                count++;
            }
        }

        if (names.Count > 0)        root["names"]        = names;
        if (descriptions.Count > 0) root["descriptions"] = descriptions;
        return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
    }

    private static bool IsOdfFormat(byte[] rawFile)
    {
        var mime = ReadZipMimetype(rawFile);
        return mime != null
            && mime.StartsWith("application/vnd.oasis.opendocument.",
                StringComparison.OrdinalIgnoreCase);
    }

    private static RecFileMetadataResult StripOdfMetadata(byte[] rawFile)
    {
        try
        {
            var zipMs = new MemoryStream();
            zipMs.Write(rawFile, 0, rawFile.Length);
            zipMs.Position = 0;

            int    count             = 0;
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

            return new RecFileMetadataResult
            {
                ssCleanFile         = zipMs.ToArray(),
                ssExtractedMetadata = extractedMetadata,
                ssRemovedEntryCount = count,
                ssIsPassthrough     = false
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
            return new RecFileMetadataResult
            {
                ssCleanFile         = rawFile,
                ssExtractedMetadata = note.ToJsonString(),
                ssRemovedEntryCount = 0,
                ssIsPassthrough     = false
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

    private const string CfbfSummaryInformationStream         = "\u0005SummaryInformation";

    private const string CfbfDocumentSummaryInformationStream = "\u0005DocumentSummaryInformation";

    private static RecFileMetadataResult StripCfbfMetadata(byte[] rawFile)
    {
        try
        {
            var ms = new MemoryStream();
            ms.Write(rawFile, 0, rawFile.Length);
            ms.Position = 0;

            int  count         = 0;
            var  extractedRoot = new JsonObject();

            using (var root = OpenMcdf.RootStorage.Open(
                       ms, OpenMcdf.StorageModeFlags.LeaveOpen))
            {
                if (root.ContainsEntry(CfbfSummaryInformationStream))
                {
                    var (json, n) = ReadOlePropertyContainer(
                        root, CfbfSummaryInformationStream,
                        OpenMcdf.Ole.ContainerType.SummaryInfo);
                    if (n > 0)
                    {
                        extractedRoot["summaryInformation"] = json;
                        count += n;
                    }
                    root.Delete(CfbfSummaryInformationStream);
                }

                if (root.ContainsEntry(CfbfDocumentSummaryInformationStream))
                {
                    var (json, n) = ReadOlePropertyContainer(
                        root, CfbfDocumentSummaryInformationStream,
                        OpenMcdf.Ole.ContainerType.DocumentSummaryInfo);
                    if (n > 0)
                    {
                        extractedRoot["documentSummaryInformation"] = json;
                        count += n;
                    }
                    root.Delete(CfbfDocumentSummaryInformationStream);
                }

                // Consolidate rewrites the container in place, keeping only
                // reachable directory entries and sectors. Without this the
                // deleted property-set streams remain in unallocated sectors
                // and the raw text (e.g. author name) is still readable from
                // the byte array.
                root.Flush(consolidate: true);
            }

            return new RecFileMetadataResult
            {
                ssCleanFile         = ms.ToArray(),
                ssExtractedMetadata = count > 0 ? extractedRoot.ToJsonString() : "[]",
                ssRemovedEntryCount = count,
                ssIsPassthrough     = false
            };
        }
        catch (Exception ex) when (
            ex is IOException             ||
            ex is InvalidDataException    ||
            ex is InvalidOperationException ||
            ex is NotSupportedException   ||
            ex is ArgumentException       ||
            ex is FormatException         ||
            ex is OverflowException       ||
            ex is EndOfStreamException    ||
            ex is IndexOutOfRangeException||
            ex is FileFormatException)
        {
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the CFBF (legacy Office) file could not be parsed. " +
                    $"Original file returned unchanged. Reason: {ex.GetType().Name}: {ex.Message}")
            };
            return new RecFileMetadataResult
            {
                ssCleanFile         = rawFile,
                ssExtractedMetadata = note.ToJsonString(),
                ssRemovedEntryCount = 0,
                ssIsPassthrough     = false
            };
        }
    }

    private static (JsonObject json, int count) ReadOlePropertyContainer(
        OpenMcdf.RootStorage root, string streamName,
        OpenMcdf.Ole.ContainerType expected)
    {
        var json  = new JsonObject();
        int count = 0;

        try
        {
            using var cfStream  = root.OpenStream(streamName);
            var       container = new OpenMcdf.Ole.OlePropertiesContainer(cfStream);

            foreach (var prop in container.Properties)
            {
                var (key, value) = MapOleProperty(expected, prop);
                if (key != null && value != null)
                {
                    json[key] = JsonValue.Create(value);
                    count++;
                }
            }

            if (container.UserDefinedProperties != null)
            {
                var custom = new JsonObject();
                var names  = container.UserDefinedProperties.PropertyNames;
                foreach (var prop in container.UserDefinedProperties.Properties)
                {
                    string? name = null;
                    names?.TryGetValue(prop.PropertyIdentifier, out name);
                    var stringValue = FormatOleValue(prop.Value);
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(stringValue))
                    {
                        custom[name!] = JsonValue.Create(stringValue);
                        count++;
                    }
                }
                if (custom.Count > 0) json["customProperties"] = custom;
            }
        }
        catch (Exception ex) when (
            ex is IOException             ||
            ex is InvalidDataException    ||
            ex is InvalidOperationException ||
            ex is NotSupportedException   ||
            ex is ArgumentException       ||
            ex is FormatException         ||
            ex is OverflowException       ||
            ex is EndOfStreamException    ||
            ex is IndexOutOfRangeException||
            ex is FileFormatException)
        {
            json["auditWarning"] = JsonValue.Create(
                $"OLE property-set could not be parsed for {streamName}: {ex.GetType().Name}: {ex.Message}");
        }

        return (json, count);
    }

    private static (string? key, string? value) MapOleProperty(
        OpenMcdf.Ole.ContainerType containerType,
        OpenMcdf.Ole.OleProperty prop)
    {
        var stringValue = FormatOleValue(prop.Value);
        if (string.IsNullOrEmpty(stringValue)) return (null, null);

        if (containerType == OpenMcdf.Ole.ContainerType.SummaryInfo)
        {
            return prop.PropertyIdentifier switch
            {
                0x02 => ("title",             stringValue),
                0x03 => ("subject",           stringValue),
                0x04 => ("author",            stringValue),
                0x05 => ("keywords",          stringValue),
                0x06 => ("comments",          stringValue),
                0x07 => ("template",          stringValue),
                0x08 => ("lastSavedBy",       stringValue),
                0x09 => ("revisionNumber",    stringValue),
                0x0A => ("totalEditingTime",  stringValue),
                0x0B => ("lastPrinted",       stringValue),
                0x0C => ("createDateTime",    stringValue),
                0x0D => ("lastSavedDateTime", stringValue),
                0x12 => ("application",       stringValue),
                _    => ((string?)null, (string?)null),
            };
        }
        if (containerType == OpenMcdf.Ole.ContainerType.DocumentSummaryInfo)
        {
            return prop.PropertyIdentifier switch
            {
                0x02 => ("category",      stringValue),
                0x0E => ("manager",       stringValue),
                0x0F => ("company",       stringValue),
                0x17 => ("appVersion",    stringValue),
                0x1A => ("contentType",   stringValue),
                0x1B => ("contentStatus", stringValue),
                0x1C => ("language",      stringValue),
                0x1D => ("docVersion",    stringValue),
                _    => ((string?)null, (string?)null),
            };
        }
        return (null, null);
    }

    private static string FormatOleValue(object? value) => value switch
    {
        null                 => string.Empty,
        string s             => s,
        DateTime dt          => dt.ToString("O"),
        byte[] _             => string.Empty,
        System.Collections.IEnumerable e when value is not string
                             => string.Join(", ", e.Cast<object?>()
                                                   .Select(v => v?.ToString())
                                                   .Where(s => !string.IsNullOrEmpty(s))),
        _                    => value.ToString() ?? string.Empty,
    };

}
