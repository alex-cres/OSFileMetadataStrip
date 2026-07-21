using System;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text.Json.Nodes;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using TagLib;
using OutSystems.HubEdition.RuntimePlatform;
using OutSystems.RuntimePublic.Db;

namespace OutSystems.NssFileMetadataStripping {

	public class CssFileMetadataStripping : IssFileMetadataStripping {

		// ── Private result struct (used by all internal helpers) ───────────────
		private struct Result {
			public byte[]  CleanFile;
			public string  ExtractedMetadata;
			public int     RemovedEntryCount;
			public bool    IsPassthrough;
		}

		// ── Public action ──────────────────────────────────────────────────────

		/// <summary>
		/// Strips metadata from a file. Supports images (EXIF/IPTC/XMP), PDFs,
		/// OOXML (DOCX/XLSX/PPTX), and audio/video (MP3/WAV/FLAC/OGG/MP4/MKV/AVI).
		/// Unrecognised formats returned unchanged with IsPassthrough=true.
		/// </summary>
		public void MssStripFileMetadata(byte[] ssRawFile, out RCFileMetadataResultRecord ssStripFileMetadata) {
			var r = Strip(ssRawFile);
			ssStripFileMetadata = new RCFileMetadataResultRecord(null);
			ssStripFileMetadata.ssCleanFile          = r.CleanFile;
			ssStripFileMetadata.ssExtractedMetadata  = r.ExtractedMetadata;
			ssStripFileMetadata.ssRemovedEntryCount  = r.RemovedEntryCount;
			ssStripFileMetadata.ssIsPassthrough      = r.IsPassthrough;
		}

		// ── Dispatch ───────────────────────────────────────────────────────────

		private enum FileCategory { Image, Pdf, OpenXml, Media, Passthrough }

		private static Result Strip(byte[] rawFile) {
			return DetectCategory(rawFile) switch {
				FileCategory.Image       => StripImageMetadata(rawFile),
				FileCategory.Pdf         => StripPdfMetadata(rawFile),
				FileCategory.OpenXml     => StripOpenXmlMetadata(rawFile),
				FileCategory.Media       => StripMediaMetadata(rawFile),
				FileCategory.Passthrough => Passthrough(rawFile),
				_                        => Passthrough(rawFile)
			};
		}

		private static FileCategory DetectCategory(byte[] rawFile) {
			if (rawFile.Length >= 4 && rawFile[0] == 0x25 && rawFile[1] == 0x50 && rawFile[2] == 0x44 && rawFile[3] == 0x46)
				return FileCategory.Pdf;
			if (rawFile.Length >= 4 && rawFile[0] == 0x50 && rawFile[1] == 0x4B && rawFile[2] == 0x03 && rawFile[3] == 0x04)
				return FileCategory.OpenXml;
			try { var info = new MagickImageInfo(rawFile); if (info.Format != MagickFormat.Unknown) return FileCategory.Image; }
			catch (MagickException) { }
			if (rawFile.Length >= 3  && rawFile[0] == 0x49 && rawFile[1] == 0x44 && rawFile[2] == 0x33) return FileCategory.Media;
			if (rawFile.Length >= 4  && rawFile[0] == 0x66 && rawFile[1] == 0x4C && rawFile[2] == 0x61 && rawFile[3] == 0x43) return FileCategory.Media;
			if (rawFile.Length >= 4  && rawFile[0] == 0x4F && rawFile[1] == 0x67 && rawFile[2] == 0x67 && rawFile[3] == 0x53) return FileCategory.Media;
			if (rawFile.Length >= 12 && rawFile[0] == 0x52 && rawFile[1] == 0x49 && rawFile[2] == 0x46 && rawFile[3] == 0x46
				&& ((rawFile[8]==0x57&&rawFile[9]==0x41&&rawFile[10]==0x56&&rawFile[11]==0x45)||(rawFile[8]==0x41&&rawFile[9]==0x56&&rawFile[10]==0x49&&rawFile[11]==0x20)))
				return FileCategory.Media;
			if (rawFile.Length >= 8  && rawFile[4] == 0x66 && rawFile[5] == 0x74 && rawFile[6] == 0x79 && rawFile[7] == 0x70) return FileCategory.Media;
			if (rawFile.Length >= 4  && rawFile[0] == 0x1A && rawFile[1] == 0x45 && rawFile[2] == 0xDF && rawFile[3] == 0xA3) return FileCategory.Media;
			if (rawFile.Length >= 4  && rawFile[0] == 0x30 && rawFile[1] == 0x26 && rawFile[2] == 0xB2 && rawFile[3] == 0x75) return FileCategory.Media;
			return FileCategory.Passthrough;
		}

