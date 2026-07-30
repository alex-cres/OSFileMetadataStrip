using System.Text;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for HTML / HTM passthrough contract.
///
/// HTML is deliberately excluded from active metadata stripping. See the note in
/// <c>docs/format-coverage.md</c> and the "🟢 Low" tier of the priorities table
/// in the handoff. The rationale:
/// <list type="bullet">
///   <item><c>&lt;meta charset&gt;</c>, <c>&lt;meta viewport&gt;</c> and
///         <c>&lt;meta http-equiv&gt;</c> are functional and required for
///         rendering — selective meta-name stripping is fragile.</item>
///   <item>The real attack surface in HTML is <c>&lt;script&gt;</c> and event
///         handlers, not meta tags. A metadata stripper that leaves
///         <c>&lt;script&gt;</c> in place gives a false sense of security.</item>
///   <item>HTML hardening belongs to a dedicated sanitiser
///         (<c>HtmlSanitizer</c> / DOMPurify / Bleach) applied downstream by
///         the caller.</item>
/// </list>
/// These tests lock in the passthrough contract so any future reclassification
/// of HTML has to update the tests deliberately.</summary>
public class HtmlPassthroughTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_HtmlWithMetaAuthor_IsPassthrough()
    {
        var input  = TestHelpers.CreateHtml(metaAuthor: "Alice");
        var result = _sut.StripFileMetadata(input, false);
        Assert.True(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_HtmlWithMetaAuthor_CleanFileEqualsInput()
    {
        var input  = TestHelpers.CreateHtml(metaAuthor: "Alice");
        var result = _sut.StripFileMetadata(input, false);
        Assert.Equal(input, result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_HtmlWithMetaAuthor_RemovedEntryCountIsZero()
    {
        var input  = TestHelpers.CreateHtml(metaAuthor: "Alice");
        var result = _sut.StripFileMetadata(input, false);
        Assert.Equal(0, result.RemovedEntryCount);
    }

    [Fact]
    public void StripFileMetadata_HtmlWithMetaAuthor_ExtractedMetadataIsEmpty()
    {
        var input  = TestHelpers.CreateHtml(metaAuthor: "Alice");
        var result = _sut.StripFileMetadata(input, false);
        Assert.Equal("[]", result.ExtractedMetadata);
    }

    [Fact]
    public void StripFileMetadata_HtmlDoctype_IsPassthrough()
    {
        // "<!DOCTYPE html" is the canonical HTML5 prefix — must fall through
        // to passthrough, not to any XML / SVG path.
        var input  = Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body></body></html>");
        var result = _sut.StripFileMetadata(input, false);
        Assert.True(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_HtmlWithTitle_TitleValuePreservedInOutput()
    {
        // Passthrough contract: the HTML `<title>` is document content, not
        // metadata. It must survive unchanged.
        var input  = TestHelpers.CreateHtml(title: "My Document Title");
        var result = _sut.StripFileMetadata(input, false);
        var text   = Encoding.UTF8.GetString(result.CleanFile);
        Assert.Contains("My Document Title", text);
    }

    [Fact]
    public void StripFileMetadata_HtmlWithMetaAuthor_MetaValuePreservedInOutput()
    {
        // Passthrough contract: `<meta name="author">` is NOT stripped by this
        // component. Callers who need to remove it must apply a dedicated HTML
        // sanitiser downstream. This test locks that contract in.
        var input  = TestHelpers.CreateHtml(metaAuthor: "PreservedAuthorName");
        var result = _sut.StripFileMetadata(input, false);
        var text   = Encoding.UTF8.GetString(result.CleanFile);
        Assert.Contains("PreservedAuthorName", text);
    }
}
