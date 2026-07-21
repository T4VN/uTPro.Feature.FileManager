using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Authorization;
using uTPro.Feature.FileManager.Models;
using uTPro.Feature.FileManager.Services;

namespace uTPro.Feature.FileManager.Controllers;

/// <summary>
/// File Manager API — requires Settings section access.
/// Permissions:
///   Admin          → full access, root = ContentRootPath
///   Settings       → browse the site web-root tree only (no file actions)
///   + SensitiveData → browse + view/edit/download files in the web root
///   Write ops (create/rename/delete/upload/extract) → Admin only
///
/// The non-admin jail follows the host's configured web root
/// (<see cref="IWebHostEnvironment.WebRootPath"/> — e.g. uTPro:Hosting:RootPath) rather than a
/// hardcoded "wwwroot", so it stays correct when the web root is relocated.
/// </summary>
[VersionedApiBackOfficeRoute("utpro/file-manager")]
[MapToApi(ConfigureFileManagerSwaggerGenOptions.ApiName)]
[ApiExplorerSettings(GroupName = "File Manager")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessSettings)]
public class FileManagerApiController(
    IFileManagerService fileManager,
    IMediaScanService mediaScan,
    IBackOfficeSecurityAccessor backOfficeSecurityAccessor,
    IWebHostEnvironment env,
    IOptions<FileManagerOptions> fileManagerOptions,
    IOptions<Umbraco.Cms.Core.Configuration.Models.ContentSettings> contentSettings) : ManagementApiControllerBase
{
    // ── Permission helpers ───────────────────────────────

    private bool HasGroup(string alias) =>
        backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser?.Groups
            .Any(g => g.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)) == true;

    private bool IsAdmin() => HasGroup(Constants.Security.AdminGroupAlias);
    private bool HasSensitiveData() => HasGroup("sensitiveData");

    /// <summary>
    /// Whether the current user may act on media (recycle/restore/delete/empty). Admins always can;
    /// otherwise the user must have access to the Media section — Umbraco's proxy for "can edit/remove media".
    /// </summary>
    private bool HasMediaAccess()
    {
        if (IsAdmin()) return true;
        var user = backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser;
        return user?.AllowedSections.Contains(Constants.Applications.Media) == true;
    }

    /// <summary>
    /// Sanitises a client-supplied path (strips traversal). The base directory it resolves
    /// against is chosen by <see cref="WebRootScope"/>: admins get the content root (full server),
    /// non-admins get the configured web root. Paths are therefore relative to that base for both.
    /// </summary>
    private static string ResolvePath(string? requestPath)
        => (requestPath ?? "").Replace('\\', '/').Trim('/').Replace("..", "");

    /// <summary>Non-admins are confined to the site web root; admins to the content root.</summary>
    private bool WebRootScope => !IsAdmin();

    // ── Locations (multi-root) ───────────────────────────

    /// <summary>Configured "Locations" the current user is allowed to see (empty = default single-root mode).</summary>
    private IEnumerable<FileManagerRootOption> AllowedRoots =>
        (fileManagerOptions.Value.Roots ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Key) && !string.IsNullOrWhiteSpace(r.Path))
            .Where(r => IsAdmin() || !r.AdminOnly);

    /// <summary>
    /// Returns the configured Locations for the current user. When none are configured (or none are
    /// visible), returns an empty array and the UI keeps its default single-root behaviour.
    /// </summary>
    [HttpGet("roots")]
    public IActionResult GetRoots() => Ok(AllowedRoots.Select(r => new
    {
        key = r.Key,
        label = string.IsNullOrWhiteSpace(r.Label) ? r.Key : r.Label,
        icon = string.IsNullOrWhiteSpace(r.Icon) ? "icon-folder" : r.Icon,
        adminOnly = r.AdminOnly
    }));

    /// <summary>
    /// Resolves a client-supplied Locations root key to an absolute base directory, enforcing the
    /// per-root permission. Returns null when the key is null/empty (default single-root mode).
    /// Throws <see cref="UnauthorizedAccessException"/> when the key is unknown or not permitted.
    /// </summary>
    private string? ResolveRequestedRoot(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var root = AllowedRoots.FirstOrDefault(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? throw new UnauthorizedAccessException("Unknown or inaccessible location.");

        // Absolute paths are used as-is; relative paths resolve against the content root.
        return Path.IsPathRooted(root.Path)
            ? Path.GetFullPath(root.Path)
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, root.Path));
    }

    // ── Permissions endpoint ─────────────────────────────

    [HttpGet("permissions")]
    public IActionResult GetPermissions() => Ok(new
    {
        isAdmin = IsAdmin(),
        hasSensitiveData = HasSensitiveData(),
        hasMediaAccess = HasMediaAccess()
    });

    // ── Browse (Settings access = tree only) ─────────────

    [HttpPost("browse")]
    public IActionResult Browse([FromBody] BrowseRequest request)
    {
        try
        {
            var resolved = ResolvePath(request.Path);
            var baseRoot = ResolveRequestedRoot(request.Root);
            var result = fileManager.Browse(resolved, request.Page, request.PageSize, request.Search, WebRootScope, baseRoot);
            return Ok(result);
        }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── File view (Admin or SensitiveData) ───────────────

    [HttpPost("file-content")]
    public IActionResult GetFileContent([FromBody] FileContentRequest request)
    {
        if (!IsAdmin() && !HasSensitiveData())
            return Unauthorized(new { error = "Sensitive Data access required to view file content." });
        try { return Ok(fileManager.GetFileContent(ResolvePath(request.Path), WebRootScope, ResolveRequestedRoot(request.Root))); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("download")]
    public IActionResult Download([FromQuery] string path, [FromQuery] bool inline = false, [FromQuery] string? root = null)
    {
        if (!IsAdmin() && !HasSensitiveData())
            return Unauthorized(new { error = "Sensitive Data access required to download files." });
        try
        {
            var resolved = ResolvePath(path);
            fileManager.ValidateNotBlocked(resolved);
            var fullPath = fileManager.GetFullPath(resolved, WebRootScope, ResolveRequestedRoot(root));
            if (!System.IO.File.Exists(fullPath))
                return NotFound(new { error = "File not found." });
            var bytes = System.IO.File.ReadAllBytes(fullPath);
            var name = Path.GetFileName(fullPath);
            var contentType = GetContentType(name);
            // SVG can carry inline script; never serve it inline in the backoffice origin — force download.
            if (IsSvg(contentType))
                return File(bytes, contentType, name);
            return inline ? File(bytes, contentType) : File(bytes, contentType, name);
        }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── Save file (Admin or SensitiveData) ───────────────

    [HttpPost("save-file")]
    public IActionResult SaveFile([FromBody] SaveFileRequest request)
    {
        if (!IsAdmin() && !HasSensitiveData())
            return Unauthorized(new { error = "Sensitive Data access required to edit files." });
        try { fileManager.SaveFileContent(ResolvePath(request.Path), request.Content, WebRootScope, ResolveRequestedRoot(request.Root)); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── Media Cleanup scan (Admin only) ──────────────────

    [HttpPost("scan-media")]
    public async Task<IActionResult> ScanMedia([FromQuery] bool force = false)
    {
        if (!IsAdmin() && !HasMediaAccess())
            return Unauthorized(new { error = "Media access required to scan media." });
        try { return Ok(await mediaScan.ScanAsync(force)); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    // Streams a media file (media-backed or orphaned) from the media file system for preview.
    [HttpGet("media-file")]
    public IActionResult GetMediaFile([FromQuery] string path, [FromQuery] bool inline = true)
    {
        if (!IsAdmin() && !HasSensitiveData())
            return Unauthorized(new { error = "Sensitive Data access required to view media files." });
        try
        {
            var content = mediaScan.ReadMediaFile(path);
            if (content is null)
                return NotFound(new { error = "Media file not found." });

            var contentType = GetContentType(content.FileName);
            // SVG can carry inline script; never serve it inline in the backoffice origin — force download.
            if (IsSvg(contentType))
                return File(content.Bytes, contentType, content.FileName);
            return inline
                ? File(content.Bytes, contentType)
                : File(content.Bytes, contentType, content.FileName);
        }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    // Super-user id fallback (-1). These endpoints are Admin-only, so CurrentUser is
    // effectively always present; the fallback just keeps the call non-null-dependent.
    private const int SuperUserIdFallback = -1;

    private int CurrentUserId() =>
        backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser?.Id ?? SuperUserIdFallback;

    /// <summary>Moves a scanned media item to the recycle bin (safe, recoverable).</summary>
    [HttpPost("recycle-media")]
    public IActionResult RecycleMedia([FromBody] MediaActionRequest request)
    {
        if (!HasMediaAccess()) return Unauthorized(new { error = "Media access required." });
        if (!Guid.TryParse(request.MediaKey, out var key))
            return BadRequest(new { error = "A valid media key is required." });

        var result = mediaScan.RecycleMedia(key, CurrentUserId());
        return result.Success
            ? Ok(new { success = true, message = result.Message })
            : BadRequest(new { error = result.Message });
    }

    /// <summary>Restores a trashed media item from the recycle bin to the media root.</summary>
    [HttpPost("restore-media")]
    public IActionResult RestoreMedia([FromBody] MediaActionRequest request)
    {
        if (!HasMediaAccess()) return Unauthorized(new { error = "Media access required." });
        if (!Guid.TryParse(request.MediaKey, out var key))
            return BadRequest(new { error = "A valid media key is required." });

        var result = mediaScan.RestoreMedia(key, CurrentUserId());
        return result.Success
            ? Ok(new { success = true, message = result.Message })
            : BadRequest(new { error = result.Message });
    }

    /// <summary>Permanently deletes a media item (used for recycle bin rows).</summary>
    [HttpPost("delete-media")]
    public IActionResult DeleteMedia([FromBody] MediaActionRequest request)
    {
        if (!HasMediaAccess()) return Unauthorized(new { error = "Media access required." });
        if (!Guid.TryParse(request.MediaKey, out var key))
            return BadRequest(new { error = "A valid media key is required." });

        var result = mediaScan.DeleteMedia(key, CurrentUserId());
        return result.Success
            ? Ok(new { success = true, message = result.Message })
            : BadRequest(new { error = result.Message });
    }

    /// <summary>Permanently deletes every media item in the recycle bin.</summary>
    [HttpPost("empty-recycle-bin")]
    public IActionResult EmptyRecycleBin()
    {
        if (!HasMediaAccess()) return Unauthorized(new { error = "Media access required." });
        var result = mediaScan.EmptyRecycleBin(CurrentUserId());
        return result.Success
            ? Ok(new { success = true, message = result.Message })
            : BadRequest(new { error = result.Message });
    }

    /// <summary>Deletes an orphaned file from the media file system.</summary>
    [HttpPost("delete-orphan")]
    public IActionResult DeleteOrphan([FromBody] MediaActionRequest request)
    {
        if (!HasMediaAccess()) return Unauthorized(new { error = "Media access required." });
        if (string.IsNullOrWhiteSpace(request.Path))
            return BadRequest(new { error = "A file path is required." });

        var result = mediaScan.DeleteOrphanedFile(request.Path);
        return result.Success
            ? Ok(new { success = true, message = result.Message })
            : BadRequest(new { error = result.Message });
    }

    // ── Write operations (Admin only) ────────────────────

    [HttpPost("create-folder")]
    public IActionResult CreateFolder([FromBody] CreateFolderRequest request)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try { fileManager.CreateFolder(request.Path, request.Name, ResolveRequestedRoot(request.Root)); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("create-file")]
    public IActionResult CreateFile([FromBody] CreateFolderRequest request)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try { fileManager.CreateFile(request.Path, request.Name, ResolveRequestedRoot(request.Root)); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("import-url")]
    public async Task<IActionResult> ImportFromUrl([FromBody] ImportUrlRequest request)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try { await fileManager.ImportFromUrl(request.Path, request.Url, ResolveRequestedRoot(request.Root)); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("rename")]
    public IActionResult Rename([FromBody] RenameRequest request)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try { fileManager.Rename(request.Path, request.NewName, ResolveRequestedRoot(request.Root)); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("delete")]
    public IActionResult Delete([FromBody] DeleteRequest request)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try { fileManager.Delete(request.Path, ResolveRequestedRoot(request.Root)); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload([FromForm] UploadRequest request)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try
        {
            var file = request.File;
            if (file is null || file.Length == 0)
                return BadRequest(new { error = "No file was uploaded." });

            var safePath = (request.Path ?? "").Replace('\\', '/').Trim('/').Replace("..", "");
            var fullDir = fileManager.GetFullPath(safePath, baseRootOverride: ResolveRequestedRoot(request.Root));
            if (!Directory.Exists(fullDir))
                return BadRequest(new { error = "Directory not found." });
            var safeFileName = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(safeFileName))
                return BadRequest(new { error = "Invalid file name." });

            var options = fileManagerOptions.Value;
            if (file.Length > options.MaxUploadSizeBytes)
                return BadRequest(new { error = $"File exceeds the maximum upload size of {options.MaxUploadSizeMB} MB." });
            if (!options.IsExtensionAllowed(safeFileName, contentSettings.Value.DisallowedUploadedFileExtensions, contentSettings.Value.AllowedUploadedFileExtensions))
            {
                var ext = Path.GetExtension(safeFileName).ToLowerInvariant();
                return BadRequest(new { error = $"File type '{ext}' is not allowed." });
            }

            var filePath = Path.Combine(fullDir, safeFileName);
            if (System.IO.File.Exists(filePath))
                return BadRequest(new { error = $"File already exists: {safeFileName}" });
            await using var stream = new FileStream(filePath, FileMode.CreateNew);
            await file.CopyToAsync(stream);
            return Ok(new { success = true, name = safeFileName });
        }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("extract-zip")]
    public IActionResult ExtractZip([FromBody] FileContentRequest request)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try { fileManager.ExtractZip(request.Path, ResolveRequestedRoot(request.Root)); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── Helpers ──────────────────────────────────────────

    private static bool IsSvg(string contentType) =>
        contentType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase);

    private static string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLower();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".ogg" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            _ => "application/octet-stream"
        };
    }
}
