namespace uTPro.Feature.FileManager.Models;

public class ImportUrlRequest
{
    public string Path { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>Optional Locations root key. Null/empty = default single-root behaviour.</summary>
    public string? Root { get; set; }
}
