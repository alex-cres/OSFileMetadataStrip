# FileMetadataStripping — O11 Extension Forge Description

> This file is the source of truth for the O11 extension description published on OutSystems Forge.
> Update it whenever the component's behaviour, supported formats, or interface changes.
> It is versioned alongside the codebase — a copy is kept per release under `docs/versions/`.

---

## Short Description (Forge subtitle — 160 chars max)

Strips EXIF, IPTC, XMP, and document metadata from uploaded files before they reach AI APIs — blocking file metadata injection attacks.

---

## Full Description

### What This Component Does
**FileMetadataStripping** is an Extension that removes all embedded metadata from uploaded files before they are forwarded to AI APIs or stored. It returns the clean file alongside a structured JSON record of what was found — enabling both security hardening and policy audit.

File metadata containers (EXIF in images, /Info in PDFs, core properties in Office files) can carry arbitrary text that is invisible to users, browsers, and image classifiers. An attacker can embed prompt-injection instructions in these fields using any standard tool, without altering the file visually. If the file reaches an AI model as part of a context message, that text is processed as trusted input.

Calling `StripFileMetadata` at the earliest point in any flow that accepts file uploads eliminates this entire attack class before it reaches any AI model.

---

### Supported File Types

Every format listed below has an explicit xUnit test in `FileMetadataStripping.O11.Tests`. No format is claimed here that is not verified by a regression test.

| Category | Formats | Metadata Stripped |

|----------|---------|-------------------|

| Standard images | JPEG, PNG, GIF, TIFF, WebP | EXIF (camera data, GPS, descriptions), IPTC (captions, keywords), XMP, ICC profiles, comments |

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

| RTF | RTF (Rich Text Format) | `\author`, `\title`, `\subject`, `\keywords`, `\comment`, `\operator`, `\company`, `\doccomm`, `\category`, `\hlinkbase`, `\manager` control-word groups inside the `\info` group. Numeric control words (`\version`, `\vern`, `\nofpages`, revision timestamps, edit-minute counters) are preserved so the document still renders. |

| Office documents | DOCX, DOTX, DOCM, DOTM, XLSX, XLTX, XLSM, XLTM, PPTX, POTX, PPSX, PPTM, POTM, PPSM | Core properties (Creator, LastModifiedBy, Created/Modified dates, Title, Subject, Description, Keywords, Category, ContentStatus, Revision, LastPrinted, Identifier, Version), application properties (Application, Company, Manager, AppVersion, Template, HyperlinkBase), custom property key/value pairs, and the embedded page-preview thumbnail (`docProps/thumbnail.*`). Body author stripping (tracked changes, comment authors, Excel 365 xl/persons entries) requires `StripBodyAuthors = True`. |

| Legacy binary Office | DOC, DOT (Word 97 – 2003), XLS, XLT (Excel 97 – 2003), PPT, POT, PPS (PowerPoint 97 – 2003) | Detected via the 8-byte CFBF magic `D0 CF 11 E0 A1 B1 1A E1`. Deletes both OLE property-set streams: `\x05SummaryInformation` (Title, Subject, Author, Keywords, Comments, Template, Last-Saved-By, Application, revision and edit-time counters, dates) and `\x05DocumentSummaryInformation` (Category, Manager, Company, ContentStatus, Language, and all user-defined custom properties). The CFBF container is consolidated after deletion so the freed sectors are dropped from the output — the raw property values do not survive in unallocated space. |

| ODF documents | ODT, ODS, ODP, OTT, OTS, OTP, ODG, OTG, ODC, ODF, ODB, ODI | dc:creator, dc:title, dc:description, dc:subject, meta:initial-creator, meta:generator, meta:editing-cycles, meta:editing-duration, and all meta:user-defined properties. Templates (OTT / OTS / OTP / OTG), drawings (ODG), charts (ODC), formulas (ODF), databases (ODB), and images (ODI) share the same strip path — matched via the `application/vnd.oasis.opendocument.*` mimetype prefix. |

| Flat ODF (single-file XML) | FODT, FODS, FODP | Same `dc:*` and `meta:*` elements as the ZIP-based ODF path — detected by the `<office:document>` root in the OASIS office namespace and processed through the shared ODF strip helper |

