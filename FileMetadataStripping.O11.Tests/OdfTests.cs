using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>
/// Tests for ODF (LibreOffice ODT/ODS/ODP) metadata stripping.
/// Covers: meta.xml fields cleared, metadata captured for audit, valid ZIP output,
/// user-defined properties, graceful handling of unreadable files.
/// </summary>
public class OdfTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_OdtWithCreator_CreatorIsCleared()
    {
        var input = TestHelpers.CreateOdt(creator: "Attacker Name");

        var result = _sut.StripFileMetadata(input, false);

        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var xdoc      = XDocument.Load(zip.GetEntry("meta.xml")!.Open());
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.True(string.IsNullOrEmpty(xdoc.Descendants(dc + "creator").FirstOrDefault()?.Value));
    }

    [Fact]
    public void StripFileMetadata_OdtWithCreator_ExtractedMetadataContainsCreator()
    {
        var input = TestHelpers.CreateOdt(creator: "Attacker Name");

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Attacker Name", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_OdtWithMetadata_RemovedEntryCountIsGreaterThanZero()
    {
        var input = TestHelpers.CreateOdt(creator: "Author", title: "Title");

        var result = _sut.StripFileMetadata(input, false);

        Assert.True(result.RemovedEntryCount > 0);
    }

    [Fact]
    public void StripFileMetadata_OdtOutput_IsValidZip()
    {
        var input = TestHelpers.CreateOdt(creator: "Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.Equal(0x50, result.CleanFile[0]);
        Assert.Equal(0x4B, result.CleanFile[1]);
    }

    [Fact]
    public void StripFileMetadata_Odt_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateOdt(), false);

        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_OdtWithUserDefinedProperties_UserDefinedAreCleared()
    {
        var input = TestHelpers.CreateOdt(
            userDefined: new Dictionary<string, string> { ["ProjectCode"] = "PRJ-001" });

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("userDefinedProperties", result.ExtractedMetadata);
        Assert.Contains("ProjectCode", result.ExtractedMetadata);
        // Verify the user-defined element is gone from the output
        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var xdoc = XDocument.Load(zip.GetEntry("meta.xml")!.Open());
        XNamespace meta = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
        Assert.Empty(xdoc.Descendants(meta + "user-defined").ToList());
    }

    [Fact]
    public void StripFileMetadata_OdtWithNoMetadata_ReturnsZeroCount()
    {
        var input = TestHelpers.CreateOdt();

        var result = _sut.StripFileMetadata(input, false);

        Assert.Equal(0, result.RemovedEntryCount);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_OdtWithMalformedMetaXml_DoesNotThrow()
    {
        var rawOdt = TestHelpers.CreateOdt(creator: "Alice");
        var ms2 = new MemoryStream();
        ms2.Write(rawOdt, 0, rawOdt.Length);
        ms2.Position = 0;
        using (var zip = new ZipArchive(ms2, ZipArchiveMode.Update, leaveOpen: true))
        {
            zip.GetEntry("meta.xml")?.Delete();
            using var s = zip.CreateEntry("meta.xml").Open();
            var garbage = System.Text.Encoding.UTF8.GetBytes("<<< not xml >>>");
            s.Write(garbage, 0, garbage.Length);
        }
        var malformed = ms2.ToArray();

        var ex = Record.Exception(() => _sut.StripFileMetadata(malformed, false));

        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_OdtWithMalformedMetaXml_ReturnsOriginalFileUnchanged()
    {
        var rawOdt = TestHelpers.CreateOdt(creator: "Alice");
        var ms2 = new MemoryStream();
        ms2.Write(rawOdt, 0, rawOdt.Length);
        ms2.Position = 0;
        using (var zip = new ZipArchive(ms2, ZipArchiveMode.Update, leaveOpen: true))
        {
            zip.GetEntry("meta.xml")?.Delete();
            using var s = zip.CreateEntry("meta.xml").Open();
            var garbage = System.Text.Encoding.UTF8.GetBytes("<<< not xml >>>");
            s.Write(garbage, 0, garbage.Length);
        }
        var malformed = ms2.ToArray();

        var result = _sut.StripFileMetadata(malformed, false);

        Assert.Equal(malformed, result.CleanFile);
        Assert.Contains("processingError", result.ExtractedMetadata);
    }
}
