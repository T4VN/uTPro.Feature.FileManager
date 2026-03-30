using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Authorization;
using uTPro.Feature.FileManager.Models;
using uTPro.Feature.FileManager.Services;

namespace uTPro.Feature.FileManager.Controllers;

[VersionedApiBackOfficeRoute("utpro/file-manager")]
[ApiExplorerSettings(GroupName = "uTPro File Manager")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessSettings)]
public class FileManagerApiController(
    IFileManagerService fileManager,
    IBackOfficeSecurityAccessor backOfficeSecurityAccessor) : ManagementApiControllerBase
{
    private static readonly HashSet<string> MediaExtensions =
    [
        ".jpg", ".jpeg", ".png", ".gif", ".svg", ".webp", ".bmp", ".ico",
        ".mp4", ".webm", ".ogg", ".mp3", ".wav", ".pdf"
    ];

    private const string NonAdminRoot = "wwwroot";

    // ── Permission helpers ───────────────────────────────

    private bool HasGroup(string alias) =>
        backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser?.Groups
            .Any(g => g.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)) == true;

    private bool IsAdmin() => HasGroup(Constants.Security.AdminGroupAlias);
    private bool HasSensitiveData() => HasGroup("sensitiveData");
    private bool HasMediaAccess() => HasGroup("media");

    /// <summary>
    /// Non-admin users are jailed to wwwroot.
    /// Their "" (root) maps to "wwwroot", their "css" maps to "wwwroot/css".
    /// Admin sees the real ContentRootPath.
    /// </summary>
    private string ResolvePath(string? requestPath)
    {
        var safe = (requestPath ?? "").Replace('\\', '/').Trim('/').Replace("..", "");
        if (IsAdmin()) return safe;

        // Non-admin: prefix wwwroot if not already
        if (safe.Length == 0) return NonAdminRoot;
        if (safe.StartsWith(NonAdminRoot, StringComparison.OrdinalIgnoreCase)) return safe;
        return $"{NonAdminRoot}/{safe}";
    }

    /// <summary>
    /// Strip wwwroot prefix from paths in results so non-admin sees clean paths.
    /// </summary>
    private void StripRootPrefix(BrowseResult result)
    {
        if (IsAdmin()) return;

        result.CurrentPath = StripPrefix(result.CurrentPath);
        result.ParentPath = StripPrefix(result.ParentPath);
        result.Items = result.Items.Select(i =>
        {
            i.Path = StripPrefix(i.Path);
            return i;
        });
    }

    private static string StripPrefix(string path)
    {
        if (path.Equals(NonAdminRoot, StringComparison.OrdinalIgnoreCase)) return "";
        if (path.StartsWith(NonAdminRoot + "/", StringComparison.OrdinalIgnoreCase))
            return path[(NonAdminRoot.Length + 1)..];
        return path;
    }

    private bool CanViewFile(string resolvedPath) =>
        IsAdmin() || HasSensitiveData() ||
        (HasMediaAccess() && resolvedPath.StartsWith(NonAdminRoot, StringComparison.OrdinalIgnoreCase) && IsMediaFile(resolvedPath));

    private static bool IsMediaFile(string path) =>
        MediaExtensions.Contains(Path.GetExtension(path).ToLower());

    // ── Permissions ──────────────────────────────────────

    [HttpGet("permissions")]
    public IActionResult GetPermissions() => Ok(new
    {
        isAdmin = IsAdmin(),
        hasSensitiveData = HasSensitiveData(),
        hasMediaAccess = HasMediaAccess(),
        rootPath = IsAdmin() ? "" : NonAdminRoot
    });

    // ── Read endpoints ───────────────────────────────────

    [HttpPost("browse")]
    public IActionResult Browse([FromBody] BrowseRequest request)
    {
        try
        {
            var resolved = ResolvePath(request.Path);
            var result = fileManager.Browse(resolved, request.Page, request.PageSize, request.Search);

            // Media-only users: filter to media files + folders
            if (!IsAdmin() && !HasSensitiveData() && HasMediaAccess())
                result.Items = result.Items.Where(i => i.Type == "folder" || IsMediaFile(i.Path));

            StripRootPrefix(result);
            return Ok(result);
        }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("file-content")]
    public IActionResult GetFileContent([FromBody] FileContentRequest request)
    {
        if (!IsAdmin() && !HasSensitiveData())
            return Unauthorized(new { error = "Sensitive data access required." });
        try
        {
            var resolved = ResolvePath(request.Path);
            return Ok(fileManager.GetFileContent(resolved));
        }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("download")]
    public IActionResult Download([FromQuery] string path, [FromQuery] bool inline = false)
    {
        var resolved = ResolvePath(path);
        if (!CanViewFile(resolved))
            return Unauthorized(new { error = "Access denied." });
        try
        {
            var fullPath = fileManager.GetFullPath(resolved);
            if (!System.IO.File.Exists(fullPath))
                return NotFound(new { error = "File not found." });

            var bytes = System.IO.File.ReadAllBytes(fullPath);
            var name = Path.GetFileName(fullPath);
            var contentType = GetContentType(name);

            if (inline)
                return File(bytes, contentType);

            return File(bytes, contentType, name);
        }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

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

    // ── Write endpoints (Admin only) ─────────────────────

    [HttpPost("save-file")]
    public IActionResult SaveFile([FromBody] SaveFileRequest request)
    {
        if (!IsAdmin()) return Unauthorized(new { error = "Admin access required." });
        try { fileManager.SaveFileContent(request.Path, request.Content); return Ok(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

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

            var filePath = Path.Combine(fullDir, file.FileName);
            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);
            return Ok(new { success = true, name = file.FileName });
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
}
