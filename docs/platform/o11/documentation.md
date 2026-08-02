INSTALLING THE EXTENSION
------------------------

1. Download the FileMetadataStripping extension from the OutSystems Forge.

2. Open Service Studio, open your app or module, and go to the Manage Dependencies dialog (Ctrl+Q).

3. Locate the FileMetadataStripping extension in the list, tick the StripFileMetadata action and the FileMetadataResult structure, then click Apply.

4. Publish the module. No server-side configuration is required.

If you are deploying to your own O11 environment for the first time:

1. Sign in to Service Center on the target environment.

2. Go to Factory > Extensions and upload FileMetadataStripping.xif (or install via LifeTime).

3. Publish the extension. All native dependencies (Magick.NET managed and native DLLs, the Microsoft VC++ 2015-2022 x64 runtime, and the required BCL shim assemblies) ship inside the XIF; there is nothing else to install on the server.


CONFIGURATION
-------------

The extension has no Site Properties, no configuration screen, and no per-tenant settings. Every call is stateless. Behaviour is controlled entirely through the StripFileMetadata input parameters.

Two-engine architecture (automatic, no configuration)

FileMetadataStripping on O11 ships with two image-processing engines and picks between them automatically at runtime. Documents, SVG, audio, and video pipelines are pure-managed and behave identically on both engines.

Primary engine - Magick.NET. Used on all healthy O11 hosts. Delivers the full format matrix (JPEG, PNG, GIF, TIFF, WebP, AVIF, HEIC, JXL, JPEG 2000, PSD, camera RAW, and every other image format listed in the Supported File Formats section).

Fallback engine - System.Drawing (GDI+). Used only on O11 hosts where the Magick.NET native runtime cannot initialise (OutSystems Personal Environment sandbox on outsystemscloud.com is the currently known case, HRESULT 0x8007045A / ERROR_DLL_INIT_FAILED). The extension detects the initialisation failure on the first image call, latches an AppDomain-scoped flag, and routes every subsequent image call through GDI+. GDI+ (gdiplus.dll) is a Windows built-in KnownDLL and is not subject to the native-code loader policies that block the Magick.NET native runtime.

Both engines produce the same FileMetadataResult contract. The only visible difference is the supported-format matrix on the fallback engine - see the Supported File Formats section below.


USAGE
-----

Call StripFileMetadata at the earliest point in any flow that accepts a file upload, before the file is forwarded to an AI API, stored, or processed further.

Input

RawFile (Binary Data) - the uploaded file in any supported format. Pass the Content field from a File Upload widget or a binary variable you have already loaded.

StripBodyAuthors (Boolean) - optional, default False. Set to True to also blank author names from tracked changes and comments inside OOXML document bodies: w:author and w:initials in DOCX (document.xml, headers, footers, footnotes, endnotes, comments.xml); author nodes in XLSX (xl/comments*.xml); name and initials in PPTX (ppt/commentAuthors.xml); and displayName and userId in xl/persons/person.xml (Excel 365 threaded comment authors). Leave False to strip only core, application, and custom properties.

Output - FileMetadataResult structure

CleanFile (Binary Data) The file with all embedded metadata removed. Use this in place of the original when calling AI APIs or saving to a database.

ExtractedMetadata (Text) JSON string of every metadata entry that was found and removed. Returns "[]" when the file contained no metadata. Use this for audit logging or security review. Contains a processingError key if the file could not be processed (e.g. encrypted or corrupted PDF/OOXML), or when the GDI+ fallback engine is active and the input is a recognised image in a format GDI+ cannot decode (see NOTES).

RemovedEntryCount (Integer) Total number of metadata entries removed. If greater than zero, the file carried embedded data that has now been stripped.

IsPassthrough (Boolean) True when the file format has no supported metadata containers (e.g. BMP, TXT, CSV, JSON, XML) and was returned unchanged. Use this in audit logs to distinguish passthrough files from actively processed files that happened to be clean.

Recommended pattern in a Server Action:

1. Receive the uploaded file as a Binary Data input parameter.

2. Call StripFileMetadata passing the binary as RawFile.

3. Use StripFileMetadata.CleanFile for all downstream calls (AI API, file storage, database).

