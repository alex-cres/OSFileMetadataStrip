using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace FileMetadataStripping.Tests;

internal static partial class TestHelpers
{
    // PDF test-data helpers.

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
}
