using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO.Packaging;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using TagLib;

namespace FileMetadataStripping;

public partial class FileMetadataStripping : IFileMetadataStripping
{
    private enum FileCategory { Image, Svg, Pdf, OpenXml, LegacyOffice, Odf, Epub, Ora, Media, Passthrough }

    public FileMetadataResult StripFileMetadata(byte[] rawFile, bool stripBodyAuthors)
    {
        return DetectCategory(rawFile) switch
        {
            FileCategory.Image        => StripImageMetadata(rawFile),
            FileCategory.Svg          => StripSvgMetadata(rawFile),
            FileCategory.Pdf          => StripPdfMetadata(rawFile),
            FileCategory.OpenXml      => StripOpenXmlMetadata(rawFile, stripBodyAuthors),
            FileCategory.LegacyOffice => StripCfbfMetadata(rawFile),
            FileCategory.Odf          => StripOdfMetadata(rawFile),
            FileCategory.Epub         => StripEpubMetadata(rawFile),
            FileCategory.Ora          => StripOraMetadata(rawFile),
            FileCategory.Media        => StripMediaMetadata(rawFile),
            FileCategory.Passthrough  => Passthrough(rawFile),
            _                         => Passthrough(rawFile)
        };
    }

    private static FileCategory DetectCategory(byte[] rawFile)
    {
        // PDF: %PDF magic bytes (0x25 0x50 0x44 0x46) — check before image (PDF can have JPEG previews)
        if (rawFile.Length >= 4
            && rawFile[0] == 0x25 && rawFile[1] == 0x50
            && rawFile[2] == 0x44 && rawFile[3] == 0x46)
            return FileCategory.Pdf;

        // Legacy binary Office (CFBF / OLE Compound Document): D0 CF 11 E0 A1 B1 1A E1.
        // Covers Word 97–2003 (.doc / .dot), Excel 97–2003 (.xls / .xlt), PowerPoint 97–2003
        // (.ppt / .pot / .pps). Detected before the ZIP check because CFBF has a distinct
        // 8-byte magic — no risk of clashing with OOXML / ODF / EPUB / ORA.
        if (rawFile.Length >= 8
            && rawFile[0] == 0xD0 && rawFile[1] == 0xCF
            && rawFile[2] == 0x11 && rawFile[3] == 0xE0
            && rawFile[4] == 0xA1 && rawFile[5] == 0xB1
            && rawFile[6] == 0x1A && rawFile[7] == 0xE1)
            return FileCategory.LegacyOffice;

        // Office Open XML (DOCX/XLSX/PPTX) or ODF (ODT/ODS/ODP) or EPUB or ORA: ZIP PK signature (0x50 0x4B 0x03 0x04)
        if (rawFile.Length >= 4
            && rawFile[0] == 0x50 && rawFile[1] == 0x4B
            && rawFile[2] == 0x03 && rawFile[3] == 0x04)
            return DetectZipCategory(rawFile);

        // BMP: no metadata containers — passthrough (magic bytes "BM")
        if (rawFile.Length >= 2 && rawFile[0] == 0x42 && rawFile[1] == 0x4D)
            return FileCategory.Passthrough;

        // DIB (Windows Device Independent Bitmap): BMP without the 14-byte
        // BITMAPFILEHEADER — no metadata containers, so return the bytes verbatim
        // rather than re-encoding through Magick.NET for no security benefit.
        if (IsDibFile(rawFile))
            return FileCategory.Passthrough;

        // SVG: XML-based vector image. Detect before Magick.NET so we can strip XML text
        // nodes (<title>, <desc>, <metadata>) that survive raster-oriented Strip() calls.
        if (IsSvgFile(rawFile))
            return FileCategory.Svg;

        // Images: JPEG, PNG, GIF, TIFF, WebP, TGA, and 100+ more — detected by Magick.NET
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
        // ISO Base Media File Format: "ftyp" at bytes 4–7.
        // HEIC, HEIF, and AVIF share this container with MP4/MOV — distinguish by major brand (bytes 8–11).
        if (rawFile.Length >= 8 && rawFile[4] == 0x66 && rawFile[5] == 0x74 && rawFile[6] == 0x79 && rawFile[7] == 0x70)
            return IsHeifOrAvifBrand(rawFile) ? FileCategory.Image : FileCategory.Media;
        // Matroska / WebM: EBML header
        if (rawFile.Length >= 4 && rawFile[0] == 0x1A && rawFile[1] == 0x45 && rawFile[2] == 0xDF && rawFile[3] == 0xA3)
            return FileCategory.Media;
        // WMA / ASF
        if (rawFile.Length >= 4 && rawFile[0] == 0x30 && rawFile[1] == 0x26 && rawFile[2] == 0xB2 && rawFile[3] == 0x75)
            return FileCategory.Media;

        // ── Image format fallbacks: formats whose magic bytes are not reliably detected
        //    by MagickImageInfo on all platform/build combinations. ────────────────────

        // TGA (Truevision Targa) v2 footer: last 18 bytes = "TRUEVISION-XFILE." + NUL.
        // TGA has no start-of-file magic; the v2 footer written by Magick.NET is reliable.
        if (rawFile.Length >= 18)
        {
            var tgaFooter = System.Text.Encoding.ASCII.GetString(rawFile, rawFile.Length - 18, 17);
            if (tgaFooter == "TRUEVISION-XFILE.")
                return FileCategory.Image;
        }
        // TGA v1 header heuristic (no footer in older/simpler files): validate fields that
        // are common to all valid TGA images — color-map type, image type, pixel depth,
        // and non-zero dimensions. Only minimal risk of false positives for binary files.
        if (rawFile.Length >= 18)
        {
            byte cmt   = rawFile[1];  // color-map type: 0 = none, 1 = present
            byte imt   = rawFile[2];  // image type: 1–3 uncompressed; 9–11 RLE
            byte depth = rawFile[16]; // pixel depth: 8, 15, 16, 24, 32
            int width  = rawFile[12] | (rawFile[13] << 8);
            int height = rawFile[14] | (rawFile[15] << 8);
            if ((cmt == 0 || cmt == 1)
                && (imt == 1 || imt == 2 || imt == 3 || imt == 9 || imt == 10 || imt == 11)
                && (depth == 8 || depth == 15 || depth == 16 || depth == 24 || depth == 32)
                && width > 0 && height > 0)
                return FileCategory.Image;
        }

        // ICO (Microsoft Icon): 0x00 0x00 0x01 0x00
        if (rawFile.Length >= 4
            && rawFile[0] == 0x00 && rawFile[1] == 0x00
            && rawFile[2] == 0x01 && rawFile[3] == 0x00)
            return FileCategory.Image;

        // XCF (GIMP): "gimp xcf " (9 ASCII bytes)
        if (rawFile.Length >= 9
            && rawFile[0] == 0x67 && rawFile[1] == 0x69 && rawFile[2] == 0x6D // g i m
            && rawFile[3] == 0x70 && rawFile[4] == 0x20 && rawFile[5] == 0x78 // p   x
            && rawFile[6] == 0x63 && rawFile[7] == 0x66 && rawFile[8] == 0x20) // c f  
            return FileCategory.Image;

        // DCM (DICOM): 128-byte preamble followed by "DICM" at offset 128.
        if (rawFile.Length >= 132
            && rawFile[128] == 0x44 && rawFile[129] == 0x49   // D I
            && rawFile[130] == 0x43 && rawFile[131] == 0x4D)  // C M
            return FileCategory.Image;

        // No known metadata format (plain text, CSV, JSON, XML, etc.) — passthrough
        return FileCategory.Passthrough;
    }

