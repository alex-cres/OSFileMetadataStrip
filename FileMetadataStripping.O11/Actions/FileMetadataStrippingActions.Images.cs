using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Packaging;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using TagLib;
namespace OutSystems.NssFileMetadataStripping;

public partial class CssFileMetadataStripping
{
    // Image + SVG strip pipelines (Magick.NET, XML text-node cleaner, format detectors).

    /// <summary>
    /// Returns <see langword="true"/> when the ISO Base Media ftyp major brand identifies a
    /// HEIC, HEIF, or AVIF image. These formats share the ISOBMFF container with MP4/MOV
    /// and must be routed to the image path, not the audio/video path.
    /// </summary>
    private static bool IsHeifOrAvifBrand(byte[] rawFile)
    {
        if (rawFile.Length < 12) return false;
        // Major brand occupies bytes 8–11.
        // HEIC/HEIF: brands beginning with "he" (heic, heix, heim, heis, hevc, hevx, …)
        if (rawFile[8] == 0x68 && rawFile[9] == 0x65) return true;
        // HEIF base variants: mif1, msf1
        if (rawFile[8] == 0x6D &&
            (rawFile[9] == 0x69 || rawFile[9] == 0x73) &&
            rawFile[10] == 0x66 && rawFile[11] == 0x31)
            return true;
        // AVIF / AVIF image sequence: avif, avis
        if (rawFile[8] == 0x61 && rawFile[9] == 0x76 &&
            rawFile[10] == 0x69 && (rawFile[11] == 0x66 || rawFile[11] == 0x73))
            return true;
        return false;
    }

    private static RecFileMetadataResult StripImageMetadata(byte[] rawFile)
    {
        MagickImageCollection images;
        try
        {
            MagickReadSettings readSettings = null;
            if (IsTgaFile(rawFile))
                readSettings = new MagickReadSettings { Format = MagickFormat.Tga };
            else if (IsApngFile(rawFile))
                readSettings = new MagickReadSettings { Format = MagickFormat.APng };
            images = readSettings != null
                ? new MagickImageCollection(rawFile, readSettings)
                : new MagickImageCollection(rawFile);
        }
        catch (Exception ex) when (ex is MagickException or OutOfMemoryException or OverflowException
                                        or InvalidOperationException or ArgumentException)
        {
            // Catch broad exception types — some codecs on certain runtimes throw non-MagickException
            // failures on malformed or platform-incompatible input.
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the image could not be parsed. " +
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

        if (images.Count == 0)
        {
            images.Dispose();
            var emptyNote = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the image decoded to an empty frame collection. " +
                    "Original file returned unchanged.")
            };
            return new RecFileMetadataResult
            {
                ssCleanFile         = rawFile,
                ssExtractedMetadata = emptyNote.ToJsonString(),
                ssRemovedEntryCount = 0,
                ssIsPassthrough     = false
            };
        }

