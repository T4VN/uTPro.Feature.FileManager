using uTPro.Feature.FileManager.Models;

namespace uTPro.Feature.FileManager.Services;

/// <summary>
/// Scans the Umbraco media library and file system to report media that is
/// broken, orphaned, unused or duplicated — and acts on those findings
/// (recycle/delete media nodes, delete orphaned files).
/// </summary>
public interface IMediaScanService
{
    /// <summary>
    /// Scans the media library and file system and returns a categorized report.
    /// Results are cached briefly; pass <paramref name="force"/> to bypass the cache.
    /// </summary>
    Task<MediaScanResult> ScanAsync(bool force = false);

    /// <summary>Reads a media file (by media-file-system path) for preview. Returns null if missing.</summary>
    MediaFileContent? ReadMediaFile(string pathOrRelative);

    /// <summary>
    /// Moves a media item (by key) to the media recycle bin. This is the safe, recoverable
    /// way to remove Unused/Broken/Duplicate media items.
    /// </summary>
    MediaActionResult RecycleMedia(Guid mediaKey, int userId);

    /// <summary>Restores a trashed media item (by key) from the recycle bin to the media root.</summary>
    MediaActionResult RestoreMedia(Guid mediaKey, int userId);

    /// <summary>Permanently deletes a media item (by key), bypassing/emptying it from the recycle bin.</summary>
    MediaActionResult DeleteMedia(Guid mediaKey, int userId);

    /// <summary>Permanently deletes every media item currently in the recycle bin.</summary>
    MediaActionResult EmptyRecycleBin(int userId);

    /// <summary>
    /// Deletes an orphaned file (a file present in the media file system that no media
    /// item references) by its media-file-system relative path.
    /// </summary>
    MediaActionResult DeleteOrphanedFile(string relativePath);
}
