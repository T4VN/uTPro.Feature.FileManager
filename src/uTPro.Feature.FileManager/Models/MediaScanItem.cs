namespace uTPro.Feature.FileManager.Models;

/// <summary>
/// A single row in a Media Cleanup scan result. Shaped to be compatible with the
/// File Manager file list (Name/Path/Size/LastModified/Extension) so the existing
/// table renderer can display it, with a few extra fields specific to media.
/// </summary>
public class MediaScanItem
{
    public string Name { get; set; } = "";

    /// <summary>Media file path (media items) or file-system relative path (orphaned files) — used for display.</summary>
    public string Path { get; set; } = "";

    public string Type { get; set; } = "file";
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string Extension { get; set; } = "";
    public bool IsEditable { get; set; }

    /// <summary>Umbraco media key (Guid) for media-backed rows; null for orphaned files.</summary>
    public string? MediaKey { get; set; }

    /// <summary>Category: all | broken | orphaned | unused | duplicate.</summary>
    public string Category { get; set; } = "";

    /// <summary>Optional human-readable detail (e.g. missing file path, duplicate group).</summary>
    public string? Detail { get; set; }

    /// <summary>For duplicate rows: identifies the group of identical files (content hash).</summary>
    public string? Group { get; set; }
}
