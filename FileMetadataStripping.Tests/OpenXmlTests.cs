using System.IO.Packaging;
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
}
