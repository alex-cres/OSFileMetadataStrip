FileMetadataStripping - Limitations

ODC Forge "Limitations" field - 1000-character max.
Keep this file under 1000 characters (excluding this header block).

---

Office Open XML - only core document properties are stripped; tracked changes, comments, embedded objects, and custom properties retain their metadata.

ICC colour profiles - removed from images alongside EXIF/IPTC/XMP; output images fall back to sRGB rendering.

Memory usage - no file-size limit is enforced; the entire file is loaded into memory. Very large files may hit ODC platform binary limits.

Content only - metadata containers are removed, but steganographic payloads hidden inside pixel values are not detected or disrupted. Pair with OSStegoGuard and OSQRGuard.

Unreadable audio/video - files that cannot be parsed by the media engine are returned unchanged with a processingError note in ExtractedMetadata; no exception is raised.
