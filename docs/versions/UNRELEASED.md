# Unreleased

Changes in progress — not yet published to OutSystems Forge.

> When publishing a Forge release, tell the OutSystems Extension Builder agent the version number. It will promote this file to `docs/versions/v{x.y.z}.md` and reset this file.

---

## Added

- Initial project scaffold: .NET 10 Class Library (`FileMetadataStripping`)
- `IFileMetadataStripping` interface decorated with `[OSInterface]` exposing `StripFileMetadata`
- `FileMetadataStripping` class implementing metadata stripping via SixLabors.ImageSharp 3.1.8
  - Clears `ExifProfile`, `IptcProfile`, and `XmpProfile` before re-encoding
  - Re-encodes as JPEG at quality 90 to ensure metadata is not propagated
- `StripFileMetadata` now returns `FileMetadataResult` structure instead of bare `BinaryData`:
  - `CleanFile` (BinaryData) — the stripped file
  - `ExtractedMetadata` (Text) — JSON of all removed entries for policy review
  - `RemovedEntryCount` (Integer) — number of metadata entries removed
- `FileMetadataResult.cs` — new `[OSStructure]` struct added to the project
- `FileMetadataStripping.Tests` xUnit test project (9 tests, all passing)
  - Happy path: clean image round-trip, dimension preservation, decodability
  - Security: EXIF removed, IPTC removed, XMP removed, all profiles removed simultaneously
  - Sanity: verifies test data actually contains metadata before stripping
- `AGENTS.md` — repo-level context and conventions for AI agents
- `.github/agents/outsystems-ext-builder.agent.md` — extension lifecycle agent
- `.github/agents/test-runner.agent.md` — test build/run/analysis subagent
- `.github/skills/os-odc-external-lib/` — ODC External Library lifecycle skill
- `.github/skills/os-o11-extension/` — O11 Integration Studio extension skill
- `CHANGELOG.md` — version index
- `docs/versions/UNRELEASED.md` — this file
- Implementation guide: `01-exif-metadata-stripping.md` (attack vector, options A/B)

## Changed

- `README.md` updated with full project objective, Server Actions table, requirements, usage, and build instructions
- `SixLabors.ImageSharp` downgraded from 4.0.0 (commercial license) to 3.1.8 (Apache 2.0 / FOSS)

## Fixed

*(nothing yet)*

## Removed

- `Class1.cs` placeholder replaced with `FileMetadataStripping.cs` implementation

