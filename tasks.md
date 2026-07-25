1. ~~alternatives to not have the SixLabors.ImageSharp package~~ ✓ done
2. ~~make a O11 version of the component as well~~ ✓ done
3. ~~review any stale documentation~~ ✓ done
4. ~~Add support for audio files (MP3, WAV, FLAC, OGG, AAC)~~ ✓ done
5. ~~Add support for video files (MP4, MOV, AVI, MKV, WebM)~~ ✓ done
6. ~~Fix animated GIF and multi-frame TIFF support — switch StripImageMetadata from MagickImage to MagickImageCollection so all frames are preserved after stripping~~ ✓ done
7. ~~Strip PDF XMP metadata streams (catalog /Metadata entry) in addition to the /Info dictionary~~ ✓ done
8. ~~Strip OOXML app properties (docProps/app.xml) and custom properties (docProps/custom.xml)~~ ✓ done
9. ~~Graceful handling of encrypted/password-protected PDFs and OOXML files — return original unchanged with a processingError audit note instead of throwing~~ ✓ done
10. ~~Investigate stripping author names from tracked changes and comments inside OOXML document bodies~~ ✓ done (implemented as opt-in via StripBodyAuthors parameter)
11. Strip SVG-native XML metadata — `<title>`, `<desc>`, and Dublin Core RDF `<metadata>` elements are plain text nodes that survive `Strip()` today; any tool that extracts SVG text for AI context would expose them verbatim.
12. Strip EPUB metadata — EPUB is a ZIP with an OPF manifest containing `dc:description`, `dc:creator`, `dc:rights`, etc. as plain text; document-processing pipelines that unzip EPUB before sending to AI include these fields in the text context. Currently mis-classified as OpenXml and left untouched.
13. Remove OOXML embedded thumbnail (`docProps/thumbnail.jpeg`) — Office apps embed a rendered page preview inside the ZIP; if the document contained injected text in a visible area the thumbnail carries that image to a vision model.
14. Parse and report XMP content — images and PDFs with XMP currently report `"xmp": "present"` in `ExtractedMetadata`; parse the XMP XML to surface the actual field values (dc:creator, xmp:CreateDate, etc.) so the audit log is meaningful.
15. Add `ExtractFileMetadata` read-only action — exposes the existing extraction logic without stripping; useful for audit/preview workflows where the caller wants to inspect what metadata a file contains before deciding to strip.
16. Extend iPhone photo testing (HEIC/HEIF) — HEIC/HEIF is the default format for iPhone cameras and carries significant EXIF metadata; Magick.NET is expected to handle it but there are no dedicated tests and the format is not documented in the README.