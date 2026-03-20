using System;
using System.IO;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using Jellyfin.Plugin.SubtitlesTools.Models;
using Jellyfin.Plugin.SubtitlesTools.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SubtitlesTools.Api;

/// <summary>
/// 提供插件配置测试、分段字幕管理、字幕记忆和播放期自动切换所需的接口。
/// </summary>
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Authorize]
public sealed class SubtitlesToolsController : ControllerBase
{
    private readonly IAuthorizationContext _authorizationContext;
    private readonly SubtitlesToolsApiClient _apiClient;
    private readonly MultipartSubtitleManagerService _multipartSubtitleManagerService;
    private readonly RememberedSubtitleAutoApplyService _rememberedSubtitleAutoApplyService;

    /// <summary>
    /// 初始化控制器。
    /// </summary>
    /// <param name="authorizationContext">当前请求的鉴权上下文。</param>
    /// <param name="apiClient">Python 服务端客户端。</param>
    /// <param name="multipartSubtitleManagerService">分段字幕管理服务。</param>
    /// <param name="rememberedSubtitleAutoApplyService">已记住字幕自动切换服务。</param>
    public SubtitlesToolsController(
        IAuthorizationContext authorizationContext,
        SubtitlesToolsApiClient apiClient,
        MultipartSubtitleManagerService multipartSubtitleManagerService,
        RememberedSubtitleAutoApplyService rememberedSubtitleAutoApplyService)
    {
        _authorizationContext = authorizationContext;
        _apiClient = apiClient;
        _multipartSubtitleManagerService = multipartSubtitleManagerService;
        _rememberedSubtitleAutoApplyService = rememberedSubtitleAutoApplyService;
    }

    /// <summary>
    /// 使用当前表单配置测试与 Python 服务端的连通性。
    /// 这个接口只允许具备字幕管理权限的管理员调用，避免普通用户借此探测服务配置。
    /// </summary>
    /// <param name="body">待测试的连接配置。</param>
    /// <returns>健康检查结果。</returns>
    [HttpPost("Jellyfin.Plugin.SubtitlesTools/TestConnection")]
    [Authorize(Policy = Policies.SubtitleManagement)]
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
    /// 获取媒体项的分段结构、现有字幕状态以及当前登录用户的已记住字幕信息。
    /// </summary>
    /// <param name="itemId">Jellyfin 媒体项标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分段管理首页响应。</returns>
    [HttpGet("Jellyfin.Plugin.SubtitlesTools/Items/{itemId:guid}/parts")]
    [ProducesResponseType(typeof(ManagedItemPartsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedItemPartsResponseDto>> GetItemParts(
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = await GetCurrentUserIdAsync().ConfigureAwait(false);
            var result = await _multipartSubtitleManagerService
                .GetItemPartsAsync(itemId, userId, cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { Message = ex.Message });
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
    /// 将某条已落盘的 sidecar 字幕记为当前用户在该分段上的默认字幕。
    /// </summary>
    /// <param name="itemId">Jellyfin 媒体项标识。</param>
    /// <param name="partId">分段标识。</param>
    /// <param name="body">记住字幕请求体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>记住结果。</returns>
    [HttpPost("Jellyfin.Plugin.SubtitlesTools/Items/{itemId:guid}/parts/{partId}/remembered-subtitle")]
    [ProducesResponseType(typeof(ManagedRememberedSubtitleResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedRememberedSubtitleResponseDto>> RememberSubtitle(
        [FromRoute] Guid itemId,
        [FromRoute] string partId,
        [FromBody] ManagedRememberedSubtitleRequestDto body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            var userId = await GetCurrentUserIdAsync().ConfigureAwait(false);
            var result = await _multipartSubtitleManagerService
                .RememberSubtitleAsync(itemId, partId, userId, body, cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { Message = ex.Message });
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
    }

    /// <summary>
    /// 清除当前用户在该分段上的已记住字幕。
    /// </summary>
    /// <param name="itemId">Jellyfin 媒体项标识。</param>
    /// <param name="partId">分段标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>清除结果。</returns>
    [HttpDelete("Jellyfin.Plugin.SubtitlesTools/Items/{itemId:guid}/parts/{partId}/remembered-subtitle")]
    [ProducesResponseType(typeof(ManagedRememberedSubtitleResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedRememberedSubtitleResponseDto>> ClearRememberedSubtitle(
        [FromRoute] Guid itemId,
        [FromRoute] string partId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = await GetCurrentUserIdAsync().ConfigureAwait(false);
            var result = await _multipartSubtitleManagerService
                .ClearRememberedSubtitleAsync(itemId, partId, userId, cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { Message = ex.Message });
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
    }

    /// <summary>
    /// 为所有分段分别搜索并下载第一名字幕候选。
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

    /// <summary>
    /// 解析当前播放会话能否自动切到已记住字幕。
    /// 这个接口只返回“是否可切换”和目标流索引，不直接强控 Jellyfin 会话。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前播放会话的自动切换状态。</returns>
    [HttpGet("Jellyfin.Plugin.SubtitlesTools/Playback/remembered-subtitle")]
    [ProducesResponseType(typeof(RememberedSubtitleAutoApplyResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RememberedSubtitleAutoApplyResponseDto>> GetRememberedSubtitleAutoApplyState(
        CancellationToken cancellationToken)
    {
        var authorizationInfo = await _authorizationContext.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        var remoteEndpoint = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var result = await _rememberedSubtitleAutoApplyService
            .ResolveAsync(authorizationInfo, remoteEndpoint, cancellationToken)
            .ConfigureAwait(false);
        return Ok(result);
    }

    private async Task<Guid> GetCurrentUserIdAsync()
    {
        var authorizationInfo = await _authorizationContext.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        if (authorizationInfo.UserId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("当前请求没有可识别的登录用户。");
        }

        return authorizationInfo.UserId;
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
