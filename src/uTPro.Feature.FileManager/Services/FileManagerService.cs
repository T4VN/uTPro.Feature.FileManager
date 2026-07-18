using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using uTPro.Feature.FileManager.Models;

namespace uTPro.Feature.FileManager.Services;

internal class FileManagerService(
    IWebHostEnvironment env,
    ILogger<FileManagerService> logger,
    IOptions<FileManagerOptions> fileManagerOptions,
    IOptions<Umbraco.Cms.Core.Configuration.Models.ContentSettings> contentSettings) : IFileManagerService
{
    // Editable text extensions, protected file names and dangerous (RCE) write extensions are all
    // configurable via FileManagerOptions (uTPro:Feature:FileManager). The two security lists are
    // additive (config can only ADD to the built-in defaults, never remove a protection).
    private FileManagerOptions Options => fileManagerOptions.Value;

    // ── Browse ───────────────────────────────────────────

    public BrowseResult Browse(string relativePath, int page = 1, int pageSize = 100, string search = "", bool webRootScope = false, string? baseRootOverride = null)
    {
        var safePath = SanitizePath(relativePath);
        var fullPath = GetFullPath(safePath, webRootScope, baseRootOverride);

        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Directory not found: {safePath}");

        var dirInfo = new DirectoryInfo(fullPath);
        var editableExtensions = Options.EffectiveEditableExtensions;

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
                IsEditable = editableExtensions.Contains(f.Extension.ToLower())
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

    public FileContentResult GetFileContent(string relativePath, bool webRootScope = false, string? baseRootOverride = null)
    {
        var safePath = SanitizePath(relativePath);
        ValidateNotBlocked(safePath);
        var fullPath = GetFullPath(safePath, webRootScope, baseRootOverride);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {safePath}");

        var ext = Path.GetExtension(fullPath).ToLower();
        if (!Options.EffectiveEditableExtensions.Contains(ext))
            throw new InvalidOperationException($"File type not editable: {ext}");

        return new FileContentResult
        {
            Path = safePath,
            Name = Path.GetFileName(fullPath),
            Content = File.ReadAllText(fullPath),
            Extension = ext
        };
    }

    public void SaveFileContent(string relativePath, string content, bool webRootScope = false, string? baseRootOverride = null)
    {
        var safePath = SanitizePath(relativePath);
        ValidateNotBlocked(safePath);
        ValidateWritableExtension(safePath);
        var fullPath = GetFullPath(safePath, webRootScope, baseRootOverride);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {safePath}");

        File.WriteAllText(fullPath, content);
        logger.LogInformation("File saved: {Path}", safePath);
    }

    // ── Create / Rename / Delete ─────────────────────────

    public void CreateFolder(string relativePath, string folderName, string? baseRootOverride = null)
    {
        var safePath = SanitizePath(relativePath);
        var safeName = SanitizeName(folderName);
        var fullPath = Path.Combine(GetFullPath(safePath, baseRootOverride: baseRootOverride), safeName);

        if (Directory.Exists(fullPath))
            throw new InvalidOperationException("Folder already exists.");

        Directory.CreateDirectory(fullPath);
        logger.LogInformation("Folder created: {Path}/{Name}", safePath, safeName);
    }

    public void CreateFile(string relativePath, string fileName, string? baseRootOverride = null)
    {
        var safePath = SanitizePath(relativePath);
        var safeName = SanitizeName(fileName);
        ValidateWritableExtension(safeName);
        var fullPath = Path.Combine(GetFullPath(safePath, baseRootOverride: baseRootOverride), safeName);

        if (File.Exists(fullPath))
            throw new InvalidOperationException("File already exists.");

        File.WriteAllText(fullPath, "");
        logger.LogInformation("File created: {Path}/{Name}", safePath, safeName);
    }

    public async Task ImportFromUrl(string relativePath, string url, string? baseRootOverride = null)
    {
        var safePath = SanitizePath(relativePath);
        var fullDir = GetFullPath(safePath, baseRootOverride: baseRootOverride);

        if (!Directory.Exists(fullDir))
            throw new DirectoryNotFoundException($"Directory not found: {safePath}");

        await ValidateUrlAsync(url);

        var options = fileManagerOptions.Value;

        var response = await GetWithValidatedRedirectsAsync(url);
        response.EnsureSuccessStatusCode();

        // Reject early if the server advertises a size that exceeds the configured limit.
        if (response.Content.Headers.ContentLength is { } contentLength
            && contentLength > options.MaxUploadSizeBytes)
            throw new InvalidOperationException($"File exceeds the maximum upload size of {options.MaxUploadSizeMB} MB.");

        // Fixed filename format: import-file-<datetime>.<ext from content-type>
        var ext = ContentTypeToExtension(response.Content.Headers.ContentType?.MediaType);
        var fileName = $"import-file-{DateTime.Now:yyyyMMddHHmmss}{ext}";

        var safeName = SanitizeName(fileName);

        if (!options.IsExtensionAllowed(safeName, contentSettings.Value.DisallowedUploadedFileExtensions, contentSettings.Value.AllowedUploadedFileExtensions))
            throw new InvalidOperationException($"File type not allowed: {ext}");

        var fullPath = Path.Combine(fullDir, safeName);

        if (File.Exists(fullPath))
            throw new InvalidOperationException($"File already exists: {safeName}");

        await using (var fileStream = new FileStream(fullPath, FileMode.CreateNew))
        {
            await response.Content.CopyToAsync(fileStream);
        }

        // Enforce the size limit again after download in case ContentLength was absent or wrong.
        var writtenLength = new FileInfo(fullPath).Length;
        if (writtenLength > options.MaxUploadSizeBytes)
        {
            File.Delete(fullPath);
            throw new InvalidOperationException($"File exceeds the maximum upload size of {options.MaxUploadSizeMB} MB.");
        }

        logger.LogInformation("Imported from URL: {Url} -> {Path}/{Name}", url, safePath, safeName);
    }

    /// <summary>
    /// Performs an HTTP GET while following redirects manually, re-validating every redirect target
    /// through <see cref="ValidateUrlAsync"/>. Auto-redirect is disabled so a redirect to an internal
    /// address (SSRF rebinding) cannot bypass the initial guard. Redirects are capped to avoid loops.
    /// </summary>
    private async Task<HttpResponseMessage> GetWithValidatedRedirectsAsync(string url)
    {
        const int maxRedirects = 5;

        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };

        var currentUrl = url;
        for (var hop = 0; ; hop++)
        {
            var response = await httpClient.GetAsync(currentUrl, HttpCompletionOption.ResponseHeadersRead);

            if (!IsRedirect(response.StatusCode))
                return response;

            if (hop >= maxRedirects)
            {
                response.Dispose();
                throw new InvalidOperationException("Too many redirects while importing from URL.");
            }

            var location = response.Headers.Location;
            response.Dispose();

            if (location is null)
                throw new InvalidOperationException("Redirect response did not include a Location header.");

            // Resolve relative redirects against the current URL, then re-run the SSRF guard.
            var nextUri = location.IsAbsoluteUri
                ? location
                : new Uri(new Uri(currentUrl), location);

            await ValidateUrlAsync(nextUri.ToString());
            currentUrl = nextUri.ToString();
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.MovedPermanently => true,   // 301
        HttpStatusCode.Found => true,               // 302
        HttpStatusCode.SeeOther => true,            // 303
        HttpStatusCode.TemporaryRedirect => true,   // 307
        HttpStatusCode.PermanentRedirect => true,   // 308
        _ => false
    };

    /// <summary>
    /// SSRF guard for <see cref="ImportFromUrl"/>: only allow http/https, and reject any URL whose
    /// host resolves to a loopback / private / link-local / unique-local address. This stops an
    /// admin-supplied URL from being used to reach internal-only services or the cloud metadata
    /// endpoint (169.254.169.254). Resolves DNS up front and validates every returned address.
    /// </summary>
    private static async Task ValidateUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Invalid URL.");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Only http and https URLs are allowed.");

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try { addresses = await Dns.GetHostAddressesAsync(uri.Host); }
            catch (SocketException) { throw new InvalidOperationException($"Could not resolve host: {uri.Host}"); }
        }

        if (addresses.Length == 0 || addresses.Any(IsPrivateOrReserved))
            throw new UnauthorizedAccessException("Access denied: the URL points to a private or reserved address.");
    }

    private static bool IsPrivateOrReserved(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork) // IPv4
        {
            return bytes[0] switch
            {
                10 => true,                                   // 10.0.0.0/8
                127 => true,                                  // 127.0.0.0/8 (loopback)
                0 => true,                                    // 0.0.0.0/8
                169 when bytes[1] == 254 => true,             // 169.254.0.0/16 (link-local / metadata)
                172 when bytes[1] >= 16 && bytes[1] <= 31 => true, // 172.16.0.0/12
                192 when bytes[1] == 168 => true,             // 192.168.0.0/16
                _ => bytes[0] >= 224                           // 224.0.0.0/4 multicast + reserved
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6) // IPv6
        {
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal   // fc00::/7
                || address.IsIPv6Multicast;
        }

        return true;
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

    public void Rename(string relativePath, string newName, string? baseRootOverride = null)
    {
        var safePath = SanitizePath(relativePath);
        ValidateNotBlocked(safePath);
        var safeName = SanitizeName(newName);
        var fullPath = GetFullPath(safePath, baseRootOverride: baseRootOverride);
        var parentDir = Path.GetDirectoryName(fullPath)!;
        var newFullPath = Path.Combine(parentDir, safeName);

        if (File.Exists(fullPath))
        {
            ValidateWritableExtension(safeName);
            File.Move(fullPath, newFullPath);
        }
        else if (Directory.Exists(fullPath))
            Directory.Move(fullPath, newFullPath);
        else
            throw new FileNotFoundException($"Not found: {safePath}");

        logger.LogInformation("Renamed: {Old} -> {New}", safePath, safeName);
    }

    public void Delete(string relativePath, string? baseRootOverride = null)
    {
        var safePath = SanitizePath(relativePath);
        ValidateNotBlocked(safePath);
        var fullPath = GetFullPath(safePath, baseRootOverride: baseRootOverride);

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

    public void ExtractZip(string relativePath, string? baseRootOverride = null)
    {
        var safePath = SanitizePath(relativePath);
        var fullPath = GetFullPath(safePath, baseRootOverride: baseRootOverride);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {safePath}");

        if (!Path.GetExtension(fullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .zip files can be extracted.");

        var extractDir = Path.Combine(Path.GetDirectoryName(fullPath)!, Path.GetFileNameWithoutExtension(fullPath));
        System.IO.Compression.ZipFile.ExtractToDirectory(fullPath, extractDir, overwriteFiles: true);
        logger.LogInformation("Zip extracted: {Path} -> {Dir}", safePath, extractDir);
    }

    // ── Helpers ──────────────────────────────────────────

    /// <summary>
    /// Resolves the base directory a request is confined to. Admins operate against the content
    /// root (full server); non-admins (<paramref name="webRootScope"/> = true) are confined to the
    /// host's configured web root (<see cref="IWebHostEnvironment.WebRootPath"/> — e.g.
    /// uTPro:Hosting:RootPath), which may be an absolute path OUTSIDE the content root.
    /// </summary>
    private string ResolveBaseRoot(bool webRootScope, string? baseRootOverride = null)
    {
        // An explicit, already-resolved Locations root always wins.
        if (!string.IsNullOrEmpty(baseRootOverride))
            return Path.GetFullPath(baseRootOverride);
        if (webRootScope && !string.IsNullOrEmpty(env.WebRootPath))
            return Path.GetFullPath(env.WebRootPath);
        return Path.GetFullPath(env.ContentRootPath);
    }

    public string GetFullPath(string relativePath, bool webRootScope = false, string? baseRootOverride = null)
    {
        var root = ResolveBaseRoot(webRootScope, baseRootOverride);
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relativePath));

        // Allow the root itself, or any path strictly under it (avoids C:\site vs C:\site2 prefix match).
        if (!full.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
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

    public void ValidateNotBlocked(string relativePath)
    {
        var fileName = Path.GetFileName(SanitizePath(relativePath)).ToLower();
        if (Options.EffectiveBlockedNames.Contains(fileName))
            throw new UnauthorizedAccessException($"Cannot access protected file: {fileName}");
    }

    /// <summary>
    /// Rejects create/write/rename operations that target a server-executable or otherwise
    /// dangerous extension (RCE guard). Applied to CreateFile, SaveFileContent and Rename.
    /// </summary>
    private void ValidateWritableExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (Options.EffectiveDangerousWriteExtensions.Contains(ext))
            throw new UnauthorizedAccessException($"Cannot create or write files with a server-executable extension: {ext}");
    }
}
