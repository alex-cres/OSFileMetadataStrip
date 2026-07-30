using System.Text.Json.Nodes;

namespace FileMetadataStripping;

public partial class FileMetadataStripping
{
    // Legacy binary Office (CFBF / OLE Compound Document) strip pipeline — Word 97–2003
    // (.doc/.dot), Excel 97–2003 (.xls/.xlt), PowerPoint 97–2003 (.ppt/.pot/.pps).
    // Both SummaryInformation and DocumentSummaryInformation OLE property-set streams
    // are captured and deleted; the container is consolidated so freed sectors are
    // dropped from the output byte array.

    private const string CfbfSummaryInformationStream         = "\u0005SummaryInformation";

    private const string CfbfDocumentSummaryInformationStream = "\u0005DocumentSummaryInformation";

    private static FileMetadataResult StripCfbfMetadata(byte[] rawFile)
    {
        try
        {
            // OpenMcdf writes back to the underlying stream in-place. Copy the
            // input into a fresh, growable MemoryStream so mutations don't
            // touch the caller's byte[].
            var ms = new MemoryStream();
            ms.Write(rawFile, 0, rawFile.Length);
            ms.Position = 0;

            int  count             = 0;
            var  extractedRoot     = new JsonObject();

            using (var root = OpenMcdf.RootStorage.Open(
                       ms, OpenMcdf.StorageModeFlags.LeaveOpen))
            {
                if (root.ContainsEntry(CfbfSummaryInformationStream))
                {
                    var (json, n) = ReadOlePropertyContainer(
                        root, CfbfSummaryInformationStream,
                        OpenMcdf.Ole.ContainerType.SummaryInfo);
                    if (n > 0)
                    {
                        extractedRoot["summaryInformation"] = json;
                        count += n;
                    }
                    root.Delete(CfbfSummaryInformationStream);
                }

                if (root.ContainsEntry(CfbfDocumentSummaryInformationStream))
                {
                    var (json, n) = ReadOlePropertyContainer(
                        root, CfbfDocumentSummaryInformationStream,
                        OpenMcdf.Ole.ContainerType.DocumentSummaryInfo);
                    if (n > 0)
                    {
                        extractedRoot["documentSummaryInformation"] = json;
                        count += n;
                    }
                    root.Delete(CfbfDocumentSummaryInformationStream);
                }

                // Consolidate rewrites the container in place, keeping only
                // reachable directory entries and sectors. Without this the
                // deleted property-set streams remain in unallocated sectors
                // and the raw text (e.g. author name) is still readable from
                // the byte array.
                root.Flush(consolidate: true);
            }

            return new FileMetadataResult
            {
                CleanFile         = ms.ToArray(),
                ExtractedMetadata = count > 0 ? extractedRoot.ToJsonString() : "[]",
                RemovedEntryCount = count,
                IsPassthrough     = false
            };
        }
        catch (Exception ex) when (
            ex is IOException             ||
            ex is InvalidDataException    ||
            ex is InvalidOperationException ||
            ex is NotSupportedException   ||
            ex is ArgumentException       ||
            ex is FormatException         ||
            ex is OverflowException       ||
            ex is EndOfStreamException    ||
            ex is IndexOutOfRangeException||
            ex is FileFormatException)
        {
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the CFBF (legacy Office) file could not be parsed. " +
                    $"Original file returned unchanged. Reason: {ex.GetType().Name}: {ex.Message}")
            };
            return new FileMetadataResult
            {
                CleanFile         = rawFile,
                ExtractedMetadata = note.ToJsonString(),
                RemovedEntryCount = 0,
                IsPassthrough     = false
            };
        }
    }

    /// <summary>
    /// Reads a CFBF OLE property-set stream (SummaryInformation or
    /// DocumentSummaryInformation), extracting well-known PII-bearing
    /// properties into a JSON object for audit. Custom user-defined properties
    /// on DocumentSummaryInformation are captured under <c>customProperties</c>
    /// using their string names from the property dictionary.
    /// </summary>
    private static (JsonObject json, int count) ReadOlePropertyContainer(
        OpenMcdf.RootStorage root, string streamName,
        OpenMcdf.Ole.ContainerType expected)
    {
        var json  = new JsonObject();
        int count = 0;

        try
        {
            using var cfStream  = root.OpenStream(streamName);
            var       container = new OpenMcdf.Ole.OlePropertiesContainer(cfStream);

            foreach (var prop in container.Properties)
            {
                var (key, value) = MapOleProperty(expected, prop);
                if (key != null && value != null)
                {
                    json[key] = JsonValue.Create(value);
                    count++;
                }
            }

            if (container.UserDefinedProperties != null)
            {
                var custom = new JsonObject();
                var names  = container.UserDefinedProperties.PropertyNames;
                foreach (var prop in container.UserDefinedProperties.Properties)
                {
                    string? name = null;
                    names?.TryGetValue(prop.PropertyIdentifier, out name);
                    var stringValue = FormatOleValue(prop.Value);
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(stringValue))
                    {
                        custom[name!] = JsonValue.Create(stringValue);
                        count++;
                    }
                }
                if (custom.Count > 0) json["customProperties"] = custom;
            }
        }
        catch (Exception ex) when (
            ex is IOException             ||
            ex is InvalidDataException    ||
            ex is InvalidOperationException ||
            ex is NotSupportedException   ||
            ex is ArgumentException       ||
            ex is FormatException         ||
            ex is OverflowException       ||
            ex is EndOfStreamException    ||
            ex is IndexOutOfRangeException||
            ex is FileFormatException)
        {
            // Property-set parse failed — the stream will still be deleted by
            // the caller. Surface the parse failure in the audit payload so
            // callers can see the property values weren't captured.
            json["auditWarning"] = JsonValue.Create(
                $"OLE property-set could not be parsed for {streamName}: {ex.GetType().Name}: {ex.Message}");
        }

        return (json, count);
    }

    /// <summary>
    /// Maps a well-known OLE property identifier on SummaryInformation /
    /// DocumentSummaryInformation to a human-readable audit key. Returns
    /// <c>(null, null)</c> for identifiers that carry no PII (page count, word
    /// count, thumbnail bytes, security flags, and reserved slots).
    /// See MS-OSHARED §2.3.3 for the identifier list.
    /// </summary>
    private static (string? key, string? value) MapOleProperty(
        OpenMcdf.Ole.ContainerType containerType,
        OpenMcdf.Ole.OleProperty prop)
    {
        var stringValue = FormatOleValue(prop.Value);
        if (string.IsNullOrEmpty(stringValue)) return (null, null);

        if (containerType == OpenMcdf.Ole.ContainerType.SummaryInfo)
        {
            return prop.PropertyIdentifier switch
            {
                0x02 => ("title",             stringValue),
                0x03 => ("subject",           stringValue),
                0x04 => ("author",            stringValue),
                0x05 => ("keywords",          stringValue),
                0x06 => ("comments",          stringValue),
                0x07 => ("template",          stringValue),
                0x08 => ("lastSavedBy",       stringValue),
                0x09 => ("revisionNumber",    stringValue),
                0x0A => ("totalEditingTime",  stringValue),
                0x0B => ("lastPrinted",       stringValue),
                0x0C => ("createDateTime",    stringValue),
                0x0D => ("lastSavedDateTime", stringValue),
                0x12 => ("application",       stringValue),
                _    => ((string?)null, (string?)null),
            };
        }
        if (containerType == OpenMcdf.Ole.ContainerType.DocumentSummaryInfo)
        {
            return prop.PropertyIdentifier switch
            {
                0x02 => ("category",      stringValue),
                0x0E => ("manager",       stringValue),
                0x0F => ("company",       stringValue),
                0x17 => ("appVersion",    stringValue),
                0x1A => ("contentType",   stringValue),
                0x1B => ("contentStatus", stringValue),
                0x1C => ("language",      stringValue),
                0x1D => ("docVersion",    stringValue),
                _    => ((string?)null, (string?)null),
            };
        }
        return (null, null);
    }

    /// <summary>
    /// Renders an OLE property value as a string for the audit payload.
    /// Handles the common cases (strings, DateTimes, primitives); byte arrays
    /// and complex property types are returned as <see cref="string.Empty"/>
    /// so the caller drops them from the audit (the stream itself is still
    /// deleted).
    /// </summary>
    private static string FormatOleValue(object? value) => value switch
    {
        null                 => string.Empty,
        string s             => s,
        DateTime dt          => dt.ToString("O"),
        byte[] _             => string.Empty,
        System.Collections.IEnumerable e when value is not string
                             => string.Join(", ", e.Cast<object?>()
                                                   .Select(v => v?.ToString())
                                                   .Where(s => !string.IsNullOrEmpty(s))),
        _                    => value.ToString() ?? string.Empty,
    };
}
