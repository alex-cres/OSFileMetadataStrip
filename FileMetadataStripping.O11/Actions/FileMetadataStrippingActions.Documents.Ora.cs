using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace OutSystems.NssFileMetadataStripping;

public partial class CssFileMetadataStripping
{
    // ORA (OpenRaster) strip pipeline — user-controlled name / description attributes
    // on every element in stack.xml.

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
}
