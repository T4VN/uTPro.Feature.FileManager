using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using uTPro.Feature.FileManager.Models;

namespace uTPro.Feature.FileManager.Services;

/// <summary>
/// Cleanup actions for <see cref="MediaScanService"/>: recycle / restore / delete / empty-bin,
/// orphaned-file deletion and media-file reads. Each mutating action clears the scan cache.
/// </summary>
internal partial class MediaScanService
{
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
}