		// ── Image ──────────────────────────────────────────────────────────────

		private static Result StripImageMetadata(byte[] rawFile) {
			using var image = new MagickImage(rawFile);
			var (meta, count) = ExtractImageMetadata(image);
			image.Strip();
			using var output = new MemoryStream();
			image.Write(output);
			return new Result { CleanFile = output.ToArray(), ExtractedMetadata = meta, RemovedEntryCount = count, IsPassthrough = false };
		}

		private static (string json, int count) ExtractImageMetadata(MagickImage image) {
			var root = new JsonObject(); var count = 0;
			var exif = image.GetExifProfile();
			if (exif != null) { var n = new JsonObject(); foreach (var v in exif.Values) { n[v.Tag.ToString()] = JsonValue.Create(v.GetValue()?.ToString()); count++; } root["exif"] = n; }
			var iptc = image.GetIptcProfile();
			if (iptc != null) { var a = new JsonArray(); foreach (var v in iptc.Values) { a.Add(new JsonObject { ["tag"] = JsonValue.Create(v.Tag.ToString()), ["value"] = JsonValue.Create(v.Value) }); count++; } root["iptc"] = a; }
			var xmp = image.GetXmpProfile();
			if (xmp != null) { root["xmp"] = "present"; count++; }
			return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
		}

		// ── PDF ────────────────────────────────────────────────────────────────

		private static Result StripPdfMetadata(byte[] rawFile) {
			using var input = new MemoryStream(rawFile);
			using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
			var (meta, count) = ExtractPdfMetadata(doc.Info);
			doc.Info.Title = string.Empty; doc.Info.Author = string.Empty; doc.Info.Subject = string.Empty;
			doc.Info.Keywords = string.Empty; doc.Info.Creator = string.Empty;
			using var output = new MemoryStream(); doc.Save(output);
			return new Result { CleanFile = output.ToArray(), ExtractedMetadata = meta, RemovedEntryCount = count, IsPassthrough = false };
		}

		private static (string json, int count) ExtractPdfMetadata(PdfDocumentInformation info) {
			var root = new JsonObject(); var count = 0;
			void Cap(string k, string v) { if (!string.IsNullOrEmpty(v)) { root[k] = JsonValue.Create(v); count++; } }
			Cap("title", info.Title); Cap("author", info.Author); Cap("subject", info.Subject);
			Cap("keywords", info.Keywords); Cap("creator", info.Creator); Cap("producer", info.Producer);
			return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
		}

		// ── Office Open XML ────────────────────────────────────────────────────

		private static Result StripOpenXmlMetadata(byte[] rawFile) {
			var ms = new MemoryStream(); ms.Write(rawFile, 0, rawFile.Length); ms.Position = 0;
			using var pkg = Package.Open(ms, FileMode.Open, FileAccess.ReadWrite);
			var (meta, count) = ExtractOpenXmlMetadata(pkg.PackageProperties);
			var p = pkg.PackageProperties;
			p.Creator = null; p.LastModifiedBy = null; p.Created = null; p.Modified = null;
			p.Title = null; p.Subject = null; p.Description = null; p.Keywords = null;
			p.Category = null; p.ContentStatus = null; p.Revision = null;
			pkg.Close();
			return new Result { CleanFile = ms.ToArray(), ExtractedMetadata = meta, RemovedEntryCount = count, IsPassthrough = false };
		}

		private static (string json, int count) ExtractOpenXmlMetadata(PackageProperties p) {
			var root = new JsonObject(); var count = 0;
			void Cap(string k, object v) { var s = v?.ToString(); if (!string.IsNullOrEmpty(s)) { root[k] = JsonValue.Create(s); count++; } }
			Cap("creator", p.Creator); Cap("lastModifiedBy", p.LastModifiedBy); Cap("created", p.Created);
			Cap("modified", p.Modified); Cap("title", p.Title); Cap("subject", p.Subject);
			Cap("description", p.Description); Cap("keywords", p.Keywords); Cap("category", p.Category);
			Cap("contentStatus", p.ContentStatus); Cap("revision", p.Revision);
			return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
		}