    /// <summary>
    /// Routes a ZIP-based file to its correct strip path based on the <c>mimetype</c>
    /// entry. ODF, EPUB, and ORA all use ZIP with an uncompressed <c>mimetype</c> entry
    /// identifying the format; anything without a recognised mimetype is treated as
    /// Office Open XML (DOCX/XLSX/PPTX/…).
    /// </summary>
    private static FileCategory DetectZipCategory(byte[] rawFile)
    {
        var mime = ReadZipMimetype(rawFile);
        if (mime != null)
        {
            if (mime.StartsWith("application/vnd.oasis.opendocument.",
                    StringComparison.OrdinalIgnoreCase))
                return FileCategory.Odf;
            if (mime.Equals("application/epub+zip", StringComparison.OrdinalIgnoreCase))
                return FileCategory.Epub;
            if (mime.Equals("image/openraster", StringComparison.OrdinalIgnoreCase))
                return FileCategory.Ora;
        }
        return FileCategory.OpenXml;
    }

    /// <summary>
    /// Reads the plain-text <c>mimetype</c> entry from a ZIP archive, if present.
    /// Returns <see langword="null"/> when the entry is missing or unreadable.
    /// </summary>
    private static string? ReadZipMimetype(byte[] rawFile)
    {
        try
        {
            using var ms  = new MemoryStream(rawFile, writable: false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
            var entry = zip.GetEntry("mimetype");
            if (entry == null) return null;
            using var stream = entry.Open();
            using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false, bufferSize: 64, leaveOpen: false);
            return reader.ReadToEnd().Trim();
        }
        catch { return null; }
    }

    private static FileMetadataResult Passthrough(byte[] rawFile) =>
        new FileMetadataResult
        {
            CleanFile         = rawFile,
            ExtractedMetadata = "[]",
            RemovedEntryCount = 0,
            IsPassthrough     = true
        };

}
