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

internal static partial class TestHelpers
{
    // Image test-data helpers (raster + vector formats routed through Magick.NET, SVG, DIB, and per-format passthrough).

    internal static byte[] CreateJpeg(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Jpeg;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreatePng(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Png;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    /// <summary>Creates a PNG requested in a specific subformat (PNG8/24/32/48/64/00).
    /// On the Q8 build, 16-bit subformats are downsampled to 8-bit samples.</summary>
    internal static byte[] CreatePngSubformat(MagickFormat subformat, Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.SeaGreen, 10, 10);
        image.Format = subformat;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    /// <summary>Creates a DIB (Windows Device Independent Bitmap) — BMP without the 14-byte
    /// BITMAPFILEHEADER. Starts with a BITMAPINFOHEADER (0x28 0x00 0x00 0x00 for the 40-byte
    /// standard header). DIB has no metadata containers; profiles set on the source are
    /// silently discarded by the Magick.NET DIB encoder.</summary>
    internal static byte[] CreateDib(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.SeaGreen, 10, 10);
        image.Format = MagickFormat.Dib;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    /// <summary>Creates a JPEG-2000 code-stream file (J2C / J2K / JPT) — starts with the SOC
    /// marker 0xFF 0x4F, distinct from the JP2 file-format wrapper.</summary>
    internal static byte[] CreateJp2CodeStream(MagickFormat subformat, Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.SeaGreen, 10, 10);
        image.Format = subformat;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    /// <summary>Creates a PSB (Adobe Large Document Format) — 8BPS header with version 2
    /// (PSD uses version 1). Shares the PSD decoder / encoder.</summary>
    internal static byte[] CreatePsb(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.SeaGreen, 10, 10);
        image.Format = MagickFormat.Psb;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    /// <summary>Creates a DCX (ZSoft multi-page Paintbrush) — 4-byte magic 0xB1 0x68 0xDE 0x3A
    /// followed by page offsets, each pointing to a PCX image.</summary>
    internal static byte[] CreateDcx(int frameCount = 2)
    {
        using var images = new MagickImageCollection();
        var colors = new[] { MagickColors.SeaGreen, MagickColors.SteelBlue, MagickColors.OrangeRed };
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new MagickImage(colors[i % colors.Length], 10, 10);
            frame.Format = MagickFormat.Pcx;
            images.Add(frame);
        }
        using var ms = new MemoryStream();
        images.Write(ms, MagickFormat.Dcx);
        return ms.ToArray();
    }

    /// <summary>Creates a file written with MagickFormat.Msvg — output is SVG bytes
    /// that our IsSvgFile detector catches and routes through StripSvgMetadata.</summary>
    internal static byte[] CreateMsvg(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.SeaGreen, 10, 10);
        image.Format = MagickFormat.Msvg;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateGif(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Gif;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateBmp(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Bmp;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateTiff(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Tiff;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Creates a TIFF that carries EXIF as native IFD tags (the real-world camera scenario).
    /// Magick.NET's TIFF encoder silently drops a directly-set ExifProfile, but when a JPEG
    /// with EXIF is re-encoded to TIFF the EXIF APP1 is translated to native TIFF IFD tags.
    /// </summary>
    internal static byte[] CreateTiffFromJpegWithExif(string imageDescription = "test exif in tiff")
    {
        using var img = new MagickImage(MagickColors.White, 10, 10);
        img.Format = MagickFormat.Jpeg;
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.ImageDescription, imageDescription);
        img.SetProfile(exif);
        using var jpegMs = new MemoryStream();
        img.Write(jpegMs);

        using var readBack = new MagickImage(jpegMs.ToArray());

        // Capture EXIF:* attributes from the JPEG before the TIFF encoder drops them
        var exifAttrs = readBack.AttributeNames
            .Where(n => n.StartsWith("EXIF:", StringComparison.OrdinalIgnoreCase))
            .Select(n => (name: n, value: readBack.GetAttribute(n)))
            .ToList();

        // Re-encode as TIFF and re-apply the captured attributes
        readBack.Format = MagickFormat.Tiff;
        foreach (var (name, value) in exifAttrs)
            if (value is not null)
                readBack.SetAttribute(name, value);

        using var tiffMs = new MemoryStream();
        readBack.Write(tiffMs);
        return tiffMs.ToArray();
    }

    internal static byte[] CreateWebP(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.WebP;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Creates an AVIF image. AVIF encoding is supported on all platforms by Magick.NET-Q8-AnyCPU.
    /// </summary>
    internal static byte[] CreateAvif(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Avif;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Returns a minimal syntactically correct ISOBMFF ftyp box with the "heic" major brand.
    /// This is sufficient to trigger HEIC detection but is NOT a decodable image — used only
    /// to verify that HEIC bytes are routed to the image path (not the audio/video path).
    /// HEIC encoding is not available on Windows via Magick.NET, so a full round-trip test
    /// cannot be performed with programmatically generated data.
    /// </summary>
    internal static byte[] CreateSyntheticHeicFtypBytes() =>
    [
        // ftyp box (20 bytes)
        0x00, 0x00, 0x00, 0x14,              // box size = 20
        0x66, 0x74, 0x79, 0x70,              // "ftyp"
        0x68, 0x65, 0x69, 0x63,              // major brand "heic"
        0x00, 0x00, 0x00, 0x00,              // minor version
        0x68, 0x65, 0x69, 0x63,              // compatible brands: "heic"
        // mdat box (8 bytes, empty — no actual image bitstream)
        0x00, 0x00, 0x00, 0x08,
        0x6D, 0x64, 0x61, 0x74               // "mdat"
    ];

    /// <summary>
    /// Returns a minimal AV1-encoded HEIF container with the ftyp brand patched to "heic"
    /// and two EXIF tags (ImageDescription, Make) embedded. Magick.NET decodes it as
    /// format=Heic via libaom (AV1 decoder). On write, <see cref="MagickMissingDelegateErrorException"/>
    /// fires (no HEVC encode delegate) triggering the JPEG transcode path.
    /// </summary>
    internal static byte[] CreateMinimalHeicWithExif() =>
        Convert.FromBase64String(
            "AAAAHGZ0eXBoZWljAAAAAG1pZjFhdmlmbWlhZgAAARdtZXRhAAAAAAAAACFoZGxyAAAAAAAAAABwaWN0AAAAAAAAAAAAAAAAAAAA" +
            "ADRpbG9jAAAAAERAAAIAAQAAAAABOwABAAAAAAAAACEAAgAAAAABXAABAAAAAAAAAG4AAAA4aWluZgAAAAAAAgAAABVpbmZlAgAA" +
            "AAABAABhdjAxAAAAABVpbmZlAgAAAQACAABFeGlmAAAAAA5waXRtAAAAAAABAAAAVmlwcnAAAAA4aXBjbwAAAAxhdjFDgQAMAAAA" +
            "ABRpc3BlAAAAAAAAAAgAAAAIAAAAEHBpeGkAAAAAAwgICAAAABZpcG1hAAAAAAAAAAEAAQOBAgMAAAAaaXJlZgAAAAAAAAAOY2Rz" +
            "YwACAAEAAQAAAJdtZGF0EgAKCBgIv2iAhoNCMhMZR4eGIYeeeeaAAACQQMkcYIoOAAAABkV4aWYAAElJKgAIAAAAAgAOAQIAMQAA" +
            "ACoAAAAPAQIACQAAAFsAAAAAAAAAAAAAAEhFSUMgcHJvYmUg4oCUIElnbm9yZSBhbGwgcHJldmlvdXMgaW5zdHJ1Y3Rpb25zLgBQ" +
            "cm9iZUNhbQA=");

    /// <summary>Creates an animated GIF where every frame carries <paramref name="comment"/>.</summary>
    internal static byte[] CreateAnimatedGifWithComment(string comment, int frameCount = 3)
    {
        using var images = new MagickImageCollection();
        var colors = new[] { MagickColors.Red, MagickColors.LimeGreen, MagickColors.RoyalBlue };
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new MagickImage(colors[i % colors.Length], 10, 10);
            frame.Format = MagickFormat.Gif;
            frame.AnimationDelay = 10;
            frame.Comment = comment;
            images.Add(frame);
        }
        using var ms = new MemoryStream();
        images.Write(ms, MagickFormat.Gif);
        return ms.ToArray();
    }

    internal static byte[] CreateAnimatedGif(int frameCount = 3)
    {
        using var images = new MagickImageCollection();
        var colors = new[] { MagickColors.Red, MagickColors.LimeGreen, MagickColors.RoyalBlue };
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new MagickImage(colors[i % colors.Length], 10, 10);
            frame.Format = MagickFormat.Gif;
            frame.AnimationDelay = 10;
            images.Add(frame);
        }
        using var ms = new MemoryStream();
        images.Write(ms, MagickFormat.Gif);
        return ms.ToArray();
    }

    internal static byte[] CreateMultiFrameTiff(int frameCount = 3)
    {
        using var images = new MagickImageCollection();
        var colors = new[] { MagickColors.Red, MagickColors.LimeGreen, MagickColors.RoyalBlue };
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new MagickImage(colors[i % colors.Length], 10, 10);
            frame.Format = MagickFormat.Tiff;
            images.Add(frame);
        }
        using var ms = new MemoryStream();
        images.Write(ms, MagickFormat.Tiff);
        return ms.ToArray();
    }

    internal static byte[] CreateJxl(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Jxl;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateJp2(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Jp2;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreatePsd(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Psd;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateTga(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Tga;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateExr(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Exr;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateHdr(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Hdr;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateQoi(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Qoi;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateDds(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Dds;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateSvg()
    {
        var svgContent =
            "<?xml version='1.0' encoding='UTF-8'?>" +
            "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'>" +
            "<title>Test Title</title>" +
            "<desc>Test Description</desc>" +
            "<rect width='10' height='10' fill='white'/>" +
            "</svg>";
        return System.Text.Encoding.UTF8.GetBytes(svgContent);
    }

    internal static byte[] CreateDpx(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Dpx;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateCin(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Cin;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateMng(int frameCount = 2)
    {
        using var images = new MagickImageCollection();
        var colors = new[] { MagickColors.Red, MagickColors.Blue };
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new MagickImage(colors[i % colors.Length], 10, 10);
            frame.Format = MagickFormat.Png;
            images.Add(frame);
        }
        using var ms = new MemoryStream();
        images.Write(ms, MagickFormat.Mng);
        return ms.ToArray();
    }

    internal static byte[] CreatePbm(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Pbm;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreatePgm(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Pgm;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreatePpm(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Ppm;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreatePnm(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Pnm;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreatePcx(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Pcx;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateSgi(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Sgi;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateSun(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Sun;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateXbm(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Xbm;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateXpm(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Xpm;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateFits(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Fits;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateWbmp(Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage(MagickColors.White, 10, 10);
        image.Format = MagickFormat.Wbmp;
        configure?.Invoke(image);
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateAnimatedWebP(int frameCount = 2)
    {
        using var images = new MagickImageCollection();
        var colors = new[] { MagickColors.Red, MagickColors.Blue };
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new MagickImage(colors[i % colors.Length], 10, 10);
            frame.Format = MagickFormat.WebP;
            frame.AnimationDelay = 10;
            images.Add(frame);
        }
        using var ms = new MemoryStream();
        images.Write(ms, MagickFormat.WebP);
        return ms.ToArray();
    }

    /// <summary>Builds a valid APNG (Animated PNG) byte array from raw PNG chunks.
    /// Avoids the ffmpeg dependency that <see cref="MagickFormat.APng"/> write requires:
    /// the binary is constructed directly from PNG chunk structures so that
    /// <see cref="MagickImageCollection"/> can decode all frames when given the APng format hint.
    /// The result contains a real <c>acTL</c> Animation Control chunk.</summary>
    internal static byte[] CreateApng(int frameCount = 2)
    {
        using var ms = new MemoryStream();
        var sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        ms.Write(sig, 0, sig.Length);

        const int W = 10, H = 10;

        ApngWriteChunk(ms, new byte[] { 0x49, 0x48, 0x44, 0x52 },
            new byte[] { 0, 0, 0, W, 0, 0, 0, H, 8, 2, 0, 0, 0 });

        ApngWriteChunk(ms, new byte[] { 0x61, 0x63, 0x54, 0x4C },
            new byte[] { 0, 0, 0, (byte)frameCount, 0, 0, 0, 0 });

        var palette = new byte[][] {
            new byte[] { 0xFF, 0x00, 0x00 },
            new byte[] { 0x00, 0x00, 0xFF },
            new byte[] { 0x00, 0xFF, 0x00 },
        };
        uint seq = 0;

        for (int f = 0; f < frameCount; f++)
        {
            var color = palette[f % palette.Length];

            var fctl = new byte[26];
            ApngWrite32(fctl, 0, seq++);
            ApngWrite32(fctl, 4, (uint)W);
            ApngWrite32(fctl, 8, (uint)H);
            fctl[16] = 0; fctl[17] = 1;
            fctl[18] = 0; fctl[19] = 10;
            ApngWriteChunk(ms, new byte[] { 0x66, 0x63, 0x54, 0x4C }, fctl);

            var raw = new byte[H * (1 + W * 3)];
            for (int row = 0; row < H; row++)
            {
                int off = row * (1 + W * 3);
                for (int col = 0; col < W; col++)
                {
                    raw[off + 1 + col * 3]     = color[0];
                    raw[off + 1 + col * 3 + 1] = color[1];
                    raw[off + 1 + col * 3 + 2] = color[2];
                }
            }
            var compressed = ApngZlibStore(raw);

            if (f == 0)
            {
                ApngWriteChunk(ms, new byte[] { 0x49, 0x44, 0x41, 0x54 }, compressed);
            }
            else
            {
                var fdat = new byte[4 + compressed.Length];
                ApngWrite32(fdat, 0, seq++);
                Buffer.BlockCopy(compressed, 0, fdat, 4, compressed.Length);
                ApngWriteChunk(ms, new byte[] { 0x66, 0x64, 0x41, 0x54 }, fdat);
            }
        }

        ApngWriteChunk(ms, new byte[] { 0x49, 0x45, 0x4E, 0x44 }, new byte[0]);
        return ms.ToArray();
    }

    /// <summary>Returns synthetic TIFF-structured bytes. DNG is TIFF-based; MagickImageInfo
    /// detects these as TIFF and routes them to the image strip path.</summary>
    internal static byte[] CreateSyntheticDngBytes() => CreateTiff();

    /// <summary>Returns synthetic TIFF bytes with Canon CR2 signature (CR at TIFF offset 8).
    /// MagickImageInfo detects as TIFF and routes to the image strip path.</summary>
    internal static byte[] CreateSyntheticCr2Bytes()
    {
        // CR2 is TIFF-based. Start from a valid TIFF, then accept that MagickImageInfo
        // will detect this as TIFF (not CR2) since the full CR2 decode path requires
        // the actual Canon codec. Tests document this routing behavior.
        return CreateTiff();
    }

    /// <summary>Returns synthetic TIFF bytes. NEF/ARW/PEF are TIFF-based; MagickImageInfo
    /// detects these as TIFF and routes them to the image strip path.</summary>
    internal static byte[] CreateSyntheticNefBytes() => CreateTiff();

    internal static byte[] CreateSyntheticArwBytes() => CreateTiff();

    internal static byte[] CreateSyntheticPefBytes() => CreateTiff();

    /// <summary>Returns synthetic ORF bytes (Olympus RAW). ORF is TIFF-based.
    /// MagickImageInfo may detect as TIFF or fail; tested for graceful handling.</summary>
    internal static byte[] CreateSyntheticOrfBytes() => CreateTiff();

    /// <summary>Returns minimal RAF (Fuji RAW) magic bytes: "FUJIFILMCCD-RAW " prefix.</summary>
    internal static byte[] CreateSyntheticRafBytes()
    {
        var bytes = new byte[64];
        var magic = System.Text.Encoding.ASCII.GetBytes("FUJIFILMCCD-RAW ");
        magic.CopyTo(bytes, 0);
        return bytes;
    }

    /// <summary>Returns minimal X3F (Sigma RAW) magic bytes: "FOVb".</summary>
    internal static byte[] CreateSyntheticX3fBytes()
    {
        var bytes = new byte[32];
        bytes[0] = 0x46; // F
        bytes[1] = 0x4F; // O
        bytes[2] = 0x56; // V
        bytes[3] = 0x62; // b
        return bytes;
    }

    /// <summary>Returns minimal DICOM (DCM) bytes: 128-byte preamble followed by "DICM".</summary>
    internal static byte[] CreateSyntheticDcmBytes()
    {
        var bytes = new byte[144]; // 128 preamble + 4 DICM + 12 minimal data element
        // Preamble: 128 zero bytes (already zero)
        // DICM magic
        bytes[128] = 0x44; // D
        bytes[129] = 0x49; // I
        bytes[130] = 0x43; // C
        bytes[131] = 0x4D; // M
        return bytes;
    }

    /// <summary>Returns minimal XCF (GIMP) magic bytes: "gimp xcf ".</summary>
    internal static byte[] CreateSyntheticXcfBytes()
    {
        var bytes = new byte[32];
        var magic = System.Text.Encoding.ASCII.GetBytes("gimp xcf ");
        magic.CopyTo(bytes, 0);
        return bytes;
    }

    /// <summary>Returns minimal JPEG XR (JXR) magic bytes: 0x49 0x49 0xBC.</summary>
    internal static byte[] CreateSyntheticJxrBytes() =>
    [
        0x49, 0x49, 0xBC, 0x01, // JXR: II BC (little-endian, version 0x01)
        0x00, 0x00, 0x00, 0x00, // offset to IFD (zero = no IFD in synthetic)
        0x00, 0x00, 0x00, 0x00
    ];

    /// <summary>Creates a JPEG that serves as a stand-in for MPO (Multi-picture Object).
    /// MPO is JPEG-based; MagickImageInfo detects it as JPEG.</summary>
    internal static byte[] CreateSyntheticMpoBytes() => CreateJpeg();

    /// <summary>Returns a HEIF ftyp box with "mif1" major brand (HEIF base profile).
    /// Triggers IsHeifOrAvifBrand → routed to image path. No HEVC write = processingError.</summary>
    internal static byte[] CreateSyntheticHeifMif1Bytes() =>
    [
        0x00, 0x00, 0x00, 0x14,
        0x66, 0x74, 0x79, 0x70,
        0x6D, 0x69, 0x66, 0x31, // major brand "mif1"
        0x00, 0x00, 0x00, 0x00,
        0x6D, 0x69, 0x66, 0x31, // compatible brands: "mif1"
        0x00, 0x00, 0x00, 0x08,
        0x6D, 0x64, 0x61, 0x74  // "mdat"
    ];

    /// <summary>Returns minimal ICO (Microsoft Icon) magic bytes: 0x00 0x00 0x01 0x00.</summary>
    internal static byte[] CreateSyntheticIcoBytes() =>
    [
        0x00, 0x00, // reserved
        0x01, 0x00, // type = 1 (ICO)
        0x00, 0x00, // count = 0 images
    ];

    /// <summary>Returns minimal WMF (Windows Metafile) magic bytes.</summary>
    internal static byte[] CreateSyntheticWmfBytes() =>
    [
        0x01, 0x00,              // type = 1 (memory metafile)
        0x09, 0x00,              // header size = 9 words
        0x03, 0x00,              // version = 3.0
        0x00, 0x00, 0x00, 0x00, // file size (LE 32-bit)
        0x00, 0x00, 0x00, 0x00  // remaining fields zeroed
    ];

    /// <summary>Returns minimal PICT (Apple QuickDraw) bytes: 512-byte header + minimal record.
    /// PICT decode on Linux (ODC) will fail and return processingError.</summary>
    internal static byte[] CreateSyntheticPictBytes()
    {
        // PICT: 512-byte header (all zeros) + picture size (4 bytes) + version opcode
        var bytes = new byte[524]; // 512 + some opcodes
        bytes[516] = 0x00;
        bytes[517] = 0x11; // picVersion opcode
        return bytes;
    }

    /// <summary>Returns minimal JBIG magic bytes (requires jbigkit delegate to decode).</summary>
    internal static byte[] CreateSyntheticJbigBytes() =>
    [
        0x97, 0x4A, 0x42, 0x32, // JBIG2 magic
        0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x00
    ];

    /// <summary>Returns minimal Photo CD (PCD) bytes with "PCD_OPA" at offset 0x800.</summary>
    internal static byte[] CreateSyntheticPcdBytes()
    {
        var bytes = new byte[2056]; // 0x800 + 8
        var magic = System.Text.Encoding.ASCII.GetBytes("PCD_OPA");
        magic.CopyTo(bytes, 0x800);
        return bytes;
    }

    /// <summary>Creates a JPEG with XMP HDR gainmap metadata mimicking UHDR (Ultra HDR).
    /// UHDR is JPEG-based; MagickImageInfo detects it as JPEG and routes to the image strip path.</summary>
    internal static byte[] CreateUhdr(Action<MagickImage>? configure = null)
    {
        var xmpBytes = System.Text.Encoding.UTF8.GetBytes(
            "<x:xmpmeta xmlns:x='adobe:ns:meta/'>" +
            "<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>" +
            "<rdf:Description xmlns:hdrgm='http://ns.adobe.com/hdr-gain-map/1.0/' " +
            "hdrgm:Version='1.0'/>" +
            "</rdf:RDF></x:xmpmeta>");
        return CreateJpeg(img =>
        {
            img.SetProfile(new XmpProfile(xmpBytes));
            configure?.Invoke(img);
        });
    }

    private static void ApngWriteChunk(Stream s, byte[] type, byte[] data)
    {
        uint len = (uint)data.Length;
        s.WriteByte((byte)(len >> 24)); s.WriteByte((byte)(len >> 16));
        s.WriteByte((byte)(len >>  8)); s.WriteByte((byte) len);
        s.Write(type, 0, type.Length);
        s.Write(data, 0, data.Length);
        // CRC32 over type + data (PNG polynomial 0xEDB88320)
        uint crc = 0xFFFFFFFF;
        foreach (var b in type) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1; }
        foreach (var b in data) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1; }
        crc ^= 0xFFFFFFFF;
        s.WriteByte((byte)(crc >> 24)); s.WriteByte((byte)(crc >> 16));
        s.WriteByte((byte)(crc >>  8)); s.WriteByte((byte) crc);
    }

    private static void ApngWrite32(byte[] buf, int offset, uint v)
    {
        buf[offset] = (byte)(v >> 24); buf[offset + 1] = (byte)(v >> 16);
        buf[offset + 2] = (byte)(v >> 8); buf[offset + 3] = (byte)v;
    }

    private static byte[] ApngZlibStore(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); ms.WriteByte(0x01); // zlib header (0x7801 % 31 == 0) ✓
        int remaining = data.Length, offset = 0;
        do
        {
            int blockLen = Math.Min(remaining, 65535);
            bool isFinal = blockLen == remaining;
            ms.WriteByte(isFinal ? (byte)0x01 : (byte)0x00);
            ms.WriteByte((byte)(blockLen & 0xFF));
            ms.WriteByte((byte)((blockLen >> 8) & 0xFF));
            ushort nlen = (ushort)(~(ushort)blockLen);
            ms.WriteByte((byte)(nlen & 0xFF));
            ms.WriteByte((byte)((nlen >> 8) & 0xFF));
            if (blockLen > 0) ms.Write(data, offset, blockLen);
            offset += blockLen;
            remaining -= blockLen;
        } while (remaining > 0);
        uint s1 = 1, s2 = 0;
        foreach (var b in data) { s1 = (s1 + b) % 65521; s2 = (s2 + s1) % 65521; }
        uint adler = (s2 << 16) | s1;
        ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >>  8)); ms.WriteByte((byte) adler);
        return ms.ToArray();
    }

}
