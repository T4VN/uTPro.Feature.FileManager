using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using uTPro.Feature.FileManager.Models;

namespace uTPro.Feature.FileManager.Services;

internal class FileManagerService(
    IWebHostEnvironment env,
    ILogger<FileManagerService> logger) : IFileManagerService
{
    private static readonly HashSet<string> EditableExtensions =
    [
        ".cshtml", ".css", ".js", ".json", ".xml", ".txt", ".html", ".htm",
        ".config", ".md", ".razor", ".ts", ".tsx", ".jsx", ".mjs",
        ".scss", ".less", ".yaml", ".yml",
        ".svg", ".csv", ".log",
        ".cs", ".csproj", ".sln", ".props", ".targets",
        ".sql", ".sh", ".bat", ".cmd", ".ps1",
        ".env", ".gitignore", ".editorconfig",
        ".map", ".lock"
    ];

    private static readonly HashSet<string> BlockedNames =
    [
        "web.config", "appsettings.json", "appsettings.development.json"
    ];

    // ── Browse ───────────────────────────────────────────

    public BrowseResult Browse(string relativePath, int page = 1, int pageSize = 100, string search = "")
    {
        var safePath = SanitizePath(relativePath);
        var fullPath = GetFullPath(safePath);

        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Directory not found: {safePath}");

        var dirInfo = new DirectoryInfo(fullPath);

        var folders = dirInfo.EnumerateDirectories()
            .Where(d => !d.Name.StartsWith('.'))
            .OrderBy(d => d.Name)
            .Select(d => new FileItemViewModel
            {
                Name = d.Name,
                Path = CombinePath(safePath, d.Name),
                Type = "folder",
                LastModified = d.LastWriteTime,
            });

        var files = dirInfo.EnumerateFiles()
            .Where(f => !f.Name.StartsWith('.'))
            .OrderBy(f => f.Name)
            .Select(f => new FileItemViewModel
            {
                Name = f.Name,
                Path = CombinePath(safePath, f.Name),
                Type = "file",
                Size = f.Length,
                LastModified = f.LastWriteTime,
                Extension = f.Extension.ToLower(),
                IsEditable = EditableExtensions.Contains(f.Extension.ToLower())
            });

        var allItems = folders.Concat(files);

        if (!string.IsNullOrWhiteSpace(search))
            allItems = allItems.Where(i => i.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

        var itemsList = allItems.ToList();
        var totalItems = itemsList.Count;
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var pagedItems = itemsList.Skip((page - 1) * pageSize).Take(pageSize);

        var parentPath = safePath.Contains('/')
            ? safePath[..safePath.LastIndexOf('/')]
            : "";

        return new BrowseResult
        {
            CurrentPath = safePath,
            ParentPath = parentPath,
            Items = pagedItems,
            TotalItems = totalItems,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages
        };
    }

    // ── File Content ─────────────────────────────────────

    public FileContentResult GetFileContent(string relativePath)
    {
        var safePath = SanitizePath(relativePath);
        var fullPath = GetFullPath(safePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {safePath}");

        var ext = Path.GetExtension(fullPath).ToLower();
        if (!EditableExtensions.Contains(ext))
            throw new InvalidOperationException($"File type not editable: {ext}");

        return new FileContentResult
        {
            Path = safePath,
            Name = Path.GetFileName(fullPath),
            Content = File.ReadAllText(fullPath),
            Extension = ext
        };
    }

    public void SaveFileContent(string relativePath, string content)
    {
        var safePath = SanitizePath(relativePath);
        ValidateNotBlocked(safePath);
        var fullPath = GetFullPath(safePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {safePath}");

        File.WriteAllText(fullPath, content);
        logger.LogInformation("File saved: {Path}", safePath);
    }

    // ── Create / Rename / Delete ─────────────────────────

    public void CreateFolder(string relativePath, string folderName)
    {
        var safePath = SanitizePath(relativePath);
        var safeName = SanitizeName(folderName);
        var fullPath = Path.Combine(GetFullPath(safePath), safeName);

        if (Directory.Exists(fullPath))
            throw new InvalidOperationException("Folder already exists.");

        Directory.CreateDirectory(fullPath);
        logger.LogInformation("Folder created: {Path}/{Name}", safePath, safeName);
    }

    public void CreateFile(string relativePath, string fileName)
    {
        var safePath = SanitizePath(relativePath);
        var safeName = SanitizeName(fileName);
        var fullPath = Path.Combine(GetFullPath(safePath), safeName);

        if (File.Exists(fullPath))
            throw new InvalidOperationException("File already exists.");

        File.WriteAllText(fullPath, "");
        logger.LogInformation("File created: {Path}/{Name}", safePath, safeName);
    }

    public async Task ImportFromUrl(string relativePath, string url)
    {
        var safePath = SanitizePath(relativePath);
        var fullDir = GetFullPath(safePath);

        if (!Directory.Exists(fullDir))
            throw new DirectoryNotFoundException($"Directory not found: {safePath}");

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        // Fixed filename format: import-file-<datetime>.<ext from content-type>
        var ext = ContentTypeToExtension(response.Content.Headers.ContentType?.MediaType);
        var fileName = $"import-file-{DateTime.Now:yyyyMMddHHmmss}{ext}";

        var safeName = SanitizeName(fileName);
        var fullPath = Path.Combine(fullDir, safeName);

        await using var fileStream = new FileStream(fullPath, FileMode.Create);
        await response.Content.CopyToAsync(fileStream);

        logger.LogInformation("Imported from URL: {Url} -> {Path}/{Name}", url, safePath, safeName);
    }

    private static string ContentTypeToExtension(string? contentType)
    {
        return contentType?.ToLower() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/svg+xml" => ".svg",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
            "application/pdf" => ".pdf",
            "application/zip" => ".zip",
            "application/json" => ".json",
            "application/xml" or "text/xml" => ".xml",
            "text/html" => ".html",
            "text/css" => ".css",
            "text/javascript" or "application/javascript" => ".js",
            "text/plain" => ".txt",
            "text/csv" => ".csv",
            "text/markdown" => ".md",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "audio/mpeg" => ".mp3",
            "audio/wav" => ".wav",
            "audio/ogg" => ".ogg",
            "application/octet-stream" => ".bin",
            _ => ".bin"
        };
    }

    public void Rename(string relativePath, string newName)
    {
        var safePath = SanitizePath(relativePath);
        ValidateNotBlocked(safePath);
        var safeName = SanitizeName(newName);
        var fullPath = GetFullPath(safePath);
        var parentDir = Path.GetDirectoryName(fullPath)!;
        var newFullPath = Path.Combine(parentDir, safeName);

        if (File.Exists(fullPath))
            File.Move(fullPath, newFullPath);
        else if (Directory.Exists(fullPath))
            Directory.Move(fullPath, newFullPath);
        else
            throw new FileNotFoundException($"Not found: {safePath}");

        logger.LogInformation("Renamed: {Old} -> {New}", safePath, safeName);
    }

    public void Delete(string relativePath)
    {
        var safePath = SanitizePath(relativePath);
        ValidateNotBlocked(safePath);
        var fullPath = GetFullPath(safePath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            logger.LogInformation("File deleted: {Path}", safePath);
        }
        else if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
            logger.LogInformation("Folder deleted: {Path}", safePath);
        }
        else
            throw new FileNotFoundException($"Not found: {safePath}");
    }

    public void ExtractZip(string relativePath)
    {
        var safePath = SanitizePath(relativePath);
        var fullPath = GetFullPath(safePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {safePath}");

        if (!Path.GetExtension(fullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .zip files can be extracted.");

        var extractDir = Path.Combine(Path.GetDirectoryName(fullPath)!, Path.GetFileNameWithoutExtension(fullPath));
        System.IO.Compression.ZipFile.ExtractToDirectory(fullPath, extractDir, overwriteFiles: true);
        logger.LogInformation("Zip extracted: {Path} -> {Dir}", safePath, extractDir);
    }

    // ── Helpers ──────────────────────────────────────────

    public string GetFullPath(string relativePath)
    {
        var root = env.ContentRootPath;
        var full = Path.GetFullPath(Path.Combine(root, relativePath));

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Access denied: path traversal detected.");

        return full;
    }

    private static string SanitizePath(string path)
        => (path ?? "").Replace('\\', '/').Trim('/').Replace("..", "");

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(clean))
            throw new ArgumentException("Invalid name.");
        return clean;
    }

    private static string CombinePath(string basePath, string name)
        => string.IsNullOrEmpty(basePath) ? name : $"{basePath}/{name}";

    private static void ValidateNotBlocked(string path)
    {
        var fileName = Path.GetFileName(path).ToLower();
        if (BlockedNames.Contains(fileName))
            throw new UnauthorizedAccessException($"Cannot modify protected file: {fileName}");
    }
}
