using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace FileMetadataStripping;

public partial class FileMetadataStripping
{
    // ORA (OpenRaster) strip pipeline — user-controlled name / description attributes
    // on every element in stack.xml.

    /// <summary>
    /// Strips user-controlled <c>name</c> and <c>description</c> attributes from every
    /// element in <c>stack.xml</c> (image, stack, layer, mask, text). Preserves the
    /// structural attributes required to render the composited image (w, h, x, y,
    /// opacity, src, mask-src, composite-op, visibility).
    /// </summary>
    private static FileMetadataResult StripOraMetadata(byte[] rawFile)
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
                    "Metadata stripping was skipped — the ORA file could not be opened. " +
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
}
