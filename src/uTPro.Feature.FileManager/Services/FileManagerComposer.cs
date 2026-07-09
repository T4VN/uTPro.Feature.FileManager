using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace uTPro.Feature.FileManager.Services;

internal class FileManagerComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Registers IHttpClientFactory (used by ImportFromUrl to avoid socket exhaustion).
        builder.Services.AddHttpClient();
        builder.Services.AddScoped<IFileManagerService, FileManagerService>();
    }
}
