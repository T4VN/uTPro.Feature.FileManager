using uTPro.Feature.FileManager.Models;

namespace uTPro.Feature.FileManager.Services;

public interface IFileManagerService
{
    BrowseResult Browse(string relativePath, int page = 1, int pageSize = 100, string search = "");
    FileContentResult GetFileContent(string relativePath);
    void SaveFileContent(string relativePath, string content);
    void CreateFolder(string relativePath, string folderName);
    void CreateFile(string relativePath, string fileName);
    Task ImportFromUrl(string relativePath, string url);
    void Rename(string relativePath, string newName);
    void Delete(string relativePath);
    void ExtractZip(string relativePath);
    string GetFullPath(string relativePath);

    /// <summary>
    /// Throws if the given path targets a protected/blocked secret file (web.config, appsettings*.json, .env…).
    /// Exposed so read/download endpoints can enforce the block list before serving bytes.
    /// </summary>
    void ValidateNotBlocked(string relativePath);
}
