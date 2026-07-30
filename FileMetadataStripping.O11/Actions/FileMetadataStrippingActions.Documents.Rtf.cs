using System;
using System.IO;
using System.Text.Json.Nodes;

namespace OutSystems.NssFileMetadataStripping;

public partial class CssFileMetadataStripping
{
    // RTF (Rich Text Format) strip pipeline.
    //
    // RTF stores document metadata as {\controlword content} groups inside the
    // top-level \info group. The strip path treats the file as ISO-8859-1 text
    // (RTF is 7-bit ASCII on disk with \'HH hex escapes for non-ASCII, so
    // Latin-1 preserves every byte 1:1), then uses a targeted regex to blank
    // out the string-bearing control-word groups. Numeric control words
    // (\version, \vern, \nofpages, revision times, edit-minute counters, etc.)
    // are left in place — they are not user-controlled prompt-injection
    // vectors, and removing them can break some readers.

    private static readonly System.Text.Encoding RtfEncoding =
        System.Text.Encoding.GetEncoding("ISO-8859-1");

    private static readonly System.Text.RegularExpressions.Regex RtfMetadataControlWord = new System.Text.RegularExpressions.Regex(
        @"\{\\(author|title|subject|keywords|comment|operator|company|doccomm|category|hlinkbase|manager)(\s+([^{}]*))?\}",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.Compiled
        | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static RecFileMetadataResult StripRtfMetadata(byte[] rawFile)
    {
        try
        {
            var text      = RtfEncoding.GetString(rawFile);
            var extracted = new JsonObject();
            int count     = 0;

            var scrubbed = RtfMetadataControlWord.Replace(text, m =>
            {
                var key   = m.Groups[1].Value.ToLowerInvariant();
                var value = m.Groups[3].Success ? m.Groups[3].Value.Trim() : string.Empty;

                if (!string.IsNullOrEmpty(value))
                {
                    // A single RTF file may contain the same control word more than
                    // once (multiple \author entries in an annotation trail, for
                    // example); preserve every occurrence as a JSON array.
                    if (extracted[key] is JsonArray arr)
                    {
                        arr.Add(JsonValue.Create(value));
                    }
                    else if (extracted[key] is JsonValue existing)
                    {
                        extracted[key] = new JsonArray
                        {
                            JsonValue.Create(existing.GetValue<string>()),
                            JsonValue.Create(value)
                        };
                    }
                    else
                    {
                        extracted[key] = JsonValue.Create(value);
                    }
                    count++;
                }

                // Preserve the original control-word spelling (case) so the
                // output is byte-diff-friendly against the input.
                return "{\\" + m.Groups[1].Value + "}";
            });

            var cleanBytes        = RtfEncoding.GetBytes(scrubbed);
            var extractedMetadata = count > 0 ? extracted.ToJsonString() : "[]";

            return new RecFileMetadataResult
            {
                ssCleanFile         = cleanBytes,
                ssExtractedMetadata = extractedMetadata,
                ssRemovedEntryCount = count,
                ssIsPassthrough     = false
            };
        }
        catch (Exception ex) when (
            ex is ArgumentException            ||
            ex is System.Text.RegularExpressions.RegexMatchTimeoutException ||
            ex is OutOfMemoryException)
        {
            var note = new JsonObject
            {
                ["processingError"] = JsonValue.Create(
                    "Metadata stripping was skipped — the RTF file could not be scanned. " +
                    "Original file returned unchanged. Reason: " + ex.GetType().Name + ": " + ex.Message)
            };
            return new RecFileMetadataResult
            {
                ssCleanFile         = rawFile,
                ssExtractedMetadata = note.ToJsonString(),
                ssRemovedEntryCount = 0,
                ssIsPassthrough     = false
            };
        }
    }
}
