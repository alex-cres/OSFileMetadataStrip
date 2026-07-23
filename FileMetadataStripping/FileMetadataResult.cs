using OutSystems.ExternalLibraries.SDK;

namespace FileMetadataStripping;

[OSStructure(Description = "Result of stripping metadata from a file, including the clean file and the metadata that was removed for policy review.")]
public struct FileMetadataResult
{
    [OSStructureField(Description = "The file with all embedded metadata stripped. Safe to forward to AI APIs or store. Identical to the input when IsPassthrough is true or when an audio/video file could not be parsed.", IsMandatory = true)]
    public byte[] CleanFile { get; set; }

    [OSStructureField(Description = "JSON object of the metadata entries found and removed. Keys vary by format: images use exif/iptc/xmp; PDFs use title/author/subject/keywords/creator/producer/xmp/annotationAuthors; OOXML core properties use creator/lastModifiedBy/created/modified/title/subject/description/keywords/category/contentStatus/revision/lastPrinted/identifier/version; OOXML app properties use appApplication/appCompany/appManager/appVersion/appTemplate/appHyperlinkBase; OOXML custom properties appear under customProperties; author names from tracked changes, comments, and xl/persons entries appear in strippedAuthors; ODF documents use creator/title/description/subject/initialCreator/generator/editingCycles/editingDuration/userDefinedProperties; audio/video use the tag fields present in the file. Returns '[]' when no metadata was found.")]
    public string ExtractedMetadata { get; set; }

    [OSStructureField(Description = "Total number of metadata entries removed. Zero when no metadata was present, the format is a passthrough, or the audio/video file could not be parsed.")]
    public int RemovedEntryCount { get; set; }

    [OSStructureField(Description = "True when the file format has no supported metadata containers (e.g. TXT, CSV, MD, JSON). The file is returned unchanged. Use this flag in audit logs to distinguish passthrough files from files that were actively processed and found clean.")]
    public bool IsPassthrough { get; set; }
}