		// ── Audio / Video ──────────────────────────────────────────────────────

		private sealed class StreamAbstraction : TagLib.File.IFileAbstraction {
			private readonly Stream _s;
			internal StreamAbstraction(string name, Stream s) { Name = name; _s = s; }
			public string Name { get; }
			public Stream ReadStream => _s;
			public Stream WriteStream => _s;
			public void CloseStream(Stream s) { }
		}

		private static Result StripMediaMetadata(byte[] rawFile) {
			var hint = GetMediaHint(rawFile);
			var ms = new MemoryStream(); ms.Write(rawFile, 0, rawFile.Length); ms.Position = 0;
			try {
				using var file = TagLib.File.Create(new StreamAbstraction("file" + hint, ms));
				var (meta, count) = ExtractMediaMetadata(file.Tag);
				file.RemoveTags(TagTypes.AllTags); file.Save();
				return new Result { CleanFile = ms.ToArray(), ExtractedMetadata = meta, RemovedEntryCount = count, IsPassthrough = false };
			} catch (Exception ex) when (ex is TagLib.UnsupportedFormatException || ex is TagLib.CorruptFileException || ex is ArgumentOutOfRangeException || ex is InvalidOperationException) {
				var note = new JsonObject { ["processingError"] = JsonValue.Create("Metadata stripping skipped — file could not be parsed. Reason: " + ex.GetType().Name + ": " + ex.Message) };
				return new Result { CleanFile = rawFile, ExtractedMetadata = note.ToJsonString(), RemovedEntryCount = 0, IsPassthrough = false };
			}
		}

		private static string GetMediaHint(byte[] f) {
			if (f.Length >= 3  && f[0]==0x49&&f[1]==0x44&&f[2]==0x33) return ".mp3";
			if (f.Length >= 4  && f[0]==0x66&&f[1]==0x4C&&f[2]==0x61&&f[3]==0x43) return ".flac";
			if (f.Length >= 4  && f[0]==0x4F&&f[1]==0x67&&f[2]==0x67&&f[3]==0x53) return ".ogg";
			if (f.Length >= 12 && f[0]==0x52&&f[1]==0x49&&f[2]==0x46&&f[3]==0x46) { if (f[8]==0x57&&f[9]==0x41&&f[10]==0x56&&f[11]==0x45) return ".wav"; if (f[8]==0x41&&f[9]==0x56&&f[10]==0x49&&f[11]==0x20) return ".avi"; }
			if (f.Length >= 8  && f[4]==0x66&&f[5]==0x74&&f[6]==0x79&&f[7]==0x70) return ".mp4";
			if (f.Length >= 4  && f[0]==0x1A&&f[1]==0x45&&f[2]==0xDF&&f[3]==0xA3) return ".mkv";
			if (f.Length >= 4  && f[0]==0x30&&f[1]==0x26&&f[2]==0xB2&&f[3]==0x75) return ".wma";
			return ".mp3";
		}

		private static (string json, int count) ExtractMediaMetadata(Tag tag) {
			var root = new JsonObject(); var count = 0;
			void Cap(string k, string v) { if (!string.IsNullOrEmpty(v)) { root[k] = JsonValue.Create(v); count++; } }
			void CapArr(string k, string[] vs) { if (vs?.Length > 0) { var ne = vs.Where(v => !string.IsNullOrEmpty(v)).ToArray(); if (ne.Length > 0) { root[k] = new JsonArray(ne.Select(v => JsonValue.Create(v)).ToArray<JsonNode?>()); count += ne.Length; } } }
			Cap("title", tag.Title); CapArr("artists", tag.Performers); Cap("album", tag.Album);
			Cap("comment", tag.Comment); CapArr("genres", tag.Genres); Cap("copyright", tag.Copyright);
			CapArr("composers", tag.Composers); Cap("conductor", tag.Conductor);
			return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
		}

		// ── Passthrough ────────────────────────────────────────────────────────

		private static Result Passthrough(byte[] rawFile) =>
			new Result { CleanFile = rawFile, ExtractedMetadata = "[]", RemovedEntryCount = 0, IsPassthrough = true };

	} // CssFileMetadataStripping

} // OutSystems.NssFileMetadataStripping

