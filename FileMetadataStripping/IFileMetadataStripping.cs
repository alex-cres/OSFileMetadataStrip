using OutSystems.ExternalLibraries.SDK;

namespace FileMetadataStripping;

[OSInterface(Description = "Strips embedded metadata from uploaded files to prevent file metadata injection before files reach AI APIs or are stored.")]
public interface IFileMetadataStripping
{
    [OSAction(Description = "Strips EXIF, IPTC, and XMP metadata from an image file. Returns the clean binary and the extracted metadata for policy review or audit logging.")]
    FileMetadataResult StripFileMetadata(byte[] rawFile);
}
