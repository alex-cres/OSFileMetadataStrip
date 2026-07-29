using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using System.IO.Packaging;
using System.Xml.Linq;
using TagLib;

namespace FileMetadataStripping.Tests;

// ── O11 adapter types ─────────────────────────────────────────────────────────
// These mirror the ODC FileMetadataResult / IFileMetadataStripping surface so
// that all five test files are byte-for-byte identical to the ODC test project.

internal struct FileMetadataResult
{
    public byte[]  CleanFile          { get; set; }
    public string  ExtractedMetadata  { get; set; }
    public int     RemovedEntryCount  { get; set; }
    public bool    IsPassthrough      { get; set; }
}

internal interface IFileMetadataStripping
{
    FileMetadataResult StripFileMetadata(byte[] rawFile, bool stripBodyAuthors);
}

internal sealed class FileMetadataStripping : IFileMetadataStripping
{
    private readonly OutSystems.NssFileMetadataStripping.CssFileMetadataStripping _inner = new();

    public FileMetadataResult StripFileMetadata(byte[] rawFile, bool stripBodyAuthors)
    {
        _inner.MssStripFileMetadata(rawFile, stripBodyAuthors, out var r);
        return new FileMetadataResult
        {
            CleanFile         = r.ssCleanFile,
            ExtractedMetadata = r.ssExtractedMetadata,
            RemovedEntryCount = r.ssRemovedEntryCount,
            IsPassthrough     = r.ssIsPassthrough
        };
    }
}

// ── Test data helpers ─────────────────────────────────────────────────────────

internal static partial class TestHelpers
{
    // Shared test-data infrastructure. All helpers generate data programmatically -- no binary files are committed. Category-specific Create methods live in TestHelpers.Images.cs, TestHelpers.Documents.cs, and TestHelpers.Media.cs.

}
