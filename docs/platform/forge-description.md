# OSFileMetadataStrip — OutSystems Forge Description

> This file is the source of truth for the component description published on OutSystems Forge.
> Update it whenever the component's behaviour, supported formats, or interface changes.
> It is versioned alongside the codebase — a copy is kept per release under `docs/versions/`.

---

## Short Description (Forge subtitle — 160 chars max)

Strips EXIF, IPTC, XMP, and document metadata from uploaded files before they reach AI APIs — blocking file metadata injection attacks.

---

## Full Description

### What This Component Does

**OSFileMetadataStrip** is an ODC External Library that removes all embedded metadata from uploaded files before they are forwarded to AI APIs or stored. It returns the clean file alongside a structured JSON record of what was found — enabling both security hardening and policy audit.

File metadata containers (EXIF in images, /Info in PDFs, core properties in Office files) can carry arbitrary text that is invisible to users, browsers, and image classifiers. An attacker can embed prompt-injection instructions in these fields using any standard tool, without altering the file visually. If the file reaches an AI model as part of a context message, that text is processed as trusted input.

Calling `StripFileMetadata` at the earliest point in any flow that accepts file uploads eliminates this entire attack class before it reaches any AI model.

---

### Supported File Types

| Category | Formats | Metadata Stripped |
|----------|---------|-------------------|
| Images | JPEG, PNG, GIF, BMP, TIFF, WebP, TGA | EXIF (camera data, GPS, descriptions), IPTC (captions, keywords), XMP |
| PDF | PDF | Title, Author, Subject, Keywords, Creator |
| Office documents | DOCX, XLSX, PPTX | Creator, LastModifiedBy, Created/Modified dates, Title, Subject, Description, Keywords, Category |

---

### Server Actions

#### `StripFileMetadata`

| Parameter | Direction | Type | Description |
|-----------|-----------|------|-------------|
| `RawFile` | Input | BinaryData | The uploaded file (any supported format) |
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
2. In your ODC application, add **OSFileMetadataStrip** as a dependency.
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
| Magick.NET-Q8-AnyCPU | Apache 2.0 | Image decoding and metadata stripping |
| PDFsharp | MIT | PDF /Info dictionary access |
| DocumentFormat.OpenXml | MIT | Office Open XML package properties |

---

### Build & Publish

```powershell
cd FileMetadataStripping
dotnet publish -c Release -r linux-x64 --no-self-contained
Compress-Archive -Path "bin/Release/net10.0/linux-x64/publish/*" -DestinationPath "ExternalLibrary.zip" -Force
```

Upload `ExternalLibrary.zip` to ODC Portal → External Logic.

---

### License

MIT — see [LICENSE](../../LICENSE)
