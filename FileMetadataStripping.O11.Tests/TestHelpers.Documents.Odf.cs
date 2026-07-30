using System.Xml.Linq;

namespace FileMetadataStripping.Tests;

internal static partial class TestHelpers
{
    // ODF test-data helpers — ZIP-based .odt (with meta.xml + META-INF/manifest.xml)
    // and the single-file Flat ODF (.fodt / .fods / .fodp).

    internal static byte[] CreateOdt(string? creator = null, string? title = null,
        Dictionary<string, string>? userDefined = null)
        => CreateOdfVariant("application/vnd.oasis.opendocument.text", creator, title, userDefined);

    /// <summary>
    /// Builds a minimal ODF ZIP package whose <c>mimetype</c> entry contains
    /// <paramref name="mimetype"/>. Used to exercise the template
    /// (<c>.ott</c>, <c>.ots</c>, <c>.otp</c>, <c>.otg</c>) and drawing / chart /
    /// formula / database / image (<c>.odg</c>, <c>.odc</c>, <c>.odf</c>,
    /// <c>.odb</c>, <c>.odi</c>) variants against the shared
    /// <c>StripOdfMetadata</c> pipeline. The <c>meta.xml</c> is populated with a
    /// <c>&lt;dc:creator&gt;</c>, optional <c>&lt;dc:title&gt;</c>, and optional
    /// <c>&lt;meta:user-defined&gt;</c> children so metadata is available to strip
    /// and audit.
    /// </summary>
    internal static byte[] CreateOdfVariant(
        string mimetype,
        string? creator = null,
        string? title = null,
        Dictionary<string, string>? userDefined = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            // mimetype must be first and uncompressed per ODF spec
            var mimetypeEntry = zip.CreateEntry("mimetype",
                System.IO.Compression.CompressionLevel.NoCompression);
            using (var s = mimetypeEntry.Open())
            {
                var bytes = System.Text.Encoding.ASCII.GetBytes(mimetype);
                s.Write(bytes, 0, bytes.Length);
            }

            var metaEntry = zip.CreateEntry("meta.xml");
            XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
            XNamespace dc     = "http://purl.org/dc/elements/1.1/";
            XNamespace meta   = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
            var officeMeta = new XElement(office + "meta");
            if (creator     != null) officeMeta.Add(new XElement(dc   + "creator", creator));
            if (title       != null) officeMeta.Add(new XElement(dc   + "title",   title));
            if (userDefined != null)
                foreach (var kvp in userDefined)
                    officeMeta.Add(new XElement(meta + "user-defined",
                        new XAttribute(meta + "name", kvp.Key), kvp.Value));
            var metaDoc = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(office + "document-meta",
                    new XAttribute(XNamespace.Xmlns + "office", office),
                    new XAttribute(XNamespace.Xmlns + "dc",     dc),
                    new XAttribute(XNamespace.Xmlns + "meta",   meta),
                    officeMeta));
            using (var s = metaEntry.Open()) metaDoc.Save(s);

            var manifestEntry = zip.CreateEntry("META-INF/manifest.xml");
            XNamespace mf = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
            var manifestDoc = new XDocument(
                new XElement(mf + "manifest",
                    new XAttribute(XNamespace.Xmlns + "manifest", mf),
                    new XElement(mf + "file-entry",
                        new XAttribute(mf + "full-path",  "/"),
                        new XAttribute(mf + "media-type", mimetype)),
                    new XElement(mf + "file-entry",
                        new XAttribute(mf + "full-path",  "meta.xml"),
                        new XAttribute(mf + "media-type", "text/xml"))));
            using (var s = manifestEntry.Open()) manifestDoc.Save(s);
        }
        return ms.ToArray();
    }

    // ── Flat ODF test helper ──────────────────────────────────────────────────
    //
    // Builds a single-file XML variant of ODF (.fodt / .fods / .fodp). The whole
    // document — meta, styles, body — lives in a single <office:document>
    // element in the OASIS office namespace, so no ZIP wrapper is used.

    internal static byte[] CreateFlatOdt(
        string? creator = null,
        string? title   = null,
        Dictionary<string, string>? userDefined = null)
    {
        XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        XNamespace dc     = "http://purl.org/dc/elements/1.1/";
        XNamespace meta   = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
        XNamespace text   = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

        var officeMeta = new XElement(office + "meta");
        if (creator != null) officeMeta.Add(new XElement(dc + "creator", creator));
        if (title   != null) officeMeta.Add(new XElement(dc + "title",   title));
        if (userDefined != null)
            foreach (var kvp in userDefined)
                officeMeta.Add(new XElement(meta + "user-defined",
                    new XAttribute(meta + "name", kvp.Key), kvp.Value));

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(office + "document",
                new XAttribute(XNamespace.Xmlns + "office", office),
                new XAttribute(XNamespace.Xmlns + "dc",     dc),
                new XAttribute(XNamespace.Xmlns + "meta",   meta),
                new XAttribute(XNamespace.Xmlns + "text",   text),
                new XAttribute(office + "version",  "1.2"),
                new XAttribute(office + "mimetype", "application/vnd.oasis.opendocument.text"),
                officeMeta,
                new XElement(office + "body",
                    new XElement(office + "text",
                        new XElement(text + "p", "Hello world.")))));

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }
}
