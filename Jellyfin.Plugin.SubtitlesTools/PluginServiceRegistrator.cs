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
        serviceCollection
            .AddHttpClient(SubtitlesToolsApiClient.HttpClientName, client =>
            {
                client.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue(
                        applicationHost.Name.Replace(' ', '_'),
                        applicationHost.ApplicationVersionString));
                client.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue(
                        "Jellyfin-Plugin-SubtitlesTools",
                        typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.1.0.0"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            });

        serviceCollection.AddSingleton<VideoHashCalculator>();
        serviceCollection.AddSingleton<VideoHashCacheService>();
        serviceCollection.AddSingleton<SubtitlesToolsApiClient>();
        serviceCollection.AddSingleton<ISubtitleProvider, SubtitlesToolsSubtitleProvider>();
    }
}
