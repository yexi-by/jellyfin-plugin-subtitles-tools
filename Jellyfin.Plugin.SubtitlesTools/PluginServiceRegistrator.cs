using System.Net.Http;
using System.Net.Http.Headers;
using Jellyfin.Plugin.SubtitlesTools.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SubtitlesTools;

/// <summary>
/// 注册插件所需的服务、后台任务和前端注入能力。
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
                    typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0"));
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            return httpClient;
        });

        serviceCollection.AddSingleton<VideoHashCalculator>();
        serviceCollection.AddSingleton<VideoHashBackfillService>();
        serviceCollection.AddSingleton<SubtitlesToolsApiClient>();
        serviceCollection.AddSingleton<SubtitleMetadataService>();
        serviceCollection.AddSingleton<FfmpegProcessService>();
        serviceCollection.AddSingleton<AndroidHwdecodeRiskService>();
        serviceCollection.AddSingleton<VideoContainerConversionService>();
        serviceCollection.AddSingleton<MkvMetadataIdentityService>();
        serviceCollection.AddSingleton<SubtitleSrtConversionService>();
        serviceCollection.AddSingleton<EmbeddedSubtitleService>();
        serviceCollection.AddSingleton<MultipartMediaParserService>();
        serviceCollection.AddSingleton<MultipartSubtitleManagerService>();
        serviceCollection.AddSingleton<PrecomputeMissingHashesScheduledTask>();
        serviceCollection.AddSingleton<VideoHashPrecomputeService>();

        serviceCollection.AddHostedService<LegacyDataCleanupService>();
        serviceCollection.AddHostedService<WebUiInjectionService>();
        serviceCollection.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<VideoHashPrecomputeService>());
    }
}
