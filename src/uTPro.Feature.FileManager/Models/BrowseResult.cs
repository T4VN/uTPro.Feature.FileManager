namespace uTPro.Feature.FileManager.Models;

public class BrowseResult
{
    public string CurrentPath { get; set; } = "";
    public string ParentPath { get; set; } = "";
    public IEnumerable<FileItemViewModel> Items { get; set; } = [];
    public int TotalItems { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public int TotalPages { get; set; }
}
