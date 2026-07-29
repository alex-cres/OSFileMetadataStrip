using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for ORA (Open Raster) files — ZIP with mimetype "image/openraster".
///
/// ORA is detected via the ZIP <c>mimetype</c> entry and routed to a dedicated path
/// that strips user-controlled <c>name</c> and <c>description</c> attributes from
/// every element in <c>stack.xml</c>. Structural attributes (w, h, x, y, opacity,
/// src, mask-src, composite-op, visibility) are preserved.</summary>
public class OraTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_OraInput_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateOra(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_OraInput_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateOra(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_OraInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateOra(), false);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_OraInput_RemovedEntryCountIsNonNegative()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateOra(), false);
        Assert.True(result.RemovedEntryCount >= 0);
    }

    [Fact]
    public void StripFileMetadata_OraWithLayerName_NameAttributeIsBlankedInOutput()
    {
        // The layer name attribute in the source must be blanked in the round-tripped stack.xml.
        var input  = TestHelpers.CreateOra("SensitiveStackName", "SensitiveLayerName");
        var result = _sut.StripFileMetadata(input, false);

        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var stackEntry = zip.GetEntry("stack.xml");
        Assert.NotNull(stackEntry);

        XDocument xdoc;
        using (var s = stackEntry!.Open()) xdoc = XDocument.Load(s);

        // Every "name" attribute at every depth must be empty after stripping.
        foreach (var el in xdoc.Descendants())
        {
            var nameAttr = el.Attribute("name");
            if (nameAttr != null)
                Assert.Equal(string.Empty, nameAttr.Value);
        }
    }

    [Fact]
    public void StripFileMetadata_OraWithLayerName_NamesAreCapturedInExtractedMetadata()
    {
        var input  = TestHelpers.CreateOra("SensitiveStackName", "SensitiveLayerName");
        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("SensitiveStackName", result.ExtractedMetadata);
        Assert.Contains("SensitiveLayerName", result.ExtractedMetadata);
        Assert.True(result.RemovedEntryCount >= 2);
    }
}
