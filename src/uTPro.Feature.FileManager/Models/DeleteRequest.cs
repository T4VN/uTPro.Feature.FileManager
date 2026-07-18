namespace uTPro.Feature.FileManager.Models;

public class DeleteRequest
{
    public string Path { get; set; } = "";

    /// <summary>Optional Locations root key. Null/empty = default single-root behaviour.</summary>
    public string? Root { get; set; }
}
