using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using uTPro.Feature.FileManager.Models;

namespace uTPro.Feature.FileManager.Services;

internal class FileManagerComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Registers IHttpClientFactory (used by ImportFromUrl to avoid socket exhaustion).
        builder.Services.AddHttpClient();
        // Ensures IMemoryCache is available for the Media Cleanup scan cache (idempotent).
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<IFileManagerService, FileManagerService>();
        builder.Services.AddScoped<IMediaScanService, MediaScanService>();

        // Bind configurable upload limits from uTPro:Feature:FileManager.
        builder.Services.Configure<FileManagerOptions>(builder.Config.GetSection(FileManagerOptions.SectionPath));
    }
}
