namespace uTPro.Feature.FileManager.Models;

/// <summary>
/// The raw bytes of a media file read from the media file system (used to preview
/// media-backed or orphaned files during a Media Cleanup scan).
/// </summary>
public class MediaFileContent
{
    public byte[] Bytes { get; set; } = [];
    public string FileName { get; set; } = "";
}
