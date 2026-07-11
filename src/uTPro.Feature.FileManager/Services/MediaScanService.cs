using System.Security.Cryptography;
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
    Microsoft.Extensions.Options.IOptions<FileManagerOptions> options,
    ILogger<MediaScanService> logger) : IMediaScanService
{
    private const int MediaPageSize = 500;

    public async Task<MediaScanResult> ScanAsync()
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
        foreach (var group in hashGroups.Values.Where(v => v.Count > 1))
        {
            foreach (var it in group)
            {
                var d = Clone(it, "duplicate");
                d.Detail = $"Duplicate ({group.Count} copies)";
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

        return new MediaScanResult
        {
            Unused = unused,
            Broken = broken,
            Duplicate = duplicate,
            Orphaned = orphaned,
            Large = large,
            LargeThresholdMB = largeThresholdMB,
            Counts = new MediaScanCounts
            {
                Unused = unused.Count,
                Broken = broken.Count,
                Duplicate = duplicate.Count,
                Orphaned = orphaned.Count,
                Large = large.Count
            }
        };
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
        Category = category
    };
}
