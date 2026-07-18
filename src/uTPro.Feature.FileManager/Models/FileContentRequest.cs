namespace uTPro.Feature.FileManager.Models;

public class FileContentRequest
{
    public string Path { get; set; } = "";
    /// <summary>Optional configured-root key (Locations).</summary>
    public string? Root { get; set; }
}
