FileMetadataStripping - Limitations

ODC Forge "Limitations" field - 1000-character max.
Keep this file under 1000 characters (excluding this header block).

---

Embedded objects - OOXML charts, images, and OLE containers retain their own metadata.

ICC profiles - removed alongside image EXIF/IPTC/XMP; sRGB fallback.

Memory - no file-size limit; entire file loaded into memory.

Steganography - pixel-level payloads not detected. Pair with OSStegoGuard and OSQRGuard.

Unreadable files - encrypted/corrupted PDF/OOXML/audio-video returned unchanged with a processingError note; no exception raised.

Body authors - stripped only when StripBodyAuthors = True.

Excel threaded comments - xl/threadedComments/ XML content retains author names; not stripped.

PDF embedded images - JPEG/PNG inside PDF content streams retain their own EXIF.

VBA projects - macro-enabled OOXML retain author data in vbaProject.bin.

Digital signatures - signer identity in _xmlsignatures/ not stripped.
