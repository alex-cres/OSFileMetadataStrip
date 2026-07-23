using System;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text.Json.Nodes;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using TagLib;

namespace OutSystems.NssFileMetadataStripping;

public class CssFileMetadataStripping : IssFileMetadataStripping
{
    private enum FileCategory { Image, Pdf, OpenXml, Media, Passthrough }

    public void MssStripFileMetadata(byte[] ssRawFile, out RecFileMetadataResult ssStripFileMetadata)
    {
        ssStripFileMetadata = DetectCategory(ssRawFile) switch
        {
            FileCategory.Image       => StripImageMetadata(ssRawFile),
            FileCategory.Pdf         => StripPdfMetadata(ssRawFile),
            FileCategory.OpenXml     => StripOpenXmlMetadata(ssRawFile),
            FileCategory.Media       => StripMediaMetadata(ssRawFile),
            FileCategory.Passthrough => Passthrough(ssRawFile),
            _                        => Passthrough(ssRawFile)
        };
    }

    // ── Detection ─────────────────────────────────────────────────────────────

    private static FileCategory DetectCategory(byte[] rawFile)
    {
        // PDF: %PDF magic bytes
        if (rawFile.Length >= 4
            && rawFile[0] == 0x25 && rawFile[1] == 0x50
            && rawFile[2] == 0x44 && rawFile[3] == 0x46)
            return FileCategory.Pdf;

        // Office Open XML (DOCX/XLSX/PPTX): ZIP PK signature
        if (rawFile.Length >= 4
            && rawFile[0] == 0x50 && rawFile[1] == 0x4B
            && rawFile[2] == 0x03 && rawFile[3] == 0x04)
            return FileCategory.OpenXml;

        // Images: detected by Magick.NET (JPEG, PNG, GIF, BMP, TIFF, WebP, TGA, 100+ more)
        try
        {
            var info = new MagickImageInfo(rawFile);
            if (info.Format != MagickFormat.Unknown)
                return FileCategory.Image;
        }
        catch (MagickException) { }

        // Audio/video — detected by magic bytes
        if (rawFile.Length >= 3 && rawFile[0] == 0x49 && rawFile[1] == 0x44 && rawFile[2] == 0x33)
            return FileCategory.Media; // MP3 ID3
        if (rawFile.Length >= 4 && rawFile[0] == 0x66 && rawFile[1] == 0x4C && rawFile[2] == 0x61 && rawFile[3] == 0x43)
            return FileCategory.Media; // FLAC
        if (rawFile.Length >= 4 && rawFile[0] == 0x4F && rawFile[1] == 0x67 && rawFile[2] == 0x67 && rawFile[3] == 0x53)
            return FileCategory.Media; // OGG
        if (rawFile.Length >= 12 && rawFile[0] == 0x52 && rawFile[1] == 0x49 && rawFile[2] == 0x46 && rawFile[3] == 0x46
            && ((rawFile[8] == 0x57 && rawFile[9] == 0x41 && rawFile[10] == 0x56 && rawFile[11] == 0x45)
             || (rawFile[8] == 0x41 && rawFile[9] == 0x56 && rawFile[10] == 0x49 && rawFile[11] == 0x20)))
            return FileCategory.Media; // WAV / AVI
        if (rawFile.Length >= 8 && rawFile[4] == 0x66 && rawFile[5] == 0x74 && rawFile[6] == 0x79 && rawFile[7] == 0x70)
            return FileCategory.Media; // MP4 / MOV
        if (rawFile.Length >= 4 && rawFile[0] == 0x1A && rawFile[1] == 0x45 && rawFile[2] == 0xDF && rawFile[3] == 0xA3)
            return FileCategory.Media; // MKV / WebM
        if (rawFile.Length >= 4 && rawFile[0] == 0x30 && rawFile[1] == 0x26 && rawFile[2] == 0xB2 && rawFile[3] == 0x75)
            return FileCategory.Media; // WMA / ASF

        return FileCategory.Passthrough;
    }

    // ── Image ─────────────────────────────────────────────────────────────────

