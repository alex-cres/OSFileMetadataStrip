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

Every format listed below has an explicit xUnit test in `FileMetadataStripping.Tests`. No format is claimed here that is not verified by a regression test.

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

| Office documents | DOCX, XLSX, PPTX | Core properties (Creator, LastModifiedBy, Created/Modified dates, Title, Subject, Description, Keywords, Category, ContentStatus, Revision, LastPrinted, Identifier, Version), application properties (Application, Company, Manager, AppVersion, Template, HyperlinkBase), custom property key/value pairs, and the embedded page-preview thumbnail (`docProps/thumbnail.*`). Body author stripping (tracked changes, comment authors, Excel 365 xl/persons entries) requires `StripBodyAuthors = True`. |

| Legacy binary Office | DOC, DOT (Word 97 – 2003), XLS, XLT (Excel 97 – 2003), PPT, POT, PPS (PowerPoint 97 – 2003) | Detected via the 8-byte CFBF magic `D0 CF 11 E0 A1 B1 1A E1`. Deletes both OLE property-set streams: `\x05SummaryInformation` (Title, Subject, Author, Keywords, Comments, Template, Last-Saved-By, Application, revision and edit-time counters, dates) and `\x05DocumentSummaryInformation` (Category, Manager, Company, ContentStatus, Language, and all user-defined custom properties). The CFBF container is consolidated after deletion so the freed sectors are dropped from the output — the raw property values do not survive in unallocated space. |

| ODF documents | ODT, ODS, ODP | dc:creator, dc:title, dc:description, dc:subject, meta:initial-creator, meta:generator, meta:editing-cycles, meta:editing-duration, and all meta:user-defined properties |

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

- OutSystems Developer Cloud (ODC)

- .NET 10.0 (provided by the ODC platform)

### Dependencies (all open-source)

| Library | License | Purpose |

|---------|---------|---------|

| Magick.NET-Q8-AnyCPU | Apache 2.0 | Image decoding and metadata stripping || TagLibSharp | LGPL 2.1 | Audio and video metadata stripping || PDFsharp | MIT | PDF /Info dictionary access |

| DocumentFormat.OpenXml | MIT | Office Open XML package properties |

| OpenMcdf + OpenMcdf.Ole | MPL 2.0 | Legacy binary Office (CFBF / OLE Compound Document) container: OLE property-set stream deletion and consolidation |

---

### License

MIT



