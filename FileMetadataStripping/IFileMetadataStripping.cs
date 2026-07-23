using OutSystems.ExternalLibraries.SDK;

namespace FileMetadataStripping;

[OSInterface(Description = "Strips embedded metadata from uploaded files to prevent file metadata injection before files reach AI APIs or are stored.", IconResourceName = "FileMetadataStripping.resources.icon.png")]
public interface IFileMetadataStripping
{
    [OSAction(Description = "Strips embedded metadata from a file and returns the cleaned binary. Supports images (EXIF, IPTC, XMP — JPEG, PNG, TIFF, WebP, and 100+ formats), PDFs (Info dictionary, XMP catalog stream, and annotation Author fields), Office Open XML documents (DOCX, XLSX, PPTX — core properties including LastPrinted/Identifier/Version, application properties, custom properties; and optionally author names from tracked changes, comments, and xl/persons entries when StripBodyAuthors is true), ODF documents (ODT, ODS, ODP — creator, title, description, initial-creator, generator, editing metadata, and user-defined properties), and audio/video files (MP3, WAV, FLAC, OGG, MP4, MKV, AVI). Unrecognised formats (TXT, CSV, JSON, etc.) are returned unchanged with IsPassthrough set to true.", IconResourceName = "FileMetadataStripping.resources.icon.png")]
    FileMetadataResult StripFileMetadata(
        [OSParameter(Description = "The raw file bytes to strip.")]
        byte[] rawFile,
        [OSParameter(Description = "When true, also blanks author names and initials from tracked changes and comments inside document bodies (DOCX w:author/w:initials, XLSX comment authors, PPTX comment authors). Set to false (default) to preserve document body structure while still stripping all dedicated metadata properties.")]
        bool stripBodyAuthors);
}
