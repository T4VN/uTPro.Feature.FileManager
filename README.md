# uTPro File Manager & Media Cleanup for Umbraco

A powerful **File Manager** and **Media Cleanup** toolkit for the **Umbraco 16+** backoffice. Browse, upload, download, edit, preview, rename and delete server files — and scan the media library to recycle, restore or delete unused, broken, duplicate, orphaned, large and disallowed media.

[![NuGet](https://img.shields.io/nuget/v/uTPro.Feature.FileManager.svg)](https://www.nuget.org/packages/uTPro.Feature.FileManager)
[![NuGet Downloads](https://img.shields.io/nuget/dt/uTPro.Feature.FileManager.svg)](https://www.nuget.org/packages/uTPro.Feature.FileManager)
[![Umbraco Marketplace](https://img.shields.io/badge/Umbraco-Marketplace-blue)](https://marketplace.umbraco.com/package/utpro.feature.filemanager)
[![Umbraco 16+](https://img.shields.io/badge/Umbraco-16%2B-3544B1)](https://umbraco.com)
[![License: Free (proprietary)](https://img.shields.io/badge/License-Free%20(proprietary)-green.svg)](LICENSE.txt)

![uTPro File Manager](https://raw.githubusercontent.com/T4VN/uTPro.Feature.FileManager/refs/heads/main/Image/v5.0.0/FileManager-default.png)

---

## Features

- Windows Explorer-style file browser with breadcrumb navigation
- Upload, download, create, rename, delete, extract ZIP, import via URL
- Built-in Monaco code editor with syntax highlighting
- Media preview (images, video, audio, PDF)
- Multi-root "Locations" support
- **Media Cleanup** — scan for unused, broken, duplicate, orphaned, large, disallowed media
- Bulk actions, smart duplicate cleanup, preview before action
- Role-based security: Admin / Settings / Sensitive Data
- RCE guard, SSRF protection, path traversal protection

---

## Quick Start

```bash
dotnet add package uTPro.Feature.FileManager
```

Navigate to **Settings → File Manager**. No configuration needed.

| Umbraco | .NET | Target |
|---|---|---|
| 16 | .NET 9 | `net9.0` |
| 17 & 18 | .NET 10 | `net10.0` |

---

## Configuration

Under `uTPro:Feature:FileManager` in `appsettings.json` (all optional):

| Key | Default | Description |
|-----|---------|-------------|
| `MaxUploadSizeMB` | `50` | Max upload size |
| `MediaLargeFileThresholdMB` | `100` | Large file threshold |
| `MediaScanCacheSeconds` | `30` | Scan cache duration |
| `Roots` | `[]` | Multi-root locations |

---

## 📖 Full Documentation

**[docs.utpro.dev/uTPro.Feature.FileManager](https://docs.utpro.dev/uTPro.Feature.FileManager/)**

---

## License

Free to use (including commercially) under a proprietary [End User License Agreement](LICENSE.txt).

---

> 📦 [NuGet](https://www.nuget.org/packages/uTPro.Feature.FileManager) · [GitHub](https://github.com/T4VN/uTPro.Feature.FileManager) · [Umbraco Marketplace](https://marketplace.umbraco.com/package/utpro.feature.filemanager)
