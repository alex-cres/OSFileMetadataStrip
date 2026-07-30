using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace OutSystems.NssFileMetadataStripping;

public partial class CssFileMetadataStripping
{
    // PDF strip pipeline (PDFsharp 1.50 for net48) — /Info fields, catalog /Metadata XMP
    // stream, and per-page annotation /Author entries. Uses PdfDocumentOpenMode.Modify
    // so the catalog dictionary is mutable in place (the ODC version relies on the newer
    // PDFsharp 6.x API and a post-save regex hack).

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
}
