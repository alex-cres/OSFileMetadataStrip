using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using System.Text.Json.Nodes;

namespace FileMetadataStripping;

public partial class FileMetadataStripping
{
    // PDF strip pipeline (PDFsharp) — /Info fields, catalog /Metadata XMP token, and
    // per-page annotation /Author entries.

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
}
