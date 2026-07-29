FileMetadataStripping - Limitations

ODC Forge "Limitations" field - 1000-character max.
Keep this file under 1000 characters (excluding this header block).

---

Embedded objects - OOXML charts, images, and OLE containers retain their own metadata.
ICC profiles - removed with image metadata; sRGB fallback.
Memory - no size limit; entire file loaded into memory.
Unreadable files - encrypted or corrupted files returned unchanged; ExtractedMetadata includes a processingError note.
Body authors - stripped only when StripBodyAuthors = True.
Excel threaded comments - xl/threadedComments/ retains author names.
PDF embedded images - retain their own EXIF; not processed.
VBA macros and digital signatures - not stripped.
HEIC / HEIF - transcoded to JPEG; x265 HEVC is GPL-licensed. transcodedFormat key set.
APNG - reading fixed; writing needs ffmpeg. Without ffmpeg, output is JPEG.
DICOM - PHI tags (patient name, ID, dates) survive; no DICOM SDK bundled.
Animated WebP - per-frame metadata survives; libwebpmux native library not bundled.
WBMP - no reliable magic bytes; returned as passthrough.
