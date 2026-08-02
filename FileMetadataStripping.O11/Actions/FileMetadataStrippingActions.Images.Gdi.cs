// System.Drawing (GDI+) fallback for the Image strip pipeline.
//
// Used automatically when Magick.NET's NativeMagickSettings type-init fails
// at runtime (HRESULT 0x8007045A) — typically on locked-down O11 hosts such
// as the OutSystems Personal Environment sandbox where the DLL loader refuses
// to complete DllMain on Magick.Native-Q8-x64.dll.
//
// Behaviour contract for the caller (matches the OSStegoGuard v0.1.2 fallback):
//
//   Actively stripped (GDI+ supports the format):
//       JPEG, PNG, GIF (incl. animated frames — first frame is stripped),
//       BMP, TIFF (multi-page — first page is stripped)
//     → IsPassthrough = false
//     → RemovedEntryCount > 0 when the input carried EXIF/IPTC/XMP/comments
//     → ExtractedMetadata = JSON list of PropertyItem IDs / names
//
//   GDI+-unsupported but recognised image format (WebP, HEIC, AVIF, JXL, JP2,
//   JXR, PSD/PSB, DDS, EXR, HDR, DPX/CIN, FITS, QOI, SGI, SUN, PCX/DCX, PNM,
//   XBM, XPM, JBIG, XCF, WMF, ICO, DCM, TGA, MNG, camera RAW variants, …):
//     → IsPassthrough = false               (NOT a security-signal downgrade)
//     → RemovedEntryCount = 0
//     → CleanFile = original bytes verbatim
//     → ExtractedMetadata = JSON with processingError prefixed
//                           "GDI+ fallback: image format 'X' is not supported…"
//
// Consumers detect "did the fallback actually strip?" with:
//   result.IsPassthrough == false && result.RemovedEntryCount > 0
//
// Consumers detect "the fallback engine refused this format" with:
//   result.IsPassthrough == false && result.RemovedEntryCount == 0
//     && result.ExtractedMetadata.Contains("GDI+ fallback:")
//
// System.Drawing (gdiplus.dll) is a Windows KnownDLL always loaded from System32,
// so it works on every Windows O11 host regardless of DLL search policy or EDR
// posture. On non-Windows hosts (never the O11 case) System.Drawing throws at
// bind time — the outer catch downgrades to a safe passthrough.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OutSystems.NssFileMetadataStripping;

