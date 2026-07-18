using System.Security.Cryptography;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using uTPro.Feature.FileManager.Models;

namespace uTPro.Feature.FileManager.Services;

/// <summary>
/// Media enumeration and file-system / hashing helpers for <see cref="MediaScanService"/>.
/// All file access goes through the media <see cref="IFileSystem"/> so it works with any storage provider.
/// </summary>
internal partial class MediaScanService
{
    /// <summary>
    /// Resolves a media item by its Guid key. Uses <see cref="IIdKeyMap"/> to map the key to
    /// an integer id, then <c>GetById(int)</c> — this is stable across Umbraco 16/17/18, whereas
    /// <c>IMediaService.GetById(Guid)</c> was removed from the interface in Umbraco 18.
    /// </summary>
    private Umbraco.Cms.Core.Models.IMedia? GetMediaByKey(Guid mediaKey)
    {
        var idAttempt = idKeyMap.GetIdForKey(mediaKey, UmbracoObjectTypes.Media);
        return idAttempt.Success ? mediaService.GetById(idAttempt.Result) : null;
    }

    // ── Media enumeration ────────────────────────────────

    private IEnumerable<Umbraco.Cms.Core.Models.IMedia> GetAllMedia()
    {
        long total;
        var page = 0L;
        do
        {
            var batch = mediaService
                .GetPagedDescendants(Constants.System.Root, page, MediaPageSize, out total)
                .ToList();

            foreach (var media in batch)
                yield return media;

            page++;
        }
        while (page * MediaPageSize < total);
    }

    private IEnumerable<Umbraco.Cms.Core.Models.IMedia> GetMediaInRecycleBin()
    {
        long total;
        var page = 0L;
        do
        {
            var batch = mediaService
                .GetPagedMediaInRecycleBin(page, MediaPageSize, out total)
                .ToList();

            foreach (var media in batch)
                yield return media;

            page++;
        }
        while (page * MediaPageSize < total);
    }

    /// <summary>
    /// Resolves the backing file path of a media item by asking the registered media URL
    /// generators to interpret each property value (handles Upload, ImageCropper, etc.).
    /// </summary>
    private string? GetMediaFilePath(Umbraco.Cms.Core.Models.IMedia media)
    {
        foreach (var prop in media.Properties)
        {
            object? value;
            try { value = prop.GetValue(); }
            catch { continue; }
            if (value is null) continue;

            if (mediaUrlGenerators.TryGetMediaPath(prop.PropertyType.PropertyEditorAlias, value, out var mediaPath)
                && !string.IsNullOrWhiteSpace(mediaPath))
                return mediaPath;
        }
        return null;
    }

    // ── File system helpers ──────────────────────────────

    private static IEnumerable<string> EnumerateAllFiles(IFileSystem fs)
    {
        var stack = new Stack<string>();
        stack.Push("");

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            string[] files;
            try { files = fs.GetFiles(dir).ToArray(); }
            catch { files = []; }
            foreach (var f in files)
                yield return NormalizeRelative(fs, f);

            string[] dirs;
            try { dirs = fs.GetDirectories(dir).ToArray(); }
            catch { dirs = []; }
            foreach (var d in dirs)
                stack.Push(d);
        }
    }

    private static string NormalizeRelative(IFileSystem fs, string pathOrUrl)
    {
        string rel;
        try { rel = fs.GetRelativePath(pathOrUrl); }
        catch { rel = pathOrUrl; }
        return rel.Replace('\\', '/').TrimStart('/');
    }

    private static bool SafeFileExists(IFileSystem fs, string rel)
    {
        try { return fs.FileExists(rel); }
        catch { return false; }
    }

    /// <summary>
    /// Fallback existence check for media that is served as a static file from a custom URL
    /// path (e.g. <c>UmbracoMediaPath = ~/uploads</c> served straight from the web root) which
    /// the Umbraco media file system may not be rooted at. Resolves the referenced URL/relative
    /// path under the web root and content root and returns true when a real file is found there,
    /// so such media is not incorrectly reported as "broken". Path traversal is guarded.
    /// </summary>
    private bool FileServedFromDisk(string? urlOrRelative)
    {
        if (string.IsNullOrWhiteSpace(urlOrRelative))
            return false;

        var relative = urlOrRelative.Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0)
            return false;

        foreach (var baseDir in new[] { env.WebRootPath, env.ContentRootPath })
        {
            if (string.IsNullOrEmpty(baseDir))
                continue;

            try
            {
                var root = Path.GetFullPath(baseDir);
                var full = Path.GetFullPath(Path.Combine(root, relative));

                // Stay within the base directory (defence in depth against traversal).
                if ((full.Equals(root, StringComparison.OrdinalIgnoreCase)
                        || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    && System.IO.File.Exists(full))
                    return true;
            }
            catch { /* ignore malformed paths */ }
        }

        return false;
    }

    private static long SafeGetSize(IFileSystem fs, string rel)
    {
        try { return fs.GetSize(rel); }
        catch { return 0; }
    }

    private static DateTime SafeGetLastModified(IFileSystem fs, string rel)
    {
        try { return fs.GetLastModified(rel).UtcDateTime; }
        catch { return DateTime.MinValue; }
    }

    private static string? TryComputeHash(IFileSystem fs, string rel)
    {
        try
        {
            using var stream = fs.OpenFile(rel);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
        catch
        {
            return null;
        }
    }

    private static MediaScanItem Clone(MediaScanItem source, string category) => new()
    {
        Name = source.Name,
        Path = source.Path,
        Type = source.Type,
        Size = source.Size,
        LastModified = source.LastModified,
        Extension = source.Extension,
        IsEditable = source.IsEditable,
        MediaKey = source.MediaKey,
        Category = category,
        Group = source.Group
    };
}
