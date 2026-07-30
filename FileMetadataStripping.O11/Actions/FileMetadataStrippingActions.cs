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

public partial class CssFileMetadataStripping : IssFileMetadataStripping
{
    private enum FileCategory { Image, Svg, Pdf, Rtf, OpenXml, WordMl, LegacyOffice, Odf, FlatOdf, Epub, Ora, Media, Passthrough }

    public void MssStripFileMetadata(byte[] ssRawFile, bool ssStripBodyAuthors, out RecFileMetadataResult ssStripFileMetadata)
    {
        ssStripFileMetadata = DetectCategory(ssRawFile) switch
        {
            FileCategory.Image        => StripImageMetadata(ssRawFile),
            FileCategory.Svg          => StripSvgMetadata(ssRawFile),
            FileCategory.Pdf          => StripPdfMetadata(ssRawFile),
            FileCategory.Rtf          => StripRtfMetadata(ssRawFile),
            FileCategory.OpenXml      => StripOpenXmlMetadata(ssRawFile, ssStripBodyAuthors),
            FileCategory.WordMl       => StripWordMlMetadata(ssRawFile, ssStripBodyAuthors),
            FileCategory.LegacyOffice => StripCfbfMetadata(ssRawFile),
            FileCategory.Odf          => StripOdfMetadata(ssRawFile),
            FileCategory.FlatOdf      => StripFlatOdfMetadata(ssRawFile),
            FileCategory.Epub         => StripEpubMetadata(ssRawFile),
            FileCategory.Ora          => StripOraMetadata(ssRawFile),
            FileCategory.Media        => StripMediaMetadata(ssRawFile),
            FileCategory.Passthrough  => Passthrough(ssRawFile),
            _                         => Passthrough(ssRawFile)
        };
    }

