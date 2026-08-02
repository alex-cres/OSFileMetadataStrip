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

// ── GDI+ mirror of the O11 test adapter surface ───────────────────────────────
// Identical to FileMetadataStripping.O11.Tests\TestHelpers.cs, with ONE
// difference: the FileMetadataStripping adapter's static constructor forces
// GDI+ fallback mode once per AppDomain, so every test in this assembly
// exercises the GDI+ implementation path regardless of Magick.NET's
// availability on the host.
//
// Assembly-level DisableTestParallelization keeps the shared static
// `_magickBroken` flag stable across all tests in this project.

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
    // Runs once per AppDomain, the first time this type is touched (which is
    // when the first test instantiates `new FileMetadataStripping()`). Forces
    // the GDI+ fallback path for every subsequent call to MssStripFileMetadata
    // in this assembly's run.
    static FileMetadataStripping()
    {
        OutSystems.NssFileMetadataStripping.CssFileMetadataStripping.ForceGdiFallbackForTesting();
    }

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

// TestHelpers per-format Create<X>() helpers are shared with the primary O11
// project via `<Compile Include="...\FileMetadataStripping.O11.Tests\TestHelpers.Images.cs" Link=...>`
// (and the Documents/Media siblings) in the csproj — same source of truth,
// single implementation.
internal static partial class TestHelpers { }
