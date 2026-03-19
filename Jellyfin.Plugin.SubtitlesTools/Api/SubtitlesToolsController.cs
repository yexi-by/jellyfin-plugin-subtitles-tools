using System;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using Jellyfin.Plugin.SubtitlesTools.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SubtitlesTools.Api;

/// <summary>
/// 提供插件配置页所需的辅助接口。
/// </summary>
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(Policy = Policies.SubtitleManagement)]
public sealed class SubtitlesToolsController : ControllerBase
{
    private readonly SubtitlesToolsApiClient _apiClient;

    /// <summary>
    /// 初始化控制器。
    /// </summary>
    /// <param name="apiClient">Python 服务端客户端。</param>
    public SubtitlesToolsController(SubtitlesToolsApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    /// <summary>
    /// 使用当前表单配置测试与 Python 服务的连通性。
    /// </summary>
    /// <param name="body">待测试的连接配置。</param>
    /// <returns>健康检查结果。</returns>
    [HttpPost("Jellyfin.Plugin.SubtitlesTools/TestConnection")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> TestConnection([FromBody] TestConnectionRequest body)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            var configuration = new PluginConfiguration
            {
                ServiceBaseUrl = PluginConfiguration.NormalizeServiceBaseUrl(body.ServiceBaseUrl),
                RequestTimeoutSeconds = PluginConfiguration.NormalizeTimeoutSeconds(body.RequestTimeoutSeconds)
            };

            var result = await _apiClient.CheckHealthAsync(configuration, CancellationToken.None).ConfigureAwait(false);
            return Ok(
                new
                {
                    Message = "连接成功。",
                    ServiceBaseUrl = result.ServiceBaseUrl,
                    TimeoutSeconds = result.TimeoutSeconds,
                    Health = result.Health
                });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }
}

/// <summary>
/// 配置页测试连接请求体。
/// </summary>
public sealed class TestConnectionRequest
{
    /// <summary>
    /// 获取或设置 Python 服务地址。
    /// </summary>
    public string? ServiceBaseUrl { get; set; }

    /// <summary>
    /// 获取或设置请求超时秒数。
    /// </summary>
    public int RequestTimeoutSeconds { get; set; }
}
