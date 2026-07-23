# Unreleased

Changes in progress — not yet published to OutSystems Forge.

> When publishing a Forge release, provide the version number.

---

## Security

- Upgraded `System.IO.Packaging` from 8.0.0 to 8.0.1 in `FileMetadataStripping.O11` and `xif/FileMetadataStripping/Source/NET` to address CVE-2024-43483 and CVE-2024-43484 (.NET Denial of Service vulnerabilities)

---

## Added

### O11 Integration Studio Extension

- `FileMetadataStripping.O11/` — full .NET Framework 4.8 port of the extension
  - `IssFileMetadataStripping.cs` — O11 interface (`MssStripFileMetadata` with `out RCFileMetadataResultRecord`)
  - `RecFileMetadataResult.cs` — O11 structure record with `ss`-prefixed fields
  - `Actions/FileMetadataStrippingActions.cs` — `CssFileMetadataStripping` implementation (identical logic to ODC, using internal `Result` struct + conversion at entry point)
  - NuGet dependencies: same packages as ODC except PDFsharp 1.50.5147 (net48 branch) and explicit `System.IO.Packaging` + `System.Text.Json` NuGet refs
- `FileMetadataStripping.O11.Tests/` — 74-test xUnit suite targeting net48
  - `TestHelpers.cs` defines O11 adapter types so all 5 test files are byte-for-byte identical to the ODC test files
- `FileMetadataStripping.O11/resources/icon.ico` — 32×32 ICO generated from the ODC icon.png (PNG-inside-ICO format, accepted by Integration Studio)
- `xif/FileMetadataStripping.xif` — Integration Studio extension file with actions, structures, descriptions, and icon pre-configured
  - All descriptions set: extension, action, input/output parameters, structure, all 4 attributes
  - `Source/NET/FileMetadataStripping.cs` contains full implementation
  - `Source/NET/FileMetadataStripping.csproj` upgraded to v4.8, LangVersion=10, NuGet PackageReferences
- `xif/FileMetadataStripping/` — extracted XIF source folder (VS solution ready to build)

### ODC External Library

- `FileMetadataStripping/resources/icon.png` — 64×64 icon (teal circle, file with red slash, space-invader badge)
- `[OSInterface]` and `[OSAction]` now include `IconResourceName` pointing to the embedded icon
- `FileMetadataStripping/generate_upload_package.ps1` — packaging script: publishes linux-x64, zips, enforces 90 MB limit
- Updated all descriptions to reflect full format support (images, PDF, OOXML, audio, video)

## Changed

- `StripImageMetadata` now uses `MagickImageCollection` instead of `MagickImage`: animated GIFs and multi-frame TIFFs are fully preserved after stripping (all frames stripped, all frames written). Previously only the first frame was kept.
- 10 new xUnit tests added to `ImageTests` (ODC + O11): animated GIF and multi-frame TIFF round-trip, frame count, dimension, and format assertions.
- `StripPdfMetadata` now removes the catalog `/Metadata` XMP stream in addition to the `/Info` dictionary fields. ODC: pages are copied to a fresh document (avoiding PdfSharp 6.x `PrepareForSave` re-adding the entry) and the `/Metadata` token in the saved bytes is whitespace-patched to keep XRef offsets valid. O11: `Elements.Remove("/Metadata")` on PdfSharp 1.50 is sufficient. 5 new xUnit tests added to `PdfTests` (ODC + O11): XMP detection, key removal, entry count, valid output, and passthrough flag.
- `StripPdfMetadata` now handles encrypted and corrupted PDFs gracefully: when PdfSharp raises `PdfReaderException`, `InvalidOperationException`, or `NotSupportedException`, the original file is returned unchanged and `ExtractedMetadata` contains a `processingError` key explaining the failure. Previously any such exception propagated to the caller.
- `StripOpenXmlMetadata` applies the same graceful-failure pattern: `FileFormatException`, `InvalidDataException`, and `NotSupportedException` from `Package.Open` are caught and returned as a `processingError` instead of throwing.
- 6 new xUnit tests added to `PdfTests` (ODC + O11): encrypted/corrupted PDF does not throw, returns the original bytes unchanged, and surfaces `processingError` with `RemovedEntryCount = 0` and `IsPassthrough = false`.
- 6 new xUnit tests added to `OpenXmlTests` (ODC + O11): same contract for encrypted/corrupted OOXML files.

- `README.md` — updated to cover both ODC and O11 platforms, NuGet diff table, O11 usage and build instructions

## Fixed

- `ExtractedMetadata` attribute `Length` in XIF corrected from IS default of 50 to unlimited
- Input/output parameter descriptions added to XIF (were empty after IS generation)

## Removed

*(nothing yet)*