4. If StripFileMetadata.RemovedEntryCount > 0, write StripFileMetadata.ExtractedMetadata to your audit log.

5. Optionally record IsPassthrough in the audit entry to distinguish passthrough files from clean processed files.

6. If StripFileMetadata.IsPassthrough is False and RemovedEntryCount is 0 and ExtractedMetadata contains the substring "GDI+ fallback:", the GDI+ fallback engine refused the format. Log the ExtractedMetadata processingError and either block the upload or retry through an alternate pipeline.


SUPPORTED FILE FORMATS
-----------------------

Every format listed below has an explicit xUnit test in the component's test projects. No format is claimed here that is not verified by a regression test.

Primary engine (Magick.NET, all healthy O11 hosts)

Standard raster images (JPEG, PNG, GIF, TIFF, WebP) Strips EXIF (camera data, GPS, descriptions), IPTC (captions, keywords), XMP, ICC profiles, image comments. Animated GIFs, animated WebPs, and multi-frame TIFFs are fully supported: metadata is stripped from every frame and all frames are preserved in the output.

AVIF Fully supported. EXIF, IPTC, XMP, and comments are stripped and the clean file is returned.

HEIC / HEIF (mif1 / msf1 brands) Detected via ISOBMFF ftyp brand check. Metadata is stripped and the output is transcoded to JPEG. The x265 HEVC encoder is GPL-licensed and cannot be bundled in a redistributable extension, so the original HEIC format is not preserved. ExtractedMetadata includes a transcodedFormat key explaining the format change.

APNG (Animated PNG) Detected by the acTL chunk. All animation frames are decoded and stripped.

RAW camera formats (ARW, CR2, DNG, NEF, ORF, PEF, RAF, X3F) Decoded via the underlying TIFF / CR2 structure; EXIF, XMP, and ICC profiles are removed.

Modern and HDR image formats (JPEG XL, JPEG 2000 JP2 / J2C / J2K / JPT, JPEG XR / WDP, Ultra HDR, OpenEXR, Radiance HDR, QOI) EXIF, XMP, ICC profiles, and encoder comments are removed. Radiance HDR encoder-inserted comment lines are stripped after write. JPT is decode-only.

Legacy raster (PSD / PSB, TGA, DDS, PCX / DCX, SGI, SUN Rasterfile, PICT, PCD / PCDS, FITS, JBIG, WMF, ICO, XCF, Netpbm PBM / PGM / PPM / PNM) EXIF, XMP, ICC profiles, and format-specific comments where present. GIMP XCF is transcoded to JPEG on write.

Film (DPX, CIN) image.Strip() plus explicit removal of any remaining dpx:* and cin:* per-image production attributes.

Medical (DICOM .dcm) Detected via 128-byte preamble + DICM signature; output is transcoded to JPEG. DICOM data-dictionary tag parsing is out of scope.

Multi-image containers (MPO, MNG) Every embedded image or frame is stripped and preserved.

SVG Removes title, desc, and metadata elements at every depth including RDF / Dublin Core payloads; output remains a valid SVG.

PDF and Adobe Illustrator (AI) Strips Title, Author, Subject, Keywords, Creator, Producer, catalog XMP metadata stream, and annotation Author fields.

RTF (Rich Text Format) Blanks author, title, subject, keywords, comment, operator, company, doccomm, category, hlinkbase, and manager control-word groups inside the info group.

Office Open XML (DOCX, DOTX, DOCM, DOTM, XLSX, XLTX, XLSM, XLTM, PPTX, POTX, PPSX, PPTM, POTM, PPSM) Core properties, application properties, custom property key/value pairs, and the embedded page-preview thumbnail. Body author stripping (tracked changes, comment authors, Excel 365 xl/persons entries) requires StripBodyAuthors = True.

Legacy binary Office (DOC, DOT, XLS, XLT, PPT, POT, PPS) Deletes both OLE property-set streams (SummaryInformation, DocumentSummaryInformation) and consolidates the CFBF container so the freed sectors are dropped from the output.

ODF documents (ODT, ODS, ODP, OTT, OTS, OTP, ODG, OTG, ODC, ODF, ODB, ODI) Strips dc:creator, dc:title, dc:description, dc:subject, meta:initial-creator, meta:generator, meta:editing-cycles, meta:editing-duration, and all meta:user-defined properties.

