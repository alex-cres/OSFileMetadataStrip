namespace FileMetadataStripping.Tests;

internal static partial class TestHelpers
{
    // ORA (OpenRaster) test-data helpers — ZIP archive with mimetype = image/openraster
    // and a stack.xml carrying user-controlled name / description attributes.

    /// <summary>Creates an ORA (Open Raster) ZIP with mimetype = image/openraster.
    /// Routed to StripOraMetadata via the ZIP mimetype dispatcher.</summary>
    internal static byte[] CreateOra()
    {
        return CreateOra("root", "Background");
    }

    /// <summary>Creates an ORA archive with caller-specified stack and layer names,
    /// so tests can verify those attributes are blanked in the output.</summary>
    internal static byte[] CreateOra(string stackName, string layerName)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var mimetypeEntry = zip.CreateEntry("mimetype",
                System.IO.Compression.CompressionLevel.NoCompression);
            using (var s = mimetypeEntry.Open())
            {
                var b = System.Text.Encoding.ASCII.GetBytes("image/openraster");
                s.Write(b, 0, b.Length);
            }
            var stackEntry = zip.CreateEntry("stack.xml");
            using (var s = stackEntry.Open())
            {
                var xml = System.Text.Encoding.UTF8.GetBytes(
                    "<?xml version='1.0'?><image w='10' h='10'>" +
                    $"<stack opacity='1' name='{stackName}'>" +
                    $"<layer name='{layerName}' src='data/layer0.png'/>" +
                    "</stack></image>");
                s.Write(xml, 0, xml.Length);
            }
        }
        return ms.ToArray();
    }
}
