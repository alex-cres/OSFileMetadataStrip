# Unreleased

Changes in progress — not yet published to OutSystems Forge.

> When publishing a Forge release, tell the OutSystems Extension Builder agent the version number.

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

- `README.md` — updated to cover both ODC and O11 platforms, NuGet diff table, O11 usage and build instructions
- `AGENTS.md` — updated project structure, coding conventions, NuGet diff table, and O11 test adapter pattern
- Agent and skill documentation updated with XIF-based workflow, descriptions/icons O11 vs ODC contrast, and backup procedure

## Fixed

- `ExtractedMetadata` attribute `Length` in XIF corrected from IS default of 50 to unlimited
- Input/output parameter descriptions added to XIF (were empty after IS generation)

## Removed

*(nothing yet)*
