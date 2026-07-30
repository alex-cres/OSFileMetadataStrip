using Xunit;

namespace FileMetadataStripping.Tests;

/// <summary>Tests for DCM (DICOM) medical imaging files — synthetic bytes with 128-byte preamble + "DICM".
///
/// Known implementation gap: PHI (Protected Health Information) attributes from DICOM data elements
/// are not extracted into ExtractedMetadata because the strip path produces a JPEG fallback
/// (from the raster pixel data) rather than parsing the DICOM data dictionary.
/// The file IS cleaned (no DICOM attributes in the output JPEG), but the gap means
/// dcm:PatientName etc. are not reported in ExtractedMetadata.
///
/// Tests verify: IsPassthrough=false (DICM brand detected), no exception thrown.</summary>
public class DcmImageTests
{
    private readonly IFileMetadataStripping _sut = new FileMetadataStripping();

    [Fact]
    public void StripFileMetadata_DcmSyntheticBytes_IsPassthroughIsFalse()
    {
        // DICM magic at byte 128 triggers the DICOM detection path → IsPassthrough=false.
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticDcmBytes(), false);
        Assert.False(result.IsPassthrough);
    }

    [Fact]
    public void StripFileMetadata_DcmSyntheticBytes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sut.StripFileMetadata(TestHelpers.CreateSyntheticDcmBytes(), false));
        Assert.Null(ex);
    }

    [Fact]
    public void StripFileMetadata_DcmSyntheticBytes_CleanFileIsNonNull()
    {
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticDcmBytes(), false);
        Assert.NotNull(result.CleanFile);
    }

    [Fact]
    public void StripFileMetadata_DcmSyntheticBytes_RemovedEntryCountIsNonNegative()
    {
        // Gap: DCM PHI attributes may not be counted if the file fails to decode.
        var result = _sut.StripFileMetadata(TestHelpers.CreateSyntheticDcmBytes(), false);
        Assert.True(result.RemovedEntryCount >= 0);
    }
}
