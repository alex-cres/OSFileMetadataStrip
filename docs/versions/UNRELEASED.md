# Unreleased

Changes in progress — not yet published to OutSystems Forge.

> When publishing a Forge release, tell the OutSystems Extension Builder agent the version number. It will promote this file to `docs/versions/v{x.y.z}.md` and reset this file.

---

## Added

- `IFileMetadataStripping` interface (`[OSInterface]`) exposing `StripFileMetadata`
- `FileMetadataResult` (`[OSStructure]`) return type with four fields:
  - `CleanFile` (BinaryData) — file with all metadata stripped
  - `ExtractedMetadata` (Text) — JSON of removed entries for policy review
  - `RemovedEntryCount` (Integer) — number of metadata entries removed
  - `IsPassthrough` (Boolean) — `True` when the file format has no metadata containers; file returned unchanged
- Multi-format support via automatic file-type detection from magic bytes:
  - **Images** (JPEG, PNG, GIF, BMP, TIFF, WebP, TGA, and 200+ more) — via Magick.NET `image.Strip()`
  - **PDF** — /Info dictionary fields (Title, Author, Subject, Keywords, Creator)
  - **Office Open XML** (DOCX, XLSX, PPTX) — core package properties (Creator, LastModifiedBy, Created, Modified, Title, Subject, Description, Keywords, Category)
  - **Plain text / unrecognised formats** (TXT, CSV, MD, JSON, XML, HTML, …) — passthrough, `IsPassthrough = true`, file returned unchanged
- Format preservation: output is re-encoded in the same format as input
- `FileMetadataStripping.Tests` xUnit test project (36 tests, all passing)
  - `ImageTests.cs`: clean round-trip, EXIF/IPTC/XMP removal, format preservation (JPEG, PNG)
  - `PdfTests.cs`: author/title cleared, audit metadata captured, valid PDF output
  - `OpenXmlTests.cs`: creator cleared, audit metadata captured, valid OOXML output
  - `PassthroughTests.cs`: plain text passthrough contract, IsPassthrough=false for active formats
  - `TestHelpers.cs`: shared programmatic test data generators (no binary files committed)
- `AGENTS.md`, `test-runner` subagent, `outsystems-ext-builder` agent, ODC and O11 skills
- `docs/platform/forge-description.md` — component description for OutSystems Forge

## Changed

- Image engine switched from SixLabors.ImageSharp (Six Labors Split License) to **Magick.NET-Q8-AnyCPU** (Apache 2.0) — cleaner license, simpler API (`image.Strip()`)
- PDF library switched from PdfSharpCore (had ImageSharp transitive dependency) to **PDFsharp 6.2.4** (MIT, no ImageSharp dependency)
- All dependencies are now Apache 2.0 or MIT — no commercial license concerns for Forge
- Publish command updated: requires `-r linux-x64` due to Magick.NET native binaries

## Fixed

*(nothing yet)*

## Removed

- `SixLabors.ImageSharp` — fully eliminated from the dependency tree
- `PdfSharpCore` — replaced by PDFsharp 6.x

