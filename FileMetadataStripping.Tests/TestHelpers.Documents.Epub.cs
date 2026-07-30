namespace FileMetadataStripping.Tests;

internal static partial class TestHelpers
{
    // EPUB test-data helpers — minimal EPUB (container-only) and full EPUB with
    // an OPF package document carrying Dublin Core metadata.

    /// <summary>Creates an EPUB ZIP with mimetype = application/epub+zip.
    /// Contains only the container.xml — no OPF — so the strip path finds nothing
    /// to remove but exits cleanly.</summary>
    internal static byte[] CreateEpub()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var mimetypeEntry = zip.CreateEntry("mimetype",
                System.IO.Compression.CompressionLevel.NoCompression);
            using (var s = mimetypeEntry.Open())
            {
                var b = System.Text.Encoding.ASCII.GetBytes("application/epub+zip");
                s.Write(b, 0, b.Length);
            }
            var containerEntry = zip.CreateEntry("META-INF/container.xml");
            using (var s = containerEntry.Open())
            {
                var xml = System.Text.Encoding.UTF8.GetBytes(
                    "<?xml version='1.0'?>" +
                    "<container version='1.0' xmlns='urn:oasis:names:tc:opendocument:xmlns:container'>" +
                    "<rootfiles><rootfile full-path='OEBPS/content.opf' " +
                    "media-type='application/oebps-package+xml'/></rootfiles></container>");
                s.Write(xml, 0, xml.Length);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Creates an EPUB with a full OPF package document carrying Dublin Core
    /// metadata. Used to verify the OPF strip path clears creator/title/description
    /// while preserving the rest of the OPF structure.</summary>
    internal static byte[] CreateEpubWithOpf(string creator, string title, string? description = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var mimetypeEntry = zip.CreateEntry("mimetype",
                System.IO.Compression.CompressionLevel.NoCompression);
            using (var s = mimetypeEntry.Open())
            {
                var b = System.Text.Encoding.ASCII.GetBytes("application/epub+zip");
                s.Write(b, 0, b.Length);
            }
            var containerEntry = zip.CreateEntry("META-INF/container.xml");
            using (var s = containerEntry.Open())
            {
                var xml = System.Text.Encoding.UTF8.GetBytes(
                    "<?xml version='1.0'?>" +
                    "<container version='1.0' xmlns='urn:oasis:names:tc:opendocument:xmlns:container'>" +
                    "<rootfiles><rootfile full-path='OEBPS/content.opf' " +
                    "media-type='application/oebps-package+xml'/></rootfiles></container>");
                s.Write(xml, 0, xml.Length);
            }
            var opfEntry = zip.CreateEntry("OEBPS/content.opf");
            using (var s = opfEntry.Open())
            {
                var descLine = description != null
                    ? $"<dc:description>{description}</dc:description>" : string.Empty;
                var xml = System.Text.Encoding.UTF8.GetBytes(
                    "<?xml version='1.0' encoding='UTF-8'?>" +
                    "<package xmlns='http://www.idpf.org/2007/opf' version='3.0' unique-identifier='pub-id'>" +
                    "<metadata xmlns:dc='http://purl.org/dc/elements/1.1/'>" +
                    $"<dc:creator>{creator}</dc:creator>" +
                    $"<dc:title>{title}</dc:title>" +
                    descLine +
                    "</metadata>" +
                    "<manifest/><spine/></package>");
                s.Write(xml, 0, xml.Length);
            }
        }
        return ms.ToArray();
    }
}
