namespace uTPro.Feature.FileManager.Models;

/// <summary>
/// Request to act on a single Media Cleanup scan row.
///
/// Media-backed rows (Unused/Broken/Duplicate, and media-backed Large rows) carry a
/// <see cref="MediaKey"/> — use recycle/delete-media endpoints.
///
/// Orphaned rows (and orphaned Large rows) have no media key, only a file-system
/// relative <see cref="Path"/> — use the delete-orphan endpoint.
/// </summary>
public class MediaActionRequest
{
    /// <summary>Umbraco media key (Guid) for media-backed rows.</summary>
    public string? MediaKey { get; set; }

    /// <summary>Media-file-system relative path for orphaned files.</summary>
    public string? Path { get; set; }
}
