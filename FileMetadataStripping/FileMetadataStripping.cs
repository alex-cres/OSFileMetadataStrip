using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.IO.Packaging;
using System.Text.Json.Nodes;
using TagLib;

namespace FileMetadataStripping;

public class FileMetadataStripping : IFileMetadataStripping
{
    private enum FileCategory { Image, Pdf, OpenXml, Media, Passthrough }

    public FileMetadataResult StripFileMetadata(byte[] rawFile)
    {
        return DetectCategory(rawFile) switch
        {
            FileCategory.Image       => StripImageMetadata(rawFile),
            FileCategory.Pdf         => StripPdfMetadata(rawFile),
            FileCategory.OpenXml     => StripOpenXmlMetadata(rawFile),
            FileCategory.Media       => StripMediaMetadata(rawFile),
            FileCategory.Passthrough => Passthrough(rawFile),
            _                        => Passthrough(rawFile)
        };
    }

    // ── Detection ─────────────────────────────────────────────────────────────

    private static FileCategory DetectCategory(byte[] rawFile)
    {
        // PDF: %PDF magic bytes (0x25 0x50 0x44 0x46) — check before image (PDF can have JPEG previews)
        if (rawFile.Length >= 4
            && rawFile[0] == 0x25 && rawFile[1] == 0x50
            && rawFile[2] == 0x44 && rawFile[3] == 0x46)
            return FileCategory.Pdf;

        // Office Open XML (DOCX/XLSX/PPTX): ZIP PK signature (0x50 0x4B 0x03 0x04)
        if (rawFile.Length >= 4
            && rawFile[0] == 0x50 && rawFile[1] == 0x4B
            && rawFile[2] == 0x03 && rawFile[3] == 0x04)
            return FileCategory.OpenXml;

        // Images: JPEG, PNG, GIF, BMP, TIFF, WebP, TGA, and 100+ more — detected by Magick.NET
        try
        {
            var info = new MagickImageInfo(rawFile);
            if (info.Format != MagickFormat.Unknown)
                return FileCategory.Image;
        }
        catch (MagickException) { }

        // Audio/video — detected by magic bytes (TagLibSharp handles these formats)
        // MP3: ID3 header
        if (rawFile.Length >= 3 && rawFile[0] == 0x49 && rawFile[1] == 0x44 && rawFile[2] == 0x33)
            return FileCategory.Media;
        // FLAC: fLaC
        if (rawFile.Length >= 4 && rawFile[0] == 0x66 && rawFile[1] == 0x4C && rawFile[2] == 0x61 && rawFile[3] == 0x43)
            return FileCategory.Media;
        // OGG: OggS
        if (rawFile.Length >= 4 && rawFile[0] == 0x4F && rawFile[1] == 0x67 && rawFile[2] == 0x67 && rawFile[3] == 0x53)
            return FileCategory.Media;
        // RIFF container: WAV (WAVE) or AVI (AVI )
        if (rawFile.Length >= 12 && rawFile[0] == 0x52 && rawFile[1] == 0x49 && rawFile[2] == 0x46 && rawFile[3] == 0x46
            && ((rawFile[8] == 0x57 && rawFile[9] == 0x41 && rawFile[10] == 0x56 && rawFile[11] == 0x45)
             || (rawFile[8] == 0x41 && rawFile[9] == 0x56 && rawFile[10] == 0x49 && rawFile[11] == 0x20)))
            return FileCategory.Media;
        // ISO Base Media (MP4, M4A, M4V, MOV): "ftyp" at bytes 4–7
        if (rawFile.Length >= 8 && rawFile[4] == 0x66 && rawFile[5] == 0x74 && rawFile[6] == 0x79 && rawFile[7] == 0x70)
            return FileCategory.Media;
        // Matroska / WebM: EBML header
        if (rawFile.Length >= 4 && rawFile[0] == 0x1A && rawFile[1] == 0x45 && rawFile[2] == 0xDF && rawFile[3] == 0xA3)
            return FileCategory.Media;
        // WMA / ASF
        if (rawFile.Length >= 4 && rawFile[0] == 0x30 && rawFile[1] == 0x26 && rawFile[2] == 0xB2 && rawFile[3] == 0x75)
            return FileCategory.Media;

        // No known metadata format (plain text, CSV, JSON, XML, etc.) — passthrough
        return FileCategory.Passthrough;
    }

