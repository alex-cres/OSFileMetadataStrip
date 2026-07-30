# Changelog

All notable changes to **OSFileMetadataStrip** are documented here.

Versions correspond to releases published on the [OutSystems Forge](https://www.outsystems.com/forge/). A version number is only assigned when the user explicitly publishes a release to Forge. All in-progress work is tracked under [Unreleased](./docs/versions/UNRELEASED.md).

---

## Versions

| Version | Date | Notes |
|---------|------|-------|
| [Unreleased](./docs/versions/UNRELEASED.md) | — | In-progress changes not yet published to Forge |
| [v0.1.5](./docs/versions/v0.1.5.md) | 2026-07-30 | Added: RTF, legacy binary Office (CFBF), Flat ODF, Word 2003 XML, SVG, EPUB, ORA, HEIC→JPEG transcode, DPX/CIN production attrs, extended audio (AIFF/APE/WavPack/MPC), 40+ image formats; OOXML/ODF template + macro-enabled variant coverage; HTML passthrough contract; Documents partial split by pipeline |
| [v0.1.4](./docs/versions/v0.1.4.md) | 2026-07-25 | Changed: BMP → passthrough; Fix: garbled UTF-8 characters in test files |
| [v0.1.3](./docs/versions/v0.1.3.md) | 2026-07-24 | Fix: TIFF metadata extraction, test helper bugs (Package flush, minimal AVI), GenerateSamples TIFF sample |
| [v0.1.2](./docs/versions/v0.1.2.md) | 2026-07-24 | Fix: image comment field now captured in ExtractedMetadata |
| [v0.1.1](./docs/versions/v0.1.1.md) | 2026-07-24 | ODF support, OOXML app/custom/body-author stripping, PDF annotation author removal, O11 extension, graceful error handling, XMP stream removal |
| [v0.1.0](./docs/versions/v0.1.0.md) | 2026-07-21 | Initial release |

---

*When a Forge release is published, provide the version number to create `docs/versions/v{x.y.z}.md`, add the entry to this table, and reset the Unreleased file.*
