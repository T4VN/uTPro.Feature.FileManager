namespace uTPro.Feature.FileManager.Models;

public class FileItemViewModel
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Type { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string Extension { get; set; } = "";
    public bool IsEditable { get; set; }
}
