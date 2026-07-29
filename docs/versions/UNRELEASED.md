# Unreleased

Changes in progress — not yet published to OutSystems Forge.

> When publishing a Forge release, tell the OutSystems Extension Builder agent the version number.

---

## Added

- **Legacy binary Office (CFBF)** files are now routed to a dedicated strip path. Word 97–2003 (`.doc`, `.dot`), Excel 97–2003 (`.xls`, `.xlt`), and PowerPoint 97–2003 (`.ppt`, `.pot`, `.pps`) all share the same Compound File Binary Format container (magic `D0 CF 11 E0 A1 B1 1A E1`); detection uses that 8-byte magic (checked before the ZIP `PK\x03\x04` signature so there's no clash with OOXML/ODF/EPUB/ORA). The strip path wipes the two OLE property-set streams `\x05SummaryInformation` (Title, Subject, Author, Keywords, Comments, Template, Last-Saved-By, Application) and `\x05DocumentSummaryInformation` (Category, Manager, Company, ContentStatus, Language, custom properties). The container is consolidated after deletion so the freed sectors are dropped from the output — the raw property values do not survive in unallocated space. Known well-named properties are captured in `ExtractedMetadata` under `summaryInformation`, `documentSummaryInformation`, and `customProperties` before deletion for audit. Depends on `OpenMcdf 3.1.4` (MPL-2.0) and `OpenMcdf.Ole 3.1.4-experimental.1` (MPL-2.0). Verified with 29 tests per platform (58 total) covering detection, per-property strip, per-extension round-trip for all seven Office extensions, custom user-defined properties, extracted-metadata audit, format validity, clean-baseline (CFBF with no metadata streams → `RemovedEntryCount = 0`), security invariant, and edge cases (truncated / corrupt containers → `processingError` note, no throw).

- AVIF is now a fully supported image format: EXIF, IPTC, XMP, and comments are stripped and returned as `CleanFile`. Verified round-trip with Magick.NET.
- HEIC and HEIF (mif1/msf1 brands) are now correctly detected as images via ISOBMFF `ftyp` brand check (bytes 8–11). Metadata is extracted and recorded in `ExtractedMetadata`; the original file is returned with a `processingError` note because Magick.NET has no HEIC encode delegate on Windows.
- `IsHeifOrAvifBrand` private helper added to both ODC and O11 implementations to perform the ISOBMFF brand check.
- Comprehensive test coverage added for 40+ previously untested image formats across both ODC (`FileMetadataStripping.Tests`) and O11 (`FileMetadataStripping.O11.Tests`) test projects, covering: JXL (JPEG XL), JP2/J2K (JPEG 2000), PSD/PSB (Photoshop), TGA, APNG, DNG/CR2/NEF/ARW (RAW camera formats treated as TIFF), UHDR (Ultra HDR as JPEG), AI (Adobe Illustrator as PDF), DCM (DICOM), EXR (OpenEXR), HDR (Radiance RGBE), ORF/RAF/PEF/X3F (RAW camera), QOI (Quite OK Image), DDS (DirectDraw Surface), SVG, HEIF mif1/msf1 brands, XCF (GIMP), DPX/CIN (film formats), JXR/WDP (JPEG XR), MPO (Multi-picture Object), MNG (animated), PBM/PGM/PPM/PNM (Netpbm), PCX, SGI, SUN, PICT, XBM, XPM, JBIG, FITS, PCD/PCDS, ICO, WMF, WBMP, ORA (Open Raster), EPUB, and Animated WebP. Known pre-existing implementation gaps are documented inline in the relevant test files.
- **APNG fixed:** `IsApngFile()` detects APNG via the `acTL` chunk and passes `MagickFormat.APng` as a read hint to `MagickImageCollection`, ensuring all animation frames are decoded. Writing APNG still requires ImageMagick's `video` delegate (ffmpeg); without it `MagickMissingDelegateErrorException` triggers the existing JPEG fallback, and `ExtractedMetadata` receives a `transcodedFormat` note. `TestHelpers.CreateApng()` now produces real APNG bytes (with `acTL`, `fcTL`, `IDAT`, `fdAT` chunks) built from raw PNG chunk structures — no ffmpeg required for test data generation.
- `GenerateSamples` tool extended with sample files for TGA, ICO, XCF, DCM, and HEIF mif1.
- **SVG** files are now routed to a dedicated XML-aware strip path. `<title>`, `<desc>`, and `<metadata>` elements are removed at every depth (matched by local name, so unnamespaced children are also cleaned). The output remains a valid SVG.
- **EPUB** files are now routed to a dedicated strip path (via the ZIP `mimetype` entry) that follows the reference in `META-INF/container.xml` to the OPF package document, then blanks every Dublin Core element (`dc:creator`, `dc:title`, `dc:description`, `dc:rights`, …) and every OPF `<meta>` refinement. Multiple values on the same DC key (e.g. two `dc:creator` elements) are preserved in `ExtractedMetadata` as an array.
- **ORA (Open Raster)** files are now routed to a dedicated strip path (via the ZIP `mimetype` entry) that blanks the `name` and `description` attributes on every element in `stack.xml` (image, stack, layer, mask, text). Structural attributes (`w`, `h`, `x`, `y`, `opacity`, `src`, `mask-src`, `composite-op`, `visibility`) are preserved so the image still renders correctly.
- **DPX / CIN** `dpx:*` and `cin:*` per-image production attributes (film title, origination device, file filename, …) are now explicitly removed after `Strip()`; the values are captured in `ExtractedMetadata` under the `dpx` / `cin` keys before being cleared.
- **OOXML embedded thumbnail** (`docProps/thumbnail.{jpeg,png,emf,wmf,gif,tiff}`) is now removed from DOCX/XLSX/PPTX outputs, along with its `_rels/.rels` thumbnail relationship. Prevents a rendered page preview from reaching a vision model.
- **Audio / video regression coverage extended** to every format the `DetectCategory` routes through the media pipeline. New tests cover **M4A** (iTunes audio), **MOV** (QuickTime), **WebM** (EBML container), **WMA** and **WMV** (ASF), **3GP** (`3gp4` brand), **3G2** (`3g2a` brand), **M4V** (Apple iTunes video), **M4B** (Apple audiobook), and **Ogg Opus** (OggS + OpusHead). Each format is verified to be detected (`IsPassthrough = false`), to survive the strip pipeline without throwing, and to preserve its container magic bytes / major brand in the output.

## Changed

- ZIP-based file dispatch was refactored: `DetectZipCategory()` reads the archive's `mimetype` entry once and routes ODF, EPUB, and ORA to their dedicated paths, falling back to `FileCategory.OpenXml` for anything else. `IsOdfFormat()` now delegates to the shared `ReadZipMimetype()` helper.

- HEIC, HEIF, and AVIF files are no longer mis-routed to TagLibSharp as audio/video; they are routed to the image pipeline via the new ISOBMFF brand check.
- `StripImageMetadata` now handles `MagickException` on read gracefully: corrupt or undecodable images are returned unchanged with `processingError` in `ExtractedMetadata` instead of raising an exception.
- `StripImageMetadata` now handles `MagickMissingDelegateErrorException` on write gracefully: when Magick.NET cannot re-encode the image to its original format (e.g. HEIC — confirmed absent on both Windows and ODC Amazon Linux 2023 because the x265 HEVC encoder is GPL-licensed and cannot be bundled in redistributable NuGet packages), the clean stripped pixels are transcoded to JPEG instead. `ExtractedMetadata` includes a `transcodedFormat` key explaining the format change and its cause. The original format is not preserved.
- **DIB (Windows Device Independent Bitmap)** is now routed to passthrough alongside BMP, WBMP, XBM, and XPM. DIB has no metadata containers, so the previous Magick.NET decode-and-re-encode round-trip was a pure no-op that added latency without any security benefit. Detection uses a new `IsDibFile()` heuristic that validates the `BITMAPINFOHEADER` structure (header size in {40, 52, 56, 108, 124}, planes = 1, and a valid bit depth of 1/4/8/16/24/32) to avoid false positives on arbitrary binary input.

## Fixed

- **`StripMediaMetadata` no longer crashes on malformed ASF/MP4 input.** TagLibSharp's ASF (WMA / WMV) and MP4 (M4A) parsers throw `NullReferenceException`, `IndexOutOfRangeException`, `EndOfStreamException`, `ArgumentException`, and `OverflowException` on crafted / truncated headers; the previous catch clause only handled `UnsupportedFormatException`, `CorruptFileException`, `ArgumentOutOfRangeException`, and `InvalidOperationException`, so a malformed WMA/WMV or M4A file would propagate the exception and crash the calling process. All of those exception types are now caught; the original file is returned with a `processingError` audit note.
- TGA files are now reliably detected using a v2 footer check combined with a v1 header heuristic (TGA has no start-of-file magic bytes). Decoding now passes an explicit `MagickFormat.Tga` hint to prevent format misidentification.
- ICO (Windows icon) files are now detected via their 4-byte magic (`00 00 01 00`) and routed through the image pipeline; previously they were undetected and passed through unchanged.
- XCF (GIMP native) files are now detected via `gimp xcf ` magic bytes and routed through the image pipeline; previously they were undetected and passed through unchanged.
- DCM (DICOM medical imaging) files are now detected via the 128-byte preamble followed by `DICM` at offset 128 and routed through the image pipeline; previously they were undetected and passed through unchanged.
- `StripImageMetadata` no longer raises `ArgumentOutOfRangeException` when the Magick.NET codec returns an empty frame collection for an input file; empty collections are now handled gracefully and the original file is returned unchanged.
- Non-`MagickException` codec failures (e.g. `InvalidOperationException` thrown by certain decoders) are now caught; the original file is returned unchanged with a `processingError` key in `ExtractedMetadata`.
- Radiance HDR (`.hdr`) output no longer contains encoder-inserted `# Created by ImageMagick` comment lines. `StripHdrCommentLines()` removes all `#`-prefixed comment lines from HDR output while preserving the mandatory `#?RADIANCE` identification line.
- `ExtractedMetadata` no longer includes ImageMagick encoder artifact comment lines under the `comment` key for any image format.

## Documentation

- Stale HEIC/HEIF description in `docs/platform/odc/forge-description.md` and `docs/platform/o11/forge-description.md` corrected — the row previously stated "original file returned with processingError note", but HEIC now transcodes to JPEG with a `transcodedFormat` key.
- Stale `TIFF EXIF` bullet removed from `docs/platform/odc/limitations.md`; added APNG, SVG, EPUB/ORA, and DPX/CIN known-gap bullets. Body kept under the 1000-char Forge limit (969 chars).
- Added HEIC transcode-to-JPEG paragraph and APNG behaviour paragraph to `docs/platform/odc/documentation.md`; added a "Known gaps in the current release" list at the end of NOTES.
- README updated: fixed broken link to the deleted `01-exif-metadata-stripping.md` (now points at `tasks.md`); added an APNG behaviour note next to the existing TIFF, HDR, and WBMP notes.
- `tasks.md` item 16 clarified to reflect that HEIC now actively transcodes to JPEG (not just graceful handling); added items 17 (format coverage expansion — completed), 18 (DPX/CIN `cin:*` stripping), and 19 (ORA mis-classification).
- Removed `untested-formats.md` (component root) — the file's "Implementation gaps" table listed DPX/CIN, SVG, EPUB, and ORA as unresolved bugs, all now fully implemented per this release, and its role has been fully superseded by `docs/format-coverage.md` (the single source of truth per HANDOFF). Two pieces of unique still-accurate content were folded into `docs/format-coverage.md` before deletion: a new **"How image stripping works"** section documenting the decode → `Strip()` → write pipeline and its three outcomes (round-trip / JPEG fallback / decode error); and a note on the WebP row recording the silent per-frame-metadata loss when the `libwebpmux` delegate is absent from Magick.NET-Q8.

## Removed

*(nothing yet)*

---