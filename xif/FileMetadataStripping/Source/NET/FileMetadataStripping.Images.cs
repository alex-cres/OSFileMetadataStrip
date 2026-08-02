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
		// Image + SVG strip pipelines (Magick.NET, XML text-node cleaner, format detectors).

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

		/// <summary>
		/// Dispatches the image strip through Magick.NET on healthy hosts and through the
		/// GDI+ fallback engine on hosts where Magick.NET's native library cannot be
		/// initialised (typical on the OutSystems Personal Environment sandbox — HRESULT
		/// 0x8007045A / ERROR_DLL_INIT_FAILED). The <see cref="_magickBroken"/> latch is
		/// AppDomain-scoped: once set, every subsequent call short-circuits to GDI+ so the
		/// per-call cost of a CLR-cached TypeInitializationException is paid at most once.
		/// </summary>
		private static RCFileMetadataResultRecord StripImageMetadataWithFallback(byte[] rawFile)
		{
			if (rawFile == null || rawFile.Length == 0)
				return Passthrough(rawFile != null ? rawFile : System.Array.Empty<byte>());

			if (System.Threading.Volatile.Read(ref _magickBroken) == 1)
				return StripImageMetadataWithGdi(rawFile);

			try
			{
				return StripImageMetadataWithMagick(rawFile);
			}
			catch (System.TypeInitializationException)
			{
				// Magick.NET native init failed on this host. Latch the state and use GDI+
				// from here on. The exception is CLR-cached, so this catch fires only on the
				// first failing call; every subsequent call short-circuits via the flag.
				System.Threading.Interlocked.Exchange(ref _magickBroken, 1);
				return StripImageMetadataWithGdi(rawFile);
			}
		}

		/// <summary>
		/// Pure-managed magic-byte image format detector. Used by <c>DetectCategory</c> when
		/// Magick.NET's native init has already failed (so <see cref="MagickImageInfo"/> is
		/// unavailable), and by the GDI+ fallback engine to distinguish "recognised image
		/// but GDI+ cannot decode it" from "not an image at all".
		/// Returns a friendly format name (e.g. "Jpeg", "Png", "WebP", "Heic") or
		/// <see langword="null"/> when the bytes are not a recognised image.
		/// </summary>
		internal static string DetectImageFormatByMagicBytes(byte[] b)
		{
			if (b == null || b.Length < 4) return null;

			// ── GDI+-decodable formats (JPEG, PNG, GIF, TIFF) ──────────────
			// BMP / DIB are intentionally NOT included here — the primary DetectCategory
			// routes them to Passthrough before this method is called (they carry no
			// metadata containers), and reintroducing them here would misclassify them
			// as "image needing strip" under the GDI+ fallback engine.
			// JPEG   : FF D8 FF
			if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "Jpeg";
			// PNG    : 89 50 4E 47
			if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "Png";
			// GIF    : 47 49 46 38  (GIF8)
			if (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38) return "Gif";
			// TIFF   : 49 49 2A 00 (LE) or 4D 4D 00 2A (BE)
			if ((b[0] == 0x49 && b[1] == 0x49 && b[2] == 0x2A && b[3] == 0x00) ||
				(b[0] == 0x4D && b[1] == 0x4D && b[2] == 0x00 && b[3] == 0x2A)) return "Tiff";

			// ── Recognised image formats NOT decodable by GDI+ ─────────────
			// These fall through to the "GDI+ unsupported format" error contract when the
			// fallback engine is active, and are handled normally by Magick.NET on healthy hosts.

			// WebP: "RIFF" .... "WEBP"
			if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
			                   && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return "WebP";

			// ISOBMFF-based: HEIC / HEIF / AVIF — "ftyp" at offset 4. Brand coverage must
			// mirror IsHeifOrAvifBrand exactly — any divergence lets brands like "hevc"
			// (the most common iPhone HEIC major brand) route through the "unknown magic"
			// branch of the GDI+ fallback and return IsPassthrough=true, which is a
			// security-signal downgrade.
			if (b.Length >= 12 && b[4] == 0x66 && b[5] == 0x74 && b[6] == 0x79 && b[7] == 0x70)
			{
				// HEIC/HEVC image family — any brand beginning with "he" (heic, heix,
				// heim, heis, hevc, hevx, …). Matches IsHeifOrAvifBrand.
				if (b[8] == 0x68 && b[9] == 0x65) return "Heic";
				// HEIF base variants: mif1, msf1
				if (b[8] == 0x6D && (b[9] == 0x69 || b[9] == 0x73) && b[10] == 0x66 && b[11] == 0x31)
					return "Heif";
				// AVIF / AVIF image sequence: avif, avis
				if (b[8] == 0x61 && b[9] == 0x76 && b[10] == 0x69 && (b[11] == 0x66 || b[11] == 0x73))
					return "Avif";
				// Other ftyp brands (mp4/mov) are audio/video, not an image — return null
				// so DetectCategory routes them via the Media detectors instead.
				return null;
			}

			// JPEG XL: naked codestream (FF 0A) or ISOBMFF box "JXL "
			if (b[0] == 0xFF && b[1] == 0x0A) return "Jxl";
			if (b.Length >= 12 && b[0] == 0x00 && b[1] == 0x00 && b[2] == 0x00 && b[3] == 0x0C
			                   && b[4] == 0x4A && b[5] == 0x58 && b[6] == 0x4C && b[7] == 0x20) return "Jxl";

			// JPEG 2000 box format "jP  "  |  code stream FF 4F FF 51
			if (b.Length >= 12 && b[0] == 0x00 && b[1] == 0x00 && b[2] == 0x00 && b[3] == 0x0C
			                   && b[4] == 0x6A && b[5] == 0x50 && b[6] == 0x20 && b[7] == 0x20) return "Jp2";
			if (b[0] == 0xFF && b[1] == 0x4F && b[2] == 0xFF && b[3] == 0x51) return "J2c";

			// JPEG XR / HD Photo: 49 49 BC 01 or 49 49 BC 00
			if (b[0] == 0x49 && b[1] == 0x49 && b[2] == 0xBC) return "Jxr";

			// Photoshop PSD / PSB: "8BPS"
			if (b[0] == 0x38 && b[1] == 0x42 && b[2] == 0x50 && b[3] == 0x53) return "Psd";

			// DirectDraw Surface: "DDS "
			if (b[0] == 0x44 && b[1] == 0x44 && b[2] == 0x53 && b[3] == 0x20) return "Dds";

			// OpenEXR: 76 2F 31 01
			if (b[0] == 0x76 && b[1] == 0x2F && b[2] == 0x31 && b[3] == 0x01) return "Exr";

			// MNG: 8A 4D 4E 47
			if (b[0] == 0x8A && b[1] == 0x4D && b[2] == 0x4E && b[3] == 0x47) return "Mng";

			// QOI: "qoif"
			if (b[0] == 0x71 && b[1] == 0x6F && b[2] == 0x69 && b[3] == 0x66) return "Qoi";

			// FITS: "SIMPLE" (space-padded to "SIMPLE  =")
			if (b.Length >= 6 && b[0] == 0x53 && b[1] == 0x49 && b[2] == 0x4D && b[3] == 0x50
			                  && b[4] == 0x4C && b[5] == 0x45) return "Fits";

			// Radiance HDR: "#?RADIANCE" or "#?RGBE" — require the full identifier.
			if (b.Length >= 10 && b[0] == 0x23 && b[1] == 0x3F
			                   && ((b[2] == 0x52 && b[3] == 0x41 && b[4] == 0x44 && b[5] == 0x49 && b[6] == 0x41 && b[7] == 0x4E && b[8] == 0x43 && b[9] == 0x45)
			                    || (b[2] == 0x52 && b[3] == 0x47 && b[4] == 0x42 && b[5] == 0x45))) return "Hdr";

			// Silicon Graphics Image: 01 DA
			if (b[0] == 0x01 && b[1] == 0xDA) return "Sgi";

			// DPX: "SDPX" or "XPDS"
			if ((b[0] == 0x53 && b[1] == 0x44 && b[2] == 0x50 && b[3] == 0x58) ||
				(b[0] == 0x58 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x53)) return "Dpx";

			// Cineon: 80 2A 5F D7 or D7 5F 2A 80
			if ((b[0] == 0x80 && b[1] == 0x2A && b[2] == 0x5F && b[3] == 0xD7) ||
				(b[0] == 0xD7 && b[1] == 0x5F && b[2] == 0x2A && b[3] == 0x80)) return "Cin";

			// Sun Raster: 59 A6 6A 95
			if (b[0] == 0x59 && b[1] == 0xA6 && b[2] == 0x6A && b[3] == 0x95) return "Sun";

			// DCX (multi-page PCX): B1 68 DE 3A
			if (b[0] == 0xB1 && b[1] == 0x68 && b[2] == 0xDE && b[3] == 0x3A) return "Dcx";

			// PCX: 0A, version 0..5, encoding 0/1
			if (b[0] == 0x0A && b[1] <= 0x05 && b[2] <= 0x01) return "Pcx";

			// Netpbm: P1..P7 followed by whitespace
			if (b[0] == 0x50 && b[1] >= 0x31 && b[1] <= 0x37
			                 && (b[2] == 0x0A || b[2] == 0x0D || b[2] == 0x20 || b[2] == 0x09)) return "Pnm";

			// XBM: "#define"
			if (b.Length >= 7 && b[0] == 0x23 && b[1] == 0x64 && b[2] == 0x65 && b[3] == 0x66
			                  && b[4] == 0x69 && b[5] == 0x6E && b[6] == 0x65) return "Xbm";

			// XPM: "/* XPM */"
			if (b.Length >= 9 && b[0] == 0x2F && b[1] == 0x2A && b[2] == 0x20 && b[3] == 0x58
			                  && b[4] == 0x50 && b[5] == 0x4D) return "Xpm";

			// JBIG2: 97 4A 42 32
			if (b[0] == 0x97 && b[1] == 0x4A && b[2] == 0x42 && b[3] == 0x32) return "Jbig";

			// GIMP XCF: "gimp xcf "
			if (b.Length >= 9 && b[0] == 0x67 && b[1] == 0x69 && b[2] == 0x6D && b[3] == 0x70
			                  && b[4] == 0x20 && b[5] == 0x78 && b[6] == 0x63 && b[7] == 0x66) return "Xcf";

			// Windows Metafile: D7 CD C6 9A (Aldus placeable) or 01 00 09 00 (raw)
			if ((b[0] == 0xD7 && b[1] == 0xCD && b[2] == 0xC6 && b[3] == 0x9A) ||
				(b[0] == 0x01 && b[1] == 0x00 && b[2] == 0x09 && b[3] == 0x00)) return "Wmf";

			// ICO: 00 00 01 00
			if (b[0] == 0x00 && b[1] == 0x00 && b[2] == 0x01 && b[3] == 0x00) return "Ico";

			// DICOM: "DICM" at offset 128
			if (b.Length >= 132 && b[128] == 0x44 && b[129] == 0x49 && b[130] == 0x43 && b[131] == 0x4D) return "Dcm";

			// TGA v2 footer or v1 header heuristic (mirrors IsTgaFile)
			if (b.Length >= 18)
			{
				var tgaFooter = System.Text.Encoding.ASCII.GetString(b, b.Length - 18, 17);
				if (tgaFooter == "TRUEVISION-XFILE.") return "Tga";
				byte cmt = b[1]; byte imt = b[2]; byte depth = b[16];
				int w = b[12] | (b[13] << 8);
				int h = b[14] | (b[15] << 8);
				if ((cmt == 0 || cmt == 1)
					&& (imt == 1 || imt == 2 || imt == 3 || imt == 9 || imt == 10 || imt == 11)
					&& (depth == 8 || depth == 15 || depth == 16 || depth == 24 || depth == 32)
					&& w > 0 && h > 0)
					return "Tga";
			}

			return null;
		}

		private static RCFileMetadataResultRecord StripImageMetadataWithMagick(byte[] rawFile)
		{
			MagickImageCollection images;
			try
			{
				MagickReadSettings readSettings = null;
				if (IsTgaFile(rawFile))
					readSettings = new MagickReadSettings { Format = MagickFormat.Tga };
				else if (IsApngFile(rawFile))
					readSettings = new MagickReadSettings { Format = MagickFormat.APng };
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
				// Format-specific per-image attributes (dpx:*, cin:*) survive the ImageMagick
				// Strip() call, so remove them explicitly here.
				foreach (var frame in images)
				{
					frame.Strip(); // removes EXIF, IPTC, XMP, ICC profiles, and comments
					RemoveNamespacedAttributes(frame);
				}

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

			// Format-specific per-image attributes that survive Strip():
			//   dpx:*  — DPX (SMPTE 268M) production metadata (file.filename, film.id, …)
			//   cin:*  — CIN (Kodak Cineon) production metadata (film.type, origination.device, …)
			foreach (var prefix in new[] { "dpx:", "cin:" })
			{
				var attrs = image.AttributeNames
					.Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
					.ToList();
				if (attrs.Count == 0) continue;
				var node = new JsonObject();
				foreach (var attrName in attrs)
				{
					var key = attrName.Substring(prefix.Length);
					node[key] = JsonValue.Create(image.GetAttribute(attrName));
					count++;
				}
				root[prefix.TrimEnd(':')] = node;
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

		private static bool IsApngFile(byte[] rawFile)
		{
			if (rawFile.Length < 12) return false;
			if (rawFile[0] != 0x89 || rawFile[1] != 0x50 || rawFile[2] != 0x4E || rawFile[3] != 0x47)
				return false;
			for (int i = 8; i < rawFile.Length - 3; i++)
			{
				if (rawFile[i] == 0x61 && rawFile[i + 1] == 0x63 &&
					rawFile[i + 2] == 0x54 && rawFile[i + 3] == 0x4C)
					return true;
			}
			return false;
		}

		/// <summary>Removes attributes with dpx:/cin: namespace prefixes that survive Strip().</summary>
		private static void RemoveNamespacedAttributes(IMagickImage<byte> frame)
		{
			var toRemove = frame.AttributeNames
				.Where(n => n.StartsWith("dpx:", StringComparison.OrdinalIgnoreCase)
				         || n.StartsWith("cin:", StringComparison.OrdinalIgnoreCase))
				.ToList();
			foreach (var attr in toRemove)
				frame.RemoveAttribute(attr);
		}

		/// <summary>Strips XML text nodes (&lt;title&gt;, &lt;desc&gt;, &lt;metadata&gt;) from an SVG document.</summary>
		private static RCFileMetadataResultRecord StripSvgMetadata(byte[] rawFile)
		{
			XDocument xdoc;
			try
			{
				using (var input = new MemoryStream(rawFile, writable: false))
					xdoc = XDocument.Load(input);
			}
			catch (Exception ex) when (
				ex is System.Xml.XmlException ||
				ex is InvalidDataException    ||
				ex is NotSupportedException)
			{
				var note = new JsonObject
				{
					["processingError"] = JsonValue.Create(
						"Metadata stripping was skipped — the SVG could not be parsed as XML. " +
						"Original file returned unchanged. Reason: " + ex.GetType().Name + ": " + ex.Message)
				};
				var er = new RCFileMetadataResultRecord(null);
				er.ssSTFileMetadataResult.ssCleanFile         = rawFile;
				er.ssSTFileMetadataResult.ssExtractedMetadata = note.ToJsonString();
				er.ssSTFileMetadataResult.ssRemovedEntryCount = 0;
				er.ssSTFileMetadataResult.ssIsPassthrough     = false;
				return er;
			}

			var root  = new JsonObject();
			int count = 0;

			foreach (var localName in new[] { "title", "desc", "metadata" })
			{
				var elements = xdoc.Descendants()
					.Where(e => string.Equals(e.Name.LocalName, localName,
						StringComparison.OrdinalIgnoreCase))
					.ToList();
				if (elements.Count == 0) continue;

				var arr = new JsonArray();
				foreach (var el in elements)
				{
					var value = el.Value?.Trim();
					if (!string.IsNullOrEmpty(value))
						arr.Add(JsonValue.Create(value));
					el.Remove();
					count++;
				}
				if (arr.Count > 0) root[localName] = arr;
			}

			using (var output = new MemoryStream())
			{
				xdoc.Save(output);
				var r = new RCFileMetadataResultRecord(null);
				r.ssSTFileMetadataResult.ssCleanFile         = output.ToArray();
				r.ssSTFileMetadataResult.ssExtractedMetadata = count > 0 ? root.ToJsonString() : "[]";
				r.ssSTFileMetadataResult.ssRemovedEntryCount = count;
				r.ssSTFileMetadataResult.ssIsPassthrough     = false;
				return r;
			}
		}

		/// <summary>Detects SVG by byte inspection (start with '&lt;' and contain '&lt;svg' within the first 4 KB).</summary>
		private static bool IsSvgFile(byte[] rawFile)
		{
			if (rawFile.Length < 4) return false;
			int i = 0;
			if (rawFile.Length >= 3 && rawFile[0] == 0xEF && rawFile[1] == 0xBB && rawFile[2] == 0xBF)
				i = 3;
			while (i < rawFile.Length && (rawFile[i] == 0x20 || rawFile[i] == 0x09
			                           || rawFile[i] == 0x0A || rawFile[i] == 0x0D)) i++;
			if (i >= rawFile.Length || rawFile[i] != 0x3C) return false;
			int scanLimit = Math.Min(rawFile.Length, 4096);
			for (int j = i; j < scanLimit - 3; j++)
			{
				if (rawFile[j] == 0x3C
					&& (rawFile[j + 1] == 0x73 || rawFile[j + 1] == 0x53)
					&& (rawFile[j + 2] == 0x76 || rawFile[j + 2] == 0x56)
					&& (rawFile[j + 3] == 0x67 || rawFile[j + 3] == 0x47))
					return true;
			}
			return false;
		}

		/// <summary>Detects DIB (BMP without the 14-byte BITMAPFILEHEADER) by validating the
		/// BITMAPINFOHEADER: header size at bytes 0–3 is one of {40, 52, 56, 108, 124}, planes=1
		/// at bytes 12–13, and bit-count at bytes 14–15 is a valid depth (1/4/8/16/24/32).</summary>
		private static bool IsDibFile(byte[] rawFile)
		{
			if (rawFile.Length < 40) return false;
			uint headerSize = (uint)(rawFile[0]
								  | (rawFile[1] << 8)
								  | (rawFile[2] << 16)
								  | (rawFile[3] << 24));
			if (headerSize != 40  && headerSize != 52  && headerSize != 56
			 && headerSize != 108 && headerSize != 124)
				return false;
			if (rawFile[12] != 0x01 || rawFile[13] != 0x00) return false;
			ushort bitCount = (ushort)(rawFile[14] | (rawFile[15] << 8));
			return bitCount == 1  || bitCount == 4  || bitCount == 8
				|| bitCount == 16 || bitCount == 24 || bitCount == 32;
		}

	} // CssFileMetadataStripping

} // OutSystems.NssFileMetadataStripping
