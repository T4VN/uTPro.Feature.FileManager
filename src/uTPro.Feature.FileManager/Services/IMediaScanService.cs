using uTPro.Feature.FileManager.Models;

namespace uTPro.Feature.FileManager.Services;

/// <summary>
/// Scans the Umbraco media library and file system to report media that is
/// broken, orphaned, unused or duplicated.
/// </summary>
public interface IMediaScanService
{
    Task<MediaScanResult> ScanAsync();
}
