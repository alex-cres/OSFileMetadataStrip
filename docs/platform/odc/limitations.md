FileMetadataStripping - Limitations

ODC Forge "Limitations" field - 1000-character max.
Keep this file under 1000 characters (excluding this header block).

---

Embedded objects - OOXML charts, images, and OLE containers retain their own metadata.
ICC profiles - removed with image metadata; sRGB fallback.
Memory - no size limit; entire file loaded into memory.
Unreadable files - encrypted or corrupted files returned unchanged; ExtractedMetadata includes a processingError note.
Body authors - stripped only when StripBodyAuthors = True.
Excel threaded comments - xl/threadedComments/ retains author names; not stripped.
PDF embedded images - retain their own EXIF; not processed.
VBA macros - author data in vbaProject.bin not stripped.
Digital signatures - signer identity not stripped.
TIFF EXIF - always stripped; count reflects only metadata present at upload. Some encoders drop EXIF silently. XMP and Comment captured.
