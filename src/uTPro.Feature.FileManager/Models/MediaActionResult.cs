namespace uTPro.Feature.FileManager.Models;

/// <summary>
/// The outcome of a Media Cleanup action (recycle/delete media node or delete orphaned file).
/// </summary>
public class MediaActionResult
{
    public bool Success { get; set; }

    /// <summary>Human-readable message describing the outcome (or the reason for failure).</summary>
    public string? Message { get; set; }

    public static MediaActionResult Ok(string? message = null) => new() { Success = true, Message = message };
    public static MediaActionResult Fail(string message) => new() { Success = false, Message = message };
}
