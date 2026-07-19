using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.IO.Packaging;
using System.Text.Json.Nodes;

namespace FileMetadataStripping;

public class FileMetadataStripping : IFileMetadataStripping
{
    private enum FileCategory { Image, Pdf, OpenXml, Passthrough }

    public FileMetadataResult StripFileMetadata(byte[] rawFile)
    {
        return DetectCategory(rawFile) switch
        {
            FileCategory.Image       => StripImageMetadata(rawFile),
            FileCategory.Pdf         => StripPdfMetadata(rawFile),
            FileCategory.OpenXml     => StripOpenXmlMetadata(rawFile),
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

        // Images: JPEG, PNG, GIF, BMP, TIFF, WebP, TGA, and 200+ more — detected by Magick.NET
        try
        {
            var info = new MagickImageInfo(rawFile);
            if (info.Format != MagickFormat.Unknown)
                return FileCategory.Image;
        }
        catch (MagickException) { }

        // No known metadata format (plain text, CSV, JSON, XML, etc.) — passthrough
        return FileCategory.Passthrough;
    }

    // ── Image ─────────────────────────────────────────────────────────────────

    private static FileMetadataResult StripImageMetadata(byte[] rawFile)
    {
        using var image = new MagickImage(rawFile);

        var (extractedMetadata, removedEntryCount) = ExtractImageMetadata(image);

        image.Strip(); // removes EXIF, IPTC, XMP, ICC profiles, and comments in one call

        using var output = new MemoryStream();
        image.Write(output); // preserves original format automatically

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
        using var input    = new MemoryStream(rawFile);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        var (extractedMetadata, removedEntryCount) = ExtractPdfMetadata(document.Info);

        document.Info.Title    = string.Empty;
        document.Info.Author   = string.Empty;
        document.Info.Subject  = string.Empty;
        document.Info.Keywords = string.Empty;
        document.Info.Creator  = string.Empty;

        using var output = new MemoryStream();
        document.Save(output);

        return new FileMetadataResult
        {
            CleanFile         = output.ToArray(),
            ExtractedMetadata = extractedMetadata,
            RemovedEntryCount = removedEntryCount,
            IsPassthrough     = false
        };
    }

    private static (string json, int count) ExtractPdfMetadata(PdfDocumentInformation info)
    {
        var root  = new JsonObject();
        var count = 0;

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

