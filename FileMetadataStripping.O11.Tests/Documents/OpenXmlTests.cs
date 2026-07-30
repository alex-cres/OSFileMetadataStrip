using System.IO.Packaging;
using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>
/// Tests for Office Open XML stripping (DOCX, XLSX, PPTX).
/// Covers: core properties cleared, metadata captured for audit, valid OOXML output, IsPassthrough flag.
/// </summary>
public class OpenXmlTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_DocxWithMetadata_CreatorIsCleared()
    {
        var input = TestHelpers.CreateDocx(creator: "Attacker", title: "Injected Title");

        var result = _sut.StripFileMetadata(input, false);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        Assert.Null(package.PackageProperties.Creator);
        Assert.Null(package.PackageProperties.Title);
    }

    [Fact]
    public void StripFileMetadata_DocxWithMetadata_ExtractedMetadataContainsCreator()
    {
        var input = TestHelpers.CreateDocx(creator: "Attacker Name");

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Attacker Name", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_DocxWithMetadata_RemovedEntryCountIsGreaterThanZero()
    {
        var input = TestHelpers.CreateDocx(creator: "Attacker", title: "Injected");

        var result = _sut.StripFileMetadata(input, false);

        Assert.True(result.RemovedEntryCount > 0);
    }

    [Fact]
    public void StripFileMetadata_DocxOutput_IsValidOoxml()
    {
        var input = TestHelpers.CreateDocx(creator: "Test Creator");

        var result = _sut.StripFileMetadata(input, false);

        // OOXML is a ZIP: PK signature
        Assert.Equal(0x50, result.CleanFile[0]);
        Assert.Equal(0x4B, result.CleanFile[1]);
    }

    [Fact]
    public void StripFileMetadata_Docx_IsPassthroughIsFalse()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateDocx(), false);

        Assert.False(result.IsPassthrough);
    }

    // ── Encrypted / unreadable OOXML ──────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_EncryptedDocx_DoesNotThrow()
    {
        var input = TestHelpers.CreateCorruptedDocx();

        var ex = Record.Exception(() => _sut.StripFileMetadata(input, false));

        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_EncryptedDocx_ReturnsOriginalFileUnchanged()
    {
        var input = TestHelpers.CreateCorruptedDocx();

        var result = _sut.StripFileMetadata(input, false);

        Assert.Equal(input, result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_EncryptedDocx_ExtractedMetadataContainsProcessingError()
    {
        var input = TestHelpers.CreateCorruptedDocx();

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("processingError", result.ExtractedMetadata);
        Assert.Equal(0, result.RemovedEntryCount);
        Assert.False(result.IsPassthrough);
    }

    // ── App properties (docProps/app.xml) ─────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_DocxWithAppCompany_CompanyIsCleared()
    {
        var input = TestHelpers.CreateDocxWithAppProperties(company: "Acme Corp");

        var result = _sut.StripFileMetadata(input, false);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        var appUri = PackUriHelper.CreatePartUri(new Uri("/docProps/app.xml", UriKind.Relative));
        Assert.True(package.PartExists(appUri));
        var xdoc = XDocument.Load(package.GetPart(appUri).GetStream());
        XNamespace ep = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
        Assert.True(string.IsNullOrEmpty(xdoc.Root?.Element(ep + "Company")?.Value));
    }

    [Fact]
    public void StripFileMetadata_DocxWithAppCompany_ExtractedMetadataContainsCompany()
    {
        var input = TestHelpers.CreateDocxWithAppProperties(company: "Acme Corp");

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("appCompany", result.ExtractedMetadata);
        Assert.Contains("Acme Corp", result.ExtractedMetadata);
    }

    // ── Custom properties (docProps/custom.xml) ───────────────────────────────────────

    [Fact]
    public void StripFileMetadata_DocxWithCustomProperties_CustomPropsAreCleared()
    {
        var input = TestHelpers.CreateDocxWithCustomProperties(new Dictionary<string, string> { ["ProjectCode"] = "PRJ-001" });

        var result = _sut.StripFileMetadata(input, false);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        var customUri = PackUriHelper.CreatePartUri(new Uri("/docProps/custom.xml", UriKind.Relative));
        if (package.PartExists(customUri))
        {
            var cp = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/custom-properties");
            var xdoc = XDocument.Load(package.GetPart(customUri).GetStream());
            Assert.Empty(xdoc.Root?.Elements(cp + "property") ?? Enumerable.Empty<XElement>());
        }
    }

    [Fact]
    public void StripFileMetadata_DocxWithCustomProperties_ExtractedMetadataContainsCustomProps()
    {
        var input = TestHelpers.CreateDocxWithCustomProperties(new Dictionary<string, string> { ["ProjectCode"] = "PRJ-001" });

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("customProperties", result.ExtractedMetadata);
        Assert.Contains("ProjectCode", result.ExtractedMetadata);
    }

    // ── Tracked changes — DOCX ───────────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_DocxWithTrackedChanges_AuthorNameIsBlankInOutput()
    {
        var input = TestHelpers.CreateDocxWithTrackedChanges(authorName: "John Doe");

        var result = _sut.StripFileMetadata(input, true);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        var docUri = PackUriHelper.CreatePartUri(new Uri("/word/document.xml", UriKind.Relative));
        var xdoc = XDocument.Load(package.GetPart(docUri).GetStream());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var authorAttrs = xdoc.Descendants()
            .Select(e => e.Attribute(w + "author"))
            .Where(a => a != null)
            .ToList();
        Assert.All(authorAttrs, a => Assert.Empty(a!.Value));
    }

    [Fact]
    public void StripFileMetadata_DocxWithTrackedChanges_ExtractedMetadataContainsStrippedAuthors()
    {
        var input = TestHelpers.CreateDocxWithTrackedChanges(authorName: "John Doe");

        var result = _sut.StripFileMetadata(input, true);

        Assert.Contains("strippedAuthors", result.ExtractedMetadata);
        Assert.Contains("John Doe", result.ExtractedMetadata);
    }

    // ── Comments — DOCX ────────────────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_DocxWithComment_AuthorNameIsBlankInOutput()
    {
        var input = TestHelpers.CreateDocxWithComment(authorName: "Jane Smith");

        var result = _sut.StripFileMetadata(input, true);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        var commentsUri = PackUriHelper.CreatePartUri(new Uri("/word/comments.xml", UriKind.Relative));
        var xdoc = XDocument.Load(package.GetPart(commentsUri).GetStream());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var authorAttrs = xdoc.Descendants(w + "comment")
            .Select(e => e.Attribute(w + "author"))
            .Where(a => a != null)
            .ToList();
        Assert.All(authorAttrs, a => Assert.Empty(a!.Value));
    }

    [Fact]
    public void StripFileMetadata_DocxWithComment_InitialsAreBlankInOutput()
    {
        var input = TestHelpers.CreateDocxWithComment(authorName: "Jane Smith");

        var result = _sut.StripFileMetadata(input, true);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        var commentsUri = PackUriHelper.CreatePartUri(new Uri("/word/comments.xml", UriKind.Relative));
        var xdoc = XDocument.Load(package.GetPart(commentsUri).GetStream());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var initialsAttrs = xdoc.Descendants(w + "comment")
            .Select(e => e.Attribute(w + "initials"))
            .Where(a => a != null)
            .ToList();
        Assert.All(initialsAttrs, a => Assert.Empty(a!.Value));
    }

    // ── Comments — XLSX ────────────────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_XlsxWithComments_AuthorNameIsBlankInOutput()
    {
        var input = TestHelpers.CreateXlsxWithComments(authorName: "Excel Author");

        var result = _sut.StripFileMetadata(input, true);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        var commentsUri = PackUriHelper.CreatePartUri(new Uri("/xl/comments1.xml", UriKind.Relative));
        var xdoc = XDocument.Load(package.GetPart(commentsUri).GetStream());
        XNamespace xl = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var authorEls = xdoc.Descendants(xl + "author").ToList();
        Assert.All(authorEls, el => Assert.Empty(el.Value));
    }

    [Fact]
    public void StripFileMetadata_XlsxWithComments_ExtractedMetadataContainsStrippedAuthors()
    {
        var input = TestHelpers.CreateXlsxWithComments(authorName: "Excel Author");

        var result = _sut.StripFileMetadata(input, true);

        Assert.Contains("strippedAuthors", result.ExtractedMetadata);
        Assert.Contains("Excel Author", result.ExtractedMetadata);
    }

    // ── Comments — PPTX ────────────────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_PptxWithCommentAuthors_AuthorNameIsBlankInOutput()
    {
        var input = TestHelpers.CreatePptxWithCommentAuthors(authorName: "Presenter");

        var result = _sut.StripFileMetadata(input, true);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        var authorsUri = PackUriHelper.CreatePartUri(new Uri("/ppt/commentAuthors.xml", UriKind.Relative));
        var xdoc = XDocument.Load(package.GetPart(authorsUri).GetStream());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var nameAttrs = xdoc.Descendants(p + "cmAuthor")
            .Select(e => e.Attribute("name"))
            .Where(a => a != null)
            .ToList();
        Assert.All(nameAttrs, a => Assert.Empty(a!.Value));
    }

    [Fact]
    public void StripFileMetadata_PptxWithCommentAuthors_ExtractedMetadataContainsStrippedAuthors()
    {
        var input = TestHelpers.CreatePptxWithCommentAuthors(authorName: "Presenter");

        var result = _sut.StripFileMetadata(input, true);

        Assert.Contains("strippedAuthors", result.ExtractedMetadata);
        Assert.Contains("Presenter", result.ExtractedMetadata);
    }

    // ── StripBodyAuthors=false (default) ─────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_DocxWithTrackedChanges_AuthorPreservedWhenStripBodyAuthorsFalse()
    {
        var input = TestHelpers.CreateDocxWithTrackedChanges(authorName: "John Doe");

        var result = _sut.StripFileMetadata(input, false);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        var docUri = PackUriHelper.CreatePartUri(new Uri("/word/document.xml", UriKind.Relative));
        var xdoc = XDocument.Load(package.GetPart(docUri).GetStream());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var authorAttrs = xdoc.Descendants()
            .Select(e => e.Attribute(w + "author"))
            .Where(a => a != null)
            .ToList();
        // Author name must NOT be blanked when stripBodyAuthors is false.
        Assert.All(authorAttrs, a => Assert.Equal("John Doe", a!.Value));
    }

    [Fact]
    public void StripFileMetadata_DocxWithTrackedChanges_StrippedAuthorsAbsentWhenStripBodyAuthorsFalse()
    {
        var input = TestHelpers.CreateDocxWithTrackedChanges(authorName: "John Doe");

        var result = _sut.StripFileMetadata(input, false);

        Assert.DoesNotContain("strippedAuthors", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_DocxWithComment_AuthorPreservedWhenStripBodyAuthorsFalse()
    {
        var input = TestHelpers.CreateDocxWithComment(authorName: "Jane Smith");

        var result = _sut.StripFileMetadata(input, false);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        var commentsUri = PackUriHelper.CreatePartUri(new Uri("/word/comments.xml", UriKind.Relative));
        var xdoc = XDocument.Load(package.GetPart(commentsUri).GetStream());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var authorAttrs = xdoc.Descendants(w + "comment")
            .Select(e => e.Attribute(w + "author"))
            .Where(a => a != null)
            .ToList();
        Assert.All(authorAttrs, a => Assert.Equal("Jane Smith", a!.Value));
    }

    [Fact]
    public void StripFileMetadata_XlsxWithComments_AuthorPreservedWhenStripBodyAuthorsFalse()
    {
        var input = TestHelpers.CreateXlsxWithComments(authorName: "Excel Author");

        var result = _sut.StripFileMetadata(input, false);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        var commentsUri = PackUriHelper.CreatePartUri(new Uri("/xl/comments1.xml", UriKind.Relative));
        var xdoc = XDocument.Load(package.GetPart(commentsUri).GetStream());
        XNamespace xl = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var authorEls = xdoc.Descendants(xl + "author").ToList();
        Assert.All(authorEls, el => Assert.Equal("Excel Author", el.Value));
    }

    [Fact]
    public void StripFileMetadata_PptxWithCommentAuthors_AuthorPreservedWhenStripBodyAuthorsFalse()
    {
        var input = TestHelpers.CreatePptxWithCommentAuthors(authorName: "Presenter");

        var result = _sut.StripFileMetadata(input, false);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        var authorsUri = PackUriHelper.CreatePartUri(new Uri("/ppt/commentAuthors.xml", UriKind.Relative));
        var xdoc = XDocument.Load(package.GetPart(authorsUri).GetStream());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var nameAttrs = xdoc.Descendants(p + "cmAuthor")
            .Select(e => e.Attribute("name"))
            .Where(a => a != null)
            .ToList();
        Assert.All(nameAttrs, a => Assert.Equal("Presenter", a!.Value));
    }

    // ── OOXML core props: LastPrinted / Identifier ───────────────────────────────────

    [Fact]
    public void StripFileMetadata_DocxWithLastPrinted_LastPrintedAndIdentifierAreCleared()
    {
        var input = TestHelpers.CreateDocxWithLastPrinted();

        var result = _sut.StripFileMetadata(input, false);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        Assert.Null(package.PackageProperties.LastPrinted);
        Assert.Null(package.PackageProperties.Identifier);
    }

    [Fact]
    public void StripFileMetadata_DocxWithLastPrinted_ExtractedMetadataContainsLastPrinted()
    {
        var input = TestHelpers.CreateDocxWithLastPrinted();

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("lastPrinted", result.ExtractedMetadata);
        Assert.Contains("identifier", result.ExtractedMetadata);
    }

    // ── Excel xl/persons (Microsoft 365 threaded comments) ────────────────────────

    [Fact]
    public void StripFileMetadata_XlsxWithPersons_DisplayNameIsBlankWhenStripBodyAuthorsTrue()
    {
        var input = TestHelpers.CreateXlsxWithPersons(displayName: "Alice Smith");

        var result = _sut.StripFileMetadata(input, true);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        var personsUri = PackUriHelper.CreatePartUri(new Uri("/xl/persons/person.xml", UriKind.Relative));
        var xdoc = XDocument.Load(package.GetPart(personsUri).GetStream());
        XNamespace ns = "http://schemas.microsoft.com/office/spreadsheetml/2017/11/persons";
        var nameAttrs = xdoc.Descendants(ns + "Person")
            .Select(e => e.Attribute("displayName"))
            .Where(a => a != null)
            .ToList();
        Assert.All(nameAttrs, a => Assert.Empty(a!.Value));
    }

    [Fact]
    public void StripFileMetadata_XlsxWithPersons_DisplayNamePreservedWhenStripBodyAuthorsFalse()
    {
        var input = TestHelpers.CreateXlsxWithPersons(displayName: "Alice Smith");

        var result = _sut.StripFileMetadata(input, false);

        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        var personsUri = PackUriHelper.CreatePartUri(new Uri("/xl/persons/person.xml", UriKind.Relative));
        var xdoc = XDocument.Load(package.GetPart(personsUri).GetStream());
        XNamespace ns = "http://schemas.microsoft.com/office/spreadsheetml/2017/11/persons";
        var nameAttrs = xdoc.Descendants(ns + "Person")
            .Select(e => e.Attribute("displayName"))
            .Where(a => a != null)
            .ToList();
        Assert.All(nameAttrs, a => Assert.Equal("Alice Smith", a!.Value));
    }

    // ── XLSX core properties ──────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_XlsxWithCreator_ExtractedMetadataContainsCreator()
    {
        var input = TestHelpers.CreateXlsx(creator: "Attacker Name");

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Attacker Name", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_XlsxWithCreator_RemovedEntryCountIsGreaterThanZero()
    {
        var input = TestHelpers.CreateXlsx(creator: "Attacker Name");

        var result = _sut.StripFileMetadata(input, false);

        Assert.True(result.RemovedEntryCount > 0);
    }

    // ── PPTX core properties ──────────────────────────────────────────────────────

    [Fact]
    public void StripFileMetadata_PptxWithCreator_ExtractedMetadataContainsCreator()
    {
        var input = TestHelpers.CreatePptx(creator: "Attacker Name");

        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Attacker Name", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_PptxWithCreator_RemovedEntryCountIsGreaterThanZero()
    {
        var input = TestHelpers.CreatePptx(creator: "Attacker Name");

        var result = _sut.StripFileMetadata(input, false);

        Assert.True(result.RemovedEntryCount > 0);
    }

    // ── OOXML embedded thumbnail removal ─────────────────────────────────────

    [Fact]
    public void StripFileMetadata_DocxWithThumbnail_ThumbnailEntryIsRemoved()
    {
        var input  = TestHelpers.CreateDocxWithThumbnail();
        var result = _sut.StripFileMetadata(input, false);

        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.Null(zip.GetEntry("docProps/thumbnail.jpeg"));
    }

    [Fact]
    public void StripFileMetadata_DocxWithThumbnail_ThumbnailRelationshipIsRemoved()
    {
        var input  = TestHelpers.CreateDocxWithThumbnail();
        var result = _sut.StripFileMetadata(input, false);

        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var relsEntry = zip.GetEntry("_rels/.rels");
        Assert.NotNull(relsEntry);

        XDocument rels;
        using (var s = relsEntry!.Open()) rels = XDocument.Load(s);

        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
        var thumbRels = rels.Descendants(ns + "Relationship")
            .Where(r => (r.Attribute("Type")?.Value ?? string.Empty)
                .EndsWith("/metadata/thumbnail", System.StringComparison.OrdinalIgnoreCase));
        Assert.Empty(thumbRels);
    }

    [Fact]
    public void StripFileMetadata_DocxWithThumbnail_ExtractedMetadataFlagsRemoval()
    {
        var input  = TestHelpers.CreateDocxWithThumbnail();
        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("thumbnail", result.ExtractedMetadata);
        Assert.Contains("removed",   result.ExtractedMetadata);
        Assert.True(result.RemovedEntryCount >= 1);
    }

    // ── OOXML template / macro-enabled variants ──────────────────────────────────────
    // All eleven variants share the ZIP → DetectZipCategory → OpenXml → StripOpenXmlMetadata
    // pipeline. The strip path operates on docProps/*.xml regardless of the main-part
    // content type, so each test asserts the same contract:
    //   - not treated as passthrough
    //   - creator captured in ExtractedMetadata
    //   - creator cleared in the output package
    //   - RemovedEntryCount > 0

    // .docm — Word 2007+ macro-enabled document
    [Fact]
    public void StripFileMetadata_DocmVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOoxmlVariant(
            "/word/document.xml",
            "application/vnd.ms-word.document.macroEnabled.main+xml",
            creator: "Docm Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Docm Author", result.ExtractedMetadata);
        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        Assert.Null(package.PackageProperties.Creator);
    }

    // .dotx — Word 2007+ template
    [Fact]
    public void StripFileMetadata_DotxVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOoxmlVariant(
            "/word/document.xml",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml",
            creator: "Dotx Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Dotx Author", result.ExtractedMetadata);
        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        Assert.Null(package.PackageProperties.Creator);
    }

    // .dotm — Word 2007+ macro-enabled template
    [Fact]
    public void StripFileMetadata_DotmVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOoxmlVariant(
            "/word/document.xml",
            "application/vnd.ms-word.template.macroEnabled.main+xml",
            creator: "Dotm Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Dotm Author", result.ExtractedMetadata);
        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        Assert.Null(package.PackageProperties.Creator);
    }

    // .xlsm — Excel 2007+ macro-enabled workbook
    [Fact]
    public void StripFileMetadata_XlsmVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOoxmlVariant(
            "/xl/workbook.xml",
            "application/vnd.ms-excel.sheet.macroEnabled.main+xml",
            creator: "Xlsm Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Xlsm Author", result.ExtractedMetadata);
        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        Assert.Null(package.PackageProperties.Creator);
    }

    // .xltx — Excel 2007+ template
    [Fact]
    public void StripFileMetadata_XltxVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOoxmlVariant(
            "/xl/workbook.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml",
            creator: "Xltx Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Xltx Author", result.ExtractedMetadata);
        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        Assert.Null(package.PackageProperties.Creator);
    }

    // .xltm — Excel 2007+ macro-enabled template
    [Fact]
    public void StripFileMetadata_XltmVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOoxmlVariant(
            "/xl/workbook.xml",
            "application/vnd.ms-excel.template.macroEnabled.main+xml",
            creator: "Xltm Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Xltm Author", result.ExtractedMetadata);
        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        Assert.Null(package.PackageProperties.Creator);
    }

    // .pptm — PowerPoint 2007+ macro-enabled presentation
    [Fact]
    public void StripFileMetadata_PptmVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOoxmlVariant(
            "/ppt/presentation.xml",
            "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml",
            creator: "Pptm Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Pptm Author", result.ExtractedMetadata);
        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        Assert.Null(package.PackageProperties.Creator);
    }

    // .potx — PowerPoint 2007+ template
    [Fact]
    public void StripFileMetadata_PotxVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOoxmlVariant(
            "/ppt/presentation.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.template.main+xml",
            creator: "Potx Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Potx Author", result.ExtractedMetadata);
        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        Assert.Null(package.PackageProperties.Creator);
    }

    // .potm — PowerPoint 2007+ macro-enabled template
    [Fact]
    public void StripFileMetadata_PotmVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOoxmlVariant(
            "/ppt/presentation.xml",
            "application/vnd.ms-powerpoint.template.macroEnabled.main+xml",
            creator: "Potm Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Potm Author", result.ExtractedMetadata);
        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        Assert.Null(package.PackageProperties.Creator);
    }

    // .ppsx — PowerPoint 2007+ slideshow
    [Fact]
    public void StripFileMetadata_PpsxVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOoxmlVariant(
            "/ppt/presentation.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml",
            creator: "Ppsx Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Ppsx Author", result.ExtractedMetadata);
        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        Assert.Null(package.PackageProperties.Creator);
    }

    // .ppsm — PowerPoint 2007+ macro-enabled slideshow
    [Fact]
    public void StripFileMetadata_PpsmVariant_CreatorClearedAndAudited()
    {
        var input = TestHelpers.CreateOoxmlVariant(
            "/ppt/presentation.xml",
            "application/vnd.ms-powerpoint.slideshow.macroEnabled.main+xml",
            creator: "Ppsm Author");

        var result = _sut.StripFileMetadata(input, false);

        Assert.False(result.IsPassthrough);
        Assert.True(result.RemovedEntryCount > 0);
        Assert.Contains("creator", result.ExtractedMetadata);
        Assert.Contains("Ppsm Author", result.ExtractedMetadata);
        using var ms = new MemoryStream(result.CleanFile);
        using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
        Assert.Null(package.PackageProperties.Creator);
    }
}