| Word 2003 XML (WordProcessingML) | XML (Word 2003) | Every child of `<o:DocumentProperties>` (Author, LastAuthor, Company, Manager, Title, Subject, Keywords, Description, Category, Template, HyperlinkBase, Application, AppVersion, TotalTime, LastPrinted, Created, LastSaved, revision counters) and every child of `<o:CustomDocumentProperties>`. When `StripBodyAuthors = True`: also blanks tracked-change and comment `w:author` / `aml:author` attributes throughout the document body. |

| EPUB | EPUB | Dublin Core metadata in the OPF package (`dc:creator`, `dc:title`, `dc:description`, `dc:publisher`, `dc:rights`, `dc:subject`, …) and every OPF `<meta>` refinement |

| ORA (Open Raster) | ORA | `name` and `description` attributes on every element in `stack.xml` (image, stack, layer, mask, text) — structural attributes preserved so the image still renders |

| Audio | MP3, WAV, FLAC, OGG (Vorbis / Opus), M4A, M4B, WMA, AIFF / AIFC, APE (Monkey's Audio), WavPack (.wv), MPC (Musepack SV7 / SV8) | ID3 tags, Vorbis / Opus comments, RIFF INFO chunks, iTunes MP4 atoms, ASF header extension objects (title, artist, album, comment, …), AIFF ID3 chunks, APE tags |

| Video | MP4, MKV, AVI, MOV, WebM, WMV, M4V, 3GP, 3G2 | Metadata atoms/tags (title, comment, encoder, …) |

| Passthrough | BMP, DIB, WBMP, XBM, XPM, TXT, CSV, MD, JSON, XML, HTML, and any unrecognised format | Returned unchanged with `IsPassthrough = true` |

---

### Server Actions

#### `StripFileMetadata`

| Parameter | Direction | Type | Description |

|-----------|-----------|------|-------------|

| `RawFile` | Input | BinaryData | The uploaded file (any supported format) |

| `StripBodyAuthors` | Input | Boolean | When `True`, also blanks author names from OOXML tracked changes and comments (DOCX `w:author`/`w:initials`, XLSX `<author>` elements, PPTX `name`/`initials`), and Excel 365 `xl/persons` entries (`displayName`/`userId`). Default: `False`. |

| *(return)* | Output | `FileMetadataResult` | Structure containing the clean file and extracted metadata |

#### `FileMetadataResult` Structure

| Field | Type | Description |

|-------|------|-------------|

| `CleanFile` | BinaryData | File with all metadata removed. Safe to forward to AI APIs or store. |

| `ExtractedMetadata` | Text | JSON object of all metadata found and removed, keyed by type (`exif`, `iptc`, `xmp`, `title`, `author`, etc.). Returns `[]` when the file had no embedded metadata. Contains a `processingError` key if the file could not be processed (e.g. encrypted or corrupted PDF/OOXML). |

| `RemovedEntryCount` | Integer | Total number of metadata entries removed. Zero when the file was already clean. |

| `IsPassthrough` | Boolean | `True` when the file format has no supported metadata containers (e.g. TXT, CSV, MD, JSON) and was returned unchanged. Use in audit logs to distinguish passthrough files from actively processed files that happened to be clean. |

---

### How to Use

1. Locate the XIF at `xif/FileMetadataStripping.xif` in the repository.

2. Open **Integration Studio** → **File → Open** → select the XIF.

3. **1-Click Publish** to Service Center.

4. In your O11 application, open **Service Studio** and add **FileMetadataStripping** as a dependency.

5. In any Action that receives an uploaded file, call `StripFileMetadata` **before** forwarding the file to an AI API:

```

(User uploads file)

↓

StripFileMetadata(RawFile: FileContent.Content)

↓ CleanFile → forward to AI API

↓ ExtractedMetadata → log for audit / policy review

↓ RemovedEntryCount → flag if > 0

```

**Tip:** If `RemovedEntryCount > 0`, log or store `ExtractedMetadata` for security review — it records exactly what injection payload was present in the original file.

---

### Requirements

- OutSystems 11

- .NET Framework 4.8

### Dependencies (all open-source)

| Library | License | Purpose |

|---------|---------|---------|

| Magick.NET-Q8-AnyCPU | Apache 2.0 | Image decoding and metadata stripping |

| TagLibSharp | LGPL 2.1 | Audio and video metadata stripping |

| PDFsharp | MIT | PDF /Info dictionary access |

| DocumentFormat.OpenXml | MIT | Office Open XML package properties |

| OpenMcdf + OpenMcdf.Ole | MPL 2.0 | Legacy binary Office (CFBF / OLE Compound Document) container: OLE property-set stream deletion and consolidation |

---

### Compatibility

FileMetadataStripping works on standard OutSystems 11 servers **and on locked-down O11 hosts** — including the OutSystems Personal Environment sandbox (`outsystemscloud.com`) — via a two-engine architecture on the image strip pipeline:

- **Primary engine — Magick.NET.** On healthy O11 hosts, image metadata stripping uses `Magick.NET-Q8-AnyCPU` and delivers the full format matrix listed in the Supported File Types table above (JPEG, PNG, GIF, TIFF, WebP, AVIF, HEIC, JXL, JPEG 2000, PSD, camera RAW, and every other image entry). The XIF ships the Microsoft VC++ 2015–2022 x64 runtime alongside `Magick.Native-Q8-x64.dll`, and the extension preloads the native library from an absolute path with `LOAD_WITH_ALTERED_SEARCH_PATH` on the first call, to maximise the set of hosts on which the primary engine initialises successfully.

- **Fallback engine — System.Drawing (GDI+).** On hosts where `Magick.Native-Q8-x64.dll` cannot initialise despite those mitigations (host-side native-code loader policy on the Personal Environment sandbox is the currently known case — HRESULT `0x8007045A` / `ERROR_DLL_INIT_FAILED`), the extension catches the `System.TypeInitializationException` on first use, latches a static AppDomain-scoped flag, and switches every subsequent image call to a pure-managed GDI+ pipeline. GDI+ (`gdiplus.dll`) is a Windows built-in KnownDLL and is not subject to the VC++ / EDR / WDAC restrictions that block the native ImageMagick runtime. No host-side configuration is required.

  The fallback engine partitions every image call into one of three deterministic outcomes:

  - **Actively stripped** (`IsPassthrough = false`, `RemovedEntryCount > 0` when metadata was present, `ExtractedMetadata` = JSON list of the GDI+ `PropertyItem` names that were removed) — JPEG, PNG, GIF (multi-frame), BMP, TIFF (multi-page). Full metadata removal path applied normally.

  - **Recognised-but-unsupported error contract** (`IsPassthrough = false`, `RemovedEntryCount = 0`, `CleanFile = originalBytes`, `ExtractedMetadata` = JSON with a `processingError` value prefixed with the fixed marker `"GDI+ fallback:"` and the format name) — WebP, HEIC, HEIF, AVIF, JXL, JPEG 2000, JPEG XR, PSD/PSB, DDS, EXR, HDR, DPX/CIN, FITS, QOI, SGI, SUN, PCX/DCX, PNM, JBIG, XCF, WMF, ICO, DCM, TGA, MNG, and camera RAW variants. The caller receives an explicit failure signal instead of a silent passthrough — log-and-reject or retry through an alternate pipeline.

  - **Passthrough** (`IsPassthrough = true`, empty `ExtractedMetadata`) — non-image / unrecognised bytes. Identical to the primary engine.

- **Document / SVG / media pipelines are unaffected.** PDF, RTF, OOXML, legacy binary Office, ODF, Flat ODF, Word 2003 XML, EPUB, ORA, SVG, audio, and video are all pure-managed and behave identically on both engines.

Consumer-facing predicate on the O11 side (both engines):

- *Actively stripped:* `IsPassthrough == false && RemovedEntryCount > 0`.
- *Fallback engine declined the format:* `IsPassthrough == false && RemovedEntryCount == 0 && ExtractedMetadata.Contains("GDI+ fallback:")`.
- *Not an image / not scoped:* `IsPassthrough == true`.

The `StripFileMetadata` action and the `FileMetadataResult` structure are unchanged on both engines — no new fields, no breaking change.

---

### License

MIT