Flat ODF (FODT, FODS, FODP) Single-file XML variant; same strip surface as ZIP-based ODF.

Word 2003 XML (WordProcessingML .xml) Every child of DocumentProperties and CustomDocumentProperties. Body tracked-change and comment author attributes are blanked when StripBodyAuthors = True.

EPUB Dublin Core metadata in the OPF package and every OPF meta refinement. OPF paths containing .. path segments are rejected as a Zip Slip guard.

ORA (Open Raster) name and description attributes on every element in stack.xml.

Audio (MP3, WAV, FLAC, OGG Vorbis / Opus, M4A, M4B, WMA, AIFF / AIFC, APE, WavPack, MPC) ID3 tags, Vorbis / Opus comments, RIFF INFO chunks, iTunes MP4 atoms, ASF header extension objects, AIFF ID3 chunks, APE tags.

Video (MP4, MKV, AVI, MOV, WebM, WMV, M4V, 3GP, 3G2) Metadata atoms and tags.

Passthrough (BMP, DIB, WBMP, XBM, XPM, TXT, CSV, MD, JSON, XML, HTML) Returned unchanged with IsPassthrough = true.

Fallback engine (System.Drawing / GDI+, locked-down O11 hosts only)

The fallback engine partitions every image call into one of three deterministic outcomes. Document, SVG, audio, and video pipelines are unaffected and behave identically to the primary engine.

Actively stripped - JPEG, PNG, GIF (multi-frame), BMP, TIFF (multi-page). GDI+ PropertyItem removal path; IsPassthrough = false; RemovedEntryCount greater than zero when metadata was present; ExtractedMetadata is a JSON list of the PropertyItem names that were removed.

Recognised-but-unsupported error contract - WebP, HEIC, HEIF, AVIF, JXL, JPEG 2000, JPEG XR, PSD/PSB, DDS, EXR, HDR, DPX/CIN, FITS, QOI, SGI, SUN, PCX/DCX, PNM, JBIG, XCF, WMF, ICO, DCM, TGA, MNG, and camera RAW variants. GDI+ cannot decode these; the call returns IsPassthrough = false, RemovedEntryCount = 0, CleanFile = original bytes verbatim, and ExtractedMetadata contains a processingError value prefixed with the fixed marker "GDI+ fallback:" and the format name. The caller receives an explicit failure signal instead of a silent passthrough - handle it in the Server Action (log and reject, or retry via an alternate pipeline).

Passthrough - unrecognised bytes (plain text, PDF-only when GDI+ engine sees it as image bytes, arbitrary binaries that are not images). IsPassthrough = true; identical to the primary engine.


NOTES
-----

Two-engine dispatch is transparent - StripFileMetadata accepts the same inputs and returns the same FileMetadataResult on both engines. No new fields, no interface break, no per-tenant configuration.

Detecting the active engine - Callers do not need to know which engine is active. To distinguish a fallback-engine error result from a normal processing error, inspect ExtractedMetadata: the fallback engine begins its processingError value with "GDI+ fallback:".

Consumer-facing predicate on the O11 side (both engines):

- Actively stripped:      IsPassthrough == false && RemovedEntryCount > 0.
- Fallback declined:      IsPassthrough == false && RemovedEntryCount == 0 && ExtractedMetadata contains "GDI+ fallback:".
- Not an image / not scoped: IsPassthrough == true.

Documents, SVG, audio, and video pipelines are pure-managed and identical on both engines - the fallback engine has no effect on them.

Empty and single-byte inputs - Returned as Passthrough with an empty ExtractedMetadata.

Decode errors - When the active engine recognises the format but the decode or encode step throws, the original file is returned unchanged, ExtractedMetadata contains a processingError entry, and IsPassthrough is False. Log the processingError and consider blocking the upload.

Security pipeline position - Layer 1, Priority 1 - highest. Run before any downstream AI call, storage, or forwarding step. Consider OSStegoGuard (Layer 1, Priorities 2 and 3) and OSQRGuard (Layer 1, Priority 4) alongside for pixel- and QR-code prompt-injection coverage.
