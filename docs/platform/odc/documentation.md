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

Images (JPEG, PNG, GIF, BMP, TIFF, WebP, TGA, and 100+ more)
Strips: EXIF, IPTC, XMP, ICC profiles, image comments.
Animated GIFs and multi-frame TIFFs are fully supported: metadata
is stripped from every frame and all frames are preserved in the output.

PDF
Strips: Title, Author, Subject, Keywords, Creator, Producer, the
catalog XMP metadata stream (/Metadata entry), and the /Author entry
from all comment, sticky-note, and markup annotations. Distinct
annotation author names are recorded in ExtractedMetadata under the
key annotationAuthors.

Office Open XML (DOCX, XLSX, PPTX)
Always strips: core properties (Creator, LastModifiedBy, Created,
Modified, Title, Subject, Description, Keywords, Category, Revision,
LastPrinted, Identifier, Version), application properties (Application,
Company, Manager, AppVersion, Template, HyperlinkBase), and all custom
property key/value pairs.

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

ODF (ODT, ODS, ODP)
Strips: dc:creator, dc:title, dc:description, dc:subject,
meta:initial-creator, meta:generator, meta:editing-cycles,
meta:editing-duration, and all meta:user-defined properties.
Values are recorded in ExtractedMetadata under the keys creator,
title, description, subject, initialCreator, generator,
editingCycles, editingDuration, and userDefinedProperties.

Audio (MP3, FLAC, OGG, WAV, M4A, WMA)
Strips: ID3 tags, Vorbis comments, all metadata atoms.

Video (MP4, MOV, AVI, MKV, WebM, WMV)
Strips: all metadata atoms and tags.

Plain text, CSV, JSON, XML, and unrecognised formats
Passthrough - file returned unchanged, IsPassthrough = true.


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
