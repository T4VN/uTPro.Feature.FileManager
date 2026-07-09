# uTPro.Feature.FileManager

A powerful file management dashboard for **Umbraco 16+** backoffice. Browse, upload, download, edit, preview, rename, and delete server files — all from within the Umbraco backoffice.

[![NuGet](https://img.shields.io/nuget/v/uTPro.Feature.FileManager.svg)](https://www.nuget.org/packages/uTPro.Feature.FileManager)
[![NuGet Downloads](https://img.shields.io/nuget/dt/uTPro.Feature.FileManager.svg)](https://www.nuget.org/packages/uTPro.Feature.FileManager)
[![Umbraco Marketplace](https://img.shields.io/badge/Umbraco-Marketplace-blue)](https://marketplace.umbraco.com/package/utpro.feature.filemanager)
[![Umbraco 16+](https://img.shields.io/badge/Umbraco-16%2B-3544B1)](https://umbraco.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## Installation

```bash
dotnet add package uTPro.Feature.FileManager
```

No configuration needed — auto-registers via Umbraco `IComposer`. After installation, navigate to **Settings → File Manager** in the backoffice.

## Features

### File Browsing
- Windows Explorer-style navigation with back, reload, and breadcrumb path bar
- Paginated file listing (100 items per page) with "Load more" for large directories
- Real-time search with debounce filtering
- URL state persistence — reload the page and return to the same folder/file
- Umbraco native icons for file types

### File Operations
- **Upload** files via button or drag & drop
- **Download** files directly from the file list or file view
- **Create** new folders and empty files
- **Rename** files and folders (with live reload in file view)
- **Delete** files and folders (single or bulk selection)
- **Import file via URL** — fetch a remote file and save it to the current folder
- **Extract ZIP** — select `.zip` files and extract them in place

### Code Editor
- Built-in Monaco Editor (via Umbraco's `umb-code-editor`) with syntax highlighting
- Supports: JavaScript, TypeScript, JSON, HTML, CSS, SCSS, Razor, C#, XML, YAML, Markdown, SQL, and more
- Unsaved changes detection with browser close/navigate warning
- Ctrl+S keyboard shortcut to save

### Media Preview
- Inline preview for images (JPG, PNG, GIF, SVG, WebP, BMP, ICO)
- Video player for MP4, WebM, OGG
- Audio player for MP3, WAV, OGG
- PDF viewer via embedded iframe
- Correct MIME types for inline rendering

### Security & Permissions
- **Settings section access** required to view the dashboard
- **Admin** — full access: browse entire server root (ContentRootPath), create, edit, rename, delete, upload, extract
- **Settings (non-admin)** — browse `wwwroot/` tree only (view folder structure, check if files exist — no file actions)
- **Settings + Sensitive Data** — browse `wwwroot/` + view/edit/download file content
- **Write operations** (create, rename, delete, upload, extract ZIP, import URL) — Admin only
- Non-admin users are jailed to `wwwroot/` — cannot access `appsettings.json`, `web.config`, or any files outside `wwwroot`
- Protected files: `web.config`, `appsettings.json`, `appsettings.development.json` cannot be modified or deleted
- Path traversal protection on all endpoints
- **Upload validation** — configurable maximum size plus optional allow-list / block-list of extensions (see [Configuration](#configuration)), enforced on both file upload and import-from-URL
- **SSRF protection on Import via URL** — the supplied URL must use `http`/`https`, and any URL whose host resolves to a loopback, private, link-local, or reserved address (e.g. the cloud metadata endpoint `169.254.169.254`) is rejected. Requests go through `IHttpClientFactory` to avoid socket exhaustion.

### UI
- Windows Explorer-style navigation bar (back, reload, home, breadcrumb path bar, search)
- **Sticky toolbar** — the navigation bar stays pinned to the top while scrolling long file lists, flush with the section header on an opaque surface
- **Workspace footer actions** — primary actions live in the Umbraco workspace footer for a clean, uncluttered toolbar:
  - Browsing: `New ▾` menu (Upload, New Folder, New File, Import via URL), bulk `Delete`, `Extract Zip`, and a live item counter
  - File open: `Save`, `Actions ▾` (Download, Rename, Delete), an `Unsaved` badge, plus file size and type
- Footer actions respect the current user's role — non-admin users never see write actions
- Multi-select with checkbox for bulk delete and zip extract
- Responsive layout with Umbraco UI Library (UUI) components

## Configuration

No configuration is required — the package ships with safe defaults. To customize the upload limits, add an optional `uTPro:Feature:FileManager` section to `appsettings.json`:

```json
{
  "uTPro": {
    "Feature": {
      "FileManager": {
        "MaxUploadSizeMB": 50,
        "AllowedUploadExtensions": [],
        "BlockedUploadExtensions": [ ".exe", ".dll", ".bat" ]
      }
    }
  }
}
```

| Key | Default | Description |
|-----|---------|-------------|
| `MaxUploadSizeMB` | `50` | Maximum allowed upload size in megabytes. Enforced on both file upload and import-from-URL. |
| `AllowedUploadExtensions` | `[]` (allow all) | Allow-list of file extensions. When non-empty, only these extensions may be uploaded. Entries are case-insensitive and may be written with or without a leading dot (`".zip"` or `"zip"`). |
| `BlockedUploadExtensions` | `[]` | Block-list of file extensions. These are always rejected, even if present in the allow-list. |

> The limits apply to write operations only and are enforced server-side, independent of any client-side checks.

## Compatibility

| Umbraco | .NET  | Package |
|---------|-------|---------|
| 16.x    | 9.0   | 1.x     |
| 16.x    | 9.0   | 2.x     |
| 17.x    | 10.0  | 2.x     |
| 18.x    | 10.0  | 2.x     |

The package multi-targets `net9.0` (Umbraco 16) and `net10.0` (Umbraco 17 & 18); a single install picks the right build for your project automatically.

## Screenshots

### uTPro.Feature.FileManager - v2.0.0
![uTPro.Feature.FileManager v2.0.0](https://raw.githubusercontent.com/T4VN/uTPro.Feature.FileManager/refs/heads/main/Image/Screenshot-UI2.0.0.png)

### Admin view — full access to browse, edit, upload, and manage all server files
![Admin view](https://raw.githubusercontent.com/T4VN/uTPro.Feature.FileManager/refs/heads/main/Image/Admin%20-%20Screenshot%202026-03-31%20122558.png)

### Code editor with syntax highlighting (Monaco) and file actions menu
![Code editor and file actions](https://raw.githubusercontent.com/T4VN/uTPro.Feature.FileManager/refs/heads/main/Image/Edit%20File%20and%20Action%20File%20-%20Screenshot%202026-03-31%20122905.png)

### Select and extract ZIP files directly in the backoffice
![Extract ZIP](https://raw.githubusercontent.com/T4VN/uTPro.Feature.FileManager/refs/heads/main/Image/Extract%20ZIP%20-%20Screenshot%202026-03-31%20122558.png)

### Paginated loading — handles 2000+ files without UI lag
![Load more](https://raw.githubusercontent.com/T4VN/uTPro.Feature.FileManager/refs/heads/main/Image/Load%20more%20-%20Screenshot%202026-03-31%20122734.png)

### Create new files, folders, or import from URL
![New file or import URL](https://raw.githubusercontent.com/T4VN/uTPro.Feature.FileManager/refs/heads/main/Image/New%20File%20or%20Import%20Url%20-%20Screenshot%202026-03-31%20122823.png)

### Non-admin users are restricted to wwwroot only
![Non-admin restricted to wwwroot](https://raw.githubusercontent.com/T4VN/uTPro.Feature.FileManager/refs/heads/main/Image/User%20Not%20Sensitive%20data%20-%20Screenshot%202026-03-31%20123133.png)

### Sensitive Data role — read-only access to edit and view all files
![Sensitive Data role](https://raw.githubusercontent.com/T4VN/uTPro.Feature.FileManager/refs/heads/main/Image/User%20Sensitive%20data%20Edit%20file%20-%20Screenshot%202026-03-31%20123133.png)

## Development

```bash
git clone https://github.com/T4VN/uTPro.Feature.FileManager.git
cd uTPro.Feature.FileManager
dotnet run --project src/uTPro.Feature.FileManager.TestSite
```

Navigate to `https://localhost:54740/umbraco`, log in, go to **Settings → File Manager**.

## Project Structure

```
src/uTPro.Feature.FileManager/
├── Controllers/
│   └── FileManagerApiController.cs    # API endpoints with role-based access
├── Services/
│   ├── IFileManagerService.cs         # Service interface
│   ├── FileManagerService.cs          # File operations implementation
│   └── FileManagerComposer.cs         # DI registration
├── Models/
│   ├── FileItemViewModel.cs           # File/folder view model
│   ├── Requests.cs                    # API request DTOs
│   └── Results.cs                     # API response DTOs
└── wwwroot/
    ├── index.js                       # Main Lit Element dashboard view
    ├── footer.js                      # Workspace footer app (New/Save/Actions/bulk + item count)
    ├── context.js                     # Shared workspace context bridging view ↔ footer
    ├── helpers.js                     # Constants, utilities, icon mapping
    ├── styles.js                      # CSS styles
    └── umbraco-package.json           # Umbraco package manifest
```

## Changelog

### 2.1.0
- **Configurable upload limits** via the `uTPro:Feature:FileManager` section — `MaxUploadSizeMB`, an `AllowedUploadExtensions` allow-list, and a `BlockedUploadExtensions` block-list — enforced on both file upload and import-from-URL. See [Configuration](#configuration).
- **Import via URL hardening** — now uses `IHttpClientFactory` and includes an SSRF guard that rejects URLs resolving to loopback/private/link-local/reserved addresses and any non-`http(s)` scheme.
- No breaking API changes.

### 2.0.1
- Maintenance release — clean rebuild from a wiped output to guarantee the shipped assembly is current (packaging now goes through a deterministic clean-pack step). No source or API changes versus 2.0.0.

### 2.0.0
- Support for Umbraco 16, 17 and 18 (multi-target `net9.0` / `net10.0`).
- Hardened path-traversal root check and upload filename sanitization.
- No breaking API changes.

### 1.x
- Initial releases — file browsing, upload/download, code editor, media preview, and role-based access for Umbraco 16.

## License

MIT

## Author

**T4VN** — [GitHub](https://github.com/T4VN)
