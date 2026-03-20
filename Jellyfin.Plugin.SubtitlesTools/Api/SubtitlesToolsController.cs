using System;
using System.IO;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using Jellyfin.Plugin.SubtitlesTools.Models;
using Jellyfin.Plugin.SubtitlesTools.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SubtitlesTools.Api;

/// <summary>
/// 提供插件配置测试、分段结构查询、字幕搜索和字幕下载等接口。
/// </summary>
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(Policy = Policies.SubtitleManagement)]
public sealed class SubtitlesToolsController : ControllerBase
{
    private readonly SubtitlesToolsApiClient _apiClient;
    private readonly MultipartSubtitleManagerService _multipartSubtitleManagerService;

    /// <summary>
    /// 初始化控制器。
    /// </summary>
    /// <param name="apiClient">Python 服务端客户端。</param>
    /// <param name="multipartSubtitleManagerService">分段字幕管理服务。</param>
    public SubtitlesToolsController(
        SubtitlesToolsApiClient apiClient,
        MultipartSubtitleManagerService multipartSubtitleManagerService)
    {
        _apiClient = apiClient;
        _multipartSubtitleManagerService = multipartSubtitleManagerService;
    }

    /// <summary>
    /// 使用当前表单配置测试与 Python 服务端的连通性。
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

    /// <summary>
    /// 获取媒体项的分段结构和现有字幕状态。
    /// </summary>
    /// <param name="itemId">Jellyfin 媒体项标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分段管理首页响应。</returns>
    [HttpGet("Jellyfin.Plugin.SubtitlesTools/Items/{itemId:guid}/parts")]
    [ProducesResponseType(typeof(ManagedItemPartsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedItemPartsResponseDto>> GetItemParts(
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _multipartSubtitleManagerService
                .GetItemPartsAsync(itemId, cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// 为指定分段搜索字幕候选。
    /// </summary>
    /// <param name="itemId">Jellyfin 媒体项标识。</param>
    /// <param name="partId">分段标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分段搜索结果。</returns>
    [HttpPost("Jellyfin.Plugin.SubtitlesTools/Items/{itemId:guid}/parts/{partId}/search")]
    [ProducesResponseType(typeof(ManagedPartSearchResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedPartSearchResponseDto>> SearchPart(
        [FromRoute] Guid itemId,
        [FromRoute] string partId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _multipartSubtitleManagerService
                .SearchPartAsync(itemId, partId, cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { Message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
        catch (SubtitlesToolsApiException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { Message = ex.Message });
        }
    }

    /// <summary>
    /// 将指定候选字幕下载到分段对应的 sidecar 文件。
    /// </summary>
    /// <param name="itemId">Jellyfin 媒体项标识。</param>
    /// <param name="partId">分段标识。</param>
    /// <param name="body">下载请求体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>下载结果。</returns>
    [HttpPost("Jellyfin.Plugin.SubtitlesTools/Items/{itemId:guid}/parts/{partId}/download")]
    [ProducesResponseType(typeof(ManagedPartDownloadResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedPartDownloadResponseDto>> DownloadPart(
        [FromRoute] Guid itemId,
        [FromRoute] string partId,
        [FromBody] ManagedPartDownloadRequestDto body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            var result = await _multipartSubtitleManagerService
                .DownloadPartAsync(itemId, partId, body, cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { Message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
        catch (IOException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = ex.Message });
        }
        catch (SubtitlesToolsApiException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { Message = ex.Message });
        }
    }

    /// <summary>
    /// 对所有分段分别搜索并下载第一名字幕候选。
    /// </summary>
    /// <param name="itemId">Jellyfin 媒体项标识。</param>
    /// <param name="body">批量下载请求体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>批量下载结果。</returns>
    [HttpPost("Jellyfin.Plugin.SubtitlesTools/Items/{itemId:guid}/download-best")]
    [ProducesResponseType(typeof(ManagedDownloadBestResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedDownloadBestResponseDto>> DownloadBest(
        [FromRoute] Guid itemId,
        [FromBody] ManagedDownloadBestRequestDto body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            var result = await _multipartSubtitleManagerService
                .DownloadBestAsync(itemId, body, cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
        catch (SubtitlesToolsApiException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { Message = ex.Message });
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
