using uTPro.Feature.FileManager.Models;

namespace uTPro.Feature.FileManager.Services;

public interface IFileManagerService
{
    // webRootScope: false = root at ContentRootPath (admins, full server); true = root at the
    // configured web root (IWebHostEnvironment.WebRootPath, e.g. uTPro:Hosting:RootPath) for
    // non-admins — works even when the web root lives outside the content root.
    //
    // baseRootOverride: when non-null, an absolute directory the operation is confined to instead
    // of the webRootScope-derived root. Used by the multi-root "Locations" feature — the controller
    // resolves + permission-checks the configured root and passes its absolute path here.
    BrowseResult Browse(string relativePath, int page = 1, int pageSize = 100, string search = "", bool webRootScope = false, string? baseRootOverride = null);
    FileContentResult GetFileContent(string relativePath, bool webRootScope = false, string? baseRootOverride = null);
    void SaveFileContent(string relativePath, string content, bool webRootScope = false, string? baseRootOverride = null);
    void CreateFolder(string relativePath, string folderName, string? baseRootOverride = null);
    void CreateFile(string relativePath, string fileName, string? baseRootOverride = null);
    Task ImportFromUrl(string relativePath, string url, string? baseRootOverride = null);
    void Rename(string relativePath, string newName, string? baseRootOverride = null);
    void Delete(string relativePath, string? baseRootOverride = null);
    void ExtractZip(string relativePath, string? baseRootOverride = null);
    string GetFullPath(string relativePath, bool webRootScope = false, string? baseRootOverride = null);

    /// <summary>
    /// Throws if the given path targets a protected/blocked secret file (web.config, appsettings*.json, .env…).
    /// Exposed so read/download endpoints can enforce the block list before serving bytes.
    /// </summary>
    void ValidateNotBlocked(string relativePath);
}
