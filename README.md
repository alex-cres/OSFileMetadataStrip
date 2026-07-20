# OSFileMetadataStrip

**OutSystems Developer Cloud (ODC) External Library**

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
| Images | JPEG, PNG, GIF, BMP, TIFF, WebP, TGA, and 200+ more | EXIF, IPTC, XMP, ICC profiles, comments |
| Audio | MP3, FLAC, OGG, WAV, M4A, WMA, and more | ID3 tags, Vorbis comments, metadata atoms (title, artist, album, comment, genre, …) |
| Video | MP4, MOV, AVI, MKV, WebM, WMV | Metadata atoms/tags (title, artist, conductor, copyright, …) |
| PDF | PDF | Title, Author, Subject, Keywords, Creator |
| Office documents | DOCX, XLSX, PPTX | Creator, LastModifiedBy, Created, Modified, Title, Subject, Description, Keywords, Category |
| Plain text / other | TXT, CSV, MD, JSON, XML, HTML, and any unrecognised format | Passthrough — returned unchanged with `IsPassthrough = true` |

---

## Exposed Server Actions

| Action | Input | Output | Description |
|--------|-------|--------|-------------|
| `StripFileMetadata` | `RawFile : BinaryData` | `FileMetadataResult` | Strips embedded metadata from any supported file. Returns the clean file, the extracted metadata for policy review, and a flag indicating whether stripping was applicable. |

### FileMetadataResult Structure

| Field | Type | Description |
|-------|------|-------------|
| `CleanFile` | `BinaryData` | The file with all metadata removed. Safe to forward to AI APIs or store. |
| `ExtractedMetadata` | `Text` | JSON object of all metadata entries found and removed (keyed by type: `exif`, `iptc`, `xmp`). Returns `[]` when no metadata was present. |
| `RemovedEntryCount` | `Integer` | Total number of metadata entries removed. Zero when the file had no embedded metadata. |
| `IsPassthrough` | `Boolean` | `True` when the file format has no supported metadata containers (e.g. TXT, CSV, MD, JSON) and was returned unchanged. Use this flag in audit logs to distinguish passthrough files from files that were actively processed and found clean. |

---

## How It Works

Detects the file type from its binary signature, then routes to a format-specific stripper:

| File type | Library | What's stripped |
|-----------|---------|----------------|
| Images (JPEG, PNG, GIF, BMP, TIFF, WebP, TGA, 200+…) | [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache 2.0) | All metadata via `image.Strip()` — EXIF, IPTC, XMP, ICC profiles, comments |
| Audio (MP3, FLAC, OGG, WAV, M4A, WMA…) | [TagLibSharp](https://github.com/mono/taglib-sharp) (LGPL 2.1) | ID3 tags, Vorbis comments, metadata atoms |
| Video (MP4, MOV, AVI, MKV, WebM, WMV) | [TagLibSharp](https://github.com/mono/taglib-sharp) (LGPL 2.1) | Metadata atoms/tags |
| PDF | [PDFsharp](https://www.pdfsharp.net/) (MIT) | /Info dictionary fields (Title, Author, Subject, Keywords, Creator) |
| Office Open XML (DOCX, XLSX, PPTX) | [DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK) (MIT) | Core properties (Creator, LastModifiedBy, Created, Modified, Title, Subject, Description, Keywords, Category) |
| Plain text / unrecognised | — | Passthrough — `IsPassthrough = true`, file returned unchanged |

```
Upload → StripFileMetadata → Clean BinaryData + ExtractedMetadata → AI API / Storage
```

---

## Requirements

- **Platform:** OutSystems Developer Cloud (ODC)
- **Runtime:** Linux container (ODC Portal)
- **.NET:** 10.0 LTS
- **NuGet packages (all Apache 2.0, MIT, or LGPL 2.1):**
  - `OutSystems.ExternalLibraries.SDK` — ODC External Library SDK
  - `Magick.NET-Q8-AnyCPU` (Apache 2.0) — image processing and metadata stripping
  - `TagLibSharp` (LGPL 2.1) — audio and video metadata stripping
  - `PDFsharp` (MIT) — PDF /Info dictionary access
  - `DocumentFormat.OpenXml` (MIT) — Office Open XML core properties

---

## Using in ODC

1. Download the latest ZIP from [Releases](./docs/versions/) or the OutSystems Forge.
2. In **ODC Portal** → **External Logic** → **Upload** the ZIP.
3. Create and publish an External Library.
4. In your ODC app, add the library as a dependency and call `StripFileMetadata` in your Server Action before forwarding the file.

---

## Development

See the [implementation guide](./01-exif-metadata-stripping.md) for full context on the attack vector and implementation options.

### Build & Publish

Magick.NET includes native linux-x64 binaries, so the runtime identifier is required:

```powershell
cd FileMetadataStripping
dotnet publish -c Release -r linux-x64 --no-self-contained
# Zip the linux-x64 publish folder contents to ExternalLibrary.zip
Compress-Archive -Path "bin/Release/net10.0/linux-x64/publish/*" -DestinationPath "ExternalLibrary.zip" -Force
```

---

## Changelog

See [CHANGELOG.md](./CHANGELOG.md) for the full version history.

---

## Third-Party Notices

See [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md) for the full list of open-source dependencies and their licenses.
