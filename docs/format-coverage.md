# Format Coverage Checklist

Compares the formats listed on the [ImageMagick supported-formats page](https://imagemagick.org/formats/) against the OSFileMetadataStrip library. For each format:

- **Detected** — does `DetectCategory()` in `FileMetadataStripping.cs` route the format to a strip pipeline?
- **Tested** — is there an explicit xUnit test in `FileMetadataStripping.Tests` / `FileMetadataStripping.O11.Tests` that exercises the format?
- **Handled by Magick.NET-Q8-AnyCPU 14.15** — is the required decoder / encoder shipped in the NuGet package?

Legend:

| Symbol | Meaning |
|:------:|---------|
| ✅ | Detected, tested, and verified working (metadata stripped OR gracefully returned with `processingError`) |
| ⚠️ | Detected and routed to Magick.NET; not explicitly tested but should work via the image pipeline |
| 🔶 | Detected but the required delegate is not bundled in Magick.NET-Q8 (falls back to `processingError` note) |
| ❌ | Not detected — falls through to `Passthrough` (file returned unchanged, `IsPassthrough = true`) |
| ⛔ | Deliberately excluded (write-only format, pseudo-format, built-in pattern, or embedded profile — cannot be uploaded as a file) |

Non-image formats (PDF, ODF, EPUB, ORA, OOXML, audio, video) are covered in a separate section at the end.

---

## How image stripping works

Every format routed to the image pipeline flows through the same three steps:

```
MagickImageCollection(rawFile)   // decode
  → Strip() per frame            // remove EXIF / IPTC / XMP / ICC / comments
  → images.Write(output)         // write back in original format
```

The outcome depends on whether the format's write delegate is bundled in Magick.NET-Q8:

| Outcome | When | Result |
|---------|------|--------|
| **Round-trip** | Format is RW and its write delegate is bundled | Output is the same format, metadata stripped |
| **JPEG fallback** | Write throws `MagickMissingDelegateErrorException` (e.g. HEIC — HEVC encoder is GPL) | Output is JPEG; `ExtractedMetadata` gains a `transcodedFormat` key |
| **Decode error** | Decode throws `MagickException` (e.g. Windows-only delegate on ODC Linux) | Original returned unchanged; `ExtractedMetadata` gains a `processingError` key |

Dedicated (non-Magick.NET) strip paths — SVG, EPUB, ORA, CFBF, ODF, OOXML, PDF — bypass this pipeline entirely and are documented in the [Non-image formats](#non-image-formats-routed-to-dedicated-pipelines) section.

---

## 🔴 Urgent priorities (next round of work)

The following formats fall through to passthrough today, leaking metadata that is otherwise stripped for their modern counterparts. All of them are one click away from any LibreOffice / Microsoft Office user's **Save As** dialog. Details, magic bytes, and implementation notes are in the [Gaps in `DetectCategory`](#gaps-in-detectcategory--formats-that-fall-to-passthrough-today) section below.

| Priority | Format group | Extensions | Why urgent |
|:--------:|--------------|-----------|------------|
| 🔴 P0 | **RTF** | `.rtf` | Trivial text-scanner fix, no NuGet needed. `\author`, `\company`, `\operator` control words leak on every RTF export. |
| 🟡 P2 | **Flat ODF + Word 2003 XML** | `.fodt`, `.fods`, `.fodp`, `.xml` (Word 2003) | Single-file XML variants of ODF/DOCX; miss our current ZIP-mimetype detector. |
| 🟡 P3 | **OOXML template / macro-enabled variants** | `.dotx`, `.dotm`, `.xltx`, `.xltm`, `.potx`, `.potm`, `.ppsx`, `.ppsm`, `.pptm`, `.xlsm`, `.docm` | Almost certainly already handled by the existing OOXML strip path, but not verified by tests. |
| 🟡 P3 | **ODF template / drawing variants** | `.ott`, `.ots`, `.otp`, `.odg`, `.otg`, `.odc`, `.odf`, `.odb`, `.odi` | Same — `IsOdfFormat` already matches these mimetypes; untested. |

> **CFBF (legacy binary Office)** — moved out of the urgent tier. Covered by `StripCfbfMetadata` (OpenMcdf + OpenMcdf.Ole); all seven extensions (`.doc`, `.dot`, `.xls`, `.xlt`, `.ppt`, `.pot`, `.pps`) share the same code path.

> **Note on HTML / HTM** — intentionally excluded from this list. HTML is a content format where `<title>` and most `<meta>` tags are integral to the document (charset, viewport, http-equiv are required for rendering). Selective meta-name stripping is fragile, and stripping metadata but leaving `<script>` / event handlers gives a false sense of security — real HTML hardening is the job of a dedicated HTML sanitiser (`HtmlSanitizer`, DOMPurify, Bleach), not a metadata stripper. HTML therefore stays in the passthrough tier alongside TXT / CSV / MD. See the [Low tier](#-low--narrow-metadata-surface-minimal-risk) below for details.

---


## Bitmap raster images

| Tag | Mode | Description | Status | Notes |
|-----|:----:|-------------|:------:|-------|
| JPEG | RW | Joint Photographic Experts Group JFIF | ✅ | `ImageTests.cs` — full EXIF/IPTC/XMP round-trip |
| PNG | RW | Portable Network Graphics | ✅ | `ImageTests.cs` |
| PNG8 / PNG24 / PNG32 / PNG48 / PNG64 / PNG00 | RW | Depth / colour-type variants of PNG | ✅ | `PngSubformatTests.cs` — 5 tests per subformat (detection, non-empty, PNG signature preserved, EXIF stripped, extracted metadata captured). Q8 build downsamples the 16-bit-per-channel subformats (PNG48, PNG64) to 8-bit samples but writes a valid PNG. |
| GIF | RW | CompuServe Graphics Interchange | ✅ | `ImageTests.cs` — static + animated |
| APNG | RW | Animated PNG | ✅ | `ApngImageTests.cs` — `acTL` detection + JPEG transcode fallback when the ffmpeg video delegate is absent |
| BMP / BMP2 / BMP3 | RW | Microsoft Windows bitmap | ✅ | `PassthroughTests.cs` — BMP has no metadata containers, routed to passthrough |
| DIB | RW | Windows Device Independent Bitmap | ✅ | `DibImageTests.cs` — BMP without the 14-byte file header. Detected by `IsDibFile()` via a BITMAPINFOHEADER heuristic (header size in {40, 52, 56, 108, 124} + planes=1 + valid bit depth) and routed to **passthrough** alongside BMP. DIB carries no metadata containers, so a re-encode round-trip would be a pure no-op — passthrough returns the input bytes verbatim. |
| TIFF | RW | Tagged image file format | ✅ | `ImageTests.cs` — multi-frame TIFF, XMP, IPTC, EXIF |
| WebP | RW | Google Weppy | ✅ | `ImageTests.cs` + `AnimatedWebPTests.cs`. **Known gap:** animated WebP requires the `libwebpmux` delegate to iterate all frames. Without it, only the first frame is decoded and the output is a static image — per-frame metadata chunks in the trailing frames survive silently. Also captured in `docs/platform/odc/limitations.md`. |
| AVIF | RW | AV1 keyframe image | ✅ | `AppleImageTests.cs` — full round-trip |
| HEIC | RW | Apple HEIC | ✅ | `AppleImageTests.cs` — decode + JPEG transcode (HEVC encoder is GPL-licensed and cannot be bundled) |
| HEIF (mif1 / msf1) | RW | HEIF base variants | ✅ | `HeifMif1ImageTests.cs` |
| JXL | RW | JPEG XL | ✅ | `JxlImageTests.cs` |
| JXR / WDP | RW | JPEG Extended Range | ✅ | `JxrWdpImageTests.cs` |
| UHDR | RW | Ultra HDR | ✅ | `UhdrImageTests.cs` |
| JP2 | RW | JPEG-2000 JP2 | ✅ | `Jp2ImageTests.cs` |
| J2C / J2K | RW | JPEG-2000 raw code streams | ✅ | `Jp2CodeStreamTests.cs` — 5 tests per variant (detection, non-empty, JPEG-2000 family output, decodability, no EXIF in output). Magick.NET-Q8 decodes the raw code stream but re-encodes to the JP2 file-format wrapper on write — the security invariant is preserved regardless of the output byte pattern. |
| JPT | RW | JPEG-2000 code stream (interactive) | ⚠️ | `Jp2CodeStreamTests.cs` — 2 graceful-failure tests using synthetic SOC bytes. Magick.NET-Q8's OpenJPEG build does **not** compile in the JPT encoder (`WriteJP2Image` throws `MagickDelegateErrorException`), so a real round-trip test cannot be produced. |
| PSD | RW | Adobe Photoshop | ✅ | `PsdImageTests.cs` |
| PSB | RW | Adobe Large Document Format | ✅ | `PsbImageTests.cs` — 6 tests (detection, non-empty, `8BPS` prefix preserved, decodability, dimension preservation, EXIF stripped). Magick.NET writes PSD (version 1) for small canvases even when `MagickFormat.Psb` is requested — the strip pipeline still routes correctly; only the version byte at offset 5 may differ. |
| TGA / ICB / VDA / VST | RW | Truevision Targa | ✅ | `TgaImageTests.cs` — v2 footer + v1 heuristic detection |
| ICO / CUR | R | Microsoft Icon / Cursor | ✅ | `IcoImageTests.cs` (CUR uses the same decoder) |
| DDS | RW | DirectDraw Surface | ✅ | `DdsImageTests.cs` |
| DPX | RW | SMPTE Digital Moving Picture Exchange | ✅ | `DpxCinImageTests.cs` — `dpx:*` production attributes stripped |
| CIN | RW | Kodak Cineon | ✅ | `DpxCinImageTests.cs` — `cin:*` production attributes stripped |
| EXR | RW | OpenEXR (Industrial Light & Magic HDR) | ✅ | `ExrImageTests.cs` |
| HDR / RGBE / RAD | RW | Radiance HDR | ✅ | `HdrImageTests.cs` — encoder-comment stripping |
| FL32 | RW | FilmLight floating-point | ⚠️ | Untested; Magick.NET decodes via the same OpenEXR-family path |
| QOI | RW | Quite OK Image | ✅ | `QoiImageTests.cs` |
| FITS | RW | Flexible Image Transport System (astronomy) | ✅ | `FitsImageTests.cs` |
| JBIG | RW | Joint Bi-level Image experts Group | ✅ | `JbigImageTests.cs` |
| MNG | RW | Multiple-image Network Graphics | ✅ | `MngImageTests.cs` |
| JNG | RW | JPEG in PNG wrapper | ⚠️ | Untested; MNG-family decoder |
| MPO | R | Multi-Picture Object | ✅ | `MpoImageTests.cs` |
| PCX | RW | ZSoft IBM PC Paintbrush | ✅ | `PcxImageTests.cs` |
| DCX | RW | ZSoft multi-page Paintbrush | ✅ | `DcxImageTests.cs` — 6 tests: detection, non-empty, 4-byte DCX magic (`B1 68 DE 3A`) preserved, decodability, all frames preserved in a multi-page round-trip, corrupt-input graceful failure. |
| SGI | RW | Irix RGB | ✅ | `SgiImageTests.cs` |
| SUN | RW | SUN Rasterfile | ✅ | `SunImageTests.cs` |
| PICT | RW | Apple QuickDraw/PICT | ✅ | `PictImageTests.cs` |
| PCD / PCDS | RW | Kodak Photo CD | ✅ | `PcdImageTests.cs` |
| WMF | R | Windows Metafile | ✅ | `WmfImageTests.cs` |
| EMF | R | Enhanced Metafile | 🔶 | Windows-only decoder; not bundled in Magick.NET-Q8 |
| XCF | R | GIMP native | ✅ | `XcfImageTests.cs` — transcoded to JPEG on write |
| XBM | RW | X Windows monochrome bitmap | ✅ | `XbmImageTests.cs` — treated as passthrough (metadata-free format) |
| XPM | RW | X Windows pixmap | ✅ | `XpmImageTests.cs` — treated as passthrough (metadata-free format) |
| WBMP | RW | Wireless Bitmap | ✅ | `WbmpImageTests.cs` — treated as passthrough (metadata-free format) |
| DCM | R | DICOM medical imaging | ✅ | `DcmImageTests.cs` — 128-byte preamble + DICM detection, transcoded to JPEG. Note: DICOM PHI tag parsing (PatientName, etc.) is out of scope. |
| PBM | RW | Netpbm portable bitmap | ✅ | `NetpbmImageTests.cs` |
| PGM | RW | Netpbm portable graymap | ✅ | `NetpbmImageTests.cs` |
| PPM | RW | Netpbm portable pixmap | ✅ | `NetpbmImageTests.cs` |
| PNM | RW | Netpbm portable anymap | ✅ | `NetpbmImageTests.cs` |
| P7 | RW | Xv Visual Schnauzer thumbnail | ⚠️ | Untested; same Netpbm decoder |
| PAM | W | Portable arbitrary map | ⚠️ | Write-only format; untested but supported by the encoder |
| PFM | RW | Portable float map | ⚠️ | Untested; part of the Netpbm family |
| PHM | RW | Portable half-precision float map | ⚠️ | Untested |
| MONO | RW | Bi-level raw bitmap | ⚠️ | Untested; requires `-size` to interpret dimensions |
| PALM | RW | Palm pixmap | ⚠️ | Untested |
| PDB | RW | Palm Database ImageViewer | ⚠️ | Untested |
| FARBFELD | RW | Farbfeld lossless | ⚠️ | Untested |
| FLIF | RW | Free Lossless Image Format | 🔶 | Requires libflif — not bundled in Magick.NET-Q8 |
| BPG | RW | Better Portable Graphics | 🔶 | Requires libbpg — not bundled |
| AAI | RW | AAI Dune image | ⚠️ | Untested; obscure |
| SF3 | R | Simple File Format Family | ⚠️ | Untested |
| PIX | R | Alias/Wavefront RLE | ⚠️ | Untested |
| RLA | R | Alias/Wavefront image | ⚠️ | Untested |
| RLE | R | Utah run-length encoded | ⚠️ | Untested |
| RGF | RW | LEGO Mindstorms EV3 Robot Graphics | ⚠️ | Untested |
| VIFF | RW | Khoros Visualization Image File Format | ⚠️ | Untested |
| VICAR | RW | VICAR raster (NASA) | ⚠️ | Untested |
| MTV | RW | MTV Raytracing | ⚠️ | Untested |
| HRZ | RW | Slow Scan TeleVision | ⚠️ | Untested |
| OTB | RW | On-the-air Bitmap | ⚠️ | Untested |
| ART | RW | PFS 1st Publisher | ⚠️ | Untested |
| MAT | R | MATLAB image | 🔶 | Requires libmat — not bundled |
| PES | R | Embrid Embroidery | ⚠️ | Untested |

## Camera RAW formats

| Tag | Mode | Description | Status | Notes |
|-----|:----:|-------------|:------:|-------|
| DNG | R | Adobe Digital Negative | ✅ | `DngImageTests.cs` — synthetic TIFF-based test |
| CR2 | R | Canon Raw v2 | ✅ | `Cr2ImageTests.cs` |
| CRW | R | Canon Raw (older) | ⚠️ | Untested; same dcraw path |
| CR3 | R | Canon Raw v3 | 🔶 | ISOBMFF-based with `crx ` brand — routed to Media by our detector because `crx ` is not in `IsHeifOrAvifBrand`; TagLibSharp cannot decode Canon RAW → `processingError`. See "Untested detection routes worth reviewing" below. |
| NEF | R | Nikon Raw | ✅ | `NefImageTests.cs` |
| ARW | R | Sony Alpha Raw | ✅ | `ArwImageTests.cs` |
| ORF | R | Olympus Raw | ✅ | `OrfImageTests.cs` |
| PEF | R | Pentax Electronic File | ✅ | `PefImageTests.cs` |
| RAF | R | Fuji CCD-RAW | ✅ | `RafImageTests.cs` |
| X3F | R | Sigma Camera Raw | ✅ | `X3fImageTests.cs` |
| MRW | R | Konica Minolta Raw | ⚠️ | Untested; dcraw-based |
| DCR | R | Kodak Digital Camera Raw | 🔶 | Requires the external DCRAW delegate program — not bundled |
| RAW | R | Generic Raw | ⚠️ | Untested; requires explicit `-size` |
| BAYER | RW | Raw mosaiced samples | ⚠️ | Untested |

## Vector / document / composite formats

| Tag | Mode | Description | Status | Notes |
|-----|:----:|-------------|:------:|-------|
| SVG | RW | Scalable Vector Graphics | ✅ | `SvgImageTests.cs` — dedicated XML-aware strip path, intercepted **before** Magick.NET |
| MVG | RW | Magick Vector Graphics | ⚠️ | Untested; internal ImageMagick format |
| MSVG | RW | Internal SVG renderer | ✅ | `MsvgImageTests.cs` — 4 tests. MSVG output is SVG XML, so `IsSvgFile()` catches it and routes it through the same XML-aware strip path as any other SVG source (Magick.NET is never invoked). |
| PDF | RW | Portable Document Format | ✅ | `PdfTests.cs` — /Info + XMP catalog + annotation Author |
| AI | RW | Adobe Illustrator | ✅ | `AiImageTests.cs` — routed via `%PDF` magic bytes |
| EPDF | RW | Encapsulated PDF | ⚠️ | Untested; PDF-family |
| EPS / EPS2 / EPS3 / EPSF / EPSI / EPT / EPI | RW | Encapsulated PostScript variants | 🔶 | Requires Ghostscript — not bundled |
| PS / PS2 / PS3 | RW | Adobe PostScript | 🔶 | Requires Ghostscript — not bundled |
| DJVU | R | DjVu document format | 🔶 | Requires libdjvu — not bundled |
| WPG | R | WordPerfect Graphics | 🔶 | Requires libwpg — not bundled |

## Formats requiring external programs (not usable from a redistributable library)

| Tag | Mode | Description | Status | Notes |
|-----|:----:|-------------|:------:|-------|
| MPEG / M2V | RW | Motion Picture Experts Group | 🔶 | Requires ffmpeg — not bundled |
| VIDEO | RW | Video formats | 🔶 | Requires ffmpeg — not bundled |
| HPGL | R | HP-GL plotter language | 🔶 | Requires hp2xx — not bundled |
| GPLT | R | Gnuplot plot files | 🔶 | Requires gnuplot — not bundled |
| MAN | R | Linux man pages | 🔶 | Requires groff + Ghostscript — not bundled |
| HTML | RW | HTML with image map | 🔶 | Requires html2ps — not bundled |
| SID / MrSID | R | Multi-resolution seamless image | 🔶 | Requires mrsidgeodecode — not bundled |
| FPX | RW | FlashPix Format | 🔶 | Requires FlashPix SDK — not bundled |
| DOT | R | Graphviz | 🔶 | Requires libgvc — not bundled |
| CUBE | R | Colour lookup table | ⚠️ | Untested |
| DMR | RW | Digital media repository | 🔶 | Requires MagickCache — not bundled |

## Font files (opening a font file returns a preview image)

| Tag | Mode | Description | Status | Notes |
|-----|:----:|-------------|:------:|-------|
| TTF | R | TrueType font | ⚠️ | Untested; would be routed to image path if Magick.NET recognises it, else passthrough |
| PFA | R | PostScript Type 1 (ASCII) | ⚠️ | Untested |
| PFB | R | PostScript Type 1 (binary) | ⚠️ | Untested |

## Formats intentionally not supported (write-only, pseudo, or non-file)

| Tag | Mode | Reason |
|-----|:----:|--------|
| BRF, UBRL, UBRL6, ISOBRL, ISOBRL6 | W | Braille — write-only, cannot appear as an upload |
| CIP | W | Cisco IP phone — write-only |
| PCL | W | HP Page Control Language — write-only |
| CLIP, CLIPBOARD, WIN, X, XC, HALD, HISTOGRAM, INFO, JSON, YAML, KERNEL, LABEL, NULL, PANGO, PLASMA, PREVIEW, PRINT, RADIAL_GRADIENT, SCAN, SCANX, SCREENSHOT, SPARSE-COLOR, STEGANO, STRIMG, TEXT, TILE, UNIQUE, VID, MAP, MASK, MATTE, MPC, MPR, DEBUG, FTXT, MSL, POCKETMOD, SHTML, PTIF, ASHLAR, CANVAS, CAPTION, FRACTAL, GRADIENT | R/W | Pseudo-formats, canvas generators, or IPC targets — never uploaded as user files |
| 8BIM, 8BIMTEXT, APP1, APP1JPEG, ICC, IPTC, IPTCTEXT | RW | Embedded profiles — accessed through profile tags on parent images, never uploaded standalone |
| GRANITE, LOGO, NETSCAPE, ROSE, WIZARD | R | Built-in images |
| BRICKS, CHECKERBOARD, CIRCLES, CROSSHATCH*, FISHSCALES, GRAY0..GRAY100, HEXAGONS, HORIZONTAL*, HS_*, LEFT*, LEFTSHINGLE, OCTAGONS, RIGHT*, RIGHTSHINGLE, SMALLFISHSCALES, VERTICAL* | R | Built-in patterns |

## Non-image formats (routed to dedicated pipelines)

| Container | Formats | Status | Notes |
|-----------|---------|:------:|-------|
| PDF | PDF, AI | ✅ | `PdfTests.cs`, `AiImageTests.cs` |
| Office Open XML | DOCX, XLSX, PPTX | ✅ | `OpenXmlTests.cs` — core / app / custom properties + thumbnail |
| Legacy binary Office (CFBF) | DOC, DOT, XLS, XLT, PPT, POT, PPS | ✅ | `LegacyOfficeTests.cs` — 29 tests. OpenMcdf + OpenMcdf.Ole. Deletes `\x05SummaryInformation` and `\x05DocumentSummaryInformation`; container consolidated so freed sectors are dropped from the output. |
| ODF | ODT, ODS, ODP | ✅ | `OdfTests.cs` |
| EPUB | EPUB | ✅ | `EpubTests.cs` — Dublin Core + Zip Slip guard |
| ORA | ORA | ✅ | `OraTests.cs` — `stack.xml` `name`/`description` |
| Audio | MP3, WAV, FLAC, OGG Vorbis, OGG Opus, M4A, M4B, WMA | ✅ | `AudioVideoTests.cs` |
| Video | MP4, MKV, AVI, MOV, WebM, WMV, M4V, 3GP, 3G2 | ✅ | `AudioVideoTests.cs` |

## Untested detection routes worth reviewing

The following code branches route input to a strip pipeline but are not yet exercised by a dedicated test. They fall into two categories: **decoder-shared with a tested format** (probably fine) and **codec-specific** (worth adding).

### Probably fine — shared decoder with a tested sibling

| Format | Shares decoder with | Recommendation |
|--------|--------------------|----------------|
| CUR | ICO | Skip — same Windows icon parser |
| CRW | dcraw path | Skip — same as CR2 |
| BMP2 / BMP3 | BMP | Skip — BMP version variants |
| PFM / PHM / P7 / PAM | PBM/PGM/PPM | Skip — Netpbm family |
| MVG | SVG | Skip — vector family |
| EPDF | PDF | Skip — PDF family |

### Codec-specific and untested — candidates for future coverage

| Format | Why worth testing |
|--------|-------------------|
| JNG | JPEG-in-PNG wrapper; distinct MNG-family parser |
| MRW | Konica Minolta RAW — dcraw path but different container |
| FARBFELD | Modern lossless format sometimes used in ML pipelines |
| CR3 (`crx ` brand) | Currently mis-routed to Media because `IsHeifOrAvifBrand()` doesn't include the `crx ` brand. TagLib fails → `processingError`. Adding CR3 to `IsHeifOrAvifBrand` would route it to Magick.NET, but Magick.NET-Q8 doesn't decode CR3 either → still `processingError`. Low priority. |
| Font files (TTF, PFA, PFB) | Magick.NET renders them as preview images; a user might upload a font as bait. Adding a passthrough test would harden the intent. |

## Gaps in `DetectCategory` — formats that fall to passthrough today

These are formats the library does **not** currently detect. If a user uploads them, they are returned unchanged with `IsPassthrough = true` and `RemovedEntryCount = 0`. They may or may not carry metadata; if they do, the metadata leaks through untouched.

### 🔴 Critical — leaks widely-used PII fields, common uploads

These formats every LibreOffice / Microsoft Office user can produce with a single click from the **Save As** dialog. They carry the same PII surface as their modern counterparts (creator, company, manager, last-saved-by, revision, template, etc.) and are the most commonly forwarded document types to AI APIs.

| Format | Extensions | Magic | Metadata surface | Status |
|--------|-----------|-------|------------------|--------|
| **Legacy binary Word** (Word 97 – 2003) | `.doc`, `.dot` (template) | `D0 CF 11 E0 A1 B1 1A E1` (CFBF / OLE Compound) | `SummaryInformation` stream (Title, Subject, Author, Keywords, Comments, Template, Last-Saved-By, Revision, Application, dates) + `DocumentSummaryInformation` stream (Category, Manager, Company, HeadingPairs, ContentStatus, Language) + Custom properties | ✅ Stripped via `StripCfbfMetadata` (OpenMcdf + OpenMcdf.Ole). Both OLE property-set streams are wiped and the CFBF is consolidated so freed sectors are dropped from the output. |
| **Legacy binary Excel** (Excel 97 – 2003) | `.xls`, `.xlt` (template) | Same CFBF magic | Same two OLE streams — book creator, company, last-modified, template path | ✅ Same code path — one detection helper + one strip method covers all seven CFBF-based extensions. |
| **Legacy binary PowerPoint** (PowerPoint 97 – 2003) | `.ppt`, `.pot` (template), `.pps` (slideshow) | Same CFBF magic | Same two OLE streams | ✅ Same code path. |
| **RTF (Rich Text Format)** | `.rtf` | `{\rtf1` (5 ASCII bytes) | `\author`, `\title`, `\subject`, `\keywords`, `\comment`, `\operator`, `\company`, `\doccomm`, `\version`, `\vern` control words | Add `IsRtfFile()` detection + implement `StripRtfMetadata` using a text-based scanner (regex replaces `\author X;` etc. with empty values). No new NuGet needed. |

### 🟠 High — leaks metadata, less common upload path

| Format | Extensions | Magic | Metadata surface | Recommendation |
|--------|-----------|-------|------------------|----------------|
| **Flat ODF** (single-file XML variants) | `.fodt`, `.fods`, `.fodp` | `<?xml` + `<office:document` (not a ZIP) | Same `office:meta` block as the ZIP-based ODF — dc:creator, dc:title, meta:initial-creator, meta:editing-cycles, etc. | Extend the SVG XML detection into a shared `IsXmlFile` router that also catches Flat ODF, then reuse `ExtractAndClearOdfMetadata` on the parsed XDocument. |
| **Word 2003 XML** | `.xml` (Word 2003 XML format) | `<?xml` + `<w:wordDocument` in the first 4 KB | `<o:DocumentProperties>` node — Author, LastAuthor, Manager, Company, Template, Revision, Version, LastPrinted | Same XML router. Route to a new `StripWordMlMetadata` helper. |
| **DocBook XML** | `.xml` | `<?xml` + `<book`, `<article`, or `<chapter` | `<info>` block — title, author, publisher, pubdate | Same XML router; low priority because DocBook is niche. |
| **Unified Office Format** | `.uot`, `.uos`, `.uop` | ZIP with mimetype `application/vnd.uoml+xml` (or similar) | Same OpenDocument-style meta block inside the ZIP | Add UOF mimetype to `DetectZipCategory` and reuse the ODF strip path (structurally identical). Very low install base — verify before prioritising. |

### 🟡 Medium — untested code paths that already exist

These variants should already be handled correctly by the current OOXML / ODF strip paths (all are ZIP-based with the same internal structure), but they are not explicitly exercised by a test. If any of them behaves differently in the wild, we'd only find out when a user reports a leak.

| Format group | Extensions | Current code path | Recommendation |
|--------------|-----------|-------------------|----------------|
| **OOXML templates and macro-enabled variants** | `.dotx`, `.dotm`, `.xltx`, `.xltm`, `.potx`, `.potm`, `.ppsx`, `.ppsm`, `.pptm`, `.xlsm`, `.docm` | ZIP → `DetectZipCategory` → default OpenXml → same core / app / custom properties + thumbnail strip | Add one test per extension in `OpenXmlTests.cs` — swap the extension into an existing DOCX fixture, run through `_sut`, verify the property is cleared. |
| **ODF templates and drawings** | `.ott`, `.ots`, `.otp`, `.odg`, `.otg`, `.odc`, `.odf`, `.odb`, `.odi` | ZIP → `IsOdfFormat` matches `application/vnd.oasis.opendocument.*` prefix → `StripOdfMetadata` (which reads `meta.xml`) | Add one test per extension in `OdfTests.cs` — construct a synthetic ODF ZIP with the target mimetype, verify strip. |

### 🟢 Low — narrow metadata surface, minimal risk

| Format | Extensions | Magic | Metadata risk | Recommendation |
|--------|-----------|-------|---------------|----------------|
| **HTML / HTM** | `.html`, `.htm` | `<!DOCTYPE html`, `<html`, `<head` | `<meta name="author">`, `<meta name="generator">`, `<title>`, and similar tags. Metadata risk exists but is fundamentally different from OOXML / PDF / EXIF: HTML meta tags are inline document content, not a hidden carrier, and stripping them selectively is fragile (charset, viewport, http-equiv, and refresh tags are required for rendering). | **Keep as passthrough.** HTML hardening — including `<script>` and event-handler removal, which is the actual attack surface — is out of scope for a metadata stripper and belongs to a dedicated HTML sanitiser (`HtmlSanitizer` / DOMPurify / Bleach) applied downstream by the caller. Add a passthrough test to lock in the contract. |
| DIF (Data Interchange Format) | `.dif` | `TABLE\r\n0,1` | Text-based tabular data, minimal metadata | Document as intentional passthrough; add a passthrough test. |
| dBASE III / IV | `.dbf` | First byte 0x03 / 0x83 / 0x8B (version) | Binary tabular; may carry a MEMO field | Document as intentional passthrough. |
| SYLK | `.slk` | `ID;P` | Text-based tabular, no user metadata | Document as intentional passthrough. |

### Audio detection gaps (unchanged from the previous checklist pass)

| Format | Magic | Metadata risk | Recommendation |
|--------|-------|---------------|----------------|
| AIFF | `FORM ... AIFF` | ID3 chunks possible | Add detection — TagLibSharp supports it |
| APE (Monkey's Audio) | `MAC ` | APEv2 / ID3v2 tags | Add detection — TagLib supports it |
| WavPack (.wv) | `wvpk` | APEv2 / ID3v1 tags | Add detection — TagLib supports it |
| MPC (Musepack) | `MP+` or `MPCK` | APEv2 tags | Add detection — TagLib supports it |
| AAC (raw ADTS) | `0xFF 0xF1` / `0xFF 0xF9` | Rare in raw ADTS; usually wrapped in M4A | Low priority — user is more likely to upload the M4A wrapper |
| FLV (Flash video) | `FLV\x01` | Metadata script atoms | Low priority — deprecated container |
| F4V | ISOBMFF ftyp `f4v ` | Same as MP4 | Would work if added to the ftyp catch-all (it currently is — but untested) |
| MJ2 (Motion JPEG 2000) | ISOBMFF ftyp `mjp2` | Same as MP4 | Would work via ftyp catch-all |

### Suggested implementation order

1. ~~**CFBF (legacy binary Office)**~~ — ✅ Done. `StripCfbfMetadata` handles `.doc`, `.dot`, `.xls`, `.xlt`, `.ppt`, `.pot`, `.pps` in a single pass via OpenMcdf + OpenMcdf.Ole.
2. **RTF** — small, dependency-free, no NuGet required. Now the highest-leverage remaining gap.
3. **Flat ODF + Word 2003 XML** — one shared XML detection routes both.
4. **Untested OOXML/ODF variants (medium)** — one small test per extension to prove the existing code path handles them.
5. **Audio detection gaps** — one detection branch per magic, TagLibSharp already parses each.
6. **HTML passthrough test** — lock in the intentional passthrough contract so the classification cannot silently change.

## How to update this file

Whenever a new format is added to `DetectCategory` **or** a new dedicated test file is added under `FileMetadataStripping.Tests`, update the corresponding row in this checklist and (if relevant) move it out of the "gaps" section.
