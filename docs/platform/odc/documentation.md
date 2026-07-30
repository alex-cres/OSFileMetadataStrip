ADDING THE LIBRARY TO YOUR APP
-------------------------------

1. Open your ODC app or library in ODC Studio.

2. Click the Dependencies icon (puzzle piece) in the toolbar.

3. Search for FileMetadataStripping and select it.

4. Tick StripFileMetadata and the FileMetadataResult structure,
then click Apply.


USAGE
-----

Call StripFileMetadata at the earliest point in any flow that
accepts a file upload, before the file is forwarded to an AI API,
stored, or processed further.

Input
RawFile (Binary Data) - the uploaded file in any supported format.
Pass the Content field from a File Upload widget or a binary
variable you have already loaded.

StripBodyAuthors (Boolean) - optional, default False.
Set to True to also blank author names from tracked changes and
comments inside OOXML document bodies: w:author and w:initials
in DOCX (document.xml, headers, footers, footnotes, endnotes,
comments.xml); author nodes in XLSX (xl/comments*.xml); name and
initials in PPTX (ppt/commentAuthors.xml). Leave False to strip
only core, application, and custom properties.

Output - FileMetadataResult structure
CleanFile (Binary Data)
The file with all embedded metadata removed. Use this in place
of the original when calling AI APIs or saving to a database.

ExtractedMetadata (Text)
JSON string of every metadata entry that was found and removed.
Returns "[]" when the file contained no metadata. Use this for
audit logging or security review.

RemovedEntryCount (Integer)
Total number of metadata entries removed. If greater than zero,
the file carried embedded data that has now been stripped.

IsPassthrough (Boolean)
True when the file format has no supported metadata containers
(e.g. TXT, CSV, JSON, XML). The file is returned unchanged in
CleanFile. Use this in audit logs to distinguish passthrough
files from actively processed files that happened to be clean.

Recommended pattern in a Server Action:

1. Receive the uploaded file as a Binary Data input parameter.
2. Call StripFileMetadata passing the binary as RawFile.
3. Use StripFileMetadata.CleanFile for all downstream calls
(AI API, file storage, database).
4. If StripFileMetadata.RemovedEntryCount > 0, write
StripFileMetadata.ExtractedMetadata to your audit log.
5. Optionally record IsPassthrough in the audit entry to
distinguish passthrough files from clean processed files.


SUPPORTED FILE FORMATS
-----------------------

Every format listed below has an explicit xUnit test in the
component's test project. No format is claimed here that is not
verified by a regression test.

Standard raster images (JPEG, PNG, GIF, TIFF, WebP)
Strips: EXIF, IPTC, XMP, ICC profiles, image comments.
Animated GIFs, animated WebPs, and multi-frame TIFFs are fully
supported: metadata is stripped from every frame and all frames
are preserved in the output.

AVIF
Fully supported. EXIF, IPTC, XMP, and comments are stripped and
the clean file is returned.

HEIC / HEIF (mif1 / msf1 brands)
Detected via ISOBMFF ftyp brand check. Metadata is stripped and the
output is transcoded to JPEG. The x265 HEVC encoder is GPL-licensed
and cannot be bundled in a redistributable library, so the original
HEIC format is not preserved. ExtractedMetadata includes a
transcodedFormat key explaining the format change.

APNG (Animated PNG)
Detected by the acTL chunk. All animation frames are decoded and
stripped. Writing APNG requires ImageMagick's video delegate
(ffmpeg). When ffmpeg is present the output is APNG; when it is
absent the clean output is transcoded to JPEG and ExtractedMetadata
includes a transcodedFormat key.

PDF
Strips: Title, Author, Subject, Keywords, Creator, Producer, the
catalog XMP metadata stream (/Metadata entry), and the /Author entry
from all comment, sticky-note, and markup annotations. Distinct
annotation author names are recorded in ExtractedMetadata under the
key annotationAuthors.

