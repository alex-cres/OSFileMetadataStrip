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

| Category | Formats | Metadata Stripped |
|----------|---------|-------------------|
| Images | JPEG, PNG, GIF, TIFF, WebP, TGA, and 100+ more | EXIF, IPTC, XMP, ICC profiles, comments |
| Audio | MP3, FLAC, OGG, WAV, M4A, WMA, and more | ID3 tags, Vorbis comments, metadata atoms (title, artist, album, comment, genre, …) |
| Video | MP4, MOV, AVI, MKV, WebM, WMV | Metadata atoms/tags (title, artist, conductor, copyright, …) |
| PDF | PDF | Title, Author, Subject, Keywords, Creator, Producer, XMP catalog metadata stream, and annotation Author fields (comment, sticky-note, markup annotations) |
| Office documents | DOCX, XLSX, PPTX | Core properties (Creator, LastModifiedBy, Created, Modified, Title, Subject, Description, Keywords, Category, ContentStatus, Revision, LastPrinted, Identifier, Version), application properties (Application, Company, Manager, AppVersion, Template, HyperlinkBase), custom property key/value pairs. When `StripBodyAuthors = True`: also blanks author names from tracked changes, comments, and Excel 365 xl/persons entries. |
| ODF documents | ODT, ODS, ODP | dc:creator, dc:title, dc:description, dc:subject, meta:initial-creator, meta:generator, meta:editing-cycles, meta:editing-duration, and all meta:user-defined properties |
| Plain text / other | BMP, TXT, CSV, MD, JSON, XML, HTML, and any unrecognised format | Passthrough — returned unchanged with `IsPassthrough = true` |

---

## Exposed Server Actions

| Action | Inputs | Output | Description |
|--------|--------|--------|-------------|
| `StripFileMetadata` | `RawFile : BinaryData`<br>`StripBodyAuthors : Boolean` | `FileMetadataResult` | Strips embedded metadata from any supported file. Set `StripBodyAuthors = True` to also blank author names from OOXML tracked changes and comments. Returns the clean file, extracted metadata, and a passthrough flag. |

### FileMetadataResult Structure

| Field | Type | Description |
|-------|------|-------------|
| `CleanFile` | `BinaryData` | The file with all metadata removed. Safe to forward to AI APIs or store. |
| `ExtractedMetadata` | `Text` | JSON object of all metadata entries found and removed. Keys vary by format: images use `exif`/`iptc`/`xmp`; PDFs use `title`/`author`/`subject`/`keywords`/`creator`/`producer`/`annotationAuthors`; OOXML uses `creator`/`lastModifiedBy`/`revision`/`lastPrinted`/`identifier`/`version` and other core property keys, `appCompany`/`appManager`/`appVersion`/`appApplication`/`appTemplate`/`appHyperlinkBase` for application properties, `customProperties` for custom properties, and `strippedAuthors` when `StripBodyAuthors = True`; ODF uses `creator`/`title`/`description`/`subject`/`initialCreator`/`generator`/`editingCycles`/`editingDuration`/`userDefinedProperties`. Returns `[]` when no metadata was present. |
| `RemovedEntryCount` | `Integer` | Total number of metadata entries removed. Zero when the file had no embedded metadata. |
| `IsPassthrough` | `Boolean` | `True` when the file format has no supported metadata containers (e.g. BMP, TXT, CSV, MD, JSON) and was returned unchanged. Use this flag in audit logs to distinguish passthrough files from files that were actively processed and found clean. |

---

## How It Works

Detects the file type from its binary signature, then routes to a format-specific stripper:

| File type | Library | What's stripped |
|-----------|---------|----------------|
| Images (JPEG, PNG, GIF, TIFF, WebP, TGA, 100+…) | [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache 2.0) | All metadata via `image.Strip()` — EXIF, IPTC, XMP, ICC profiles, comments |
| Audio (MP3, FLAC, OGG, WAV, M4A, WMA…) | [TagLibSharp](https://github.com/mono/taglib-sharp) (LGPL 2.1) | ID3 tags, Vorbis comments, metadata atoms |
| Video (MP4, MOV, AVI, MKV, WebM, WMV) | [TagLibSharp](https://github.com/mono/taglib-sharp) (LGPL 2.1) | Metadata atoms/tags |
| PDF | [PDFsharp](https://www.pdfsharp.net/) (MIT) | /Info dictionary fields (Title, Author, Subject, Keywords, Creator, Producer), XMP catalog metadata stream (/Metadata entry), and annotation /Author fields (comment, sticky-note, and markup annotations) |
| Office Open XML (DOCX, XLSX, PPTX) | [DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK) (MIT) | Core properties (Creator, LastModifiedBy, Created, Modified, Title, Subject, Description, Keywords, Category, ContentStatus, Revision, LastPrinted, Identifier, Version), application properties (Application, Company, Manager, AppVersion, Template, HyperlinkBase), custom property key/value pairs. When `StripBodyAuthors = True`: also blanks `w:author`/`w:initials` in DOCX, `<author>` elements in XLSX, `name`/`initials` in PPTX comment authors, and `displayName`/`userId` in xl/persons. |
| ODF (ODT, ODS, ODP) | System.IO.Compression (BCL) | dc:creator, dc:title, dc:description, dc:subject, meta:initial-creator, meta:generator, meta:editing-cycles, meta:editing-duration, and all meta:user-defined properties in meta.xml |
| BMP / plain text / unrecognised | — | Passthrough — `IsPassthrough = true`, file returned unchanged |

> **Note:** If a PDF or OOXML file is encrypted, password-protected, or corrupted and cannot be opened, the original file is returned unchanged (`IsPassthrough = false`, `RemovedEntryCount = 0`) and `ExtractedMetadata` contains a `processingError` key. No exception is raised.

> **TIFF and EXIF:** TIFF stores EXIF as native IFD tags (not as an APP1 segment like JPEG). `image.Strip()` removes all metadata regardless of how it is embedded. For `ExtractedMetadata` reporting, EXIF is read via `GetExifProfile()`, which works for real-world camera TIFFs. If a TIFF was produced by a tool that dropped the EXIF during encoding (including Magick.NET itself), `RemovedEntryCount` will reflect only the metadata that was actually present — XMP and Comment fields are always captured when present.

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

All packages are Apache 2.0, MIT, or LGPL 2.1.

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

See the [implementation guide](./01-exif-metadata-stripping.md) for full context on the attack vector and implementation options.

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
