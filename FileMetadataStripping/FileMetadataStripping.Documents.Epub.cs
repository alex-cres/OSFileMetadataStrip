using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace FileMetadataStripping;

public partial class FileMetadataStripping
{
    // EPUB strip pipeline — Dublin Core metadata and OPF <meta> refinements from the
    // OPF package document referenced by META-INF/container.xml.

    /// <summary>
    /// Strips Dublin Core metadata (<c>dc:creator</c>, <c>dc:title</c>, <c>dc:description</c>,
    /// <c>dc:rights</c>, …) and OPF <c>&lt;meta&gt;</c> refinements from the OPF package
    /// document referenced by <c>META-INF/container.xml</c>. Preserves the ZIP structure
    /// and the reading order.
    /// </summary>
    private static FileMetadataResult StripEpubMetadata(byte[] rawFile)
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
                    "Metadata stripping was skipped — the EPUB file could not be opened. " +
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

    /// <summary>
    /// Returns the <c>full-path</c> attribute of the first <c>rootfile</c> element in
    /// <c>META-INF/container.xml</c>, or <see langword="null"/> when the entry is
    /// missing, malformed, or contains a path-traversal segment. Rejecting <c>..</c>
    /// segments prevents an attacker-crafted EPUB from causing the output archive to
    /// contain a Zip Slip payload that would escape the extraction root of any
    /// downstream naïve unzipper.
    /// </summary>
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

    /// <summary>
    /// Normalises the OPF path and rejects any value containing a parent-directory
    /// segment (<c>..</c>) or an absolute-path anchor (<c>/</c> at the start, drive
    /// letter, backslash). Returns <see langword="null"/> for unsafe inputs.
    /// </summary>
    private static string? SanitiseEpubOpfPath(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var path = raw!.Replace('\\', '/').Trim();
        if (path.Length == 0) return null;
        // Reject absolute paths — EPUB rootfile paths must be relative to the archive root.
        if (path.StartsWith("/") || path.Length >= 2 && path[1] == ':') return null;
        // Reject any traversal segment.
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

        // Dublin Core elements — may repeat (multiple creators, subjects, …).
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

        // OPF <meta> refinements — OPF 3 uses property attributes ("dcterms:creator"),
        // OPF 2 uses name/content attribute pairs.
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
}