    private static FileCategory DetectCategory(byte[] rawFile)
    {
        // PDF: %PDF magic bytes
        if (rawFile.Length >= 4
            && rawFile[0] == 0x25 && rawFile[1] == 0x50
            && rawFile[2] == 0x44 && rawFile[3] == 0x46)
            return FileCategory.Pdf;

        // RTF (Rich Text Format): {\rtf1 (6 ASCII bytes). The '{' byte alone is common
        // in binary data, so a strict 6-byte prefix check is required to avoid false
        // positives. RTF is 7-bit ASCII with \'HH hex escapes for non-ASCII, so a
        // regex-based scrubber over the file text is sufficient — no NuGet needed.
        if (rawFile.Length >= 6
            && rawFile[0] == 0x7B  // '{'
            && rawFile[1] == 0x5C  // '\\'
            && rawFile[2] == 0x72  // 'r'
            && rawFile[3] == 0x74  // 't'
            && rawFile[4] == 0x66  // 'f'
            && rawFile[5] == 0x31) // '1'
            return FileCategory.Rtf;

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

        // Office Open XML (DOCX/XLSX/PPTX) or ODF (ODT/ODS/ODP) or EPUB or ORA: ZIP PK signature
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

        // Flat ODF (.fodt / .fods / .fodp): single-file XML variant of ODF whose root
        // element is <office:document> in the OASIS namespace. Detected before SVG so
        // Flat ODF documents that mention <svg> in the body aren't misrouted.
        if (IsFlatOdfFile(rawFile))
            return FileCategory.FlatOdf;

        // Word 2003 XML (.xml): WordProcessingML root <w:wordDocument> with the
        // Microsoft 2003 WordML namespace and an <o:DocumentProperties> block.
        if (IsWordMlFile(rawFile))
            return FileCategory.WordMl;

        // SVG: XML-based vector image. Detect before Magick.NET so we can strip XML text
        // nodes (<title>, <desc>, <metadata>) that survive raster-oriented Strip() calls.
        if (IsSvgFile(rawFile))
            return FileCategory.Svg;

        // Images: detected by Magick.NET (JPEG, PNG, GIF, TIFF, WebP, TGA, 100+ more)
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
        // ISO Base Media File Format: "ftyp" at bytes 4–7.
        // HEIC, HEIF, and AVIF share this container with MP4/MOV — distinguish by major brand (bytes 8–11).
        if (rawFile.Length >= 8 && rawFile[4] == 0x66 && rawFile[5] == 0x74 && rawFile[6] == 0x79 && rawFile[7] == 0x70)
            return IsHeifOrAvifBrand(rawFile) ? FileCategory.Image : FileCategory.Media;
        if (rawFile.Length >= 4 && rawFile[0] == 0x1A && rawFile[1] == 0x45 && rawFile[2] == 0xDF && rawFile[3] == 0xA3)
            return FileCategory.Media; // MKV / WebM
        if (rawFile.Length >= 4 && rawFile[0] == 0x30 && rawFile[1] == 0x26 && rawFile[2] == 0xB2 && rawFile[3] == 0x75)
            return FileCategory.Media; // WMA / ASF

        // AIFF / AIFC (Audio Interchange File Format): FORM at bytes 0–3, AIFF or AIFC at bytes 8–11.
        // Big-endian sibling of RIFF WAV.
        if (rawFile.Length >= 12
            && rawFile[0] == 0x46 && rawFile[1] == 0x4F && rawFile[2] == 0x52 && rawFile[3] == 0x4D
            && rawFile[8] == 0x41 && rawFile[9] == 0x49 && rawFile[10] == 0x46
            && (rawFile[11] == 0x46 || rawFile[11] == 0x43))
            return FileCategory.Media; // AIFF / AIFC

        // APE (Monkey's Audio): "MAC " (4 ASCII bytes, trailing space).
        if (rawFile.Length >= 4 && rawFile[0] == 0x4D && rawFile[1] == 0x41 && rawFile[2] == 0x43 && rawFile[3] == 0x20)
            return FileCategory.Media; // APE

        // WavPack (.wv): "wvpk" magic bytes.
        if (rawFile.Length >= 4 && rawFile[0] == 0x77 && rawFile[1] == 0x76 && rawFile[2] == 0x70 && rawFile[3] == 0x6B)
            return FileCategory.Media; // WavPack

        // MPC (Musepack): SV8 "MPCK" or SV7 "MP+" + version byte with 0x07 in the low nibble.
        if (rawFile.Length >= 4 && rawFile[0] == 0x4D && rawFile[1] == 0x50 && rawFile[2] == 0x43 && rawFile[3] == 0x4B)
            return FileCategory.Media; // MPC SV8
        if (rawFile.Length >= 4
            && rawFile[0] == 0x4D && rawFile[1] == 0x50 && rawFile[2] == 0x2B
            && (rawFile[3] & 0x0F) == 0x07)
            return FileCategory.Media; // MPC SV7

        // ── Image format fallbacks: formats not reliably detected by MagickImageInfo ──────

        // TGA v2 footer: last 18 bytes = "TRUEVISION-XFILE." + NUL.
        if (rawFile.Length >= 18)
        {
            var tgaFooter = System.Text.Encoding.ASCII.GetString(rawFile, rawFile.Length - 18, 17);
            if (tgaFooter == "TRUEVISION-XFILE.")
                return FileCategory.Image;
        }
        // TGA v1 header heuristic: validate bytes 1 (color-map type), 2 (image type),
        // 16 (pixel depth) and non-zero dimensions (bytes 12–15).
        if (rawFile.Length >= 18)
        {
            byte cmt   = rawFile[1]; byte imt   = rawFile[2]; byte depth = rawFile[16];
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

        // XCF (GIMP): "gimp xcf "
        if (rawFile.Length >= 9
            && rawFile[0] == 0x67 && rawFile[1] == 0x69 && rawFile[2] == 0x6D
            && rawFile[3] == 0x70 && rawFile[4] == 0x20 && rawFile[5] == 0x78
            && rawFile[6] == 0x63 && rawFile[7] == 0x66 && rawFile[8] == 0x20)
            return FileCategory.Image;

        // DCM (DICOM): 128-byte preamble followed by "DICM" at offset 128.
        if (rawFile.Length >= 132
            && rawFile[128] == 0x44 && rawFile[129] == 0x49
            && rawFile[130] == 0x43 && rawFile[131] == 0x4D)
            return FileCategory.Image;

        return FileCategory.Passthrough;
    }

    /// <summary>Routes a ZIP-based file (ODF / EPUB / ORA / OOXML) via its mimetype entry.</summary>
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
                false, 64, false);
            return reader.ReadToEnd().Trim();
        }
        catch { return null; }
    }

    private static RecFileMetadataResult Passthrough(byte[] rawFile) =>
        new RecFileMetadataResult
        {
            ssCleanFile         = rawFile,
            ssExtractedMetadata = "[]",
            ssRemovedEntryCount = 0,
            ssIsPassthrough     = true
        };

}
