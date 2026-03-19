using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SubtitlesTools;

/// <summary>
/// 插件主入口，负责暴露配置页和全局配置实例。
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private static readonly Guid PluginGuid = Guid.Parse("b812b763-a31d-4d19-bfcc-15b9eac432cb");

    /// <summary>
    /// 初始化插件实例。
    /// </summary>
    /// <param name="applicationPaths">Jellyfin 应用路径服务。</param>
    /// <param name="xmlSerializer">配置序列化器。</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// 获取当前插件单例。
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Subtitles Tools";

    /// <inheritdoc />
    public override Guid Id => PluginGuid;

    /// <summary>
    /// 返回插件配置页和前端脚本资源。
    /// </summary>
    /// <returns>插件页面定义列表。</returns>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                DisplayName = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Web.subtitlesTools.html",
                    GetType().Namespace)
            },
            new PluginPageInfo
            {
                Name = "subtitlesToolsjs",
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Web.subtitlesTools.js",
                    GetType().Namespace)
            }
        ];
    }
}