    // ── Image ─────────────────────────────────────────────────────────────────

    private static FileMetadataResult StripImageMetadata(byte[] rawFile)
    {
        using var images = new MagickImageCollection(rawFile);

        // Extract metadata from the first frame; file-level profiles live there.
        var (extractedMetadata, removedEntryCount) = ExtractImageMetadata((MagickImage)images[0]);

        // Strip every frame — preserves animated GIFs and multi-frame TIFFs in full.
        foreach (var frame in images)
            frame.Strip(); // removes EXIF, IPTC, XMP, ICC profiles, and comments

        using var output = new MemoryStream();
        images.Write(output); // preserves original format and all frames automatically

        return new FileMetadataResult
        {
            CleanFile         = output.ToArray(),
            ExtractedMetadata = extractedMetadata,
            RemovedEntryCount = removedEntryCount,
            IsPassthrough     = false
        };
    }

    private static (string json, int count) ExtractImageMetadata(MagickImage image)
    {
        var root  = new JsonObject();
        var count = 0;

        var exifProfile = image.GetExifProfile();
        if (exifProfile != null)
        {
            var exifNode = new JsonObject();
            foreach (var v in exifProfile.Values)
            {
                exifNode[v.Tag.ToString()] = JsonValue.Create(v.GetValue()?.ToString());
                count++;
            }
            root["exif"] = exifNode;
        }

        var iptcProfile = image.GetIptcProfile();
        if (iptcProfile != null)
        {
            var iptcArray = new JsonArray();
            foreach (var v in iptcProfile.Values)
            {
                iptcArray.Add(new JsonObject
                {
                    ["tag"]   = JsonValue.Create(v.Tag.ToString()),
                    ["value"] = JsonValue.Create(v.Value)
                });
                count++;
            }
            root["iptc"] = iptcArray;
        }

        var xmpProfile = image.GetXmpProfile();
        if (xmpProfile != null)
        {
            root["xmp"] = "present";
            count++;
        }

        return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
    }

    // ── PDF ───────────────────────────────────────────────────────────────────

    private static FileMetadataResult StripPdfMetadata(byte[] rawFile)
    {
        using var input  = new MemoryStream(rawFile);
        using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);

        // Extract metadata from the source document before creating the clean copy.
        var (extractedMetadata, removedEntryCount) = ExtractPdfMetadata(source);

        // Create a fresh document and copy all pages.
        // A new document has neither /Info metadata nor a catalog /Metadata entry.
        using var dest   = new PdfDocument();
        using var output = new MemoryStream();

        for (int i = 0; i < source.PageCount; i++)
            dest.AddPage(source.Pages[i]);

        // Explicitly blank /Info fields so read-back returns string.Empty rather than null.
        dest.Info.Title    = string.Empty;
        dest.Info.Author   = string.Empty;
        dest.Info.Subject  = string.Empty;
        dest.Info.Keywords = string.Empty;
        dest.Info.Creator  = string.Empty;

        dest.Save(output);

        // PdfSharp 6.x's PdfCatalog.PrepareForSave() re-adds /Metadata to the
        // catalog during Save, even when removed from Elements beforehand.
        // Post-process the output bytes: replace every /Metadata indirect-reference
        // token with an equal-length whitespace run.  Because no bytes are added or
        // removed, all XRef byte offsets stay valid and the file remains well-formed.
        var cleanBytes = EraseCatalogXmpKey(output.ToArray());

