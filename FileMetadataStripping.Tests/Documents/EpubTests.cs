using System.IO;
using System.IO.Compression;
using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for EPUB (Electronic Publication) files — ZIP with mimetype "application/epub+zip".
///
/// EPUB is detected via the ZIP <c>mimetype</c> entry and routed to a dedicated path
/// that strips Dublin Core metadata (dc:creator, dc:title, dc:description, dc:rights, …)
/// and OPF &lt;meta&gt; refinements from the OPF package document referenced by
/// META-INF/container.xml. The synthetic EPUB used here contains no OPF, so the
/// strip path returns the archive unchanged with RemovedEntryCount = 0.</summary>
public class EpubTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_EpubInput_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.StripFileMetadata(TestHelpers.CreateEpub(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_EpubInput_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateEpub(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_EpubInput_CleanFileIsNonEmpty()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateEpub(), false);
        Assert.NotEmpty(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_EpubInput_RemovedEntryCountIsNonNegative()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateEpub(), false);
        Assert.True(result.RemovedEntryCount >= 0);
    }

    [Fact]
    public void StripFileMetadata_EpubWithOpfMetadata_CreatorIsStrippedFromOutputOpf()
    {
        // dc:creator and dc:title in the OPF must be blanked in the round-tripped OPF.
        var input  = TestHelpers.CreateEpubWithOpf("Sensitive Author", "Confidential Title");
        var result = _sut.StripFileMetadata(input, false);

        using var ms  = new MemoryStream(result.CleanFile);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var opfEntry = zip.GetEntry("OEBPS/content.opf");
        Assert.NotNull(opfEntry);

        string opfText;
        using (var s = opfEntry!.Open())
        using (var reader = new StreamReader(s))
            opfText = reader.ReadToEnd();

        Assert.DoesNotContain("Sensitive Author",    opfText);
        Assert.DoesNotContain("Confidential Title", opfText);
    }

    [Fact]
    public void StripFileMetadata_EpubWithOpfMetadata_ValuesAreCapturedInExtractedMetadata()
    {
        var input  = TestHelpers.CreateEpubWithOpf("Sensitive Author", "Confidential Title", "Secret Description");
        var result = _sut.StripFileMetadata(input, false);

        Assert.Contains("Sensitive Author",    result.ExtractedMetadata);
        Assert.Contains("Confidential Title",  result.ExtractedMetadata);
        Assert.Contains("Secret Description",  result.ExtractedMetadata);
        Assert.True(result.RemovedEntryCount >= 3);
    }

    [Fact]
    public void StripFileMetadata_EpubWithTraversalFullPath_OutputArchiveIsUnchanged()
    {
        // An EPUB whose container.xml declares a rootfile at "../evil.opf" must NOT
        // cause the output archive to contain a traversal-payload entry. Path traversal
        // is rejected at parse time and the OPF strip step is skipped entirely.
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mimetypeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var s = mimetypeEntry.Open())
            {
                var b = System.Text.Encoding.ASCII.GetBytes("application/epub+zip");
                s.Write(b, 0, b.Length);
            }
            var container = zip.CreateEntry("META-INF/container.xml");
            using (var s = container.Open())
            {
                var b = System.Text.Encoding.UTF8.GetBytes(
                    "<?xml version='1.0'?>" +
                    "<container version='1.0' xmlns='urn:oasis:names:tc:opendocument:xmlns:container'>" +
                    "<rootfiles><rootfile full-path='../../evil.opf' " +
                    "media-type='application/oebps-package+xml'/></rootfiles></container>");
                s.Write(b, 0, b.Length);
            }
        }
        var result = _sut.StripFileMetadata(ms.ToArray(), false);

        using var outMs = new MemoryStream(result.CleanFile);
        using var outZip = new ZipArchive(outMs, ZipArchiveMode.Read);
        Assert.All(outZip.Entries, entry =>
            Assert.DoesNotContain("..", entry.FullName));
    }
}
