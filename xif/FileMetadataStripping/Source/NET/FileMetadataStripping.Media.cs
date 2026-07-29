using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Packaging;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using TagLib;
using OutSystems.HubEdition.RuntimePlatform;
using OutSystems.RuntimePublic.Db;

namespace OutSystems.NssFileMetadataStripping {

	public partial class CssFileMetadataStripping
	{
		// Audio / video strip pipeline (TagLibSharp).

		private sealed class MemoryStreamAbstraction : TagLib.File.IFileAbstraction
		{
			private readonly Stream _stream;
			internal MemoryStreamAbstraction(string name, Stream stream) { Name = name; _stream = stream; }
			public string Name { get; }
			public Stream ReadStream  => _stream;
			public Stream WriteStream => _stream;
			public void CloseStream(Stream stream) { }
		}

		private static RCFileMetadataResultRecord StripMediaMetadata(byte[] rawFile)
		{
			var hint = GetMediaExtensionHint(rawFile);

			var ms = new MemoryStream();
			ms.Write(rawFile, 0, rawFile.Length);
			ms.Position = 0;

			try
			{
				using (var file = TagLib.File.Create(new MemoryStreamAbstraction("file" + hint, ms)))
				{
					var (extractedMetadata, removedEntryCount) = ExtractMediaMetadata(file.Tag);

					file.RemoveTags(TagTypes.AllTags);
					file.Save();

					var r = new RCFileMetadataResultRecord(null);
					r.ssSTFileMetadataResult.ssCleanFile         = ms.ToArray();
					r.ssSTFileMetadataResult.ssExtractedMetadata = extractedMetadata;
					r.ssSTFileMetadataResult.ssRemovedEntryCount = removedEntryCount;
					r.ssSTFileMetadataResult.ssIsPassthrough     = false;
					return r;
				}
			}
			catch (Exception ex) when (
				ex is TagLib.UnsupportedFormatException ||
				ex is TagLib.CorruptFileException       ||
				ex is ArgumentOutOfRangeException       ||
				ex is InvalidOperationException)
			{
				var note = new JsonObject
				{
					["processingError"] = JsonValue.Create(
						"Metadata stripping was skipped — the file could not be parsed by the audio/video engine. " +
						$"Original file returned unchanged. Reason: {ex.GetType().Name}: {ex.Message}")
				};
				var er = new RCFileMetadataResultRecord(null);
				er.ssSTFileMetadataResult.ssCleanFile         = rawFile;
				er.ssSTFileMetadataResult.ssExtractedMetadata = note.ToJsonString();
				er.ssSTFileMetadataResult.ssRemovedEntryCount = 0;
				er.ssSTFileMetadataResult.ssIsPassthrough     = false;
				return er;
			}
		}

		private static string GetMediaExtensionHint(byte[] rawFile)
		{
			if (rawFile.Length >= 3 && rawFile[0] == 0x49 && rawFile[1] == 0x44 && rawFile[2] == 0x33) return ".mp3";
			if (rawFile.Length >= 4 && rawFile[0] == 0x66 && rawFile[1] == 0x4C && rawFile[2] == 0x61 && rawFile[3] == 0x43) return ".flac";
			if (rawFile.Length >= 4 && rawFile[0] == 0x4F && rawFile[1] == 0x67 && rawFile[2] == 0x67 && rawFile[3] == 0x53) return ".ogg";
			if (rawFile.Length >= 12 && rawFile[0] == 0x52 && rawFile[1] == 0x49 && rawFile[2] == 0x46 && rawFile[3] == 0x46)
			{
				if (rawFile[8] == 0x57 && rawFile[9] == 0x41 && rawFile[10] == 0x56 && rawFile[11] == 0x45) return ".wav";
				if (rawFile[8] == 0x41 && rawFile[9] == 0x56 && rawFile[10] == 0x49 && rawFile[11] == 0x20) return ".avi";
			}
			if (rawFile.Length >= 8 && rawFile[4] == 0x66 && rawFile[5] == 0x74 && rawFile[6] == 0x79 && rawFile[7] == 0x70) return ".mp4";
			if (rawFile.Length >= 4 && rawFile[0] == 0x1A && rawFile[1] == 0x45 && rawFile[2] == 0xDF && rawFile[3] == 0xA3) return ".mkv";
			if (rawFile.Length >= 4 && rawFile[0] == 0x30 && rawFile[1] == 0x26 && rawFile[2] == 0xB2 && rawFile[3] == 0x75) return ".wma";
			return ".mp3";
		}

		private static (string json, int count) ExtractMediaMetadata(Tag tag)
		{
			var root  = new JsonObject();
			var count = 0;

			void Capture(string key, string? value)
			{
				if (!string.IsNullOrEmpty(value)) { root[key] = JsonValue.Create(value); count++; }
			}

			void CaptureArray(string key, string[]? values)
			{
				if (values?.Length > 0)
				{
					var nonEmpty = values.Where(v => !string.IsNullOrEmpty(v)).ToArray();
					if (nonEmpty.Length > 0)
					{
						root[key] = new JsonArray(nonEmpty.Select(v => JsonValue.Create(v)).ToArray<JsonNode?>());
						count += nonEmpty.Length;
					}
				}
			}

			Capture("title",          tag.Title);
			CaptureArray("artists",   tag.Performers);
			Capture("album",          tag.Album);
			Capture("comment",        tag.Comment);
			CaptureArray("genres",    tag.Genres);
			Capture("copyright",      tag.Copyright);
			CaptureArray("composers", tag.Composers);
			Capture("conductor",      tag.Conductor);

			return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
		}

	} // CssFileMetadataStripping

} // OutSystems.NssFileMetadataStripping
