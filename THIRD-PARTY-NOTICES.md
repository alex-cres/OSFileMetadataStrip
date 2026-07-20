# Third-Party Notices

This file lists the open-source packages used by **OSFileMetadataStrip** at runtime, along with their licenses and source locations. Test-only packages are excluded.

---

## Direct Runtime Dependencies

### OutSystems.ExternalLibraries.SDK

- **Version:** 1.5.0
- **License:** OutSystems proprietary (required to build and publish OutSystems ODC External Libraries)
- **Source:** https://www.nuget.org/packages/OutSystems.ExternalLibraries.SDK

---

### Magick.NET-Q8-AnyCPU

- **Version:** 14.15.0
- **License:** [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
- **Source:** https://github.com/dlemstra/Magick.NET
- **NuGet:** https://www.nuget.org/packages/Magick.NET-Q8-AnyCPU
- **Purpose:** Image decoding and metadata stripping (EXIF, IPTC, XMP, ICC profiles, comments) via `image.Strip()`.

---

### PDFsharp

- **Version:** 6.2.4
- **License:** [MIT License](https://opensource.org/licenses/MIT)
- **Source:** https://github.com/empira/PDFsharp
- **NuGet:** https://www.nuget.org/packages/PDFsharp
- **Purpose:** Reading and clearing PDF /Info dictionary fields (Title, Author, Subject, Keywords, Creator).

---

### DocumentFormat.OpenXml

- **Version:** 3.5.1
- **License:** [MIT License](https://opensource.org/licenses/MIT)
- **Copyright:** Microsoft Corporation
- **Source:** https://github.com/dotnet/Open-XML-SDK
- **NuGet:** https://www.nuget.org/packages/DocumentFormat.OpenXml
- **Purpose:** Reading and clearing Office Open XML package core properties (Creator, Title, Subject, etc.) for DOCX, XLSX, and PPTX files.

---

### TagLibSharp

- **Version:** 2.3.0
- **License:** [GNU Lesser General Public License v2.1](https://www.gnu.org/licenses/lgpl-2.1.html) **or** [Mozilla Public License 1.1](https://www.mozilla.org/en-US/MPL/1.1/) (dual-licensed, choose either)
- **Source:** https://github.com/mono/taglib-sharp
- **NuGet:** https://www.nuget.org/packages/TagLibSharp
- **Purpose:** Reading and stripping audio/video metadata (ID3 tags, Vorbis comments, metadata atoms) from MP3, FLAC, OGG, WAV, MP4, MOV, AVI, MKV, WebM, WMV, WMA, and other formats.

> **LGPL note:** Under the LGPL you are permitted to use TagLibSharp in commercial applications without open-sourcing your own code. You must allow users to relink against a modified version of the library. This requirement is satisfied when the library is distributed as a separate DLL (the standard NuGet distribution model).

---

## Transitive Runtime Dependencies

The following packages are pulled in automatically by the direct dependencies above.

| Package | Version | License | Introduced by |
|---------|---------|---------|--------------|
| `Magick.NET.Core` | 14.15.0 | Apache 2.0 | Magick.NET-Q8-AnyCPU |
| `DocumentFormat.OpenXml.Framework` | 3.5.1 | MIT | DocumentFormat.OpenXml |
| `System.IO.Packaging` | 10.0.2 | MIT | DocumentFormat.OpenXml |
| `System.Security.Cryptography.Pkcs` | 8.0.1 | MIT | PDFsharp |
| `Microsoft.Extensions.Logging.Abstractions` | 8.0.3 | MIT | PDFsharp |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 8.0.2 | MIT | PDFsharp |

---

## Project License

**OSFileMetadataStrip** itself is released under the [MIT License](./LICENSE).
