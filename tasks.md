1. ~~alternatives to not have the SixLabors.ImageSharp package~~ ✓ done
2. make a O11 version of the component as well
3. ~~review any stale documentation~~ ✓ done
4. ~~Add support for audio files (MP3, WAV, FLAC, OGG, AAC)~~ ✓ done
5. ~~Add support for video files (MP4, MOV, AVI, MKV, WebM)~~ ✓ done
6. ~~Fix animated GIF and multi-frame TIFF support — switch StripImageMetadata from MagickImage to MagickImageCollection so all frames are preserved after stripping~~ ✓ done
7. ~~Strip PDF XMP metadata streams (catalog /Metadata entry) in addition to the /Info dictionary~~ ✓ done
8. Strip OOXML app properties (docProps/app.xml) and custom properties (docProps/custom.xml)
9. Graceful handling of encrypted/password-protected PDFs and OOXML files — return original unchanged with a processingError audit note instead of throwing
10. Investigate stripping author names from tracked changes and comments inside OOXML document bodies