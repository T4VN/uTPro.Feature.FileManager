using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
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
///   Settings       → browse wwwroot tree only (no file actions)
///   + SensitiveData → browse + view/edit/download files in wwwroot
///   Write ops (create/rename/delete/upload/extract) → Admin only
/// </summary>
[VersionedApiBackOfficeRoute("utpro/file-manager")]
[ApiExplorerSettings(GroupName = "uTPro File Manager")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessSettings)]
public class FileManagerApiController(
    IFileManagerService fileManager,
    IMediaScanService mediaScan,
    IBackOfficeSecurityAccessor backOfficeSecurityAccessor,
    IOptions<FileManagerOptions> fileManagerOptions) : ManagementApiControllerBase
{
    private const string NonAdminRoot = "wwwroot";

    // ── Permission helpers ───────────────────────────────

    private bool HasGroup(string alias) =>
        backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser?.Groups
            .Any(g => g.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)) == true;

    private bool IsAdmin() => HasGroup(Constants.Security.AdminGroupAlias);
    private bool HasSensitiveData() => HasGroup("sensitiveData");

    /// <summary>
    /// Admin: path as-is (full server root).
    /// Non-admin: jailed to wwwroot.
    /// </summary>
    private string ResolvePath(string? requestPath)
    {
        var safe = (requestPath ?? "").Replace('\\', '/').Trim('/').Replace("..", "");
        if (IsAdmin()) return safe;
        if (safe.Length == 0) return NonAdminRoot;
        if (safe.StartsWith(NonAdminRoot, StringComparison.OrdinalIgnoreCase)) return safe;
        return $"{NonAdminRoot}/{safe}";
    }

    private void StripRootPrefix(BrowseResult result)
    {
        if (IsAdmin()) return;
        result.CurrentPath = StripPrefix(result.CurrentPath);
        result.ParentPath = StripPrefix(result.ParentPath);
        result.Items = result.Items.Select(i => { i.Path = StripPrefix(i.Path); return i; });
    }

    private static string StripPrefix(string path)
    {
        if (path.Equals(NonAdminRoot, StringComparison.OrdinalIgnoreCase)) return "";
        if (path.StartsWith(NonAdminRoot + "/", StringComparison.OrdinalIgnoreCase))
            return path[(NonAdminRoot.Length + 1)..];
        return path;
    }

    // ── Permissions endpoint ─────────────────────────────

    [HttpGet("permissions")]
    public IActionResult GetPermissions() => Ok(new
    {
        isAdmin = IsAdmin(),
        hasSensitiveData = HasSensitiveData()
    });

    // ── Browse (Settings access = tree only) ─────────────

    [HttpPost("browse")]
    public IActionResult Browse([FromBody] BrowseRequest request)
    {
        try
        {
            var resolved = ResolvePath(request.Path);
            var result = fileManager.Browse(resolved, request.Page, request.PageSize, request.Search);
            StripRootPrefix(result);
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
        try { return Ok(fileManager.GetFileContent(ResolvePath(request.Path))); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("download")]
    public IActionResult Download([FromQuery] string path, [FromQuery] bool inline = false)
    {
        if (!IsAdmin() && !HasSensitiveData())
            return Unauthorized(new { error = "Sensitive Data access required to download files." });
        try
        {
            var fullPath = fileManager.GetFullPath(ResolvePath(path));
            if (!System.IO.File.Exists(fullPath))
                return NotFound(new { error = "File not found." });
            var bytes = System.IO.File.ReadAllBytes(fullPath);
            var name = Path.GetFileName(fullPath);
            var contentType = GetContentType(name);
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
        try { fileManager.SaveFileContent(ResolvePath(request.Path), request.Content); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── Media Cleanup scan (Admin only) ──────────────────

    [HttpPost("scan-media")]
    public async Task<IActionResult> ScanMedia()
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try { return Ok(await mediaScan.ScanAsync()); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── Write operations (Admin only) ────────────────────

    [HttpPost("create-folder")]
    public IActionResult CreateFolder([FromBody] CreateFolderRequest request)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try { fileManager.CreateFolder(request.Path, request.Name); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("create-file")]
    public IActionResult CreateFile([FromBody] CreateFolderRequest request)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try { fileManager.CreateFile(request.Path, request.Name); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("import-url")]
    public async Task<IActionResult> ImportFromUrl([FromBody] ImportUrlRequest request)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try { await fileManager.ImportFromUrl(request.Path, request.Url); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("rename")]
    public IActionResult Rename([FromBody] RenameRequest request)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try { fileManager.Rename(request.Path, request.NewName); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("delete")]
    public IActionResult Delete([FromBody] DeleteRequest request)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try { fileManager.Delete(request.Path); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload([FromForm] string path, [FromForm] IFormFile file)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try
        {
            var safePath = (path ?? "").Replace('\\', '/').Trim('/').Replace("..", "");
            var fullDir = fileManager.GetFullPath(safePath);
            if (!Directory.Exists(fullDir))
                return BadRequest(new { error = "Directory not found." });
            var safeFileName = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(safeFileName))
                return BadRequest(new { error = "Invalid file name." });

            var options = fileManagerOptions.Value;
            if (file.Length > options.MaxUploadSizeBytes)
                return BadRequest(new { error = $"File exceeds the maximum upload size of {options.MaxUploadSizeMB} MB." });
            if (!options.IsExtensionAllowed(safeFileName))
            {
                var ext = Path.GetExtension(safeFileName).ToLowerInvariant();
                return BadRequest(new { error = $"File type '{ext}' is not allowed." });
            }

            var filePath = Path.Combine(fullDir, safeFileName);
            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);
            return Ok(new { success = true, name = safeFileName });
        }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("extract-zip")]
    public IActionResult ExtractZip([FromBody] FileContentRequest request)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try { fileManager.ExtractZip(request.Path); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── Helpers ──────────────────────────────────────────

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
