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
| Images | JPEG, PNG, GIF, BMP, TIFF, WebP, TGA | EXIF, IPTC, XMP profiles |
| PDF | PDF | Title, Author, Subject, Keywords, Creator |
| Office documents | DOCX, XLSX, PPTX | Creator, LastModifiedBy, Created, Modified, Title, Subject, Description, Keywords, Category |

---

## Exposed Server Actions

| Action | Input | Output | Description |
|--------|-------|--------|-------------|
| `StripFileMetadata` | `RawFile : BinaryData` | `FileMetadataResult` | Strips EXIF, IPTC, and XMP metadata from an image. Returns the clean file and the extracted metadata for policy review. |

### FileMetadataResult Structure

| Field | Type | Description |
|-------|------|-------------|
| `CleanFile` | `BinaryData` | The file with all metadata removed. Safe to forward to AI APIs or store. |
| `ExtractedMetadata` | `Text` | JSON object of all metadata entries found and removed (keyed by type: `exif`, `iptc`, `xmp`). Returns `[]` when no metadata was present. |
| `RemovedEntryCount` | `Integer` | Total number of metadata entries removed. Zero when the file had no embedded metadata. |

---

## How It Works

Uses [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) to decode the image, explicitly null out all metadata profiles (EXIF, IPTC, XMP), and re-encode it. ImageSharp does not propagate metadata by default on re-encode, giving a defence-in-depth guarantee even if the nulling step were skipped.

```
Upload → StripFileMetadata → Clean BinaryData → AI API / Storage
```

---

## Requirements

- **Platform:** OutSystems Developer Cloud (ODC)
- **Runtime:** Linux container (ODC Portal)
- **.NET:** 10.0 LTS
- **NuGet packages:**
  - `OutSystems.ExternalLibraries.SDK`
  - `SixLabors.ImageSharp` — image format support
  - `PdfSharpCore` — PDF metadata access
  - `DocumentFormat.OpenXml` — Office Open XML metadata access

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

```powershell
cd FileMetadataStripping
dotnet publish -c Release --no-self-contained
# Zip contents of bin/Release/net10.0/publish/* to ExternalLibrary.zip
Compress-Archive -Path "bin/Release/net10.0/publish/*" -DestinationPath "ExternalLibrary.zip" -Force
```

---

## Changelog

See [CHANGELOG.md](./CHANGELOG.md) for the full version history.
