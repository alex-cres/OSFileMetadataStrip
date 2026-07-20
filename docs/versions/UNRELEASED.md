# Unreleased

Changes in progress — not yet published to OutSystems Forge.

> When publishing a Forge release, tell the OutSystems Extension Builder agent the version number. It will promote this file to `docs/versions/v{x.y.z}.md` and reset this file.

---

## Added

### ODC External Library

- `IFileMetadataStripping` (`[OSInterface]`) — exposes a single Server Action: `StripFileMetadata(rawFile: BinaryData)`
- `FileMetadataResult` (`[OSStructure]`) — return type with four fields:
  - `CleanFile` (BinaryData) — the file with all metadata stripped, safe to forward to AI APIs or store
  - `ExtractedMetadata` (Text) — JSON of every metadata entry found and removed; `"[]"` when none present
  - `RemovedEntryCount` (Integer) — total number of metadata entries removed
  - `IsPassthrough` (Boolean) — `True` when the format has no supported metadata containers; file returned unchanged

### Format Support (magic-byte detection, evaluated in order)

| Format | Detected by | Stripped fields |
|--------|-------------|-----------------|
| **Images** — JPEG, PNG, GIF, BMP, TIFF, WebP, TGA, 100+ more | Magick.NET `MagickImageInfo` | EXIF, IPTC, XMP, ICC profiles, image comments |
| **PDF** | `%PDF` magic bytes | /Info: Title, Author, Subject, Keywords, Creator, Producer |
| **Office Open XML** — DOCX, XLSX, PPTX | ZIP `PK` magic bytes | Core properties: Creator, LastModifiedBy, Created, Modified, Title, Subject, Description, Keywords, Category, ContentStatus, Revision |
| **Audio** — MP3, FLAC, OGG, WAV, M4A, WMA | ID3 / fLaC / OggS / RIFF / ASF magic bytes | ID3 tags, Vorbis comments, metadata atoms (`RemoveTags(AllTags)`) |
| **Video** — MP4, MOV, AVI, MKV, WebM, WMV | RIFF / ftyp / EBML / ASF magic bytes | Metadata atoms/tags (`RemoveTags(AllTags)`) |
| **Plain text / unrecognised** — TXT, CSV, MD, JSON, XML, HTML, … | fallthrough | Passthrough — `IsPassthrough = true`, file returned unchanged |

- Format preservation: output is always re-encoded in the same format as input (Magick.NET auto-detects format on write)
- Graceful error handling for media files: if TagLibSharp cannot parse the file, the original bytes are returned unchanged and `ExtractedMetadata` contains a `processingError` audit note

### NuGet Dependencies

- `OutSystems.ExternalLibraries.SDK` 1.5.0 — ODC External Libraries SDK
- `Magick.NET-Q8-AnyCPU` 14.15.0 — image processing (Apache 2.0)
- `PDFsharp` 6.2.4 — PDF metadata stripping (MIT)
- `DocumentFormat.OpenXml` 3.5.1 — OOXML package properties (MIT)
- `TagLibSharp` 2.3.0 — audio/video tag stripping (LGPL 2.1)

### Test Suite

- `FileMetadataStripping.Tests` — xUnit test project, 66 tests, all passing
  - `ImageTests.cs` (19 tests): clean round-trip, EXIF/IPTC/XMP removal, format preservation for JPEG and PNG
  - `PdfTests.cs` (6 tests): author/title cleared, audit metadata captured, valid PDF output
  - `OpenXmlTests.cs` (5 tests): creator cleared, audit metadata captured, valid OOXML output
  - `PassthroughTests.cs` (6 tests): plain-text passthrough contract, `IsPassthrough = false` for active formats
  - `AudioVideoTests.cs` (30 tests): WAV + MP3 full strip; FLAC/OGG/MP4/MKV/AVI detection; `processingError` audit note when TagLibSharp cannot parse a file
  - `TestHelpers.cs`: shared programmatic test-data generators for all supported formats — no binary files committed

### Repository & Tooling

- `docs/platform/forge-description.md` — component description for OutSystems Forge
- `CHANGELOG.md` — version index linking to per-version docs
- `docs/versions/UNRELEASED.md` — in-progress changelog
- `THIRD-PARTY-NOTICES.md` — third-party licence attributions

## Changed

*(nothing yet)*


## Fixed

*(nothing yet)*

## Deleted

*(nothing yet)*