RTF (Rich Text Format)
Detected via the 6-byte prefix {\rtf1. The file is scanned as
ISO-8859-1 text (RTF is 7-bit ASCII on disk with \'HH hex escapes for
non-ASCII, so Latin-1 preserves every byte 1:1) and the string-bearing
control-word groups inside the \info group are blanked. The control
word is retained with an empty payload so the RTF structure remains
well-formed for readers.

Content-bearing control words targeted: \author, \title, \subject,
\keywords, \comment (private, invisible), \operator, \company,
\doccomm (visible "Comments" in Word Properties), \category,
\hlinkbase (hyperlink base URL), \manager. Numeric control words
(\version, \vern, \nofpages, revision timestamps, edit-minute counters)
are preserved because they are not user-controlled prompt-injection
vectors and removing them can break some readers.

Removed values are recorded in ExtractedMetadata under the control-word
name (author, title, subject, ...). If the same control word appears
more than once (a rare pattern in annotation trails), every occurrence
is captured as a JSON array.

Office Open XML (DOCX, XLSX, PPTX)
Always strips: core properties (Creator, LastModifiedBy, Created,
Modified, Title, Subject, Description, Keywords, Category, Revision,
LastPrinted, Identifier, Version), application properties (Application,
Company, Manager, AppVersion, Template, HyperlinkBase), all custom
property key/value pairs, and the embedded page-preview thumbnail
(docProps/thumbnail.jpeg, .png, .emf, .wmf, .gif, .tiff) together
with its _rels/.rels thumbnail relationship. Removing the thumbnail
prevents a rendered page preview from reaching a vision model.
When a thumbnail was present, ExtractedMetadata includes a
thumbnail key naming the removed part.

When StripBodyAuthors = True, also blanks author names from tracked
changes and comments inside the document body: w:author and w:initials
in DOCX (document.xml, headers, footers, footnotes, endnotes,
comments.xml); author nodes in XLSX (xl/comments*.xml); name and
initials in PPTX (ppt/commentAuthors.xml); and displayName and userId
in xl/persons/person.xml (Excel 365 threaded comment authors). Distinct
author names are sorted alphabetically and recorded in ExtractedMetadata
under the key strippedAuthors. The count is included in RemovedEntryCount.

Application property values are recorded individually in
ExtractedMetadata: appApplication, appCompany, appManager,
appVersion, appTemplate, appHyperlinkBase.

Custom properties are recorded as a JSON object under the key
customProperties in ExtractedMetadata.

Legacy binary Office (DOC, DOT, XLS, XLT, PPT, POT, PPS)
Word / Excel / PowerPoint 97 - 2003 files share the same Compound
File Binary Format (CFBF, also called OLE Compound Document)
container. Detected via the 8-byte magic D0 CF 11 E0 A1 B1 1A E1,
checked before the ZIP PK signature so there is no clash with
OOXML, ODF, EPUB, or ORA.

Deletes both OLE property-set streams: the SummaryInformation
stream (Title, Subject, Author, Keywords, Comments, Template,
Last-Saved-By, Application, revision and edit-time counters,
create / save / print dates) and the DocumentSummaryInformation
stream (Category, Manager, Company, ContentStatus, Language, and
all user-defined custom properties). After deletion the CFBF
container is consolidated so the freed sectors are dropped from
the output - the raw property values do not survive in unallocated
space.

Well-named properties are captured in ExtractedMetadata before the
streams are deleted, for audit: summaryInformation for the first
stream, documentSummaryInformation for the second, and
customProperties for user-defined properties. A single detection
helper and a single strip method cover all seven Office extensions.

If the file has the CFBF magic bytes but the container is truncated
or corrupt, the original file is returned unchanged and
ExtractedMetadata contains a processingError key. No exception is
raised.

ODF (ODT, ODS, ODP)
Strips: dc:creator, dc:title, dc:description, dc:subject,
meta:initial-creator, meta:generator, meta:editing-cycles,
meta:editing-duration, and all meta:user-defined properties.
Values are recorded in ExtractedMetadata under the keys creator,
title, description, subject, initialCreator, generator,
editingCycles, editingDuration, and userDefinedProperties.

Flat ODF (FODT, FODS, FODP)
Single-file XML variant of ODF. Detected by the <office:document>
root element in the OASIS office namespace
(urn:oasis:names:tc:opendocument:xmlns:office:1.0) - the file is
not a ZIP. Parsed with XDocument.Load and processed by the same
strip helper as the ZIP-based ODF path, so the strip surface and
the ExtractedMetadata keys (creator, title, description, subject,
initialCreator, generator, editingCycles, editingDuration,
userDefinedProperties) are identical. The output remains a valid
Flat ODF XML document.

Word 2003 XML (WordProcessingML - .xml)
Detected by the <w:wordDocument> root element in the WordProcessingML
namespace (http://schemas.microsoft.com/office/word/2003/wordml).
Strips every child of <o:DocumentProperties> - Author, LastAuthor,
Company, Manager, Title, Subject, Keywords, Description, Category,
Template, HyperlinkBase, Application, AppVersion, TotalTime,
LastPrinted, Created, LastSaved, and the Pages / Words / Characters /
CharactersWithSpaces / Lines / Paragraphs revision counters - and
removes every child of <o:CustomDocumentProperties>. When
StripBodyAuthors = True, also blanks the w:author and aml:author
attributes on tracked-change and comment elements throughout the
document body.

Removed values are captured in ExtractedMetadata under
documentProperties (built-in properties, keyed by element name),
customDocumentProperties (user-defined properties, keyed by the
name attribute), and bodyAuthors (populated only when
StripBodyAuthors = True; distinct author names sorted
alphabetically). The output remains a valid Word 2003 XML document.

EPUB
Detected by the ZIP mimetype entry. Reads META-INF/container.xml to
locate the OPF package document, then blanks every Dublin Core
element (dc:creator, dc:title, dc:description, dc:publisher,
dc:rights, dc:subject, dc:language, dc:date, dc:identifier, ...) and
every OPF <meta> refinement inside the metadata section. The original
values are recorded in ExtractedMetadata under the dc:* keys, with
repeated elements preserved as arrays. OPF paths containing .. path
segments are rejected as a Zip Slip guard; the original file is
returned unchanged with a processingError entry.

ORA (Open Raster)
Detected by the ZIP mimetype entry (image/openraster). Blanks the
name and description attributes on every element in stack.xml
(image, stack, layer, mask, text). Structural attributes (w, h, x,
y, opacity, src, mask-src, composite-op, visibility) are preserved
so the image still renders correctly. Removed attribute values are
recorded in ExtractedMetadata.

SVG
Parsed as XML. Removes <title>, <desc>, and <metadata> elements at
every depth, matched by local name so unnamespaced children are also
cleaned. Removed text content is recorded in ExtractedMetadata under
the keys title, desc, and metadata. The output remains a valid SVG.

DPX and CIN (film image formats)
Routed through the image pipeline. In addition to image.Strip(),
any remaining per-image production attributes prefixed dpx:* (DPX)
or cin:* (CIN) - film title, origination device, source filename,
frame position, and so on - are explicitly removed from the output.
The captured values are recorded in ExtractedMetadata under the
dpx and cin keys.

RAW camera formats
ARW (Sony), CR2 (Canon), DNG (Adobe), NEF (Nikon), ORF (Olympus),
PEF (Pentax), RAF (Fuji), X3F (Sigma). Decoded via the underlying
TIFF/CR2 structure; EXIF, XMP, and ICC profiles are removed.

Modern and HDR image formats
JPEG XL (JXL), JPEG 2000 (JP2 and the raw code streams J2C / J2K /
JPT), JPEG XR (JXR / WDP), Ultra HDR (UHDR), OpenEXR (EXR),
Radiance HDR (.hdr), and QOI. EXIF, XMP, ICC profiles, and encoder
comments are removed. Radiance HDR encoder-injected
"# Created by ImageMagick" comment lines are stripped after write.
JPT is decode-only — Magick.NET-Q8's OpenJPEG build does not
compile in the JPT encoder, so JPT input is returned unchanged with
a processingError note.

Legacy raster formats
PSD / PSB (Photoshop), TGA (Truevision), DDS (DirectDraw Surface),
PCX (single-page) and DCX (multi-page Paintbrush), SGI, SUN
Rasterfile, PICT, PCD / PCDS (Photo CD), FITS, JBIG, WMF (Windows
Metafile), ICO (Windows Icon), XCF (GIMP), and Netpbm (PBM, PGM,
PPM, PNM). EXIF, XMP, ICC profiles, and format-specific comments
are removed where present. GIMP XCF is transcoded to JPEG on write.

Medical imaging (DICOM .dcm)
Detected via 128-byte preamble + DICM signature; output is
transcoded to JPEG (pixel data preserved). DICOM data-dictionary
tag parsing (PHI fields such as PatientName, PatientID, StudyDate,
InstitutionName) is out of scope for this release.

MPO and MNG (multi-image containers)
Every embedded image / frame is stripped and preserved.

Audio (MP3, WAV, FLAC, OGG Vorbis / Opus, M4A, M4B, WMA, AIFF / AIFC,
APE, WavPack, MPC)
Strips: ID3 tags, Vorbis / Opus comments, RIFF INFO chunks, iTunes
MP4 atoms, ASF header extension objects (title, artist, album,
comment, and so on), AIFF ID3 chunks, and APE tags. AIFF, APE
(Monkey's Audio), WavPack (.wv) and MPC (Musepack SV7 and SV8) are
detected by their respective magic bytes (FORM+AIFF/AIFC, "MAC ",
"wvpk", "MPCK" or "MP+"+SV7-marker) and routed to the same
TagLibSharp-backed strip pipeline as the other audio formats.

Video (MP4, MKV, AVI, MOV, WebM, WMV, M4V, 3GP, 3G2)
Strips: all metadata atoms and tags (title, comment, encoder, and
so on). 3GP and 3G2 are the ISOBMFF variants used by legacy mobile
video recorders.

Passthrough (BMP, DIB, WBMP, XBM, XPM, TXT, CSV, MD, JSON, XML, HTML,
and any unrecognised format)
File returned unchanged, IsPassthrough = true. BMP, DIB, WBMP, XBM,
and XPM have no standard metadata containers and are always returned
unchanged regardless of content.


NOTES
-----

No configuration is required. Format detection is automatic and
based on magic bytes - no file extension or MIME type hint is
needed.

Format is preserved: the output file is always in the same format
as the input.

If an audio or video file cannot be fully parsed by the media
engine, the original file is returned unchanged and ExtractedMetadata
will contain a processingError key explaining why stripping was
skipped.

If a PDF or Office Open XML file is encrypted, password-protected,
or corrupted and cannot be opened, the original file is returned
unchanged and ExtractedMetadata will contain a processingError key.
RemovedEntryCount will be 0 and IsPassthrough will be false.

For animated GIFs and multi-frame TIFFs, metadata extraction reads
the first frame only (file-level metadata is stored there). All frames
are stripped and written to the output.

For Radiance HDR files, encoder-inserted comment lines (such as
# Created by ImageMagick) are removed from the output in addition
to the standard metadata strip. Only the #?RADIANCE format
identification line is preserved. ExtractedMetadata will not
include encoder artifact lines under the comment key.

Known gaps in the current release
- DICOM medical imaging files are detected and routed through the
  image pipeline, but PHI tags carried in the DICOM header (patient
  name, patient ID, study dates, institution) survive stripping. A
  DICOM-aware SDK would be required to clear those fields and none
  is bundled.
- Animated WebP files are decoded and their file-level metadata is
  stripped, but per-frame metadata chunks survive because the
  libwebpmux native library required to rewrite them is not bundled.
- WBMP has no reliable magic bytes and is treated as passthrough.