        using (images)
        {
            // Extract metadata from the first frame; file-level profiles live there.
            var (extractedMetadata, removedEntryCount) = ExtractImageMetadata((MagickImage)images[0]);

            // Strip every frame — preserves animated GIFs and multi-frame TIFFs in full.
            // Format-specific per-image attributes (dpx:*, cin:*) survive the ImageMagick
            // Strip() call, so remove them explicitly here.
            foreach (var frame in images)
            {
                frame.Strip(); // removes EXIF, IPTC, XMP, ICC profiles, and comments
                RemoveNamespacedAttributes(frame);
            }

            using var output = new MemoryStream();
            try
            {
                images.Write(output); // preserves original format and all frames automatically
            }
            catch (MagickMissingDelegateErrorException)
            {
                // The format can be decoded but has no write delegate (e.g. HEIC on both Windows
                // and ODC Linux). The HEVC encoder (x265) is GPL-licensed and cannot be bundled
                // in a redistributable NuGet package, so no available libheif build includes it.
                // Metadata was fully stripped in memory — transcode to JPEG so the caller receives
                // a clean, usable file. The original format (e.g. HEIC) is not preserved.
                using var jpegOutput = new MemoryStream();
                images.Write(jpegOutput, MagickFormat.Jpeg);

                var metaNode = extractedMetadata == "[]"
                    ? new JsonObject()
                    : JsonNode.Parse(extractedMetadata)!.AsObject();
                metaNode["transcodedFormat"] = JsonValue.Create(
                    "jpeg — the original image format (e.g. HEIC) requires an HEVC encode delegate " +
                    "that is absent on all platforms because the x265 codec is GPL-licensed and " +
                    "cannot be bundled in a redistributable library. Metadata was fully stripped " +
                    "and the clean image was transcoded to JPEG. The original format is not preserved.");

                return new RecFileMetadataResult
                {
                    ssCleanFile         = jpegOutput.ToArray(),
                    ssExtractedMetadata = metaNode.ToJsonString(),
                    ssRemovedEntryCount = removedEntryCount,
                    ssIsPassthrough     = false
                };
            }

            var cleanBytes = output.ToArray();
            // Radiance HDR output starts with '#?' (magic marker '#?RADIANCE' or '#?RGBE').
            // Only match the two-byte '#?' prefix so XBM files (which start with '#define')
            // are never passed through the HDR comment stripper.
            if (cleanBytes.Length >= 2 && cleanBytes[0] == 0x23 && cleanBytes[1] == 0x3F) // '#?'
                cleanBytes = StripHdrCommentLines(cleanBytes);

            return new RecFileMetadataResult
            {
                ssCleanFile          = cleanBytes,
                ssExtractedMetadata  = extractedMetadata,
                ssRemovedEntryCount  = removedEntryCount,
                ssIsPassthrough      = false
            };
        }
    }

    /// <summary>Removes encoder-injected comment lines from a Radiance HDR byte stream.
    /// Strips every '#' line that is not a '#?' magic marker. The resolution string
    /// and binary pixel data are preserved verbatim.</summary>
    private static byte[] StripHdrCommentLines(byte[] hdrBytes)
    {
        using var outMs = new MemoryStream(hdrBytes.Length);
        int i = 0;
        while (i < hdrBytes.Length)
        {
            int lineStart = i;
            while (i < hdrBytes.Length && hdrBytes[i] != 0x0A) i++;
            int lineEnd = i;
            if (i < hdrBytes.Length) i++;
            if (lineEnd > lineStart && (hdrBytes[lineStart] == 0x2D || hdrBytes[lineStart] == 0x2B))
            {
                outMs.Write(hdrBytes, lineStart, i - lineStart);
                if (i < hdrBytes.Length) outMs.Write(hdrBytes, i, hdrBytes.Length - i);
                break;
            }
            bool isMagicMarker = lineEnd - lineStart >= 2 && hdrBytes[lineStart] == 0x23 && hdrBytes[lineStart + 1] == 0x3F;
            if (lineEnd > lineStart && hdrBytes[lineStart] == 0x23 && !isMagicMarker) continue;
            outMs.Write(hdrBytes, lineStart, i - lineStart);
        }
        return outMs.ToArray();
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

        // Format-specific per-image attributes that survive Strip():
        //   dpx:*  — DPX (SMPTE 268M) production metadata (file.filename, film.id, …)
        //   cin:*  — CIN (Kodak Cineon) production metadata (film.type, origination.device, …)
        foreach (var prefix in new[] { "dpx:", "cin:" })
        {
            var attrs = image.AttributeNames
                .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (attrs.Count == 0) continue;
            var node = new JsonObject();
            foreach (var attrName in attrs)
            {
                var key = attrName.Substring(prefix.Length);
                node[key] = JsonValue.Create(image.GetAttribute(attrName));
                count++;
            }
            root[prefix.TrimEnd(':')] = node;
        }

        var comment = image.Comment;
        if (!string.IsNullOrEmpty(comment))
        {
            var filteredLines = comment.Split('\n')
                .Where(line => line.IndexOf("Created by ImageMagick", StringComparison.OrdinalIgnoreCase) < 0)
                // Filter the Radiance HDR magic-marker string: Magick.NET exposes '#?RADIANCE'
                // (or '#?RGBE') as image.Comment with the leading '#' stripped, yielding
                // "?RADIANCE" / "?RGBE". This is a format artifact, not user metadata.
                .Where(line => !line.TrimStart().StartsWith("?RADIANCE", StringComparison.OrdinalIgnoreCase))
                .Where(line => !line.TrimStart().StartsWith("?RGBE", StringComparison.OrdinalIgnoreCase))
                .Where(line => !string.IsNullOrWhiteSpace(line));
            var filteredComment = string.Join("\n", filteredLines).Trim();
            if (!string.IsNullOrEmpty(filteredComment))
            {
                root["comment"] = filteredComment;
                count++;
            }
        }

        return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
    }

    private static bool IsTgaFile(byte[] rawFile)
    {
        if (rawFile.Length < 18) return false;
        var footer = System.Text.Encoding.ASCII.GetString(rawFile, rawFile.Length - 18, 17);
        if (footer == "TRUEVISION-XFILE.") return true;
        byte cmt = rawFile[1]; byte imt = rawFile[2]; byte depth = rawFile[16];
        int w = rawFile[12] | (rawFile[13] << 8);
        int h = rawFile[14] | (rawFile[15] << 8);
        return (cmt == 0 || cmt == 1)
            && (imt == 1 || imt == 2 || imt == 3 || imt == 9 || imt == 10 || imt == 11)
            && (depth == 8 || depth == 15 || depth == 16 || depth == 24 || depth == 32)
            && w > 0 && h > 0;
    }

    private static bool IsApngFile(byte[] rawFile)
    {
        if (rawFile.Length < 12) return false;
        if (rawFile[0] != 0x89 || rawFile[1] != 0x50 || rawFile[2] != 0x4E || rawFile[3] != 0x47)
            return false;
        for (int i = 8; i < rawFile.Length - 3; i++)
        {
            if (rawFile[i] == 0x61 && rawFile[i + 1] == 0x63 &&
                rawFile[i + 2] == 0x54 && rawFile[i + 3] == 0x4C)
                return true;
        }
        return false;
    }

    /// <summary>Removes attributes with dpx:/cin: namespace prefixes that survive Strip().</summary>
    private static void RemoveNamespacedAttributes(IMagickImage<byte> frame)
    {
        var toRemove = frame.AttributeNames
            .Where(n => n.StartsWith("dpx:", StringComparison.OrdinalIgnoreCase)
                     || n.StartsWith("cin:", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var attr in toRemove)
            frame.RemoveAttribute(attr);
    }

    /// <summary>Strips XML text nodes (&lt;title&gt;, &lt;desc&gt;, &lt;metadata&gt;) from an SVG document.</summary>
    private static RecFileMetadataResult StripSvgMetadata(byte[] rawFile)
    {
        XDocument xdoc;
        try
        {
            using var input = new MemoryStream(rawFile, writable: false);
            xdoc = XDocument.Load(input);
        }
        catch (Exception ex) when (
            ex is System.Xml.XmlException ||
            ex is InvalidDataException    ||
            ex is NotSupportedException)
        {
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the SVG could not be parsed as XML. " +
                    "Original file returned unchanged. Reason: " + ex.GetType().Name + ": " + ex.Message)
            };
            return new RecFileMetadataResult
            {
                ssCleanFile         = rawFile,
                ssExtractedMetadata = note.ToJsonString(),
                ssRemovedEntryCount = 0,
                ssIsPassthrough     = false
            };
        }

        var root  = new JsonObject();
        int count = 0;

        foreach (var localName in new[] { "title", "desc", "metadata" })
        {
            var elements = xdoc.Descendants()
                .Where(e => string.Equals(e.Name.LocalName, localName,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (elements.Count == 0) continue;

            var arr = new JsonArray();
            foreach (var el in elements)
            {
                var value = el.Value?.Trim();
                if (!string.IsNullOrEmpty(value))
                    arr.Add(JsonValue.Create(value));
                el.Remove();
                count++;
            }
            if (arr.Count > 0) root[localName] = arr;
        }

        using var output = new MemoryStream();
        xdoc.Save(output);
        return new RecFileMetadataResult
        {
            ssCleanFile         = output.ToArray(),
            ssExtractedMetadata = count > 0 ? root.ToJsonString() : "[]",
            ssRemovedEntryCount = count,
            ssIsPassthrough     = false
        };
    }

    /// <summary>Detects SVG by byte inspection (start with '&lt;' and contain '&lt;svg' within the first 4 KB).</summary>
    private static bool IsSvgFile(byte[] rawFile)
    {
        if (rawFile.Length < 4) return false;
        int i = 0;
        if (rawFile.Length >= 3 && rawFile[0] == 0xEF && rawFile[1] == 0xBB && rawFile[2] == 0xBF)
            i = 3;
        while (i < rawFile.Length && (rawFile[i] == 0x20 || rawFile[i] == 0x09
                                   || rawFile[i] == 0x0A || rawFile[i] == 0x0D)) i++;
        if (i >= rawFile.Length || rawFile[i] != 0x3C) return false;
        int scanLimit = Math.Min(rawFile.Length, 4096);
        for (int j = i; j < scanLimit - 3; j++)
        {
            if (rawFile[j] == 0x3C
                && (rawFile[j + 1] == 0x73 || rawFile[j + 1] == 0x53)
                && (rawFile[j + 2] == 0x76 || rawFile[j + 2] == 0x56)
                && (rawFile[j + 3] == 0x67 || rawFile[j + 3] == 0x47))
                return true;
        }
        return false;
    }

    /// <summary>Detects DIB (BMP without the 14-byte BITMAPFILEHEADER) by validating the
    /// BITMAPINFOHEADER: header size at bytes 0–3 is one of {40, 52, 56, 108, 124}, planes=1
    /// at bytes 12–13, and bit-count at bytes 14–15 is a valid depth (1/4/8/16/24/32).</summary>
    private static bool IsDibFile(byte[] rawFile)
    {
        if (rawFile.Length < 40) return false;
        uint headerSize = (uint)(rawFile[0]
                              | (rawFile[1] << 8)
                              | (rawFile[2] << 16)
                              | (rawFile[3] << 24));
        if (headerSize != 40  && headerSize != 52  && headerSize != 56
         && headerSize != 108 && headerSize != 124)
            return false;
        if (rawFile[12] != 0x01 || rawFile[13] != 0x00) return false;
        ushort bitCount = (ushort)(rawFile[14] | (rawFile[15] << 8));
        return bitCount == 1  || bitCount == 4  || bitCount == 8
            || bitCount == 16 || bitCount == 24 || bitCount == 32;
    }

}
