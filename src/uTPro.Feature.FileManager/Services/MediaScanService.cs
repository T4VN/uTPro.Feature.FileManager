using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using uTPro.Feature.FileManager.Models;

namespace uTPro.Feature.FileManager.Services;

/// <summary>
/// Media Cleanup scanner.
///
/// Detection strategy:
///   • Broken    — a media item references a file path, but the file does not exist in the media file system.
///   • Orphaned  — a file exists in the media file system that no media item references.
///   • Unused    — a media item that nothing depends on (via Umbraco tracked references). Best-effort:
///                 references made only inside free-form markup (rich text, templates, CSS/JS) may not be
///                 tracked, so treat "unused" as a suggestion — always recover from the recycle bin, never
///                 hard-delete blindly.
///   • Duplicate — media items whose backing files share the same SHA-256 content hash.
///
/// All file access goes through <see cref="MediaFileManager.FileSystem"/> so it works with any storage
/// provider (physical disk, Azure Blob, S3, …), not just wwwroot.
/// </summary>
internal class MediaScanService(
    IMediaService mediaService,
    MediaFileManager mediaFileManager,
    MediaUrlGeneratorCollection mediaUrlGenerators,
    ITrackedReferencesService trackedReferencesService,
    IIdKeyMap idKeyMap,
    IMemoryCache cache,
    Microsoft.Extensions.Options.IOptions<FileManagerOptions> options,
    ILogger<MediaScanService> logger) : IMediaScanService
{
    private const int MediaPageSize = 500;
    private const string ScanCacheKey = "uTPro.FileManager.MediaScan";

    /// <summary>Drops any cached scan result so the next scan re-reads the library.</summary>
    private void InvalidateScanCache() => cache.Remove(ScanCacheKey);

    public async Task<MediaScanResult> ScanAsync(bool force = false)
    {
        var ttl = options.Value.MediaScanCacheSeconds;
        if (!force && ttl > 0 && cache.TryGetValue(ScanCacheKey, out MediaScanResult? cached) && cached is not null)
            return cached;

        var result = await ScanCoreAsync();

        if (ttl > 0)
            cache.Set(ScanCacheKey, result, TimeSpan.FromSeconds(ttl));

        return result;
    }

    private async Task<MediaScanResult> ScanCoreAsync()
    {
        var fs = mediaFileManager.FileSystem;
        var largeThresholdMB = options.Value.MediaLargeFileThresholdMB;
        var largeThresholdBytes = options.Value.MediaLargeFileThresholdBytes;

        var all = new List<MediaScanItem>();
        var broken = new List<MediaScanItem>();
        var unused = new List<MediaScanItem>();
        var duplicate = new List<MediaScanItem>();

        // Normalized (relative) file paths referenced by media items — used for orphan detection.
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var media in GetAllMedia())
        {
            // Skip media folders (containers) — they have no backing file and are not actionable.
            if (string.Equals(media.ContentType.Alias, Constants.Conventions.MediaTypes.Folder, StringComparison.OrdinalIgnoreCase))
                continue;

            var filePath = GetMediaFilePath(media);
            var item = new MediaScanItem
            {
                Name = string.IsNullOrWhiteSpace(media.Name) ? "(unnamed)" : media.Name!,
                Path = filePath ?? "",
                Type = "file",
                LastModified = media.UpdateDate,
                Extension = filePath is not null ? Path.GetExtension(filePath).ToLowerInvariant() : "",
                MediaKey = media.Key.ToString(),
                Category = "all"
            };

            if (filePath is not null)
            {
                var rel = NormalizeRelative(fs, filePath);
                referenced.Add(rel);

                if (SafeFileExists(fs, rel))
                {
                    item.Size = SafeGetSize(fs, rel);
                }
                else
                {
                    var b = Clone(item, "broken");
                    b.Detail = $"Missing file: {filePath}";
                    broken.Add(b);
                }
            }

            all.Add(item);

            // Unused: nothing depends on this media item.
            try
            {
                var refs = await trackedReferencesService.GetPagedRelationsForItemAsync(media.Key, 0, 1, false);
                if (refs.Total == 0)
                    unused.Add(Clone(item, "unused"));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Media scan: could not resolve references for media {Key}", media.Key);
            }
        }

        // Duplicates: group existing media files by content hash.
        var hashGroups = new Dictionary<string, List<MediaScanItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in all)
        {
            if (string.IsNullOrEmpty(item.Path)) continue;
            var rel = NormalizeRelative(fs, item.Path);
            if (!SafeFileExists(fs, rel)) continue;

            var hash = TryComputeHash(fs, rel);
            if (hash is null) continue;

            if (!hashGroups.TryGetValue(hash, out var list))
            {
                list = [];
                hashGroups[hash] = list;
            }
            list.Add(item);
        }
        foreach (var kv in hashGroups.Where(v => v.Value.Count > 1))
        {
            foreach (var it in kv.Value)
            {
                var d = Clone(it, "duplicate");
                d.Detail = $"Duplicate ({kv.Value.Count} copies)";
                d.Group = kv.Key;
                duplicate.Add(d);
            }
        }

        // Orphaned: files present in the media file system that no media item references.
        var orphaned = new List<MediaScanItem>();
        foreach (var rel in EnumerateAllFiles(fs))
        {
            if (referenced.Contains(rel)) continue;

            var name = Path.GetFileName(rel);
            orphaned.Add(new MediaScanItem
            {
                Name = name,
                Path = rel,
                Type = "file",
                Size = SafeGetSize(fs, rel),
                LastModified = SafeGetLastModified(fs, rel),
                Extension = Path.GetExtension(name).ToLowerInvariant(),
                Category = "orphaned"
            });
        }

        // Large files: any scanned file (media-backed or orphaned) at/above the configured threshold,
        // largest first.
        var large = all
            .Where(i => i.Size >= largeThresholdBytes)
            .Concat(orphaned.Where(o => o.Size >= largeThresholdBytes))
            .OrderByDescending(i => i.Size)
            .Select(i =>
            {
                var l = Clone(i, "large");
                l.Detail = $"≥ {largeThresholdMB} MB";
                return l;
            })
            .ToList();

        // Recycle bin: media items currently trashed (separate tree from the live media above).
        var recycleBin = new List<MediaScanItem>();
        foreach (var media in GetMediaInRecycleBin())
        {
            if (string.Equals(media.ContentType.Alias, Constants.Conventions.MediaTypes.Folder, StringComparison.OrdinalIgnoreCase))
                continue;

            var filePath = GetMediaFilePath(media);
            var rel = filePath is not null ? NormalizeRelative(fs, filePath) : null;

            recycleBin.Add(new MediaScanItem
            {
                Name = string.IsNullOrWhiteSpace(media.Name) ? "(unnamed)" : media.Name!,
                Path = filePath ?? "",
                Type = "file",
                Size = rel is not null && SafeFileExists(fs, rel) ? SafeGetSize(fs, rel) : 0,
                LastModified = media.UpdateDate,
                Extension = filePath is not null ? Path.GetExtension(filePath).ToLowerInvariant() : "",
                MediaKey = media.Key.ToString(),
                Category = "recyclebin",
                Detail = "In recycle bin"
            });
        }

        return new MediaScanResult
        {
            Unused = unused,
            Broken = broken,
            Duplicate = duplicate,
            Orphaned = orphaned,
            Large = large,
            RecycleBin = recycleBin,
            LargeThresholdMB = largeThresholdMB,
            Counts = new MediaScanCounts
            {
                Unused = unused.Count,
                Broken = broken.Count,
                Duplicate = duplicate.Count,
                Orphaned = orphaned.Count,
                Large = large.Count,
                RecycleBin = recycleBin.Count
            }
        };
    }

    // ── Actions ──────────────────────────────────────────

    public MediaActionResult RecycleMedia(Guid mediaKey, int userId)
    {
        InvalidateScanCache();
        var media = GetMediaByKey(mediaKey);
        if (media is null)
            return MediaActionResult.Fail($"Media '{mediaKey}' was not found.");

        if (media.Trashed)
            return MediaActionResult.Ok($"'{media.Name}' is already in the recycle bin.");

        try
        {
            var attempt = mediaService.MoveToRecycleBin(media, userId);
            if (!attempt.Success)
                return MediaActionResult.Fail($"Could not move '{media.Name}' to the recycle bin.");

            return MediaActionResult.Ok($"'{media.Name}' moved to the recycle bin.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Media cleanup: failed to recycle media {Key}", mediaKey);
            return MediaActionResult.Fail(ex.Message);
        }
    }

    public MediaActionResult RestoreMedia(Guid mediaKey, int userId)
    {
        InvalidateScanCache();
        var media = GetMediaByKey(mediaKey);
        if (media is null)
            return MediaActionResult.Fail($"Media '{mediaKey}' was not found.");

        if (!media.Trashed)
            return MediaActionResult.Ok($"'{media.Name}' is not in the recycle bin.");

        try
        {
            // Restore to the media root (matches Umbraco's behaviour when the original parent is gone).
            var attempt = mediaService.Move(media, Constants.System.Root, userId);
            if (!attempt.Success)
                return MediaActionResult.Fail($"Could not restore '{media.Name}'.");

            return MediaActionResult.Ok($"'{media.Name}' was restored to the media root.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Media cleanup: failed to restore media {Key}", mediaKey);
            return MediaActionResult.Fail(ex.Message);
        }
    }

    public MediaActionResult DeleteMedia(Guid mediaKey, int userId)
    {
        InvalidateScanCache();
        var media = GetMediaByKey(mediaKey);
        if (media is null)
            return MediaActionResult.Fail($"Media '{mediaKey}' was not found.");

        try
        {
            var attempt = mediaService.Delete(media, userId);
            if (!attempt.Success)
                return MediaActionResult.Fail($"Could not delete '{media.Name}'.");

            return MediaActionResult.Ok($"'{media.Name}' was permanently deleted.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Media cleanup: failed to delete media {Key}", mediaKey);
            return MediaActionResult.Fail(ex.Message);
        }
    }

    public MediaActionResult EmptyRecycleBin(int userId)
    {
        InvalidateScanCache();
        try
        {
            var result = mediaService.EmptyRecycleBin(userId);
            return result.Success
                ? MediaActionResult.Ok("The media recycle bin was emptied.")
                : MediaActionResult.Fail("Could not empty the media recycle bin.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Media cleanup: failed to empty the media recycle bin");
            return MediaActionResult.Fail(ex.Message);
        }
    }

    public MediaActionResult DeleteOrphanedFile(string relativePath)
    {
        InvalidateScanCache();
        if (string.IsNullOrWhiteSpace(relativePath))
            return MediaActionResult.Fail("No file path was provided.");

        var fs = mediaFileManager.FileSystem;
        var rel = NormalizeRelative(fs, relativePath);

        if (!SafeFileExists(fs, rel))
            return MediaActionResult.Fail($"File '{rel}' was not found in the media file system.");

        // Guard: never delete a file that is actually referenced by a media item. This
        // protects against acting on a stale scan (files added/re-referenced since the scan).
        if (IsFileReferenced(fs, rel))
            return MediaActionResult.Fail($"'{rel}' is referenced by a media item and was not deleted. Re-scan and try again.");

        try
        {
            fs.DeleteFile(rel);
            return MediaActionResult.Ok($"Orphaned file '{Path.GetFileName(rel)}' was deleted.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Media cleanup: failed to delete orphaned file {Path}", rel);
            return MediaActionResult.Fail(ex.Message);
        }
    }

    public MediaFileContent? ReadMediaFile(string pathOrRelative)
    {
        if (string.IsNullOrWhiteSpace(pathOrRelative))
            return null;

        var fs = mediaFileManager.FileSystem;
        var rel = NormalizeRelative(fs, pathOrRelative);
        if (!SafeFileExists(fs, rel))
            return null;

        try
        {
            using var stream = fs.OpenFile(rel);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return new MediaFileContent
            {
                Bytes = ms.ToArray(),
                FileName = Path.GetFileName(rel)
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Media cleanup: failed to read media file {Path}", rel);
            return null;
        }
    }

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

    /// <summary>
    /// Returns true if any (non-folder) media item currently references the given
    /// media-file-system relative path.
    /// </summary>
    private bool IsFileReferenced(IFileSystem fs, string rel)
    {
        foreach (var media in GetAllMedia())
        {
            if (string.Equals(media.ContentType.Alias, Constants.Conventions.MediaTypes.Folder, StringComparison.OrdinalIgnoreCase))
                continue;

            var filePath = GetMediaFilePath(media);
            if (filePath is null) continue;

            if (string.Equals(NormalizeRelative(fs, filePath), rel, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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
