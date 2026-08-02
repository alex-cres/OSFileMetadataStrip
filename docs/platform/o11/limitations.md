FileMetadataStripping - O11 Limitations

O11 Forge "Limitations" field - 1000-character max.
Keep this file body under 1000 characters (excluding this header block).

All limitations listed in docs/platform/odc/limitations.md apply here as well.
This file lists limitations that are O11-only.

---

GDI+ fallback (O11 only) - on OutSystems Personal Environment and other locked-down O11 hosts where the Magick.NET native runtime cannot initialise, the image strip pipeline automatically switches to a System.Drawing (GDI+) fallback engine.
Active stripping on fallback - only JPEG, PNG, GIF, BMP and TIFF are actively stripped; other recognised image formats (WebP, HEIC, AVIF, JXL, JP2, PSD, camera RAW, DDS, EXR, HDR, DPX/CIN, FITS, QOI, SGI/SUN, PCX/DCX, PNM, JBIG, XCF, WMF, ICO, DCM, TGA, MNG, and others) are returned unchanged with IsPassthrough=false, RemovedEntryCount=0, and an ExtractedMetadata processingError prefixed "GDI+ fallback:". Log and reject or retry.
Documents, SVG, audio and video - unaffected; identical behaviour on both engines.
Inactive on healthy hosts - the fallback is dormant when Magick.NET initialises normally.
