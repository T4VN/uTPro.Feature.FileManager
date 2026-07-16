namespace uTPro.Feature.FileManager.Models;

/// <summary>
/// Configurable upload limits for the File Manager feature.
/// Bound from the <c>uTPro:Feature:FileManager</c> configuration section.
/// </summary>
public class FileManagerOptions
{
    public const string SectionPath = "uTPro:Feature:FileManager";

    /// <summary>Maximum allowed upload size in megabytes.</summary>
    public int MaxUploadSizeMB { get; set; } = 50;

    /// <summary>
    /// Media Cleanup: files at or above this size (in megabytes) are reported as "large files".
    /// Defaults to 100 MB.
    /// </summary>
    public int MediaLargeFileThresholdMB { get; set; } = 100;

    /// <summary>
    /// Media Cleanup: how long (in seconds) a scan result is cached so repeated tab switches
    /// don't re-scan the whole library. A forced reload (or any cleanup action) bypasses/clears
    /// the cache. Set to 0 to disable caching. Defaults to 30 seconds.
    /// </summary>
    public int MediaScanCacheSeconds { get; set; } = 30;

    /// <summary>
    /// Media Cleanup: media item keys (Guids, as strings) to exclude from the Unused and Large
    /// categories — useful to silence known false positives (e.g. media referenced only via
    /// hardcoded URLs). Invalid entries are ignored.
    /// </summary>
    public string[] IgnoredMediaIds { get; set; } = [];

    /// <summary>
    /// Media Cleanup: maximum number of files to walk during the file-system scan (orphaned /
    /// disallowed detection) before stopping early. Protects very large libraries. Default 50,000.
    /// Set to 0 for no limit.
    /// </summary>
    public int MediaScanMaxFiles { get; set; } = 50000;

    /// <summary>
    /// Media Cleanup: time budget (in seconds) for the expensive scan phases (file-system walk and
    /// duplicate hashing) before stopping early. Default 30 seconds. Set to 0 for no limit.
    /// </summary>
    public int MediaScanTimeBudgetSeconds { get; set; } = 30;

    /// <summary>Media Cleanup large-file threshold in bytes.</summary>
    public long MediaLargeFileThresholdBytes => (long)MediaLargeFileThresholdMB * 1024 * 1024;

    /// <summary>
    /// Allowed file extensions for File Manager uploads/imports. Empty = allow all (subject to the block
    /// list). When set, it also unions with Umbraco's <c>Content:AllowedUploadedFileExtensions</c> so both
    /// whitelists widen what's permitted. Leaving this empty avoids inheriting Umbraco's media-only
    /// whitelist, so File Manager can still upload site files (css/js/cshtml…).
    /// </summary>
    public string[] AllowedUploadExtensions { get; set; } = [];

    /// <summary>
    /// Additional blocked file extensions for File Manager uploads/imports. Always rejected regardless
    /// of the allow list. This list is combined (union) with Umbraco's
    /// <c>Content:DisallowedUploadedFileExtensions</c>, so you can keep the shared web-dangerous list in
    /// Umbraco and use this only for File-Manager-specific extras (e.g. binaries like .exe/.dll).
    /// </summary>
    public string[] BlockedUploadExtensions { get; set; } = [];

    /// <summary>Maximum allowed upload size in bytes.</summary>
    public long MaxUploadSizeBytes => (long)MaxUploadSizeMB * 1024 * 1024;

    /// <summary>
    /// Determines whether the given file name's extension is allowed to be uploaded.
    /// Comparisons are case-insensitive and tolerant of configured entries with or
    /// without a leading dot.
    /// </summary>
    public bool IsExtensionAllowed(
        string fileName,
        IEnumerable<string>? extraBlockedExtensions = null,
        IEnumerable<string>? extraAllowedExtensions = null)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        // Block list = our BlockedUploadExtensions ∪ Umbraco's DisallowedUploadedFileExtensions
        // (passed in as extraBlockedExtensions). Either match rejects the upload.
        if (IsInList(ext, BlockedUploadExtensions) || IsInList(ext, extraBlockedExtensions))
            return false;

        // Allow list (whitelist) applies ONLY when OUR AllowedUploadExtensions is configured — this
        // keeps File Manager able to upload non-media files (css/js/cshtml…) by default. When it IS
        // configured, we also honour Umbraco's AllowedUploadedFileExtensions (union), so both lists
        // widen what's permitted.
        if (AllowedUploadExtensions is { Length: > 0 }
            && !IsInList(ext, AllowedUploadExtensions)
            && !IsInList(ext, extraAllowedExtensions))
            return false;

        return true;
    }

    private static bool IsInList(string ext, IEnumerable<string>? list)
        => list is not null && list.Any(e => NormalizeExtension(e) == ext);

    /// <summary>
    /// Normalizes a configured extension to the lower-cased, leading-dot form used for comparison.
    /// </summary>
    private static string NormalizeExtension(string extension)
    {
        var trimmed = (extension ?? "").Trim().ToLowerInvariant();
        if (trimmed.Length == 0)
            return "";
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }
}
