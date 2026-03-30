namespace uTPro.Feature.FileManager.Models;

public class BrowseRequest
{
    public string Path { get; set; } = "";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public string Search { get; set; } = "";
}

public class FileContentRequest
{
    public string Path { get; set; } = "";
}

public class SaveFileRequest
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
}

public class CreateFolderRequest
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
}

public class RenameRequest
{
    public string Path { get; set; } = "";
    public string NewName { get; set; } = "";
}

public class DeleteRequest
{
    public string Path { get; set; } = "";
}

public class ImportUrlRequest
{
    public string Path { get; set; } = "";
    public string Url { get; set; } = "";
}
