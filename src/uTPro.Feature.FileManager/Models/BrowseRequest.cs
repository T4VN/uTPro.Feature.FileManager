namespace uTPro.Feature.FileManager.Models;

public class BrowseRequest
{
    public string Path { get; set; } = "";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public string Search { get; set; } = "";
}
