using System.Net.Http;
using System.Net.Http.Headers;
using Jellyfin.Plugin.SubtitlesTools.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SubtitlesTools;

/// <summary>
/// 注册插件所需的服务和字幕提供器。
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton(_ =>
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(
                    applicationHost.Name.Replace(' ', '_'),
                    applicationHost.ApplicationVersionString));
            httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(
                    "Jellyfin-Plugin-SubtitlesTools",
                    typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.1.2.0"));
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            return httpClient;
        });
        serviceCollection.AddSingleton<VideoHashCalculator>();
        serviceCollection.AddSingleton<VideoHashCacheService>();
        serviceCollection.AddSingleton<VideoHashResolverService>();
        serviceCollection.AddSingleton<VideoHashBackfillService>();
        serviceCollection.AddSingleton<SubtitlesToolsApiClient>();
        serviceCollection.AddSingleton<PrecomputeMissingHashesScheduledTask>();
        serviceCollection.AddSingleton<VideoHashPrecomputeService>();
        serviceCollection.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<VideoHashPrecomputeService>());
        serviceCollection.AddSingleton<ISubtitleProvider, SubtitlesToolsSubtitleProvider>();
    }
}