        return new FileMetadataResult
        {
            CleanFile         = cleanBytes,
            ExtractedMetadata = extractedMetadata,
            RemovedEntryCount = removedEntryCount,
            IsPassthrough     = false
        };
    }

    /// <summary>
    /// Replaces every /Metadata indirect-reference token in a PDF file with an
    /// equal-length run of spaces.  This neutralises the catalog XMP entry that
    /// PdfSharp 6.x writes unconditionally during PrepareForSave, without altering
    /// any byte positions (so all XRef offsets remain valid).
    /// </summary>
    private static byte[] EraseCatalogXmpKey(byte[] pdfBytes)
    {
        // PDF uses Latin-1 (ISO 8859-1) for its syntactic structure; converting to
        // a Latin-1 string and back is a lossless round-trip for every byte value.
        var text = System.Text.Encoding.Latin1.GetString(pdfBytes);

        // In PdfSharp 6.x output the catalog /Metadata value is always an indirect
        // object reference:  /Metadata N M R  (e.g. /Metadata 6 0 R)
        // Replacing with the same number of ASCII spaces preserves byte positions.
        var patched = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"/Metadata\s+\d+\s+\d+\s+R",
            m => new string(' ', m.Length));

        // Safety net for the edge case where PdfSharp preserved a direct PdfString.
        patched = System.Text.RegularExpressions.Regex.Replace(
            patched,
            @"/Metadata\s*\([^)]*\)",
            m => new string(' ', m.Length));

        return System.Text.Encoding.Latin1.GetBytes(patched);
    }

    private static (string json, int count) ExtractPdfMetadata(PdfDocument document)
    {
        var root  = new JsonObject();
        var count = 0;
        var info  = document.Info;

        void Capture(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value)) { root[key] = JsonValue.Create(value); count++; }
        }

        Capture("title",    info.Title);
        Capture("author",   info.Author);
        Capture("subject",  info.Subject);
        Capture("keywords", info.Keywords);
        Capture("creator",  info.Creator);
        Capture("producer", info.Producer);

        if (document.Internals.Catalog.Elements.ContainsKey("/Metadata"))
        {
            root["xmp"] = "present";
            count++;
        }

        return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
    }

    // ── Office Open XML (DOCX / XLSX / PPTX) ─────────────────────────────────

    private static FileMetadataResult StripOpenXmlMetadata(byte[] rawFile)
    {
        var ms = new MemoryStream();
        ms.Write(rawFile, 0, rawFile.Length);
        ms.Position = 0;

        using var package = Package.Open(ms, FileMode.Open, FileAccess.ReadWrite);

        var (extractedMetadata, removedEntryCount) = ExtractOpenXmlMetadata(package.PackageProperties);

        var props = package.PackageProperties;
        props.Creator        = null;
        props.LastModifiedBy = null;
        props.Created        = null;
        props.Modified       = null;
        props.Title          = null;
        props.Subject        = null;
        props.Description    = null;
        props.Keywords       = null;
        props.Category       = null;
        props.ContentStatus  = null;
        props.Revision       = null;

        package.Close();

        return new FileMetadataResult
        {
            CleanFile         = ms.ToArray(),
            ExtractedMetadata = extractedMetadata,
            RemovedEntryCount = removedEntryCount,
            IsPassthrough     = false
        };
    }

    private static (string json, int count) ExtractOpenXmlMetadata(PackageProperties props)
    {
        var root  = new JsonObject();
        var count = 0;

        void Capture(string key, object? value)
        {
            var str = value?.ToString();
            if (!string.IsNullOrEmpty(str)) { root[key] = JsonValue.Create(str); count++; }
        }

        Capture("creator",        props.Creator);
        Capture("lastModifiedBy", props.LastModifiedBy);
        Capture("created",        props.Created);
        Capture("modified",       props.Modified);
        Capture("title",          props.Title);
        Capture("subject",        props.Subject);
        Capture("description",    props.Description);
        Capture("keywords",       props.Keywords);
        Capture("category",       props.Category);
        Capture("contentStatus",  props.ContentStatus);
        Capture("revision",       props.Revision);

        return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
    }

    // ── Audio / Video (TagLibSharp) ───────────────────────────────────────────

    /// <summary>Allows TagLibSharp to read/write from an in-memory stream.</summary>
    private sealed class MemoryStreamAbstraction : TagLib.File.IFileAbstraction
    {
        private readonly Stream _stream;
        internal MemoryStreamAbstraction(string name, Stream stream) { Name = name; _stream = stream; }
        public string Name { get; }
        public Stream ReadStream  => _stream;
        public Stream WriteStream => _stream;
        public void CloseStream(Stream stream) { /* lifecycle managed by caller */ }
    }

    private static FileMetadataResult StripMediaMetadata(byte[] rawFile)
    {
        var hint = GetMediaExtensionHint(rawFile);

        var ms = new MemoryStream();
        ms.Write(rawFile, 0, rawFile.Length);
        ms.Position = 0;

        try
        {
            using var file = TagLib.File.Create(new MemoryStreamAbstraction("file" + hint, ms));

            var (extractedMetadata, removedEntryCount) = ExtractMediaMetadata(file.Tag);

            file.RemoveTags(TagTypes.AllTags);
            file.Save();

            return new FileMetadataResult
            {
                CleanFile         = ms.ToArray(),
                ExtractedMetadata = extractedMetadata,
                RemovedEntryCount = removedEntryCount,
                IsPassthrough     = false
            };
        }
        catch (Exception ex) when (
            ex is TagLib.UnsupportedFormatException ||
            ex is TagLib.CorruptFileException       ||
            ex is ArgumentOutOfRangeException       ||
            ex is InvalidOperationException)
        {
            // File has the correct magic bytes but TagLibSharp could not fully parse it.
            // The original file is returned unchanged and the audit note explains why.
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the file could not be parsed by the audio/video engine. " +
                    $"Original file returned unchanged. Reason: {ex.GetType().Name}: {ex.Message}")
            };
            return new FileMetadataResult
            {
                CleanFile         = rawFile,
                ExtractedMetadata = note.ToJsonString(),
                RemovedEntryCount = 0,
                IsPassthrough     = false
            };
        }
    }

    private static string GetMediaExtensionHint(byte[] rawFile)
    {
        if (rawFile.Length >= 3 && rawFile[0] == 0x49 && rawFile[1] == 0x44 && rawFile[2] == 0x33)
            return ".mp3";  // MP3 ID3 header
        if (rawFile.Length >= 4 && rawFile[0] == 0x66 && rawFile[1] == 0x4C && rawFile[2] == 0x61 && rawFile[3] == 0x43)
            return ".flac"; // fLaC
        if (rawFile.Length >= 4 && rawFile[0] == 0x4F && rawFile[1] == 0x67 && rawFile[2] == 0x67 && rawFile[3] == 0x53)
            return ".ogg";  // OggS
        if (rawFile.Length >= 12 && rawFile[0] == 0x52 && rawFile[1] == 0x49 && rawFile[2] == 0x46 && rawFile[3] == 0x46)
        {
            if (rawFile[8] == 0x57 && rawFile[9] == 0x41 && rawFile[10] == 0x56 && rawFile[11] == 0x45)
                return ".wav"; // RIFF WAVE
            if (rawFile[8] == 0x41 && rawFile[9] == 0x56 && rawFile[10] == 0x49 && rawFile[11] == 0x20)
                return ".avi"; // RIFF AVI
        }
        if (rawFile.Length >= 8 && rawFile[4] == 0x66 && rawFile[5] == 0x74 && rawFile[6] == 0x79 && rawFile[7] == 0x70)
            return ".mp4"; // ISO Base Media (MP4/M4A/MOV)
        if (rawFile.Length >= 4 && rawFile[0] == 0x1A && rawFile[1] == 0x45 && rawFile[2] == 0xDF && rawFile[3] == 0xA3)
            return ".mkv"; // Matroska / WebM
        if (rawFile.Length >= 4 && rawFile[0] == 0x30 && rawFile[1] == 0x26 && rawFile[2] == 0xB2 && rawFile[3] == 0x75)
            return ".wma"; // WMA / ASF
        return ".mp3"; // fallback
    }

    private static (string json, int count) ExtractMediaMetadata(Tag tag)
    {
        var root  = new JsonObject();
        var count = 0;

        void Capture(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value)) { root[key] = JsonValue.Create(value); count++; }
        }

        void CaptureArray(string key, string[]? values)
        {
            if (values?.Length > 0)
            {
                var nonEmpty = values.Where(v => !string.IsNullOrEmpty(v)).ToArray();
                if (nonEmpty.Length > 0)
                {
                    root[key] = new JsonArray(nonEmpty.Select(v => JsonValue.Create(v)).ToArray<JsonNode?>());
                    count += nonEmpty.Length;
                }
            }
        }

        Capture("title",           tag.Title);
        CaptureArray("artists",    tag.Performers);
        Capture("album",           tag.Album);
        Capture("comment",         tag.Comment);
        CaptureArray("genres",     tag.Genres);
        Capture("copyright",       tag.Copyright);
        CaptureArray("composers",  tag.Composers);
        Capture("conductor",       tag.Conductor);

        return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
    }

    // ── Passthrough (plain text, CSV, JSON, XML, MD, etc.) ────────────────────

    private static FileMetadataResult Passthrough(byte[] rawFile) =>
        new FileMetadataResult
        {
            CleanFile         = rawFile,
            ExtractedMetadata = "[]",
            RemovedEntryCount = 0,
            IsPassthrough     = true
        };

}

