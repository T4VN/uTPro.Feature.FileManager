namespace uTPro.Feature.FileManager.Models;

public class CreateFolderRequest
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Optional configured-root key (Locations).</summary>
    public string? Root { get; set; }
}
