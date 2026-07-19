using OutSystems.ExternalLibraries.SDK;

namespace FileMetadataStripping;

[OSStructure(Description = "Result of stripping metadata from a file, including the clean file and the metadata that was removed for policy review.")]
public struct FileMetadataResult
{
    [OSStructureField(Description = "The file with all embedded metadata stripped. Safe to forward to AI APIs or store.", IsMandatory = true)]
    public byte[] CleanFile { get; set; }

    [OSStructureField(Description = "JSON object containing all metadata entries found and removed (keyed by type: exif, iptc, xmp). Use for policy review or audit logging. Returns '[]' when no metadata was present.")]
    public string ExtractedMetadata { get; set; }

    [OSStructureField(Description = "Total number of metadata entries removed from the file. Zero when the file contained no embedded metadata.")]
    public int RemovedEntryCount { get; set; }
}
