using System;
using System.Collections;
using System.Data;
using OutSystems.HubEdition.RuntimePlatform;

namespace OutSystems.NssFileMetadataStripping {

	public interface IssFileMetadataStripping {

		/// <summary>
		/// Strips embedded metadata from a file and returns the cleaned binary. Supports images (EXIF, IPTC, XMP — JPEG, PNG, TIFF, WebP, AVIF, HEIC, and 100+ formats), PDFs (Info dictionary, XMP catalog stream, and annotation Author fields), Office Open XML documents (DOCX, XLSX, PPTX — core properties including LastPrinted/Identifier/Version, application properties, custom properties, embedded thumbnail; and optionally author names from tracked changes, comments, and xl/persons entries when StripBodyAuthors is true), legacy binary Office (DOC, DOT, XLS, XLT, PPT, POT, PPS — SummaryInformation and DocumentSummaryInformation streams), RTF (info-group control words), ODF documents (ODT, ODS, ODP), EPUB (Dublin Core in the OPF), ORA (stack.xml name/description), SVG (title/desc/metadata elements), and audio/video files (MP3, WAV, FLAC, OGG Vorbis/Opus, MP4, MKV, AVI, MOV, WebM, WMA, WMV, M4V, M4A, M4B, 3GP, 3G2, AIFF/AIFC, APE, WavPack, MPC). Unrecognised formats (TXT, CSV, JSON, HTML, etc.) are returned unchanged with IsPassthrough set to true.
		/// </summary>
		/// <param name="ssRawFile">The uploaded file in any supported format: images (JPEG, PNG, TIFF, WebP, AVIF, HEIC, etc.), PDF, Office documents (DOCX, XLSX, PPTX; DOC, XLS, PPT and templates), RTF, ODF (ODT, ODS, ODP), EPUB, ORA, SVG, or audio/video (MP3, WAV, MP4, MKV, AVI, MOV, etc.).</param>
		/// <param name="ssStripBodyAuthors">When True, also blanks author names and initials from tracked changes and comments inside document bodies (DOCX w:author/w:initials, XLSX comment authors, PPTX comment authors) and Excel 365 xl/persons entries. Set to False (default) to preserve document body structure while still stripping all dedicated metadata properties.</param>
		/// <param name="ssStripFileMetadata">The stripped file and audit metadata. CleanFile is safe to forward to AI APIs. ExtractedMetadata contains the removed entries as JSON. RemovedEntryCount and IsPassthrough support audit logging.</param>
		void MssStripFileMetadata(byte[] ssRawFile, bool ssStripBodyAuthors, out RCFileMetadataResultRecord ssStripFileMetadata);

	} // IssFileMetadataStripping

} // OutSystems.NssFileMetadataStripping
