namespace uTPro.Feature.FileManager.Models;

public class SaveFileRequest
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
    /// <summary>Optional configured-root key (Locations).</summary>
    public string? Root { get; set; }
}
