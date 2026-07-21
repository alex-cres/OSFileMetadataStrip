using System;
using System.Collections;
using System.Data;
using OutSystems.HubEdition.RuntimePlatform;
using OutSystems.RuntimePublic.Db;

namespace OutSystems.NssFileMetadataStripping {

	public class CssFileMetadataStripping: IssFileMetadataStripping {

		/// <summary>
		/// Strips metadata from a file. Supports images (EXIF/IPTC/XMP), PDFs, OOXML (DOCX/XLSX/PPTX), and audio/video (MP3/WAV/FLAC/OGG/MP4/MKV/AVI). Unrecognised formats returned unchanged with IsPassthrough=true.
		/// </summary>
		/// <param name="ssRawFile"></param>
		/// <param name="ssStripFileMetadata"></param>
		public void MssStripFileMetadata(byte[] ssRawFile, out RCFileMetadataResultRecord ssStripFileMetadata) {
			ssStripFileMetadata = new RCFileMetadataResultRecord(null);
			// TODO: Write implementation for action
		} // MssStripFileMetadata

	} // CssFileMetadataStripping

} // OutSystems.NssFileMetadataStripping

