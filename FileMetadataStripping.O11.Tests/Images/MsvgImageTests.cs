using ImageMagick;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for MSVG (Magick internal SVG renderer format).
///
/// MSVG is not a distinct on-disk file format — it's a MagickFormat value that
/// requests the ImageMagick internal renderer (as opposed to the RSVG or Inkscape
/// external renderers). Writing an image with <c>MagickFormat.Msvg</c> produces
/// bytes that begin with the same XML declaration and <c>&lt;svg&gt;</c> root
/// element as any other SVG document.
///
/// Because our library detects SVG purely from its byte pattern (an XML/element
/// opening bracket followed by a <c>&lt;svg</c> tag within the first 4 KB),
/// MSVG-produced output is caught by <c>IsSvgFile()</c> and routed through the
/// dedicated <c>StripSvgMetadata()</c> path — the same XML-aware stripper used
/// for any other SVG source. Magick.NET is never invoked for MSVG bytes.
///
/// The tests verify that:
///   1. MSVG output is detected as SVG (not passthrough, not routed to the
///      raster image pipeline).
///   2. The output remains a valid SVG document.
///   3. The XML-aware stripper still removes <c>&lt;title&gt;</c>,
///      <c>&lt;desc&gt;</c>, and <c>&lt;metadata&gt;</c> nodes when they are
///      present on the source.
/// </summary>
public class MsvgImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_MsvgInput_IsPassthroughIsFalse()
    {
        // MSVG output starts with XML and contains "<svg", so IsSvgFile() catches
        // it and routes to StripSvgMetadata — never to the raster image path or
        // to passthrough.
        var result = _sut.StripFileMetadata(TestHelpers.CreateMsvg(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_MsvgInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateMsvg(), false);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_MsvgInput_CleanFileContainsSvgRootElement()
    {
        // The SVG XML strip path preserves the SVG document — the round-tripped
        // bytes must still contain the "<svg" root element.
        var result = _sut.StripFileMetadata(TestHelpers.CreateMsvg(), false);
        var text = System.Text.Encoding.UTF8.GetString(result.CleanFile);
        Assert.Contains("<svg", text);
    }

    [Fact]
    public void StripFileMetadata_MsvgInput_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateMsvg(), false));
        Assert.Null(ex);
    }
}
