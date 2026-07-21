using Microsoft.Extensions.DependencyInjection; // SwaggerDoc(...) extension lives here
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;
#if NET10_0_OR_GREATER
// Umbraco 17/18: Swashbuckle 10.x + Microsoft.OpenApi 2.x (namespace flattened to Microsoft.OpenApi).
using Microsoft.OpenApi;
#else
// Umbraco 16: Swashbuckle 8.x + Microsoft.OpenApi 1.x (types live under Microsoft.OpenApi.Models).
using Microsoft.OpenApi.Models;
#endif

namespace uTPro.Feature.FileManager;

/// <summary>
/// Registers a dedicated Swagger document for the File Manager API so it appears as its own
/// entry in the backoffice Swagger UI "Select a definition" dropdown, instead of being folded
/// into the generic "Umbraco Management API" document.
///
/// Paired with <c>[MapToApi(ApiName)]</c> on the controller and registered from the composer via
/// <c>builder.Services.ConfigureOptions&lt;ConfigureFileManagerSwaggerGenOptions&gt;();</c>.
/// The <c>OpenApiInfo</c> type moved namespace between Microsoft.OpenApi 1.x (Umbraco 16) and
/// 2.x (Umbraco 17/18), so the using above is selected per target framework.
/// </summary>
public class ConfigureFileManagerSwaggerGenOptions : IConfigureOptions<SwaggerGenOptions>
{
    /// <summary>The Swagger document name. Must match the value passed to <c>[MapToApi]</c>.</summary>
    public const string ApiName = "utpro-file-manager";

    public void Configure(SwaggerGenOptions options)
    {
        options.SwaggerDoc(ApiName, new OpenApiInfo
        {
            Title = "uTPro File Manager API",
            Version = "1.0",
            Description = "File & media management endpoints for the uTPro backoffice.",
        });
    }
}
