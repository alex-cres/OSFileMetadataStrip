using OutSystems.ExternalLibraries.SDK;

namespace FileMetadataStripping;

[OSInterface(Description = "Strips embedded metadata from uploaded files to prevent file metadata injection before files reach AI APIs or are stored.", IconResourceName = "FileMetadataStripping.resources.icon.png")]
public interface IFileMetadataStripping
{
    [OSAction(Description = "Strips embedded metadata from a file and returns the cleaned binary. Supports images (EXIF, IPTC, XMP — JPEG, PNG, TIFF, WebP, and 100+ formats), PDFs, Office Open XML documents (DOCX, XLSX, PPTX), and audio/video files (MP3, WAV, FLAC, OGG, MP4, MKV, AVI). Unrecognised formats (TXT, CSV, JSON, etc.) are returned unchanged with IsPassthrough set to true.", IconResourceName = "FileMetadataStripping.resources.icon.png")]
    FileMetadataResult StripFileMetadata(byte[] rawFile);
}
