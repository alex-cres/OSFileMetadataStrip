# FileMetadataStripping — OutSystems Forge Description

> This file is the source of truth for the component description published on OutSystems Forge.
> Update it whenever the component's behaviour, supported formats, or interface changes.
> It is versioned alongside the codebase — a copy is kept per release under `docs/versions/`.

---

## Short Description (Forge subtitle — 160 chars max)

Strips EXIF, IPTC, XMP, and document metadata from uploaded files before they reach AI APIs — blocking file metadata injection attacks.

---

## Full Description

### What This Component Does
**FileMetadataStripping** is a Library that removes all embedded metadata from uploaded files before they are forwarded to AI APIs or stored. It returns the clean file alongside a structured JSON record of what was found — enabling both security hardening and policy audit.

File metadata containers (EXIF in images, /Info in PDFs, core properties in Office files) can carry arbitrary text that is invisible to users, browsers, and image classifiers. An attacker can embed prompt-injection instructions in these fields using any standard tool, without altering the file visually. If the file reaches an AI model as part of a context message, that text is processed as trusted input.

Calling `StripFileMetadata` at the earliest point in any flow that accepts file uploads eliminates this entire attack class before it reaches any AI model.

---

### Supported File Types

Every format listed below has an explicit xUnit test in the component's test project. No format is claimed here that is not verified by a regression test.

| Category | Formats | Metadata Stripped |

|----------|---------|-------------------|

| Standard images | JPEG, PNG, GIF, TIFF, WebP | EXIF (camera data, GPS, descriptions), IPTC (captions, keywords), XMP, ICC profiles, comments |

| Animated / multi-frame | Animated GIF, Animated WebP, APNG, MPO, MNG | Metadata stripped from every frame; all frames preserved |

| AVIF | AVIF | Full read/write round-trip: EXIF, IPTC, XMP, ICC profiles, comments |

| HEIC / HEIF | HEIC, HEIF (mif1 / msf1) | Metadata extracted; output transcoded to JPEG (x265 HEVC encoder is GPL-licensed and cannot be bundled). `ExtractedMetadata` includes a `transcodedFormat` key. |

| RAW camera formats | ARW, CR2, DNG, NEF, ORF, PEF, RAF, X3F | EXIF, XMP, ICC profiles |

| Modern / HDR | JPEG XL, JPEG 2000 (JP2 / J2C / J2K / JPT), JPEG XR / WDP, Ultra HDR, OpenEXR, Radiance HDR, QOI | EXIF, XMP, ICC profiles; Radiance HDR encoder-comment lines. JPT is decode-only. |

| Legacy raster | PSD / PSB, TGA, DDS, PCX / DCX, SGI, SUN, PICT, PCD / PCDS, FITS, JBIG, WMF, ICO, XCF (GIMP), Netpbm (PBM / PGM / PPM / PNM) | EXIF, XMP, ICC profiles, and format-specific comments where present. GIMP XCF is transcoded to JPEG on write. |

| Film formats | DPX, CIN | Standard image profiles plus per-image `dpx:*` and `cin:*` production attributes |

| Medical imaging | DICOM (.dcm) | Detected via preamble + `DICM` signature; output transcoded to JPEG. DICOM tag parsing is out of scope. |

| SVG | SVG | `<title>`, `<desc>`, `<metadata>` at every depth |

| PDF | PDF, AI (Adobe Illustrator) | Title, Author, Subject, Keywords, Creator, Producer, XMP catalog stream, annotation Author fields |

| RTF | RTF | `\author`, `\title`, `\subject`, `\keywords`, `\comment`, `\operator`, `\company`, `\doccomm`, `\category`, `\hlinkbase`, `\manager` control-word groups in `\info`. Numeric control words preserved. |

| Office documents | DOCX, XLSX, PPTX | Core / app / custom properties + `docProps/thumbnail.*`. Body author stripping (tracked changes, comment authors, Excel 365 xl/persons entries) requires `StripBodyAuthors = True`. |

| Legacy binary Office | DOC, DOT, XLS, XLT, PPT, POT, PPS (Word / Excel / PowerPoint 97 – 2003) | `\x05SummaryInformation` stream (Title, Subject, Author, Keywords, Comments, Template, Last-Saved-By, Application) and `\x05DocumentSummaryInformation` stream (Category, Manager, Company, ContentStatus, Language, custom user-defined properties). Detected via the CFBF 8-byte magic; the container is consolidated after deletion so freed sectors are dropped from the output. |

| ODF documents | ODT, ODS, ODP | dc:creator, dc:title, dc:description, dc:subject, meta:initial-creator, meta:generator, meta:editing-cycles, meta:editing-duration, and all meta:user-defined properties |

| EPUB | EPUB | Dublin Core (`dc:*`) and every OPF `<meta>` refinement |

| ORA (Open Raster) | ORA | `name` / `description` attributes on every element in `stack.xml` |

| Audio | MP3, WAV, FLAC, OGG (Vorbis / Opus), M4A, M4B, WMA, AIFF / AIFC, APE, WavPack, MPC | ID3 tags, Vorbis / Opus comments, RIFF INFO chunks, iTunes MP4 atoms, ASF header extension objects, AIFF ID3 chunks, APE tags |

| Video | MP4, MKV, AVI, MOV, WebM, WMV, M4V, 3GP, 3G2 | Metadata atoms / tags |

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

| `ExtractedMetadata` | Text | JSON object of all metadata found and removed, keyed by type (`exif`, `iptc`, `xmp`, `title`, `author`, etc.). Returns `[]` when the file had no embedded metadata. |

| `RemovedEntryCount` | Integer | Total number of metadata entries removed. Zero when the file was already clean. |

| `IsPassthrough` | Boolean | `True` when the file format has no supported metadata containers (e.g. TXT, CSV, MD, JSON) and was returned unchanged. Use in audit logs to distinguish passthrough files from actively processed files that happened to be clean. |

---

### How to Use

1. Upload the component ZIP to **ODC Portal → External Logic** and publish it as an External Library.

2. In your ODC application, add **FileMetadataStripping** as a dependency.

3. In any Server Action that receives an uploaded file, call `StripFileMetadata` **before** forwarding the file to an AI API:

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

- OutSystems Developer Cloud (ODC) **or** OutSystems 11 (O11)
- ODC: .NET 10.0 (provided by the ODC platform)
- O11: .NET Framework 4.8 — XIF available in the [GitHub repository](https://github.com/OutSystems/OSFileMetadataStrip) under `xif/`

### Dependencies (all open-source)

| Library | License | Purpose |

|---------|---------|---------|

| Magick.NET-Q8-AnyCPU | Apache 2.0 | Image decoding and metadata stripping || TagLibSharp | LGPL 2.1 | Audio and video metadata stripping || PDFsharp | MIT | PDF /Info dictionary access |

| DocumentFormat.OpenXml | MIT | Office Open XML package properties |

| OpenMcdf + OpenMcdf.Ole | MPL 2.0 | Legacy binary Office (CFBF) container: OLE property-set stream deletion and consolidation |

---

### License

MIT



