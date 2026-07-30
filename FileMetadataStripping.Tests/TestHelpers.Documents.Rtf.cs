namespace FileMetadataStripping.Tests;

internal static partial class TestHelpers
{
    // RTF (Rich Text Format) test-data helper.

    internal static byte[] CreateRtf(
        string? author   = null,
        string? title    = null,
        string? subject  = null,
        string? keywords = null,
        string? company  = null,
        string? manager  = null,
        string? comment  = null,
        string? doccomm  = null,
        string? operatorName = null,
        string? category = null,
        string? hlinkbase    = null,
        string  body     = "Hello world.")
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(@"{\rtf1\ansi\deff0");
        sb.Append(@"{\fonttbl{\f0\froman Times New Roman;}}");

        // Only emit an \info group when at least one metadata value is set so
        // the "clean baseline" test can start from an RTF with no \info.
        var infoParts = new List<string>();
        void Add(string cw, string? v)
        {
            if (!string.IsNullOrEmpty(v))
                infoParts.Add("{\\" + cw + " " + v + "}");
        }
        Add("author",       author);
        Add("title",        title);
        Add("subject",      subject);
        Add("keywords",     keywords);
        Add("company",      company);
        Add("manager",      manager);
        Add("comment",      comment);
        Add("doccomm",      doccomm);
        Add("operator",     operatorName);
        Add("category",     category);
        Add("hlinkbase",    hlinkbase);

        if (infoParts.Count > 0)
        {
            sb.Append(@"{\info");
            foreach (var p in infoParts) sb.Append(p);
            sb.Append('}');
        }

        sb.Append(@"\pard\f0 ").Append(body).Append(@"\par}");

        return System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(sb.ToString());
    }
}
