using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace FileMetadataStripping;

public partial class FileMetadataStripping
{
    // ODF strip pipelines — the ZIP-based ODT/ODS/ODP variants share meta.xml with the
    // single-file Flat ODF (.fodt/.fods/.fodp) via the shared ExtractAndClearOdfMetadata
    // helper. The XML-prefix sniffer at the bottom is also used by the Word 2003 XML
    // detector (which lives in the OpenXml partial).

    private static bool IsOdfFormat(byte[] rawFile)
    {
        var mime = ReadZipMimetype(rawFile);
        return mime != null
            && mime.StartsWith("application/vnd.oasis.opendocument.",
                StringComparison.OrdinalIgnoreCase);
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

    // ── Flat ODF (.fodt / .fods / .fodp) — single-file XML variant of ODF ─────
    //
    // Flat ODF stores the same <office:meta> block as ZIP-based ODF, but the
    // whole document is a single XML file. Detection matches the OASIS office
    // namespace URI in the first 4 KB; stripping delegates to the shared
    // ExtractAndClearOdfMetadata helper and serialises the document back.

    private static bool IsFlatOdfFile(byte[] rawFile)
    {
        if (!StartsWithXmlAngleBracket(rawFile)) return false;
        int scanLimit = Math.Min(rawFile.Length, 4096);
        var scan = System.Text.Encoding.ASCII.GetString(rawFile, 0, scanLimit);
        return scan.IndexOf("urn:oasis:names:tc:opendocument:xmlns:office:1.0",
                            StringComparison.Ordinal) >= 0
            && scan.IndexOf(":document", StringComparison.Ordinal) >= 0;
    }

    private static FileMetadataResult StripFlatOdfMetadata(byte[] rawFile)
    {
        try
        {
            XDocument xdoc;
            using (var input = new MemoryStream(rawFile, writable: false))
                xdoc = XDocument.Load(input);

            var (json, n) = ExtractAndClearOdfMetadata(xdoc);

            using var output = new MemoryStream();
            xdoc.Save(output);
            return new FileMetadataResult
            {
                CleanFile         = output.ToArray(),
                ExtractedMetadata = json,
                RemovedEntryCount = n,
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
                    "Metadata stripping was skipped — the Flat ODF file could not be parsed as XML. " +
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
    /// Returns <see langword="true"/> when the byte buffer looks like an XML document —
    /// after an optional UTF-8 BOM and leading whitespace, the first non-whitespace
    /// byte is <c>&lt;</c>. Used as a cheap prefilter for the Flat ODF and WordML
    /// byte-scanners.
    /// </summary>
    private static bool StartsWithXmlAngleBracket(byte[] rawFile)
    {
        if (rawFile.Length < 4) return false;
        int i = 0;
        if (rawFile.Length >= 3 && rawFile[0] == 0xEF && rawFile[1] == 0xBB && rawFile[2] == 0xBF)
            i = 3;
        while (i < rawFile.Length && (rawFile[i] == 0x20 || rawFile[i] == 0x09
                                   || rawFile[i] == 0x0A || rawFile[i] == 0x0D)) i++;
        return i < rawFile.Length && rawFile[i] == 0x3C;
    }
}
