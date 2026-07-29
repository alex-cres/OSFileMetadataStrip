# OSFileMetadataStrip

**ODC External Library + O11 Integration Studio Extension**

Strips embedded metadata from uploaded files before they reach AI APIs or are stored — eliminating the file metadata injection attack vector at the preprocessing layer.

---

## Objective

File metadata containers (EXIF in images, IPTC/XMP in documents) can hold arbitrary text invisible to users, browsers, and image classifiers. An attacker can embed prompt-injection instructions in these fields using any standard image editor, without altering the file visually. If the file is forwarded to an AI model as part of a context message, that text is processed as trusted input.

**OSFileMetadataStrip** removes all embedded metadata from files before they reach any downstream consumer, neutralising the attack at the earliest possible point.

| Control Layer | Priority | Effort |
|---------------|----------|--------|
| Layer 1 — Preprocessing | 1 (highest) | Lowest — eliminates entire vector class |

---

## Supported File Types

Every format listed below has an explicit xUnit test in `FileMetadataStripping.Tests` (mirrored in `FileMetadataStripping.O11.Tests`). No format is claimed here that is not verified by a regression test.

| Category | Formats | Metadata Stripped |
|----------|---------|-------------------|
| Standard images | JPEG, PNG, GIF, TIFF, WebP | EXIF, IPTC, XMP, ICC profiles, comments |
| Animated / multi-frame | Animated GIF, Animated WebP, APNG (Animated PNG), MPO (Multi-Picture Object), MNG | Metadata stripped from every frame; all frames preserved in the output |
| AVIF | AVIF | Full read/write round-trip: EXIF, IPTC, XMP, ICC profiles, comments |
| HEIC / HEIF | HEIC, HEIF (mif1 / msf1 brands) | Metadata extracted; output transcoded to JPEG — the x265 HEVC encoder is GPL-licensed and cannot be bundled in a redistributable library. `ExtractedMetadata` includes a `transcodedFormat` key explaining the format change. |
| RAW camera formats | ARW (Sony), CR2 (Canon), DNG (Adobe), NEF (Nikon), ORF (Olympus), PEF (Pentax), RAF (Fuji), X3F (Sigma) | EXIF, XMP, ICC profiles |
| Modern / HDR | JPEG XL (JXL), JPEG 2000 (JP2 / J2C / J2K / JPT), JPEG XR (JXR / WDP), Ultra HDR (UHDR), OpenEXR (EXR), Radiance HDR (.hdr), QOI | EXIF, XMP, ICC profiles; Radiance HDR encoder-comment lines. JPT is decode-only — encoder unavailable in Magick.NET-Q8. |
| Legacy raster | PSD / PSB (Photoshop), TGA (Truevision), DDS (DirectDraw Surface), PCX / DCX (single- and multi-page Paintbrush), SGI, SUN Rasterfile, PICT, PCD / PCDS (Photo CD), FITS, JBIG, WMF (Windows Metafile), ICO (Windows Icon), XCF (GIMP), Netpbm (PBM / PGM / PPM / PNM) | EXIF, XMP, ICC profiles, and format-specific comments where present. GIMP XCF is transcoded to JPEG on write. |
| Film formats | DPX (SMPTE 268M), CIN (Kodak Cineon) | Standard image profiles plus per-image `dpx:*` and `cin:*` production attributes (film title, origination device, source filename, …) |
| Medical imaging | DICOM (.dcm) | Detected via 128-byte preamble + `DICM` signature; output transcoded to JPEG (pixel data preserved). DICOM data-dictionary tag parsing is out of scope. |
| SVG | SVG | `<title>`, `<desc>`, and `<metadata>` elements at every depth (including RDF / Dublin Core payloads); output remains a valid SVG |
| PDF | PDF, AI (Adobe Illustrator — PDF-based) | Title, Author, Subject, Keywords, Creator, Producer, XMP catalog metadata stream, and annotation Author fields (comment, sticky-note, markup annotations) |
| RTF | RTF (Rich Text Format) | `\author`, `\title`, `\subject`, `\keywords`, `\comment`, `\operator`, `\company`, `\doccomm`, `\category`, `\hlinkbase`, `\manager` control-word groups inside the `\info` group. Numeric control words (`\version`, `\vern`, `\nofpages`, revision timestamps, edit-minute counters) are preserved. |
| Office documents | DOCX, XLSX, PPTX | Core properties (Creator, LastModifiedBy, Created, Modified, Title, Subject, Description, Keywords, Category, ContentStatus, Revision, LastPrinted, Identifier, Version), application properties (Application, Company, Manager, AppVersion, Template, HyperlinkBase), custom property key/value pairs, and the embedded page-preview thumbnail (`docProps/thumbnail.*`). When `StripBodyAuthors = True`: also blanks author names from tracked changes, comments, and Excel 365 xl/persons entries. |
| Legacy binary Office | DOC, DOT, XLS, XLT, PPT, POT, PPS (Word / Excel / PowerPoint 97 – 2003) | `\x05SummaryInformation` stream (Title, Subject, Author, Keywords, Comments, Template, Last-Saved-By, Application) and `\x05DocumentSummaryInformation` stream (Category, Manager, Company, ContentStatus, Language, custom user-defined properties). The CFBF container is consolidated after deletion so the freed sectors are dropped from the output. |
| ODF documents | ODT, ODS, ODP | dc:creator, dc:title, dc:description, dc:subject, meta:initial-creator, meta:generator, meta:editing-cycles, meta:editing-duration, and all meta:user-defined properties |
| EPUB | EPUB | Dublin Core metadata in the OPF package (`dc:creator`, `dc:title`, `dc:description`, `dc:publisher`, `dc:rights`, `dc:subject`, …) and every OPF `<meta>` refinement |
| ORA (Open Raster) | ORA | `name` and `description` attributes on every element in `stack.xml` (image, stack, layer, mask, text). Structural attributes are preserved so the image still renders. |
| Audio | MP3, WAV, FLAC, OGG (Vorbis / Opus), M4A, M4B, WMA, AIFF / AIFC, APE (Monkey's Audio), WavPack (.wv), MPC (Musepack SV7 / SV8) | ID3 tags, Vorbis / Opus comments, RIFF INFO chunks, iTunes MP4 atoms, ASF header extension objects (title, artist, album, comment, …), AIFF ID3 chunks, APE tags |
| Video | MP4, MKV, AVI, MOV, WebM, WMV, M4V, 3GP, 3G2 | Metadata atoms/tags (title, comment, encoder, …) |
| Passthrough | BMP, DIB, WBMP, XBM, XPM, TXT, CSV, MD, JSON, XML, HTML, and any unrecognised format | Returned unchanged with `IsPassthrough = true` |

---

## Exposed Server Actions

| Action | Inputs | Output | Description |
|--------|--------|--------|-------------|
| `StripFileMetadata` | `RawFile : BinaryData`<br>`StripBodyAuthors : Boolean` | `FileMetadataResult` | Strips embedded metadata from any supported file. Set `StripBodyAuthors = True` to also blank author names from OOXML tracked changes and comments. Returns the clean file, extracted metadata, and a passthrough flag. |

### FileMetadataResult Structure

| Field | Type | Description |
|-------|------|-------------|
| `CleanFile` | `BinaryData` | The file with all metadata removed. Safe to forward to AI APIs or store. |
| `ExtractedMetadata` | `Text` | JSON object of all metadata entries found and removed. Keys vary by format: images use `exif`/`iptc`/`xmp` (DPX/CIN also add `dpx`/`cin` maps for the per-image production attributes); PDFs use `title`/`author`/`subject`/`keywords`/`creator`/`producer`/`annotationAuthors`; OOXML uses `creator`/`lastModifiedBy`/`revision`/`lastPrinted`/`identifier`/`version` and other core property keys, `appCompany`/`appManager`/`appVersion`/`appApplication`/`appTemplate`/`appHyperlinkBase` for application properties, `customProperties` for custom properties, `thumbnail` when an embedded page-preview thumbnail was removed, and `strippedAuthors` when `StripBodyAuthors = True`; legacy binary Office (DOC/XLS/PPT) nests removed values under `summaryInformation` (title, subject, author, keywords, comments, template, lastSavedBy, application), `documentSummaryInformation` (category, manager, company, contentStatus, language), and `customProperties`; ODF uses `creator`/`title`/`description`/`subject`/`initialCreator`/`generator`/`editingCycles`/`editingDuration`/`userDefinedProperties`; EPUB uses the same `dc:*` keys as ODF plus any OPF `<meta>` refinements; ORA records the removed `name`/`description` attributes per element; SVG records the removed `<title>`/`<desc>`/`<metadata>` text; RTF records the removed values under the control-word names (`author`/`title`/`subject`/`keywords`/`comment`/`operator`/`company`/`doccomm`/`category`/`hlinkbase`/`manager`), with repeated occurrences of the same control word preserved as a JSON array. Returns `[]` when no metadata was present. |
| `RemovedEntryCount` | `Integer` | Total number of metadata entries removed. Zero when the file had no embedded metadata. |
| `IsPassthrough` | `Boolean` | `True` when the file format has no supported metadata containers (e.g. BMP, TXT, CSV, MD, JSON) and was returned unchanged. Use this flag in audit logs to distinguish passthrough files from files that were actively processed and found clean. |

---

## How It Works

Detects the file type from its binary signature, then routes to a format-specific stripper:

| File type | Library | What's stripped |
|-----------|---------|----------------|
| Standard raster images (JPEG, PNG, GIF, TIFF, WebP) | [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache 2.0) | All metadata via `image.Strip()` — EXIF, IPTC, XMP, ICC profiles, comments |
| Animated / multi-frame (Animated GIF, Animated WebP, APNG, MPO, MNG) | [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache 2.0) | Per-frame `Strip()`; every frame preserved in the output |
| AVIF | [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache 2.0) | Full read/write round-trip; EXIF, IPTC, XMP, ICC profiles, comments |
| HEIC / HEIF (mif1 / msf1) | [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache 2.0) | Metadata stripped in memory; output transcoded to JPEG — HEVC re-encoding requires x265 (GPL), which cannot be bundled in a redistributable library |
| RAW camera (ARW, CR2, DNG, NEF, ORF, PEF, RAF, X3F) | [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache 2.0) | EXIF, XMP, ICC profiles |
| Modern / HDR (JPEG XL, JPEG 2000 JP2 / J2C / J2K / JPT, JPEG XR / WDP, Ultra HDR, OpenEXR, Radiance HDR, QOI) | [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache 2.0) | EXIF, XMP, ICC profiles; Radiance HDR encoder-comment lines. JPT is decode-only — encoder unavailable in Magick.NET-Q8. |
| Legacy raster (PSD/PSB, TGA, DDS, PCX/DCX, SGI, SUN, PICT, PCD/PCDS, FITS, JBIG, WMF, ICO, XCF (GIMP), Netpbm) | [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache 2.0) | `image.Strip()`; format-specific comments where present. GIMP XCF is transcoded to JPEG on write. |
| Film (DPX, CIN) | [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache 2.0) | `image.Strip()` plus explicit removal of any remaining `dpx:*` and `cin:*` per-image production attributes |
| Medical (DICOM .dcm) | [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache 2.0) | Detected via preamble + `DICM` signature; output transcoded to JPEG |
| SVG | System.Xml.Linq (BCL) | `<title>`, `<desc>`, and `<metadata>` elements matched by local name at every depth |
| Audio (MP3, WAV, FLAC, OGG Vorbis / Opus, M4A, M4B, WMA, AIFF / AIFC, APE, WavPack, MPC) | [TagLibSharp](https://github.com/mono/taglib-sharp) (LGPL 2.1) | ID3 tags, Vorbis / Opus comments, RIFF INFO chunks, iTunes MP4 atoms, ASF header extension objects, AIFF ID3 chunks, APE tags |
| Video (MP4, MKV, AVI, MOV, WebM, WMV, M4V, 3GP, 3G2) | [TagLibSharp](https://github.com/mono/taglib-sharp) (LGPL 2.1) | Metadata atoms/tags |
| PDF (PDF, Adobe Illustrator AI) | [PDFsharp](https://www.pdfsharp.net/) (MIT) | /Info dictionary fields (Title, Author, Subject, Keywords, Creator, Producer), XMP catalog metadata stream (/Metadata entry), and annotation /Author fields (comment, sticky-note, and markup annotations) |
| RTF | System.Text.RegularExpressions (BCL) | Text-scanner strip path. Detects the 6-byte `{\rtf1` prefix, then blanks the string-bearing control-word groups (`\author`, `\title`, `\subject`, `\keywords`, `\comment`, `\operator`, `\company`, `\doccomm`, `\category`, `\hlinkbase`, `\manager`) while preserving every other byte so the document still renders. |
| Office Open XML (DOCX, XLSX, PPTX) | [DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK) (MIT) + System.IO.Compression (BCL) | Core properties (Creator, LastModifiedBy, Created, Modified, Title, Subject, Description, Keywords, Category, ContentStatus, Revision, LastPrinted, Identifier, Version), application properties (Application, Company, Manager, AppVersion, Template, HyperlinkBase), custom property key/value pairs, and the embedded `docProps/thumbnail.*` page-preview along with its `_rels/.rels` relationship. When `StripBodyAuthors = True`: also blanks `w:author`/`w:initials` in DOCX, `<author>` elements in XLSX, `name`/`initials` in PPTX comment authors, and `displayName`/`userId` in xl/persons. |
| Legacy binary Office (DOC, DOT, XLS, XLT, PPT, POT, PPS) | [OpenMcdf](https://github.com/openmcdf/openmcdf) (MPL 2.0) + [OpenMcdf.Ole](https://github.com/openmcdf/openmcdf) (MPL 2.0) | Detected via the 8-byte CFBF magic `D0 CF 11 E0 A1 B1 1A E1`. Deletes the two OLE property-set streams `\x05SummaryInformation` and `\x05DocumentSummaryInformation`, then consolidates the container so freed sectors are dropped from the output. Known well-named properties (title, author, company, manager, custom properties, …) are captured in `ExtractedMetadata` under `summaryInformation` / `documentSummaryInformation` / `customProperties` before deletion. |
| ODF (ODT, ODS, ODP) | System.IO.Compression (BCL) | dc:creator, dc:title, dc:description, dc:subject, meta:initial-creator, meta:generator, meta:editing-cycles, meta:editing-duration, and all meta:user-defined properties in meta.xml |
| EPUB | System.IO.Compression (BCL) | Dublin Core metadata elements and OPF `<meta>` refinements inside the OPF package document referenced by `META-INF/container.xml`. Zip Slip guarded: OPF paths containing `..` segments are rejected. |
| ORA (Open Raster) | System.IO.Compression (BCL) | `name` and `description` attributes on every element in `stack.xml`. Structural attributes (`w`, `h`, `x`, `y`, `opacity`, `src`, `mask-src`, `composite-op`, `visibility`) are preserved. |
| Passthrough (BMP, DIB, WBMP, XBM, XPM, TXT, CSV, MD, JSON, XML, HTML) | — | `IsPassthrough = true`, file returned unchanged |

> **Note:** If a PDF or OOXML file is encrypted, password-protected, or corrupted and cannot be opened, the original file is returned unchanged (`IsPassthrough = false`, `RemovedEntryCount = 0`) and `ExtractedMetadata` contains a `processingError` key. No exception is raised.

> **HEIC / HEIF:** HEIC and HEIF files are detected via an ISOBMFF `ftyp` brand check (bytes 8–11) and routed to the image pipeline. Magick.NET decodes them and strips all embedded metadata. Re-encoding as HEIC requires the x265 HEVC encoder, which is GPL-licensed and cannot be bundled in any redistributable NuGet package (confirmed absent on both Windows and ODC Amazon Linux 2023). The clean stripped image is therefore **transcoded to JPEG**. `ExtractedMetadata` includes a `transcodedFormat` key explaining the format change and its cause. If you need to preserve the HEIC format, you must supply an x265 build under a commercial license and integrate it with libheif outside of this component.

> **AVIF:** AVIF files are detected via the same ISOBMFF `ftyp` brand check and fully supported: EXIF, IPTC, XMP, and comments are stripped and the clean file is returned.

> **HDR (Radiance RGBE):** Radiance HDR files are processed through the image pipeline. In addition to `image.Strip()`, encoder-inserted comment lines (e.g. `# Created by ImageMagick`) are removed from the output by `StripHdrCommentLines()`. Only the mandatory `#?RADIANCE` identification line is preserved. `ExtractedMetadata` will not include encoder-inserted artifacts under the `comment` key.

> **WBMP (Wireless Bitmap):** WBMP has no reliable start-of-file magic bytes and is returned as passthrough (`IsPassthrough = true`, file returned unchanged).

> **TIFF and EXIF:** TIFF stores EXIF as native IFD tags (not as an APP1 segment like JPEG). `image.Strip()` removes all metadata regardless of how it is embedded. For `ExtractedMetadata` reporting, EXIF is read via `GetExifProfile()`, which works for real-world camera TIFFs. If a TIFF was produced by a tool that dropped the EXIF during encoding (including Magick.NET itself), `RemovedEntryCount` will reflect only the metadata that was actually present — XMP and Comment fields are always captured when present.

> **APNG (Animated PNG):** APNG is detected by the `acTL` chunk and passed to `MagickImageCollection` with the `APng` format hint so every frame is decoded and stripped. Writing APNG requires ImageMagick's `video` delegate (ffmpeg). When ffmpeg is present the output is APNG; when it is absent the clean output is transcoded to JPEG and `ExtractedMetadata` includes a `transcodedFormat` key.

```
Upload → StripFileMetadata → Clean BinaryData + ExtractedMetadata → AI API / Storage
```

---

## Requirements

| | ODC | O11 |
|-|-----|-----|
| Platform | OutSystems Developer Cloud | OutSystems 11 |
| Runtime | Linux container (ODC Portal) | Windows (.NET Framework 4.8) |
| .NET | 10.0 LTS | Framework 4.8 |

### NuGet Packages

| Package | ODC | O11 | Notes |
|---------|-----|-----|-------|
| `OutSystems.ExternalLibraries.SDK` | 1.5.0 | — | ODC-only; O11 uses Integration Studio DLLs |
| `PDFsharp` | 6.2.4 | 1.50.5147 | 6.x targets net6+ only; 1.50 is the last net48-compatible release |
| `System.IO.Packaging` | — | 8.0.1 | Built into net10 BCL; explicit NuGet required on net48 |
| `System.Text.Json` | — | 8.0.5 | Built into net10 BCL; explicit NuGet required on net48 |
| `Magick.NET-Q8-AnyCPU` | 14.15.0 | 14.15.0 | Identical — netstandard2.0 |
| `DocumentFormat.OpenXml` | 3.5.1 | 3.5.1 | Identical — netstandard2.0 |
| `TagLibSharp` | 2.3.0 | 2.3.0 | Identical — netstandard2.0 |
| `OpenMcdf` | 3.1.4 | 3.1.4 | Identical — netstandard2.0. Legacy binary Office (CFBF) container support. |
| `OpenMcdf.Ole` | 3.1.4-experimental.1 | 3.1.4-experimental.1 | Identical — netstandard2.0. OLE property-set audit inside CFBF. Upstream API is marked "experimental"; version pinned to lock behaviour. |

All packages are Apache 2.0, MIT, LGPL 2.1, or MPL 2.0.

---

## Using in ODC

1. Download the latest ZIP from [Releases](./docs/versions/) or the OutSystems Forge.
2. In **ODC Portal** → **External Logic** → **Upload** the ZIP.
3. Create and publish an External Library.
4. In your ODC app, add the library as a dependency and call `StripFileMetadata` in your Server Action before forwarding the file.

## Using in O11

1. Locate the XIF in the repo at `xif/FileMetadataStripping.xif`.
2. Open **Integration Studio** → **File → Open** → select the XIF.
3. IS runs **Update Source Code** automatically, populating `Records.cs` with `RCFileMetadataResultRecord`.
4. Close Integration Studio.
5. Open `xif/FileMetadataStripping/Source/NET/FileMetadataStripping.sln` in **Visual Studio**.
6. Restore NuGet packages (`dotnet restore` or VS Package Manager → Restore).
7. Build the solution.
8. Re-open Integration Studio → **1-Click Publish** to Service Center.

---

## Development

See the [tasks list](./tasks.md) for open items and completed work.

### First-Time Setup

After cloning, activate the pre-commit hooks (fixes NuGet versions that Integration Studio reverts on every save):

```sh
git config core.hooksPath .githooks
```

### Build & Publish (ODC)

Magick.NET includes native linux-x64 binaries, so the runtime identifier is required:

```powershell
.\FileMetadataStripping\generate_upload_package.ps1
```

This publishes for `linux-x64`, zips the output to `ExternalLibrary.zip`, and verifies the file is under the 90 MB ODC Portal limit.

### Build (O11)

```powershell
cd FileMetadataStripping.O11
dotnet build -c Release
```

---

## Changelog

See [CHANGELOG.md](./CHANGELOG.md) for the full version history.

---

## Third-Party Notices

See [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md) for the full list of open-source dependencies and their licenses.
