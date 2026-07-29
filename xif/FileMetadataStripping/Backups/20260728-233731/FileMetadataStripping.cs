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

	public class CssFileMetadataStripping : IssFileMetadataStripping {

		private enum FileCategory { Image, Pdf, OpenXml, Odf, Media, Passthrough }

		/// <summary>
		/// Strips metadata from a file. Supports images (EXIF/IPTC/XMP), PDFs, OOXML (DOCX/XLSX/PPTX), and audio/video (MP3/WAV/FLAC/OGG/MP4/MKV/AVI). Unrecognised formats returned unchanged with IsPassthrough=true.
		/// </summary>
		/// <param name="ssRawFile">The uploaded file in any supported format.</param>
		/// <param name="ssStripFileMetadata">The stripped file and audit metadata.</param>
		public void MssStripFileMetadata(byte[] ssRawFile, out RCFileMetadataResultRecord ssStripFileMetadata)
		{
			ssStripFileMetadata = DetectCategory(ssRawFile) switch
			{
				FileCategory.Image       => StripImageMetadata(ssRawFile),
				FileCategory.Pdf         => StripPdfMetadata(ssRawFile),
				FileCategory.OpenXml     => StripOpenXmlMetadata(ssRawFile, false),
				FileCategory.Odf         => StripOdfMetadata(ssRawFile),
				FileCategory.Media       => StripMediaMetadata(ssRawFile),
				FileCategory.Passthrough => Passthrough(ssRawFile),
				_                        => Passthrough(ssRawFile)
			};
		} // MssStripFileMetadata

		// ── Detection ─────────────────────────────────────────────────────────────

		private static FileCategory DetectCategory(byte[] rawFile)
		{
			// PDF: %PDF magic bytes
			if (rawFile.Length >= 4
				&& rawFile[0] == 0x25 && rawFile[1] == 0x50
				&& rawFile[2] == 0x44 && rawFile[3] == 0x46)
				return FileCategory.Pdf;

			// Office Open XML (DOCX/XLSX/PPTX) or ODF (ODT/ODS/ODP): ZIP PK signature
			if (rawFile.Length >= 4
				&& rawFile[0] == 0x50 && rawFile[1] == 0x4B
				&& rawFile[2] == 0x03 && rawFile[3] == 0x04)
				return IsOdfFormat(rawFile) ? FileCategory.Odf : FileCategory.OpenXml;

			// BMP: no metadata containers — passthrough (magic bytes "BM")
			if (rawFile.Length >= 2 && rawFile[0] == 0x42 && rawFile[1] == 0x4D)
				return FileCategory.Passthrough;

			// Images: detected by Magick.NET (JPEG, PNG, GIF, TIFF, WebP, TGA, 100+ more)
			try
			{
				var info = new MagickImageInfo(rawFile);
				if (info.Format != MagickFormat.Unknown)
					return FileCategory.Image;
			}
			catch (MagickException) { }

			// Audio/video — detected by magic bytes
			if (rawFile.Length >= 3 && rawFile[0] == 0x49 && rawFile[1] == 0x44 && rawFile[2] == 0x33)
				return FileCategory.Media; // MP3 ID3
			if (rawFile.Length >= 4 && rawFile[0] == 0x66 && rawFile[1] == 0x4C && rawFile[2] == 0x61 && rawFile[3] == 0x43)
				return FileCategory.Media; // FLAC
			if (rawFile.Length >= 4 && rawFile[0] == 0x4F && rawFile[1] == 0x67 && rawFile[2] == 0x67 && rawFile[3] == 0x53)
				return FileCategory.Media; // OGG
			if (rawFile.Length >= 12 && rawFile[0] == 0x52 && rawFile[1] == 0x49 && rawFile[2] == 0x46 && rawFile[3] == 0x46
				&& ((rawFile[8] == 0x57 && rawFile[9] == 0x41 && rawFile[10] == 0x56 && rawFile[11] == 0x45)
				 || (rawFile[8] == 0x41 && rawFile[9] == 0x56 && rawFile[10] == 0x49 && rawFile[11] == 0x20)))
				return FileCategory.Media; // WAV / AVI
			// ISO Base Media File Format: "ftyp" at bytes 4–7.
			// HEIC, HEIF, and AVIF share this container with MP4/MOV — distinguish by major brand (bytes 8–11).
			if (rawFile.Length >= 8 && rawFile[4] == 0x66 && rawFile[5] == 0x74 && rawFile[6] == 0x79 && rawFile[7] == 0x70)
				return IsHeifOrAvifBrand(rawFile) ? FileCategory.Image : FileCategory.Media;
			if (rawFile.Length >= 4 && rawFile[0] == 0x1A && rawFile[1] == 0x45 && rawFile[2] == 0xDF && rawFile[3] == 0xA3)
				return FileCategory.Media; // MKV / WebM
			if (rawFile.Length >= 4 && rawFile[0] == 0x30 && rawFile[1] == 0x26 && rawFile[2] == 0xB2 && rawFile[3] == 0x75)
				return FileCategory.Media; // WMA / ASF

			// ── Image format fallbacks: formats not reliably detected by MagickImageInfo ──────

			// TGA v2 footer: last 18 bytes = "TRUEVISION-XFILE." + NUL.
			if (rawFile.Length >= 18)
			{
				var tgaFooter = System.Text.Encoding.ASCII.GetString(rawFile, rawFile.Length - 18, 17);
				if (tgaFooter == "TRUEVISION-XFILE.")
					return FileCategory.Image;
			}
			// TGA v1 header heuristic: validate bytes 1 (color-map type), 2 (image type),
			// 16 (pixel depth) and non-zero dimensions (bytes 12–15).
			if (rawFile.Length >= 18)
			{
				byte cmt   = rawFile[1]; byte imt   = rawFile[2]; byte depth = rawFile[16];
				int width  = rawFile[12] | (rawFile[13] << 8);
				int height = rawFile[14] | (rawFile[15] << 8);
				if ((cmt == 0 || cmt == 1)
					&& (imt == 1 || imt == 2 || imt == 3 || imt == 9 || imt == 10 || imt == 11)
					&& (depth == 8 || depth == 15 || depth == 16 || depth == 24 || depth == 32)
					&& width > 0 && height > 0)
					return FileCategory.Image;
			}

			// ICO (Microsoft Icon): 0x00 0x00 0x01 0x00
			if (rawFile.Length >= 4
				&& rawFile[0] == 0x00 && rawFile[1] == 0x00
				&& rawFile[2] == 0x01 && rawFile[3] == 0x00)
				return FileCategory.Image;

			// XCF (GIMP): "gimp xcf "
			if (rawFile.Length >= 9
				&& rawFile[0] == 0x67 && rawFile[1] == 0x69 && rawFile[2] == 0x6D
				&& rawFile[3] == 0x70 && rawFile[4] == 0x20 && rawFile[5] == 0x78
				&& rawFile[6] == 0x63 && rawFile[7] == 0x66 && rawFile[8] == 0x20)
				return FileCategory.Image;

			// DCM (DICOM): 128-byte preamble followed by "DICM" at offset 128.
			if (rawFile.Length >= 132
				&& rawFile[128] == 0x44 && rawFile[129] == 0x49
				&& rawFile[130] == 0x43 && rawFile[131] == 0x4D)
				return FileCategory.Image;

			return FileCategory.Passthrough;
		}

		/// <summary>
		/// Returns true when the ISO Base Media ftyp major brand identifies a
		/// HEIC, HEIF, or AVIF image. These formats share the ISOBMFF container with MP4/MOV
		/// and must be routed to the image path, not the audio/video path.
		/// </summary>
		private static bool IsHeifOrAvifBrand(byte[] rawFile)
		{
			if (rawFile.Length < 12) return false;
			// Major brand occupies bytes 8–11.
			// HEIC/HEIF: brands beginning with "he" (heic, heix, heim, heis, hevc, hevx, …)
			if (rawFile[8] == 0x68 && rawFile[9] == 0x65) return true;
			// HEIF base variants: mif1, msf1
			if (rawFile[8] == 0x6D &&
				(rawFile[9] == 0x69 || rawFile[9] == 0x73) &&
				rawFile[10] == 0x66 && rawFile[11] == 0x31)
				return true;
			// AVIF / AVIF image sequence: avif, avis
			if (rawFile[8] == 0x61 && rawFile[9] == 0x76 &&
				rawFile[10] == 0x69 && (rawFile[11] == 0x66 || rawFile[11] == 0x73))
				return true;
			return false;
		}

		// ── Image ─────────────────────────────────────────────────────────────────

		private static RCFileMetadataResultRecord StripImageMetadata(byte[] rawFile)
		{
			MagickImageCollection images;
			try
			{
				var readSettings = IsTgaFile(rawFile)
					? new MagickReadSettings { Format = MagickFormat.Tga }
					: null;
				images = readSettings != null
					? new MagickImageCollection(rawFile, readSettings)
					: new MagickImageCollection(rawFile);
			}
			catch (Exception ex) when (ex is MagickException || ex is OutOfMemoryException || ex is OverflowException
			                                || ex is InvalidOperationException || ex is ArgumentException)
			{
				// Catch broad exception types — some codecs on certain runtimes throw non-MagickException
				// failures on malformed or platform-incompatible input.
				var note = new JsonObject
				{
					["processingError"] = JsonValue.Create(
						"Metadata stripping was skipped — the image could not be parsed. " +
						$"Original file returned unchanged. Reason: {ex.GetType().Name}: {ex.Message}")
				};
				var er = new RCFileMetadataResultRecord(null);
				er.ssSTFileMetadataResult.ssCleanFile         = rawFile;
				er.ssSTFileMetadataResult.ssExtractedMetadata = note.ToJsonString();
				er.ssSTFileMetadataResult.ssRemovedEntryCount = 0;
				er.ssSTFileMetadataResult.ssIsPassthrough     = false;
				return er;
			}

			if (images.Count == 0)
			{
				images.Dispose();
				var emptyNote = new JsonObject
				{
					["processingError"] = JsonValue.Create(
						"Metadata stripping was skipped — the image decoded to an empty frame collection. " +
						"Original file returned unchanged.")
				};
				var ee = new RCFileMetadataResultRecord(null);
				ee.ssSTFileMetadataResult.ssCleanFile         = rawFile;
				ee.ssSTFileMetadataResult.ssExtractedMetadata = emptyNote.ToJsonString();
				ee.ssSTFileMetadataResult.ssRemovedEntryCount = 0;
				ee.ssSTFileMetadataResult.ssIsPassthrough     = false;
				return ee;
			}

			using (images)
			{
				// Extract metadata from the first frame; file-level profiles live there.
				var (extractedMetadata, removedEntryCount) = ExtractImageMetadata((MagickImage)images[0]);

				// Strip every frame — preserves animated GIFs and multi-frame TIFFs in full.
				foreach (var frame in images)
					frame.Strip(); // removes EXIF, IPTC, XMP, ICC profiles, and comments

				using (var output = new MemoryStream())
				{
					try
					{
						images.Write(output); // preserves original format and all frames automatically
					}
					catch (MagickMissingDelegateErrorException)
					{
						// The format can be decoded but has no write delegate (e.g. HEIC on both Windows
						// and ODC Linux). The HEVC encoder (x265) is GPL-licensed and cannot be bundled
						// in a redistributable NuGet package, so no available libheif build includes it.
						// Metadata was fully stripped in memory — transcode to JPEG so the caller receives
						// a clean, usable file. The original format (e.g. HEIC) is not preserved.
						using (var jpegOutput = new MemoryStream())
						{
							images.Write(jpegOutput, MagickFormat.Jpeg);

							var metaNode = extractedMetadata == "[]"
								? new JsonObject()
								: JsonNode.Parse(extractedMetadata)!.AsObject();
							metaNode["transcodedFormat"] = JsonValue.Create(
								"jpeg — the original image format (e.g. HEIC) requires an HEVC encode delegate " +
								"that is absent on all platforms because the x265 codec is GPL-licensed and " +
								"cannot be bundled in a redistributable library. Metadata was fully stripped " +
								"and the clean image was transcoded to JPEG. The original format is not preserved.");

							var jr = new RCFileMetadataResultRecord(null);
							jr.ssSTFileMetadataResult.ssCleanFile         = jpegOutput.ToArray();
							jr.ssSTFileMetadataResult.ssExtractedMetadata = metaNode.ToJsonString();
							jr.ssSTFileMetadataResult.ssRemovedEntryCount = removedEntryCount;
							jr.ssSTFileMetadataResult.ssIsPassthrough     = false;
							return jr;
						}
					}

					var cleanBytes = output.ToArray();
					// Radiance HDR output starts with '#?' (magic marker '#?RADIANCE' or '#?RGBE').
					// Only match the two-byte '#?' prefix so XBM files (which start with '#define')
					// are never passed through the HDR comment stripper.
					if (cleanBytes.Length >= 2 && cleanBytes[0] == 0x23 && cleanBytes[1] == 0x3F) // '#?'
						cleanBytes = StripHdrCommentLines(cleanBytes);

					var r = new RCFileMetadataResultRecord(null);
					r.ssSTFileMetadataResult.ssCleanFile         = cleanBytes;
					r.ssSTFileMetadataResult.ssExtractedMetadata = extractedMetadata;
					r.ssSTFileMetadataResult.ssRemovedEntryCount = removedEntryCount;
					r.ssSTFileMetadataResult.ssIsPassthrough     = false;
					return r;
				}
			}
		}

		private static (string json, int count) ExtractImageMetadata(MagickImage image)
		{
			var root  = new JsonObject();
			var count = 0;

			var exifProfile = image.GetExifProfile();
			if (exifProfile != null)
			{
				var exifNode = new JsonObject();
				foreach (var v in exifProfile.Values)
				{
					exifNode[v.Tag.ToString()] = JsonValue.Create(v.GetValue()?.ToString());
					count++;
				}
				root["exif"] = exifNode;
			}

			var iptcProfile = image.GetIptcProfile();
			if (iptcProfile != null)
			{
				var iptcArray = new JsonArray();
				foreach (var v in iptcProfile.Values)
				{
					iptcArray.Add(new JsonObject
					{
						["tag"]   = JsonValue.Create(v.Tag.ToString()),
						["value"] = JsonValue.Create(v.Value)
					});
					count++;
				}
				root["iptc"] = iptcArray;
			}

			var xmpProfile = image.GetXmpProfile();
			if (xmpProfile != null)
			{
				root["xmp"] = "present";
				count++;
			}

			var comment = image.Comment;
			if (!string.IsNullOrEmpty(comment))
			{
				var filteredLines = comment.Split('\n')
					.Where(line => line.IndexOf("Created by ImageMagick", StringComparison.OrdinalIgnoreCase) < 0)
					// Filter the Radiance HDR magic-marker string: Magick.NET exposes '#?RADIANCE'
					// (or '#?RGBE') as image.Comment with the leading '#' stripped, yielding
					// "?RADIANCE" / "?RGBE". This is a format artifact, not user metadata.
					.Where(line => !line.TrimStart().StartsWith("?RADIANCE", StringComparison.OrdinalIgnoreCase))
					.Where(line => !line.TrimStart().StartsWith("?RGBE", StringComparison.OrdinalIgnoreCase))
					.Where(line => !string.IsNullOrWhiteSpace(line));
				var filteredComment = string.Join("\n", filteredLines).Trim();
				if (!string.IsNullOrEmpty(filteredComment))
				{
					root["comment"] = filteredComment;
					count++;
				}
			}

			return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
		}

		/// <summary>Removes encoder-injected comment lines from a Radiance HDR byte stream.
		/// Strips every '#' line that is not a '#?' magic marker. The resolution string
		/// and binary pixel data are preserved verbatim.</summary>
		private static byte[] StripHdrCommentLines(byte[] hdrBytes)
		{
			using (var outMs = new MemoryStream(hdrBytes.Length))
			{
				int i = 0;
				while (i < hdrBytes.Length)
				{
					int lineStart = i;
					while (i < hdrBytes.Length && hdrBytes[i] != 0x0A) i++;
					int lineEnd = i;
					if (i < hdrBytes.Length) i++;
					if (lineEnd > lineStart && (hdrBytes[lineStart] == 0x2D || hdrBytes[lineStart] == 0x2B))
					{
						outMs.Write(hdrBytes, lineStart, i - lineStart);
						if (i < hdrBytes.Length) outMs.Write(hdrBytes, i, hdrBytes.Length - i);
						break;
					}
					bool isMagicMarker = lineEnd - lineStart >= 2 && hdrBytes[lineStart] == 0x23 && hdrBytes[lineStart + 1] == 0x3F;
					if (lineEnd > lineStart && hdrBytes[lineStart] == 0x23 && !isMagicMarker) continue;
					outMs.Write(hdrBytes, lineStart, i - lineStart);
				}
				return outMs.ToArray();
			}
		}

		// ── PDF ───────────────────────────────────────────────────────────────────

		private static RCFileMetadataResultRecord StripPdfMetadata(byte[] rawFile)
		{
			using (var input = new MemoryStream(rawFile))
			{
				PdfDocument document;
				try
				{
					document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
				}
				catch (Exception ex) when (
					ex is PdfReaderException        ||
					ex is InvalidOperationException ||
					ex is NotSupportedException)
				{
					var note = new JsonObject
					{
						["processingError"] = JsonValue.Create(
							"Metadata stripping was skipped — the PDF could not be opened (it may be encrypted or password-protected). " +
							$"Original file returned unchanged. Reason: {ex.GetType().Name}: {ex.Message}")
					};
					var er = new RCFileMetadataResultRecord(null);
					er.ssSTFileMetadataResult.ssCleanFile         = rawFile;
					er.ssSTFileMetadataResult.ssExtractedMetadata = note.ToJsonString();
					er.ssSTFileMetadataResult.ssRemovedEntryCount = 0;
					er.ssSTFileMetadataResult.ssIsPassthrough     = false;
					return er;
				}

				using (document)
				{
					var (extractedMetadata, removedEntryCount) = ExtractPdfMetadata(document);

					// Clear annotation /Author entries on every page.
					var annotAuthors = new HashSet<string>(StringComparer.Ordinal);
					int annotEntries = 0;
					for (int i = 0; i < document.PageCount; i++)
					{
						var (cleared, authors) = ClearPageAnnotationAuthors(document.Pages[i]);
						annotEntries += cleared;
						foreach (var a in authors) annotAuthors.Add(a);
					}

					if (annotEntries > 0)
					{
						var metaNode = extractedMetadata == "[]"
							? new JsonObject()
							: JsonNode.Parse(extractedMetadata)!.AsObject();
						var arr = new JsonArray();
						foreach (var a in annotAuthors.OrderBy(x => x)) arr.Add(JsonValue.Create(a));
						metaNode["annotationAuthors"] = arr;
						extractedMetadata = metaNode.ToJsonString();
						removedEntryCount += annotEntries;
					}

					document.Info.Title    = string.Empty;
					document.Info.Author   = string.Empty;
					document.Info.Subject  = string.Empty;
					document.Info.Keywords = string.Empty;
					document.Info.Creator  = string.Empty;

					// Strip catalog /Metadata XMP stream
					var catalog = document.Internals.Catalog;
					if (catalog.Elements.ContainsKey("/Metadata"))
						catalog.Elements.Remove("/Metadata");

					using (var output = new MemoryStream())
					{
						document.Save(output);

						var r = new RCFileMetadataResultRecord(null);
						r.ssSTFileMetadataResult.ssCleanFile         = output.ToArray();
						r.ssSTFileMetadataResult.ssExtractedMetadata = extractedMetadata;
						r.ssSTFileMetadataResult.ssRemovedEntryCount = removedEntryCount;
						r.ssSTFileMetadataResult.ssIsPassthrough     = false;
						return r;
					}
				}
			}
		}

		private static (string json, int count) ExtractPdfMetadata(PdfDocument document)
		{
			var root  = new JsonObject();
			var count = 0;
			var info  = document.Info;

			void Capture(string key, string? value)
			{
				if (!string.IsNullOrEmpty(value)) { root[key] = JsonValue.Create(value); count++; }
			}

			Capture("title",     info.Title);
			Capture("author",    info.Author);
			Capture("subject",   info.Subject);
			Capture("keywords",  info.Keywords);
			Capture("creator",   info.Creator);
			Capture("producer",  info.Producer);

			if (document.Internals.Catalog.Elements.ContainsKey("/Metadata"))
			{
				root["xmp"] = "present";
				count++;
			}

			return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
		}

		// ── Office Open XML (DOCX / XLSX / PPTX) ─────────────────────────────────

		private static RCFileMetadataResultRecord StripOpenXmlMetadata(byte[] rawFile, bool stripBodyAuthors)
		{
			try
			{
				var ms = new MemoryStream();
				ms.Write(rawFile, 0, rawFile.Length);
				ms.Position = 0;

				var root       = new JsonObject();
				var count      = 0;
				var partWrites = new Dictionary<string, XDocument>();

				using (var package = Package.Open(ms, FileMode.Open, FileAccess.ReadWrite))
				{
					ExtractOpenXmlCoreMetadata(package.PackageProperties, root, ref count);
					var coreProps = package.PackageProperties;
					coreProps.Creator        = null;
					coreProps.LastModifiedBy = null;
					coreProps.Created        = null;
					coreProps.Modified       = null;
					coreProps.Title          = null;
					coreProps.Subject        = null;
					coreProps.Description    = null;
					coreProps.Keywords       = null;
					coreProps.Category       = null;
					coreProps.ContentStatus  = null;
					coreProps.Revision       = null;
					coreProps.LastPrinted    = null;
					coreProps.Identifier     = null;
					coreProps.Version        = null;

					ExtractAndClearAppProperties(package, root, ref count, partWrites);
					ExtractAndClearCustomProperties(package, root, ref count, partWrites);
					if (stripBodyAuthors)
						StripOoxmlAuthorNames(package, root, ref count, partWrites);
				}

				if (partWrites.Count > 0)
				{
					var packageBytes = ms.ToArray();
					using (var zipMs = new MemoryStream())
					{
						zipMs.Write(packageBytes, 0, packageBytes.Length);
						zipMs.Position = 0;
						using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Update, leaveOpen: true))
						{
							foreach (var kvp in partWrites)
							{
								zip.GetEntry(kvp.Key)?.Delete();
								using (var s = zip.CreateEntry(kvp.Key).Open())
									kvp.Value.Save(s);
							}
						}
						var r = new RCFileMetadataResultRecord(null);
						r.ssSTFileMetadataResult.ssCleanFile         = zipMs.ToArray();
						r.ssSTFileMetadataResult.ssExtractedMetadata = count > 0 ? root.ToJsonString() : "[]";
						r.ssSTFileMetadataResult.ssRemovedEntryCount = count;
						r.ssSTFileMetadataResult.ssIsPassthrough     = false;
						return r;
					}
				}

				var result = new RCFileMetadataResultRecord(null);
				result.ssSTFileMetadataResult.ssCleanFile         = ms.ToArray();
				result.ssSTFileMetadataResult.ssExtractedMetadata = count > 0 ? root.ToJsonString() : "[]";
				result.ssSTFileMetadataResult.ssRemovedEntryCount = count;
				result.ssSTFileMetadataResult.ssIsPassthrough     = false;
				return result;
			}
			catch (Exception ex) when (
				ex is FileFormatException  ||
				ex is InvalidDataException ||
				ex is NotSupportedException)
			{
				var note = new JsonObject
				{
					["processingError"] = JsonValue.Create(
						"Metadata stripping was skipped — the OOXML file could not be opened (it may be encrypted or password-protected). " +
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

		private static void ExtractOpenXmlCoreMetadata(PackageProperties props, JsonObject root, ref int count)
		{
			int localCount = 0;
			void Capture(string key, object? value)
			{
				var str = value?.ToString();
				if (!string.IsNullOrEmpty(str)) { root[key] = JsonValue.Create(str); localCount++; }
			}

			Capture("creator",        props.Creator);
			Capture("lastModifiedBy", props.LastModifiedBy);
			Capture("created",        props.Created);
			Capture("modified",       props.Modified);
			Capture("title",          props.Title);
			Capture("subject",        props.Subject);
			Capture("description",    props.Description);
			Capture("keywords",       props.Keywords);
			Capture("category",       props.Category);
			Capture("contentStatus",  props.ContentStatus);
			Capture("revision",       props.Revision);
			Capture("lastPrinted",    props.LastPrinted);
			Capture("identifier",     props.Identifier);
			Capture("version",        props.Version);

			count += localCount;
		}

		private static void ExtractAndClearAppProperties(Package package, JsonObject root, ref int count,
			Dictionary<string, XDocument> partWrites)
		{
			var appUri = PackUriHelper.CreatePartUri(new Uri("/docProps/app.xml", UriKind.Relative));
			if (!package.PartExists(appUri)) return;

			XDocument xdoc;
			using (var stream = package.GetPart(appUri).GetStream(FileMode.Open, FileAccess.Read))
				xdoc = XDocument.Load(stream);

			XNamespace ep = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
			bool modified = false;
			var fields = new[]
			{
				("Application",   "appApplication"),
				("Company",       "appCompany"),
				("Manager",       "appManager"),
				("AppVersion",    "appVersion"),
				("Template",      "appTemplate"),
				("HyperlinkBase", "appHyperlinkBase")
			};
			foreach (var (xmlField, jsonKey) in fields)
			{
				var el = xdoc.Root?.Element(ep + xmlField);
				if (el != null && !string.IsNullOrEmpty(el.Value))
				{
					root[jsonKey] = JsonValue.Create(el.Value);
					count++;
					el.Value = string.Empty;
					modified = true;
				}
			}

			if (modified)
				partWrites["docProps/app.xml"] = xdoc;
		}

		private static void ExtractAndClearCustomProperties(Package package, JsonObject root, ref int count,
			Dictionary<string, XDocument> partWrites)
		{
			var customUri = PackUriHelper.CreatePartUri(new Uri("/docProps/custom.xml", UriKind.Relative));
			if (!package.PartExists(customUri)) return;

			XDocument xdoc;
			using (var stream = package.GetPart(customUri).GetStream(FileMode.Open, FileAccess.Read))
				xdoc = XDocument.Load(stream);

			XNamespace cp = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
			var customProps = new JsonObject();
			foreach (var prop in xdoc.Root?.Elements(cp + "property") ?? Enumerable.Empty<XElement>())
			{
				var name  = prop.Attribute("name")?.Value;
				var value = prop.Elements().FirstOrDefault()?.Value;
				if (!string.IsNullOrEmpty(name))
				{
					customProps[name] = JsonValue.Create(value ?? string.Empty);
					count++;
				}
			}

			if (customProps.Count > 0)
			{
				root["customProperties"] = customProps;
				xdoc.Root?.RemoveNodes();
				partWrites["docProps/custom.xml"] = xdoc;
			}
		}

		private static readonly HashSet<string> _wordAuthorContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
			"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml",
			"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml",
			"application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml",
			"application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml",
			"application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml"
		};

		private static void StripOoxmlAuthorNames(Package package, JsonObject root, ref int count,
			Dictionary<string, XDocument> partWrites)
		{
			var authorNames = new HashSet<string>(StringComparer.Ordinal);

			foreach (var part in package.GetParts())
			{
				var ct = part.ContentType;
				XDocument? modified;
				if (_wordAuthorContentTypes.Contains(ct))
					modified = StripWordAuthorAttributes(part, authorNames);
				else if (ct == "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml")
					modified = StripExcelCommentAuthors(part, authorNames);
				else if (ct == "application/vnd.ms-excel.person+xml")
					modified = StripExcelPersonAuthors(part, authorNames);
				else if (ct == "application/vnd.openxmlformats-officedocument.presentationml.commentAuthors+xml")
					modified = StripPptCommentAuthors(part, authorNames);
				else
					continue;

				if (modified != null)
					partWrites[part.Uri.ToString().TrimStart('/')] = modified;
			}

			if (authorNames.Count > 0)
			{
				var arr = new JsonArray();
				foreach (var name in authorNames.OrderBy(x => x))
					arr.Add(JsonValue.Create(name));
				root["strippedAuthors"] = arr;
				count += authorNames.Count;
			}
		}

		private static XDocument? StripWordAuthorAttributes(PackagePart part, HashSet<string> authorNames)
		{
			XDocument xdoc;
			using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
				xdoc = XDocument.Load(stream);

			XNamespace w       = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
			XName authorAttr   = w + "author";
			XName initialsAttr = w + "initials";
			bool modified      = false;

			foreach (var el in xdoc.Descendants())
			{
				var author = el.Attribute(authorAttr);
				if (author != null && !string.IsNullOrEmpty(author.Value))
				{
					authorNames.Add(author.Value);
					author.Value = string.Empty;
					modified = true;
				}
				var initials = el.Attribute(initialsAttr);
				if (initials != null && !string.IsNullOrEmpty(initials.Value))
				{
					initials.Value = string.Empty;
					modified = true;
				}
			}

			return modified ? xdoc : null;
		}

		private static XDocument? StripExcelCommentAuthors(PackagePart part, HashSet<string> authorNames)
		{
			XDocument xdoc;
			using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
				xdoc = XDocument.Load(stream);

			XNamespace xl = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
			bool modified = false;

			foreach (var el in xdoc.Descendants(xl + "author"))
			{
				if (!string.IsNullOrEmpty(el.Value))
				{
					authorNames.Add(el.Value);
					el.Value = string.Empty;
					modified = true;
				}
			}

			return modified ? xdoc : null;
		}

		private static XDocument? StripPptCommentAuthors(PackagePart part, HashSet<string> authorNames)
		{
			XDocument xdoc;
			using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
				xdoc = XDocument.Load(stream);

			XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
			bool modified = false;

			foreach (var el in xdoc.Descendants(p + "cmAuthor"))
			{
				var name = el.Attribute("name");
				if (name != null && !string.IsNullOrEmpty(name.Value))
				{
					authorNames.Add(name.Value);
					name.Value = string.Empty;
					modified = true;
				}
				var initials = el.Attribute("initials");
				if (initials != null && !string.IsNullOrEmpty(initials.Value))
				{
					initials.Value = string.Empty;
					modified = true;
				}
			}

			return modified ? xdoc : null;
		}

		private static XDocument? StripExcelPersonAuthors(PackagePart part, HashSet<string> authorNames)
		{
			XDocument xdoc;
			using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
				xdoc = XDocument.Load(stream);

			XNamespace ns = "http://schemas.microsoft.com/office/spreadsheetml/2017/11/persons";
			bool modified = false;

			foreach (var el in xdoc.Descendants(ns + "Person"))
			{
				var displayName = el.Attribute("displayName");
				if (displayName != null && !string.IsNullOrEmpty(displayName.Value))
				{
					authorNames.Add(displayName.Value);
					displayName.Value = string.Empty;
					modified = true;
				}
				var userId = el.Attribute("userId");
				if (userId != null && !string.IsNullOrEmpty(userId.Value))
				{
					userId.Value = string.Empty;
					modified = true;
				}
			}

			return modified ? xdoc : null;
		}

		private static (int cleared, string[] authors) ClearPageAnnotationAuthors(PdfPage page)
		{
			if (!page.Elements.ContainsKey("/Annots")) return (0, new string[0]);

			var annotsObj = page.Elements["/Annots"];
			var annots    = annotsObj as PdfArray;
			if (annots == null && annotsObj is PdfReference ar) annots = ar.Value as PdfArray;
			if (annots == null || annots.Elements.Count == 0) return (0, new string[0]);

			var authors = new HashSet<string>(StringComparer.Ordinal);
			int cleared = 0;

			for (int j = 0; j < annots.Elements.Count; j++)
			{
				var item      = annots.Elements[j];
				var annotDict = item as PdfDictionary;
				if (annotDict == null && item is PdfReference annotRef)
					annotDict = annotRef.Value as PdfDictionary;
				if (annotDict == null) continue;

				if (annotDict.Elements.ContainsKey("/Author"))
				{
					var author = annotDict.Elements.GetString("/Author");
					if (!string.IsNullOrEmpty(author)) authors.Add(author);
					annotDict.Elements.Remove("/Author");
					cleared++;
				}
			}

			return (cleared, authors.ToArray());
		}

		// ── ODF (LibreOffice ODT / ODS / ODP) ─────────────────────────────────────

		private static bool IsTgaFile(byte[] rawFile)
		{
			if (rawFile.Length < 18) return false;
			var footer = System.Text.Encoding.ASCII.GetString(rawFile, rawFile.Length - 18, 17);
			if (footer == "TRUEVISION-XFILE.") return true;
			byte cmt = rawFile[1]; byte imt = rawFile[2]; byte depth = rawFile[16];
			int w = rawFile[12] | (rawFile[13] << 8);
			int h = rawFile[14] | (rawFile[15] << 8);
			return (cmt == 0 || cmt == 1)
				&& (imt == 1 || imt == 2 || imt == 3 || imt == 9 || imt == 10 || imt == 11)
				&& (depth == 8 || depth == 15 || depth == 16 || depth == 24 || depth == 32)
				&& w > 0 && h > 0;
		}

		private static bool IsOdfFormat(byte[] rawFile)
		{
			try
			{
				using (var ms  = new MemoryStream(rawFile, writable: false))
				using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false))
				{
					var entry = zip.GetEntry("mimetype");
					if (entry == null) return false;
					using (var stream = entry.Open())
					using (var reader = new System.IO.StreamReader(stream, System.Text.Encoding.ASCII,
						false, 64, false))
					{
						var mime = reader.ReadToEnd().Trim();
						return mime.StartsWith("application/vnd.oasis.opendocument.",
							StringComparison.OrdinalIgnoreCase);
					}
				}
			}
			catch { return false; }
		}

		private static RCFileMetadataResultRecord StripOdfMetadata(byte[] rawFile)
		{
			try
			{
				var zipMs = new MemoryStream();
				zipMs.Write(rawFile, 0, rawFile.Length);
				zipMs.Position = 0;

				int    count             = 0;
				string extractedMetadata = "[]";

				using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Update, leaveOpen: true))
				{
					var metaEntry = zip.GetEntry("meta.xml");
					if (metaEntry != null)
					{
						XDocument xdoc;
						using (var s = metaEntry.Open()) xdoc = XDocument.Load(s);

						var (json, n) = ExtractAndClearOdfMetadata(xdoc);
						if (n > 0)
						{
							extractedMetadata = json;
							count             = n;
							metaEntry.Delete();
							using (var ws = zip.CreateEntry("meta.xml").Open())
								xdoc.Save(ws);
						}
					}
				}

				var r = new RCFileMetadataResultRecord(null);
				r.ssSTFileMetadataResult.ssCleanFile         = zipMs.ToArray();
				r.ssSTFileMetadataResult.ssExtractedMetadata = extractedMetadata;
				r.ssSTFileMetadataResult.ssRemovedEntryCount = count;
				r.ssSTFileMetadataResult.ssIsPassthrough     = false;
				return r;
			}
			catch (Exception ex) when (
				ex is InvalidDataException    ||
				ex is NotSupportedException   ||
				ex is System.Xml.XmlException)
			{
				var note = new JsonObject
				{
					["processingError"] = JsonValue.Create(
						"Metadata stripping was skipped — the ODF file could not be opened. " +
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

		private static (string json, int count) ExtractAndClearOdfMetadata(XDocument metaDoc)
		{
			XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
			XNamespace dc     = "http://purl.org/dc/elements/1.1/";
			XNamespace meta   = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";

			var officeMeta = metaDoc.Descendants()
				.FirstOrDefault(e => e.Name.LocalName == "meta"
				                  && e.Name.Namespace == office);
			if (officeMeta == null) return ("[]", 0);

			var root  = new JsonObject();
			var count = 0;

			void Capture(string key, XElement? el)
			{
				if (el != null && !string.IsNullOrEmpty(el.Value))
				{
					root[key] = JsonValue.Create(el.Value);
					count++;
					el.Value = string.Empty;
				}
			}

			Capture("title",           officeMeta.Element(dc   + "title"));
			Capture("creator",         officeMeta.Element(dc   + "creator"));
			Capture("description",     officeMeta.Element(dc   + "description"));
			Capture("subject",         officeMeta.Element(dc   + "subject"));
			Capture("initialCreator",  officeMeta.Element(meta + "initial-creator"));
			Capture("generator",       officeMeta.Element(meta + "generator"));
			Capture("editingCycles",   officeMeta.Element(meta + "editing-cycles"));
			Capture("editingDuration", officeMeta.Element(meta + "editing-duration"));

			var userDefined = officeMeta.Elements(meta + "user-defined").ToList();
			if (userDefined.Count > 0)
			{
				var customProps = new JsonObject();
				foreach (var el in userDefined)
				{
					var name = el.Attribute(meta + "name")?.Value;
					if (!string.IsNullOrEmpty(name))
					{
						customProps[name] = JsonValue.Create(el.Value);
						count++;
					}
				}
				if (customProps.Count > 0) root["userDefinedProperties"] = customProps;
				foreach (var el in userDefined) el.Remove();
			}

			return count > 0 ? (root.ToJsonString(), count) : ("[]", 0);
		}

		// ── Audio / Video (TagLibSharp) ───────────────────────────────────────────

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

		// ── Passthrough ───────────────────────────────────────────────────────────

		private static RCFileMetadataResultRecord Passthrough(byte[] rawFile)
		{
			var r = new RCFileMetadataResultRecord(null);
			r.ssSTFileMetadataResult.ssCleanFile         = rawFile;
			r.ssSTFileMetadataResult.ssExtractedMetadata = "[]";
			r.ssSTFileMetadataResult.ssRemovedEntryCount = 0;
			r.ssSTFileMetadataResult.ssIsPassthrough     = true;
			return r;
		}

	} // CssFileMetadataStripping

} // OutSystems.NssFileMetadataStripping

