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

    /// <summary>Media Cleanup large-file threshold in bytes.</summary>
    public long MediaLargeFileThresholdBytes => (long)MediaLargeFileThresholdMB * 1024 * 1024;

    /// <summary>Allowed file extensions. Empty = allow all (subject to the block list).</summary>
    public string[] AllowedUploadExtensions { get; set; } = [];

    /// <summary>Blocked file extensions. Always rejected regardless of the allow list.</summary>
    public string[] BlockedUploadExtensions { get; set; } = [];

    /// <summary>Maximum allowed upload size in bytes.</summary>
    public long MaxUploadSizeBytes => (long)MaxUploadSizeMB * 1024 * 1024;

    /// <summary>
    /// Determines whether the given file name's extension is allowed to be uploaded.
    /// Comparisons are case-insensitive and tolerant of configured entries with or
    /// without a leading dot.
    /// </summary>
    public bool IsExtensionAllowed(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        if (BlockedUploadExtensions is { Length: > 0 }
            && BlockedUploadExtensions.Any(e => NormalizeExtension(e) == ext))
            return false;

        if (AllowedUploadExtensions is { Length: > 0 }
            && !AllowedUploadExtensions.Any(e => NormalizeExtension(e) == ext))
            return false;

        return true;
    }

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
