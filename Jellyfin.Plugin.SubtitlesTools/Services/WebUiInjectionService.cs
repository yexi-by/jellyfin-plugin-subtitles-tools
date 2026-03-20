using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 在 Jellyfin Web 入口页注入插件自带的全局脚本，使详情页能够显示“分段字幕管理”入口。
/// </summary>
public sealed class WebUiInjectionService : IHostedService
{
    private const string GlobalScriptFileName = "subtitles-tools-global.js";
    private const string GlobalScriptResourceName = "Jellyfin.Plugin.SubtitlesTools.Web.subtitlesToolsGlobal.js";
    private const string InjectionStartMarker = "<!-- SubtitlesTools Global Script Start -->";
    private const string InjectionEndMarker = "<!-- SubtitlesTools Global Script End -->";
    private readonly ILogger<WebUiInjectionService> _logger;

    /// <summary>
    /// 初始化 Web 前端注入服务。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    public WebUiInjectionService(ILogger<WebUiInjectionService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var webRootDirectory = ResolveWebRootDirectory();
            var indexFile = new FileInfo(Path.Combine(webRootDirectory.FullName, "index.html"));
            if (!indexFile.Exists)
            {
                _logger.LogWarning("未找到 Jellyfin Web 入口文件，跳过前端注入。路径：{IndexPath}", indexFile.FullName);
                return;
            }

            var scriptFile = new FileInfo(Path.Combine(webRootDirectory.FullName, GlobalScriptFileName));
            await WriteGlobalScriptAsync(scriptFile, cancellationToken).ConfigureAwait(false);
            await InjectScriptTagAsync(indexFile, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Jellyfin Web 分段字幕管理脚本注入完成。入口文件：{IndexPath}", indexFile.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Jellyfin Web 前端注入失败，详情页入口按钮将不可用。");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static DirectoryInfo ResolveWebRootDirectory()
    {
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var webRootDirectory = new DirectoryInfo(Path.Combine(baseDirectory.FullName, "jellyfin-web"));
        if (!webRootDirectory.Exists)
        {
            throw new InvalidOperationException($"未找到 Jellyfin Web 目录：{webRootDirectory.FullName}");
        }

        return webRootDirectory;
    }

    private static async Task InjectScriptTagAsync(FileInfo indexFile, CancellationToken cancellationToken)
    {
        var originalHtml = await File.ReadAllTextAsync(indexFile.FullName, cancellationToken).ConfigureAwait(false);
        var version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        var snippet = $"{InjectionStartMarker}<script defer=\"defer\" src=\"{GlobalScriptFileName}?v={version}\"></script>{InjectionEndMarker}";
        string updatedHtml;

        var startIndex = originalHtml.IndexOf(InjectionStartMarker, StringComparison.Ordinal);
        var endIndex = originalHtml.IndexOf(InjectionEndMarker, StringComparison.Ordinal);
        if (startIndex >= 0 && endIndex > startIndex)
        {
            var replaceLength = (endIndex + InjectionEndMarker.Length) - startIndex;
            updatedHtml = originalHtml.Remove(startIndex, replaceLength).Insert(startIndex, snippet);
        }
        else
        {
            var bodyCloseIndex = originalHtml.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyCloseIndex < 0)
            {
                throw new InvalidOperationException("Jellyfin Web index.html 不包含 </body>，无法注入全局脚本。");
            }

            updatedHtml = originalHtml.Insert(bodyCloseIndex, snippet);
        }

        if (!string.Equals(originalHtml, updatedHtml, StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(indexFile.FullName, updatedHtml, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteGlobalScriptAsync(FileInfo scriptFile, CancellationToken cancellationToken)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(GlobalScriptResourceName)
            ?? throw new InvalidOperationException($"未找到嵌入资源：{GlobalScriptResourceName}");
        using var reader = new StreamReader(stream);
        var scriptContent = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(scriptFile.FullName, scriptContent, cancellationToken).ConfigureAwait(false);
    }
}