public partial class CssFileMetadataStripping
{
    // JSON options used ONLY for the GDI+ fallback path's diagnostic messages.
    // UnsafeRelaxedJsonEscaping keeps '+' as literal '+' (instead of the default
    // '\u002B'), so consumers can grep the ExtractedMetadata payload for the
    // fixed marker `"GDI+ fallback:"` without having to reason about JSON
    // escape sequences. This encoder is safe here because we only serialise
    // strings we constructed ourselves — no user-supplied input crosses this
    // boundary.
    private static readonly JsonSerializerOptions _gdiJsonOpts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    /// <summary>
    /// Strips metadata from an image using GDI+ (System.Drawing). Used when Magick.NET
    /// is unavailable. See file header for the full behavioural contract.
    /// </summary>
    private static RecFileMetadataResult StripImageMetadataWithGdi(byte[] rawFile)
    {
        var detected = DetectImageFormatByMagicBytes(rawFile);
        if (detected == null)
        {
            // Not a recognised image format. Genuine passthrough — the caller passed
            // arbitrary bytes to a broken engine, and we have nothing meaningful to say.
            return Passthrough(rawFile);
        }

        if (!IsGdiSupportedFormat(detected))
        {
            // Recognised image but GDI+ cannot decode this format. Return the error
            // contract — IsPassthrough=false is deliberate (setting it to true here
            // would be a security-signal downgrade, because the file DOES carry
            // metadata containers and the fallback engine did NOT strip them).
            var errNode = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "GDI+ fallback: image format '" + detected + "' is not supported by the " +
                    "GDI+ fallback engine — bytes returned unchanged. FileMetadataStripping " +
                    "falls back to GDI+ on hosts where Magick.NET's native library cannot be " +
                    "initialised (e.g. OutSystems Personal Environment sandbox); on those " +
                    "hosts only JPEG, PNG, GIF, BMP and TIFF are actively stripped.")
            };
            return new RecFileMetadataResult
            {
                ssCleanFile         = rawFile,
                ssExtractedMetadata = errNode.ToJsonString(_gdiJsonOpts),
                ssRemovedEntryCount = 0,
                ssIsPassthrough     = false
            };
        }

        try
        {
            using var input = new MemoryStream(rawFile, writable: false);
            using var img = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: false);

            // Snapshot metadata BEFORE removing (RemovePropertyItem invalidates the collection).
            var propertyIds = img.PropertyIdList?.ToArray() ?? Array.Empty<int>();
            var extracted = new JsonObject();
            var namesArray = new JsonArray();
            int count = 0;
            foreach (var id in propertyIds)
            {
                namesArray.Add(JsonValue.Create(GdiPropertyName(id)));
                count++;
            }
            if (count > 0) extracted["gdiPropertyItems"] = namesArray;

            // Strip every PropertyItem from the in-memory image.
            foreach (var id in propertyIds)
            {
                try { img.RemovePropertyItem(id); }
                catch (ArgumentException) { /* already removed by an earlier iteration */ }
            }

            byte[] cleanBytes;
            var rawFormat = img.RawFormat;
            if (IsMultiFrameSaveable(img, out var frameDim, out var frameCount) && frameCount > 1
                && (detected == "Gif" || detected == "Tiff"))
            {
                cleanBytes = SaveMultiFrameStripped(img, frameDim, frameCount, detected);
            }
            else
            {
                using var output = new MemoryStream();
                // Preserve the input format on save via RawFormat.
                img.Save(output, rawFormat);
                cleanBytes = output.ToArray();
            }

            return new RecFileMetadataResult
            {
                ssCleanFile         = cleanBytes,
                ssExtractedMetadata = count > 0 ? extracted.ToJsonString(_gdiJsonOpts) : "[]",
                ssRemovedEntryCount = count,
                ssIsPassthrough     = false
            };
        }
        catch (Exception ex)
        {
            // GDI+ decode error on a magic-byte-detected format (e.g. corrupt JPEG).
            // Preserve the "did not strip" contract with a diagnostic in ExtractedMetadata.
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "GDI+ fallback: the image could not be decoded — bytes returned unchanged. " +
                    "Reason: " + ex.GetType().Name + ": " + ex.Message)
            };
            return new RecFileMetadataResult
            {
                ssCleanFile         = rawFile,
                ssExtractedMetadata = note.ToJsonString(_gdiJsonOpts),
                ssRemovedEntryCount = 0,
                ssIsPassthrough     = false
            };
        }
    }

    private static bool IsGdiSupportedFormat(string? format) =>
        format is "Jpeg" or "Png" or "Gif" or "Bmp" or "Tiff";

    // -------------------------------------------------------------------
    // Multi-frame save (animated GIF, multi-page TIFF)
    //
    // GDI+ requires the first frame to be saved with EncoderValue.MultiFrame,
    // followed by SaveAdd(EncoderValue.FrameDimensionTime | FrameDimensionPage)
    // for each subsequent frame, then SaveAdd(EncoderValue.Flush).
    // Per-frame PropertyItems (e.g. GIF frame delay) are best-effort — this
    // fallback engine focuses on stripping metadata, not preserving animation
    // timing perfectly.
    // -------------------------------------------------------------------

    private static bool IsMultiFrameSaveable(Image img, out FrameDimension? dim, out int count)
    {
        dim = null;
        count = 1;
        try
        {
            var dims = img.FrameDimensionsList;
            if (dims == null || dims.Length == 0) return false;
            dim = new FrameDimension(dims[0]);
            count = img.GetFrameCount(dim);
            return count > 1;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] SaveMultiFrameStripped(Image img, FrameDimension? dim, int frameCount, string detected)
    {
        if (dim == null || frameCount <= 1)
        {
            using var single = new MemoryStream();
            img.Save(single, img.RawFormat);
            return single.ToArray();
        }

        var encoder = detected == "Gif"
            ? GetEncoder(ImageFormat.Gif)
            : GetEncoder(ImageFormat.Tiff);
        if (encoder == null)
        {
            // No multi-frame encoder registered — save the first (already stripped) frame only.
            using var single = new MemoryStream();
            img.Save(single, img.RawFormat);
            return single.ToArray();
        }

        img.SelectActiveFrame(dim, 0);
        // The first frame's PropertyItems were already removed on `img` above; we're now
        // saving that same in-memory copy.
        using var outStream = new MemoryStream();
        var firstParams = new EncoderParameters(1);
        firstParams.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.MultiFrame);
        img.Save(outStream, encoder, firstParams);

        var addFlag = detected == "Gif"
            ? EncoderValue.FrameDimensionTime
            : EncoderValue.FrameDimensionPage;
        var addParams = new EncoderParameters(1);
        addParams.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)addFlag);

        for (int i = 1; i < frameCount; i++)
        {
            img.SelectActiveFrame(dim, i);
            // Strip PropertyItems from this frame too.
            var frameIds = img.PropertyIdList?.ToArray() ?? Array.Empty<int>();
            foreach (var id in frameIds)
            {
                try { img.RemovePropertyItem(id); }
                catch (ArgumentException) { }
            }
            img.SaveAdd(img, addParams);
        }

        var flush = new EncoderParameters(1);
        flush.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.Flush);
        img.SaveAdd(flush);
        return outStream.ToArray();
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format) =>
        ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == format.Guid);

    // -------------------------------------------------------------------
    // GDI+ PropertyItem ID → friendly name
    //
    // Small hardcoded table for the common EXIF / GPS / TIFF tags a user is most
    // likely to have injected. This fallback is a best-effort strip, not a full
    // Magick.NET replacement; unknown IDs are exposed as "PropertyId_0xNNNN"
    // so the diagnostic remains actionable.
    //
    // Reference: MSDN Image Property Tag Constants
    //   https://learn.microsoft.com/dotnet/api/system.drawing.imaging.propertyitem
    // -------------------------------------------------------------------

    private static readonly Dictionary<int, string> _gdiPropertyNames = new()
    {
        // TIFF / EXIF core
        { 0x010E, "ImageDescription" },
        { 0x010F, "Make" },
        { 0x0110, "Model" },
        { 0x0112, "Orientation" },
        { 0x0131, "Software" },
        { 0x0132, "DateTime" },
        { 0x013B, "Artist" },
        { 0x013C, "HostComputer" },
        { 0x8298, "Copyright" },
        { 0x8769, "ExifIFDPointer" },
        { 0x8825, "GpsIFDPointer" },
        { 0x9003, "DateTimeOriginal" },
        { 0x9004, "DateTimeDigitized" },
        { 0x9286, "UserComment" },
        { 0x9290, "SubsecTime" },
        { 0x9291, "SubsecTimeOriginal" },
        { 0x9292, "SubsecTimeDigitized" },
        { 0xA430, "CameraOwnerName" },
        { 0xA431, "BodySerialNumber" },
        { 0xA432, "LensSpecification" },
        { 0xA433, "LensMake" },
        { 0xA434, "LensModel" },
        { 0xA435, "LensSerialNumber" },
        // Exposure
        { 0x829A, "ExposureTime" },
        { 0x829D, "FNumber" },
        { 0x8827, "ISOSpeedRatings" },
        { 0x9201, "ShutterSpeedValue" },
        { 0x9202, "ApertureValue" },
        // GPS
        { 0x0000, "GpsVersionID" },
        { 0x0001, "GpsLatitudeRef" },
        { 0x0002, "GpsLatitude" },
        { 0x0003, "GpsLongitudeRef" },
        { 0x0004, "GpsLongitude" },
        { 0x0005, "GpsAltitudeRef" },
        { 0x0006, "GpsAltitude" },
        { 0x001D, "GpsDateStamp" },
        // XMP / IPTC / thumbnail
        { 0x02BC, "XmpPacket" },
        { 0x83BB, "IptcNaa" },
        { 0x5023, "ThumbnailData" },
        { 0x5100, "FrameDelay" },
        { 0x5101, "LoopCount" },
    };

    private static string GdiPropertyName(int id) =>
        _gdiPropertyNames.TryGetValue(id, out var name)
            ? name
            : "PropertyId_0x" + id.ToString("X4");
}
