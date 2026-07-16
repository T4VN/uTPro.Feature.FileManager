namespace uTPro.Feature.FileManager.Models;

/// <summary>
/// The result of a Media Cleanup scan. Each category is returned as a separate list so
/// the front-end can switch filters client-side without re-querying the server.
/// </summary>
public class MediaScanResult
{
    /// <summary>Media items that no content/entity references (best-effort, see notes).</summary>
    public IEnumerable<MediaScanItem> Unused { get; set; } = [];

    /// <summary>Media items whose backing file is missing on disk/storage.</summary>
    public IEnumerable<MediaScanItem> Broken { get; set; } = [];

    /// <summary>Media items sharing identical file content (same hash).</summary>
    public IEnumerable<MediaScanItem> Duplicate { get; set; } = [];

    /// <summary>Files in the media file system that are not referenced by any media item.</summary>
    public IEnumerable<MediaScanItem> Orphaned { get; set; } = [];

    /// <summary>Files at or above the configured large-file threshold, largest first.</summary>
    public IEnumerable<MediaScanItem> Large { get; set; } = [];

    /// <summary>Media items currently in the Umbraco media recycle bin (trashed).</summary>
    public IEnumerable<MediaScanItem> RecycleBin { get; set; } = [];

    /// <summary>Files whose extension is in Umbraco's DisallowedUploadedFileExtensions (potential security risk).</summary>
    public IEnumerable<MediaScanItem> Disallowed { get; set; } = [];

    /// <summary>The configured large-file threshold (MB) that produced the <see cref="Large"/> list.</summary>
    public int LargeThresholdMB { get; set; }

    /// <summary>True if the scan stopped early due to the configured file-count or time-budget limits.</summary>
    public bool Truncated { get; set; }

    public MediaScanCounts Counts { get; set; } = new();
}

public class MediaScanCounts
{
    public int Unused { get; set; }
    public int Broken { get; set; }
    public int Duplicate { get; set; }
    public int Orphaned { get; set; }
    public int Large { get; set; }
    public int RecycleBin { get; set; }
    public int Disallowed { get; set; }
}
