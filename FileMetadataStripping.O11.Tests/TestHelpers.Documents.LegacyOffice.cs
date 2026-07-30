namespace FileMetadataStripping.Tests;

internal static partial class TestHelpers
{
    // Legacy binary Office (CFBF / OLE Compound Document) test-data helpers.
    // OpenMcdf-based factories for Word 97–2003 (.doc), Excel 97–2003 (.xls),
    // PowerPoint 97–2003 (.ppt), and template variants.

    internal static byte[] CreateCfbf(
        string?    title       = "Confidential Report",
        string?    subject     = null,
        string?    author      = "Alice Attacker",
        string?    keywords    = null,
        string?    comments    = null,
        string?    lastSavedBy = null,
        string?    application = "Microsoft Office Word",
        string?    company     = "Acme Corp",
        string?    manager     = null,
        string?    category    = null,
        Dictionary<string, string>? customProperties = null)
    {
        var ms = new MemoryStream();
        using (var root = OpenMcdf.RootStorage.Create(
                   ms, OpenMcdf.Version.V3, OpenMcdf.StorageModeFlags.LeaveOpen))
        {
            // SummaryInformation
            var summary = new OpenMcdf.Ole.OlePropertiesContainer(
                1252, OpenMcdf.Ole.ContainerType.SummaryInfo);
            AddCodePage(summary, 1252);
            AddLpstr(summary, 0x02, title);
            AddLpstr(summary, 0x03, subject);
            AddLpstr(summary, 0x04, author);
            AddLpstr(summary, 0x05, keywords);
            AddLpstr(summary, 0x06, comments);
            AddLpstr(summary, 0x08, lastSavedBy);
            AddLpstr(summary, 0x12, application);
            using (var summaryStream = root.CreateStream("\u0005SummaryInformation"))
                summary.Save(summaryStream);

            var docSummary = new OpenMcdf.Ole.OlePropertiesContainer(
                1252, OpenMcdf.Ole.ContainerType.DocumentSummaryInfo);
            AddCodePage(docSummary, 1252);
            AddLpstr(docSummary, 0x02, category);
            AddLpstr(docSummary, 0x0E, manager);
            AddLpstr(docSummary, 0x0F, company);

            if (customProperties != null && customProperties.Count > 0)
            {
                var udp = docSummary.CreateUserDefinedProperties(1252);
                foreach (var kv in customProperties)
                {
                    var p = udp.AddUserDefinedProperty(
                        OpenMcdf.Ole.VTPropertyType.VT_LPSTR, kv.Key);
                    p.Value = kv.Value;
                }
            }

            using (var docSummaryStream = root.CreateStream(
                       "\u0005DocumentSummaryInformation"))
                docSummary.Save(docSummaryStream);

            root.Flush();
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a CFBF file with NO property-set streams. Used to verify the
    /// "clean baseline" contract: a valid CFBF that is detected (IsPassthrough
    /// = false) but leaves RemovedEntryCount = 0.
    /// </summary>
    internal static byte[] CreateCfbfWithoutMetadata()
    {
        var ms = new MemoryStream();
        using (var root = OpenMcdf.RootStorage.Create(
                   ms, OpenMcdf.Version.V3, OpenMcdf.StorageModeFlags.LeaveOpen))
        {
            // Add one arbitrary stream so the container isn't empty (empty CFBF
            // is still valid but a body stream is closer to a real Office file).
            using var body = root.CreateStream("WordDocument");
            var placeholder = new byte[16];
            body.Write(placeholder, 0, placeholder.Length);
            root.Flush();
        }
        return ms.ToArray();
    }

    private static void AddLpstr(
        OpenMcdf.Ole.OlePropertiesContainer container, uint id, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var prop = container.CreateProperty(
            OpenMcdf.Ole.VTPropertyType.VT_LPSTR, id);
        prop.Value = value;
        container.Add(prop);
    }

    private static void AddCodePage(
        OpenMcdf.Ole.OlePropertiesContainer container, int codePage)
    {
        var prop = container.CreateProperty(
            OpenMcdf.Ole.VTPropertyType.VT_I2, 0x01);
        prop.Value = (short)codePage;
        container.Add(prop);
    }
}
