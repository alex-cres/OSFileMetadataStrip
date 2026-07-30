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
        // Build a valid ODF ZIP but inject invalid XML into meta.xml.
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

    [Fact]
    public void StripFileMetadata_OdtOutput_IsValidZipAndMetadataIsStripped()
    {
        var input  = TestHelpers.CreateOdt(creator: "ToBeStripped", title: "SecretTitle");
        var result = _sut.StripFileMetadata(input, false);

        // Output must be a valid ZIP
        Assert.Equal(0x50, result.CleanFile[0]);
        Assert.Equal(0x4B, result.CleanFile[1]);
        // Creator and title must be gone from meta.xml
        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var xdoc      = XDocument.Load(zip.GetEntry("meta.xml")!.Open());
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.True(string.IsNullOrEmpty(xdoc.Descendants(dc + "creator").FirstOrDefault()?.Value));
        Assert.True(string.IsNullOrEmpty(xdoc.Descendants(dc + "title").FirstOrDefault()?.Value));
    }

    [Fact]
    public void StripFileMetadata_OdtWithMetadata_CountAndStripAreConsistent()
    {
        var input  = TestHelpers.CreateOdt(creator: "Attacker", title: "Injected");
        var result = _sut.StripFileMetadata(input, false);

        // Count reflects actual fields removed
        Assert.True(result.RemovedEntryCount > 0);
        // And those fields must be gone from the output
        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var xdoc      = XDocument.Load(zip.GetEntry("meta.xml")!.Open());
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.True(string.IsNullOrEmpty(xdoc.Descendants(dc + "creator").FirstOrDefault()?.Value));
    }

    // ── ODF template / drawing / chart / formula / database / image variants ────────
    // All nine variants share the ZIP → DetectZipCategory → Odf → StripOdfMetadata
    // pipeline. Routing is triggered by any mimetype starting with
    // "application/vnd.oasis.opendocument.", and the strip path operates on
    // meta.xml regardless of the specific variant. Each test asserts the same
    // contract:
    //   - not treated as passthrough
    //   - creator captured in ExtractedMetadata
    //   - creator cleared in meta.xml of the output package
    //   - RemovedEntryCount > 0

    // .ott — OpenDocument Text Template
    [Fact]
    public void StripFileMetadata_OttVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOdfVariant(
            "application/vnd.oasis.opendocument.text-template",
            creator: "Ott Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Ott Author", result.ExtractedMetadata);
        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var xdoc      = XDocument.Load(zip.GetEntry("meta.xml")!.Open());
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.True(string.IsNullOrEmpty(xdoc.Descendants(dc + "creator").FirstOrDefault()?.Value));
    }

    // .ots — OpenDocument Spreadsheet Template
    [Fact]
    public void StripFileMetadata_OtsVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOdfVariant(
            "application/vnd.oasis.opendocument.spreadsheet-template",
            creator: "Ots Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Ots Author", result.ExtractedMetadata);
        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var xdoc      = XDocument.Load(zip.GetEntry("meta.xml")!.Open());
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.True(string.IsNullOrEmpty(xdoc.Descendants(dc + "creator").FirstOrDefault()?.Value));
    }

    // .otp — OpenDocument Presentation Template
    [Fact]
    public void StripFileMetadata_OtpVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOdfVariant(
            "application/vnd.oasis.opendocument.presentation-template",
            creator: "Otp Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Otp Author", result.ExtractedMetadata);
        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var xdoc      = XDocument.Load(zip.GetEntry("meta.xml")!.Open());
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.True(string.IsNullOrEmpty(xdoc.Descendants(dc + "creator").FirstOrDefault()?.Value));
    }

    // .odg — OpenDocument Drawing
    [Fact]
    public void StripFileMetadata_OdgVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOdfVariant(
            "application/vnd.oasis.opendocument.graphics",
            creator: "Odg Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Odg Author", result.ExtractedMetadata);
        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var xdoc      = XDocument.Load(zip.GetEntry("meta.xml")!.Open());
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.True(string.IsNullOrEmpty(xdoc.Descendants(dc + "creator").FirstOrDefault()?.Value));
    }

    // .otg — OpenDocument Drawing Template
    [Fact]
    public void StripFileMetadata_OtgVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOdfVariant(
            "application/vnd.oasis.opendocument.graphics-template",
            creator: "Otg Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Otg Author", result.ExtractedMetadata);
        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var xdoc      = XDocument.Load(zip.GetEntry("meta.xml")!.Open());
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.True(string.IsNullOrEmpty(xdoc.Descendants(dc + "creator").FirstOrDefault()?.Value));
    }

    // .odc — OpenDocument Chart
    [Fact]
    public void StripFileMetadata_OdcVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOdfVariant(
            "application/vnd.oasis.opendocument.chart",
            creator: "Odc Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Odc Author", result.ExtractedMetadata);
        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var xdoc      = XDocument.Load(zip.GetEntry("meta.xml")!.Open());
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.True(string.IsNullOrEmpty(xdoc.Descendants(dc + "creator").FirstOrDefault()?.Value));
    }

    // .odf — OpenDocument Formula
    [Fact]
    public void StripFileMetadata_OdfVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOdfVariant(
            "application/vnd.oasis.opendocument.formula",
            creator: "Odf Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Odf Author", result.ExtractedMetadata);
        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var xdoc      = XDocument.Load(zip.GetEntry("meta.xml")!.Open());
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.True(string.IsNullOrEmpty(xdoc.Descendants(dc + "creator").FirstOrDefault()?.Value));
    }

    // .odb — OpenDocument Database
    [Fact]
    public void StripFileMetadata_OdbVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOdfVariant(
            "application/vnd.oasis.opendocument.database",
            creator: "Odb Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Odb Author", result.ExtractedMetadata);
        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var xdoc      = XDocument.Load(zip.GetEntry("meta.xml")!.Open());
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.True(string.IsNullOrEmpty(xdoc.Descendants(dc + "creator").FirstOrDefault()?.Value));
    }

    // .odi — OpenDocument Image
    [Fact]
    public void StripFileMetadata_OdiVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOdfVariant(
            "application/vnd.oasis.opendocument.image",
            creator: "Odi Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Odi Author", result.ExtractedMetadata);
        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var xdoc      = XDocument.Load(zip.GetEntry("meta.xml")!.Open());
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.True(string.IsNullOrEmpty(xdoc.Descendants(dc + "creator").FirstOrDefault()?.Value));
    }
}