    private static RecFileMetadataResult StripImageMetadata(byte[] rawFile)
    {
        using var images = new MagickImageCollection(rawFile);

        // Extract metadata from the first frame; file-level profiles live there.
        var (extractedMetadata, removedEntryCount) = ExtractImageMetadata((MagickImage)images[0]);

        // Strip every frame — preserves animated GIFs and multi-frame TIFFs in full.
        foreach (var frame in images)
            frame.Strip(); // removes EXIF, IPTC, XMP, ICC profiles, and comments

        using var output = new MemoryStream();
        images.Write(output); // preserves original format and all frames automatically

        return new RecFileMetadataResult
        {
            ssCleanFile          = output.ToArray(),
            ssExtractedMetadata  = extractedMetadata,
            ssRemovedEntryCount  = removedEntryCount,
            ssIsPassthrough      = false
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

    private static RecFileMetadataResult StripPdfMetadata(byte[] rawFile)
    {
        using var input = new MemoryStream(rawFile);
        PdfDocument document;
        try
        {
            document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        }
        catch (Exception ex) when (
            ex is PdfReaderException        ||
            ex is InvalidOperationException ||
            ex is NotSupportedException)
        {
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the PDF could not be opened (it may be encrypted or password-protected). " +
                    $"Original file returned unchanged. Reason: {ex.GetType().Name}: {ex.Message}")
            };
            return new RecFileMetadataResult
            {
                ssCleanFile         = rawFile,
                ssExtractedMetadata = note.ToJsonString(),
                ssRemovedEntryCount = 0,
                ssIsPassthrough     = false
            };
        }

        using (document)
        {
            var (extractedMetadata, removedEntryCount) = ExtractPdfMetadata(document);

            document.Info.Title    = string.Empty;
            document.Info.Author   = string.Empty;
            document.Info.Subject  = string.Empty;
            document.Info.Keywords = string.Empty;
            document.Info.Creator  = string.Empty;

            // Strip catalog /Metadata XMP stream
            var catalog = document.Internals.Catalog;
            if (catalog.Elements.ContainsKey("/Metadata"))
                catalog.Elements.Remove("/Metadata");

            using var output = new MemoryStream();
            document.Save(output);

            return new RecFileMetadataResult
            {
                ssCleanFile         = output.ToArray(),
                ssExtractedMetadata = extractedMetadata,
                ssRemovedEntryCount = removedEntryCount,
                ssIsPassthrough     = false
            };
        }
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
        Capture("keywords",  info.Keywords);
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

    private static RecFileMetadataResult StripOpenXmlMetadata(byte[] rawFile)
    {
        try
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

            return new RecFileMetadataResult
            {
                ssCleanFile         = ms.ToArray(),
                ssExtractedMetadata = extractedMetadata,
                ssRemovedEntryCount = removedEntryCount,
                ssIsPassthrough     = false
            };
        }
        catch (Exception ex) when (
            ex is FileFormatException  ||
            ex is InvalidDataException ||
            ex is NotSupportedException)
        {
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the OOXML file could not be opened (it may be encrypted or password-protected). " +
                    $"Original file returned unchanged. Reason: {ex.GetType().Name}: {ex.Message}")
            };
            return new RecFileMetadataResult
            {
                ssCleanFile         = rawFile,
                ssExtractedMetadata = note.ToJsonString(),
                ssRemovedEntryCount = 0,
                ssIsPassthrough     = false
            };
        }
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

    private sealed class MemoryStreamAbstraction : TagLib.File.IFileAbstraction
    {
        private readonly Stream _stream;
        internal MemoryStreamAbstraction(string name, Stream stream) { Name = name; _stream = stream; }
        public string Name { get; }
        public Stream ReadStream  => _stream;
        public Stream WriteStream => _stream;
        public void CloseStream(Stream stream) { }
    }

    private static RecFileMetadataResult StripMediaMetadata(byte[] rawFile)
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

            return new RecFileMetadataResult
            {
                ssCleanFile         = ms.ToArray(),
                ssExtractedMetadata = extractedMetadata,
                ssRemovedEntryCount = removedEntryCount,
                ssIsPassthrough     = false
            };
        }
        catch (Exception ex) when (
            ex is TagLib.UnsupportedFormatException ||
            ex is TagLib.CorruptFileException       ||
            ex is ArgumentOutOfRangeException       ||
            ex is InvalidOperationException)
        {
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the file could not be parsed by the audio/video engine. " +
                    $"Original file returned unchanged. Reason: {ex.GetType().Name}: {ex.Message}")
            };
            return new RecFileMetadataResult
            {
                ssCleanFile         = rawFile,
                ssExtractedMetadata = note.ToJsonString(),
                ssRemovedEntryCount = 0,
                ssIsPassthrough     = false
            };
        }
    }

    private static string GetMediaExtensionHint(byte[] rawFile)
    {
        if (rawFile.Length >= 3 && rawFile[0] == 0x49 && rawFile[1] == 0x44 && rawFile[2] == 0x33) return ".mp3";
        if (rawFile.Length >= 4 && rawFile[0] == 0x66 && rawFile[1] == 0x4C && rawFile[2] == 0x61 && rawFile[3] == 0x43) return ".flac";
        if (rawFile.Length >= 4 && rawFile[0] == 0x4F && rawFile[1] == 0x67 && rawFile[2] == 0x67 && rawFile[3] == 0x53) return ".ogg";
        if (rawFile.Length >= 12 && rawFile[0] == 0x52 && rawFile[1] == 0x49 && rawFile[2] == 0x46 && rawFile[3] == 0x46)
        {
            if (rawFile[8] == 0x57 && rawFile[9] == 0x41 && rawFile[10] == 0x56 && rawFile[11] == 0x45) return ".wav";
            if (rawFile[8] == 0x41 && rawFile[9] == 0x56 && rawFile[10] == 0x49 && rawFile[11] == 0x20) return ".avi";
        }
        if (rawFile.Length >= 8 && rawFile[4] == 0x66 && rawFile[5] == 0x74 && rawFile[6] == 0x79 && rawFile[7] == 0x70) return ".mp4";
        if (rawFile.Length >= 4 && rawFile[0] == 0x1A && rawFile[1] == 0x45 && rawFile[2] == 0xDF && rawFile[3] == 0xA3) return ".mkv";
        if (rawFile.Length >= 4 && rawFile[0] == 0x30 && rawFile[1] == 0x26 && rawFile[2] == 0xB2 && rawFile[3] == 0x75) return ".wma";
        return ".mp3";
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

        Capture("title",          tag.Title);
        CaptureArray("artists",   tag.Performers);
        Capture("album",          tag.Album);
        Capture("comment",        tag.Comment);
        CaptureArray("genres",    tag.Genres);
        Capture("copyright",      tag.Copyright);
        CaptureArray("composers", tag.Composers);
        Capture("conductor",      tag.Conductor);

        return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
    }

    // ── Passthrough ───────────────────────────────────────────────────────────

    private static RecFileMetadataResult Passthrough(byte[] rawFile) =>
        new RecFileMetadataResult
        {
            ssCleanFile         = rawFile,
            ssExtractedMetadata = "[]",
            ssRemovedEntryCount = 0,
            ssIsPassthrough     = true
        };
}
