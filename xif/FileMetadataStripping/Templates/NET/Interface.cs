using System;
using System.Collections;
using System.Data;
using OutSystems.HubEdition.RuntimePlatform;

namespace OutSystems.NssFileMetadataStripping {

	public interface IssFileMetadataStripping {

		/// <summary>
		/// Strips metadata from a file. Supports images (EXIF/IPTC/XMP), PDFs, OOXML (DOCX/XLSX/PPTX), and audio/video (MP3/WAV/FLAC/OGG/MP4/MKV/AVI). Unrecognised formats returned unchanged with IsPassthrough=true.
		/// </summary>
		/// <param name="ssRawFile">The uploaded file in any supported format: images (JPEG, PNG, TIFF, WebP, etc.), PDF, Office documents (DOCX, XLSX, PPTX), or audio/video (MP3, WAV, MP4, etc.).</param>
		/// <param name="ssStripFileMetadata">The stripped file and audit metadata. CleanFile is safe to forward to AI APIs. ExtractedMetadata contains the removed entries as JSON. RemovedEntryCount and IsPassthrough support audit logging.</param>
		void MssStripFileMetadata(byte[] ssRawFile, out RCFileMetadataResultRecord ssStripFileMetadata);

	} // IssFileMetadataStripping

} // OutSystems.NssFileMetadataStripping
