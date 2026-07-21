using Microsoft.AspNetCore.Http;

namespace uTPro.Feature.FileManager.Models;

/// <summary>
/// Multipart/form-data body for the file upload endpoint.
/// Wrapping the form fields (including <see cref="IFormFile"/>) in a single [FromForm] model
/// keeps Swashbuckle able to generate the OpenAPI schema — multiple loose [FromForm]
/// parameters combined with IFormFile break swagger.json generation.
/// </summary>
public class UploadRequest
{
    /// <summary>Target directory (relative to the resolved root).</summary>
    public string Path { get; set; } = "";

    /// <summary>The uploaded file.</summary>
    public IFormFile File { get; set; } = default!;

    /// <summary>Optional configured-root key (Locations).</summary>
    public string? Root { get; set; }
}
