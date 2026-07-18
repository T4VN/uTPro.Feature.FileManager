namespace uTPro.Feature.FileManager.Models;

public class RenameRequest
{
    public string Path { get; set; } = "";
    public string NewName { get; set; } = "";

    /// <summary>Optional Locations root key. Null/empty = default single-root behaviour.</summary>
    public string? Root { get; set; }
}
