using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
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
///
/// This class is split across several files (all <c>partial</c>):
///   • MediaScanService.cs            — orchestration (ScanAsync / ScanCoreAsync) and configuration.
///   • MediaScanService.Detection.cs  — reference/unused/duplicate detection helpers.
///   • MediaScanService.Actions.cs    — recycle/restore/delete/empty/read actions.
///   • MediaScanService.FileSystem.cs — media enumeration and file-system/hash helpers.
/// </summary>
internal partial class MediaScanService(
    IMediaService mediaService,
    MediaFileManager mediaFileManager,
    MediaUrlGeneratorCollection mediaUrlGenerators,
    ITrackedReferencesService trackedReferencesService,
    IIdKeyMap idKeyMap,
    IMemoryCache cache,
    IWebHostEnvironment env,
    Microsoft.Extensions.Options.IOptions<FileManagerOptions> options,
    Microsoft.Extensions.Options.IOptions<Umbraco.Cms.Core.Configuration.Models.ContentSettings> contentSettings,
    ILogger<MediaScanService> logger) : IMediaScanService
{
    private const int MediaPageSize = 500;
    private const string ScanCacheKey = "uTPro.FileManager.MediaScan";

    /// <summary>Max number of media keys per tracked-references batch query (bounds the SQL IN(...) list).</summary>
    private const int RelationQueryBatchSize = 500;

    /// <summary>Page size (rows) when paging through a single tracked-references batch query.</summary>
    private const long RelationQueryPageSize = 500;

    /// <summary>Media keys configured to be excluded from Unused/Large (false-positive suppression).</summary>
    private HashSet<Guid> IgnoredMediaKeys()
    {
        var set = new HashSet<Guid>();
        foreach (var id in options.Value.IgnoredMediaIds ?? [])
            if (Guid.TryParse(id, out var g)) set.Add(g);
        return set;
    }

    /// <summary>Disallowed upload extensions from Umbraco config, normalized to lower-case without a leading dot.</summary>
    private HashSet<string> DisallowedExtensions()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in contentSettings.Value.DisallowedUploadedFileExtensions ?? Enumerable.Empty<string>())
        {
            var norm = (e ?? "").Trim().TrimStart('.').ToLowerInvariant();
            if (norm.Length > 0) set.Add(norm);
        }
        return set;
    }

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
        var ignored = IgnoredMediaKeys();
        var disallowedExts = DisallowedExtensions();

        // Scan guardrails: stop the expensive phases early on very large libraries.
        var maxFiles = Math.Max(0, options.Value.MediaScanMaxFiles);
        var budgetMs = (long)Math.Max(0, options.Value.MediaScanTimeBudgetSeconds) * 1000;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var truncated = false;
        bool BudgetExceeded() => budgetMs > 0 && sw.ElapsedMilliseconds > budgetMs;

        var all = new List<MediaScanItem>();
        var broken = new List<MediaScanItem>();
        var unused = new List<MediaScanItem>();
        var duplicate = new List<MediaScanItem>();
        var disallowed = new List<MediaScanItem>();

        // Normalized (relative) file paths referenced by media items — used for orphan detection.
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Map of referenced file path -> its media-backed row (so disallowed files can carry a media key).
        var refByPath = new Dictionary<string, MediaScanItem>(StringComparer.OrdinalIgnoreCase);

        // Count of existing files per size — lets duplicate detection skip hashing files whose size is
        // unique (a unique size can never be a duplicate), avoiding the vast majority of file reads.
        var sizeCounts = new Dictionary<long, int>();

        // Media items eligible for the "unused" check (non-folder, not on the ignore list), kept in
        // enumeration order so the resulting Unused list matches the previous per-item behaviour exactly.
        var unusedCandidates = new List<(Guid Key, MediaScanItem Item)>();

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
                refByPath[rel] = item;

                if (SafeFileExists(fs, rel))
                {
                    item.Size = SafeGetSize(fs, rel);
                    sizeCounts[item.Size] = sizeCounts.TryGetValue(item.Size, out var c) ? c + 1 : 1;
                }
                else if (FileServedFromDisk(filePath) || FileServedFromDisk(rel))
                {
                    // Not in the Umbraco media file system, but the referenced file physically
                    // exists under the web/content root — e.g. media served as static files from a
                    // custom URL path (UmbracoMediaPath) that the media file system isn't rooted at.
                    // It resolves/serves fine, so it is NOT broken (avoids false positives).
                }
                else
                {
                    var b = Clone(item, "broken");
                    b.Detail = $"Missing file: {filePath}";
                    broken.Add(b);
                }
            }

            all.Add(item);

            // Unused: nothing depends on this media item (skip ignored keys). Resolved in bulk after the
            // loop (see GetReferencedMediaKeysAsync) instead of one query per item to avoid N+1 round-trips.
            if (ignored.Contains(media.Key))
                continue;
            unusedCandidates.Add((media.Key, item));
        }

        // One batched reference lookup for every candidate key (was: one query per media item).
        var referencedKeys = await GetReferencedMediaKeysAsync(unusedCandidates.Select(c => c.Key).ToList());
        foreach (var (key, item) in unusedCandidates)
        {
            if (!referencedKeys.Contains(key))
                unused.Add(Clone(item, "unused"));
        }

        // Duplicates: group existing media files by content hash. Files whose size is unique cannot be
        // duplicates (identical content ⇒ identical size), so they are skipped without being read/hashed.
        var hashGroups = new Dictionary<string, List<MediaScanItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in all)
        {
            if (BudgetExceeded()) { truncated = true; break; }
            if (string.IsNullOrEmpty(item.Path)) continue;
            var rel = NormalizeRelative(fs, item.Path);
            if (!SafeFileExists(fs, rel)) continue;

            // Skip hashing files with a unique size — they have no possible duplicate.
            if (sizeCounts.TryGetValue(item.Size, out var sc) && sc < 2) continue;

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

        // Single file-system walk → orphaned + disallowed-extension detection (with guardrails).
        var orphaned = new List<MediaScanItem>();
        var scannedFiles = 0;
        foreach (var rel in EnumerateAllFiles(fs))
        {
            if ((maxFiles > 0 && scannedFiles >= maxFiles) || BudgetExceeded()) { truncated = true; break; }
            scannedFiles++;

            var name = Path.GetFileName(rel);
            var ext = Path.GetExtension(name).ToLowerInvariant();

            // Disallowed extension: a physical file whose type Umbraco would reject on upload.
            if (disallowedExts.Count > 0 && disallowedExts.Contains(ext.TrimStart('.')))
            {
                if (refByPath.TryGetValue(rel, out var backed))
                {
                    var di = Clone(backed, "disallowed");
                    di.Detail = $"Disallowed extension: {ext}";
                    disallowed.Add(di);
                }
                else
                {
                    disallowed.Add(new MediaScanItem
                    {
                        Name = name,
                        Path = rel,
                        Type = "file",
                        Size = SafeGetSize(fs, rel),
                        LastModified = SafeGetLastModified(fs, rel),
                        Extension = ext,
                        Category = "disallowed",
                        Detail = $"Disallowed extension: {ext}"
                    });
                }
            }

            if (referenced.Contains(rel)) continue;

            orphaned.Add(new MediaScanItem
            {
                Name = name,
                Path = rel,
                Type = "file",
                Size = SafeGetSize(fs, rel),
                LastModified = SafeGetLastModified(fs, rel),
                Extension = ext,
                Category = "orphaned"
            });
        }

        // Large files: any scanned file (media-backed or orphaned) at/above the configured threshold,
        // largest first.
        var large = all
            .Where(i => i.Size >= largeThresholdBytes && !IsIgnored(i, ignored))
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
            Disallowed = disallowed,
            LargeThresholdMB = largeThresholdMB,
            Truncated = truncated,
            Counts = new MediaScanCounts
            {
                Unused = unused.Count,
                Broken = broken.Count,
                Duplicate = duplicate.Count,
                Orphaned = orphaned.Count,
                Large = large.Count,
                RecycleBin = recycleBin.Count,
                Disallowed = disallowed.Count
            }
        };
    }

    private static bool IsIgnored(MediaScanItem item, HashSet<Guid> ignored)
        => item.MediaKey is not null && Guid.TryParse(item.MediaKey, out var g) && ignored.Contains(g);
}
