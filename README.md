# uTPro.Feature.FileManager

A powerful file management dashboard for **Umbraco 16+** backoffice. Browse, upload, download, edit, preview, rename, and delete server files — all from within the Umbraco backoffice.

[![NuGet](https://img.shields.io/nuget/v/uTPro.Feature.FileManager.svg)](https://www.nuget.org/packages/uTPro.Feature.FileManager)
[![Umbraco Marketplace](https://img.shields.io/badge/Umbraco-Marketplace-blue)](https://marketplace.umbraco.com/package/utpro.feature.filemanager)
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

### UI
- Windows Explorer-style navigation bar (back, reload, path bar, search)
- Grouped action menus (New ▾, Actions ▾) for clean toolbar
- Multi-select with checkbox for bulk delete and zip extract
- File view bar showing file size, last modified date, and file type
- Responsive layout with Umbraco UI Library (UUI) components

## Compatibility

| Umbraco | .NET | Package |
|---------|------|---------|
| 16.x    | 9.0  | 1.x     |

## Screenshots

*Settings → File Manager dashboard*

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
    ├── index.js                       # Main Lit Element dashboard component
    ├── helpers.js                     # Constants, utilities, icon mapping
    ├── styles.js                      # CSS styles
    └── umbraco-package.json           # Umbraco package manifest
```

## License

MIT

## Author

**T4VN** — [GitHub](https://github.com/T4VN)
