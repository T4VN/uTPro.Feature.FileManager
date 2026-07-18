namespace uTPro.Feature.FileManager.Models;

public class BrowseRequest
{
    public string Path { get; set; } = "";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public string Search { get; set; } = "";
    /// <summary>Optional configured-root key (Locations). Empty = default root for the user.</summary>
    public string? Root { get; set; }
}
