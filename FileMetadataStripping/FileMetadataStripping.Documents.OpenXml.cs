using System.IO.Compression;
using System.IO.Packaging;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace FileMetadataStripping;

public partial class FileMetadataStripping
{
    // Office Open XML strip pipeline (DOCX / XLSX / PPTX and template variants) and
    // Word 2003 XML (WordProcessingML) — both are Microsoft Word document surfaces
    // that share <o:*> property groups and w:author scrubbing semantics.

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
            // ZIP entries to delete during the post-Package rewrite stage. Used for the
            // embedded thumbnail image, which is a full-fidelity page preview that would
            // otherwise reach a vision model along with the document body.
            var partDeletes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? thumbnailPath = null;

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

                // Embedded thumbnail (docProps/thumbnail.{jpeg,png,emf,wmf,gif,tiff}).
                thumbnailPath = FindOoxmlThumbnailPath(package);
                if (thumbnailPath != null)
                {
                    partDeletes.Add(thumbnailPath);
                    root["thumbnail"] = JsonValue.Create("removed");
                    count++;
                }
            }
            // Package is now closed; core property changes have been flushed to ms.

            // Apply staged XML part modifications using ZipArchive — avoids PackagePart
            // write-back issues and works reliably on all target platforms.
            if (partWrites.Count > 0 || partDeletes.Count > 0)
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
                    foreach (var entryName in partDeletes)
                        zip.GetEntry(entryName)?.Delete();
                    if (thumbnailPath != null)
                        RemoveOoxmlThumbnailRelationship(zip, thumbnailPath);
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

    /// <summary>
    /// Returns the ZIP entry path (relative, no leading slash) of the OOXML embedded
    /// thumbnail image, or <see langword="null"/> when no thumbnail relationship is
    /// declared in the package. The thumbnail is a full-fidelity rendered preview
    /// that would otherwise reach a vision model along with the document body.
    /// </summary>
    private static string? FindOoxmlThumbnailPath(Package package)
    {
        const string ThumbRelType =
            "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail";
        var rel = package.GetRelationshipsByType(ThumbRelType).FirstOrDefault();
        if (rel == null) return null;
        // TargetUri is a relative reference from the package root.
        var target = rel.TargetUri.OriginalString;
        return target.TrimStart('/');
    }

    /// <summary>
    /// Removes the thumbnail relationship from <c>_rels/.rels</c> once the referenced
    /// image entry has been deleted, so no OOXML reader trips on a dangling reference.
    /// </summary>
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

    // ── Word 2003 XML (.xml with WordProcessingML root) ──────────────────────
    //
    // A Word 2003 XML document is a single XML file whose root is
    // <w:wordDocument> in namespace http://schemas.microsoft.com/office/word/2003/wordml.
    // Document properties live in <o:DocumentProperties> (namespace
    // urn:schemas-microsoft-com:office:office) and custom properties in
    // <o:CustomDocumentProperties>. Both blocks are stripped; when
    // stripBodyAuthors is true the tracked-changes / comment author attributes
    // are also blanked (w:Author on <w:ins> / <w:del> / <aml:annotation>).

    private const string WordMl2003Namespace = "http://schemas.microsoft.com/office/word/2003/wordml";

    private static bool IsWordMlFile(byte[] rawFile)
    {
        if (!StartsWithXmlAngleBracket(rawFile)) return false;
        int scanLimit = Math.Min(rawFile.Length, 4096);
        var scan = System.Text.Encoding.ASCII.GetString(rawFile, 0, scanLimit);
        return scan.IndexOf("schemas.microsoft.com/office/word/2003/wordml",
                            StringComparison.Ordinal) >= 0;
    }

    private static FileMetadataResult StripWordMlMetadata(byte[] rawFile, bool stripBodyAuthors)
    {
        try
        {
            XDocument xdoc;
            using (var input = new MemoryStream(rawFile, writable: false))
                xdoc = XDocument.Load(input);

            XNamespace o   = "urn:schemas-microsoft-com:office:office";
            XNamespace w   = WordMl2003Namespace;
            XNamespace aml = "http://schemas.microsoft.com/aml/2001/core";

            var root  = new JsonObject();
            int count = 0;

            // Standard document properties — clear each child's text content.
            var docProps = xdoc.Descendants(o + "DocumentProperties").FirstOrDefault();
            if (docProps != null)
            {
                var props = new JsonObject();
                foreach (var child in docProps.Elements().ToList())
                {
                    if (!string.IsNullOrEmpty(child.Value))
                    {
                        props[child.Name.LocalName] = JsonValue.Create(child.Value);
                        child.Value = string.Empty;
                        count++;
                    }
                }
                if (props.Count > 0) root["documentProperties"] = props;
            }

            // User-defined properties — capture then remove the whole child list.
            var customProps = xdoc.Descendants(o + "CustomDocumentProperties").FirstOrDefault();
            if (customProps != null)
            {
                var props = new JsonObject();
                foreach (var child in customProps.Elements().ToList())
                {
                    if (!string.IsNullOrEmpty(child.Value))
                    {
                        props[child.Name.LocalName] = JsonValue.Create(child.Value);
                        count++;
                    }
                    child.Remove();
                }
                if (props.Count > 0) root["customDocumentProperties"] = props;
            }

            // Body-author scrubbing (opt-in): tracked-changes and comment authors.
            if (stripBodyAuthors)
            {
                var authorAttrNames = new[]
                {
                    w   + "author",
                    aml + "author"
                };
                var authors = new JsonArray();
                foreach (var el in xdoc.Descendants().ToList())
                {
                    foreach (var attrName in authorAttrNames)
                    {
                        var attr = el.Attribute(attrName);
                        if (attr != null && !string.IsNullOrEmpty(attr.Value))
                        {
                            authors.Add(JsonValue.Create(attr.Value));
                            attr.Value = string.Empty;
                            count++;
                        }
                    }
                }
                if (authors.Count > 0) root["bodyAuthors"] = authors;
            }

            using var output = new MemoryStream();
            xdoc.Save(output);
            return new FileMetadataResult
            {
                CleanFile         = output.ToArray(),
                ExtractedMetadata = count > 0 ? root.ToJsonString() : "[]",
                RemovedEntryCount = count,
                IsPassthrough     = false
            };
        }
        catch (Exception ex) when (
            ex is System.Xml.XmlException ||
            ex is InvalidDataException    ||
            ex is NotSupportedException)
        {
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the Word 2003 XML file could not be parsed. " +
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
}
