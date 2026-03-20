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
/// 提供插件配置测试、分段转换、字幕搜索、下载内封和字幕流删除所需的接口。
/// 当前版本不再管理外挂字幕，而是统一面向 MKV 内封字幕。
/// </summary>
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Authorize]
public sealed class SubtitlesToolsController : ControllerBase
{
    private readonly SubtitlesToolsApiClient _apiClient;
    private readonly FfmpegProcessService _ffmpegProcessService;
    private readonly MultipartSubtitleManagerService _multipartSubtitleManagerService;

    /// <summary>
    /// 初始化控制器。
    /// </summary>
    public SubtitlesToolsController(
        SubtitlesToolsApiClient apiClient,
        FfmpegProcessService ffmpegProcessService,
        MultipartSubtitleManagerService multipartSubtitleManagerService)
    {
        _apiClient = apiClient;
        _ffmpegProcessService = ffmpegProcessService;
        _multipartSubtitleManagerService = multipartSubtitleManagerService;
    }

    /// <summary>
    /// 使用当前表单配置测试 Python 服务端和 FFmpeg 的可用性。
    /// </summary>
    /// <param name="body">待测试的连接配置。</param>
    /// <returns>检查结果。</returns>
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
                RequestTimeoutSeconds = PluginConfiguration.NormalizeTimeoutSeconds(body.RequestTimeoutSeconds),
                EnableAutoHashPrecompute = body.EnableAutoHashPrecompute,
                HashPrecomputeConcurrency = PluginConfiguration.NormalizeHashPrecomputeConcurrency(body.HashPrecomputeConcurrency),
                EnableAutoVideoConvertToMkv = body.EnableAutoVideoConvertToMkv,
                VideoConvertConcurrency = PluginConfiguration.NormalizeVideoConvertConcurrency(body.VideoConvertConcurrency),
                FfmpegExecutablePath = PluginConfiguration.NormalizeFfmpegExecutablePath(body.FfmpegExecutablePath)
            };

            var result = await _apiClient.CheckHealthAsync(configuration, CancellationToken.None).ConfigureAwait(false);
            var ffmpegValidation = _ffmpegProcessService.ValidateExecutables();

            return Ok(
                new
                {
                    Message = "连接成功。",
                    ServiceBaseUrl = result.ServiceBaseUrl,
                    TimeoutSeconds = result.TimeoutSeconds,
                    Health = result.Health,
                    Ffmpeg = new
                    {
                        ffmpegPath = ffmpegValidation.FfmpegPath,
                        ffprobePath = ffmpegValidation.FfprobePath
                    }
                });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// 获取媒体项的分段结构与内封字幕状态。
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
    /// 手动把当前分段转换为 MKV。
    /// </summary>
    [HttpPost("Jellyfin.Plugin.SubtitlesTools/Items/{itemId:guid}/parts/{partId}/convert")]
    [ProducesResponseType(typeof(ManagedPartConvertResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedPartConvertResponseDto>> ConvertPart(
        [FromRoute] Guid itemId,
        [FromRoute] string partId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _multipartSubtitleManagerService
                .ConvertPartAsync(itemId, partId, cancellationToken)
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
        catch (FfmpegExecutionException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = ex.Message });
        }
    }

    /// <summary>
    /// 一键把整组分段转换为 MKV。
    /// </summary>
    [HttpPost("Jellyfin.Plugin.SubtitlesTools/Items/{itemId:guid}/convert-group")]
    [ProducesResponseType(typeof(ManagedBatchOperationResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedBatchOperationResponseDto>> ConvertGroup(
        [FromRoute] Guid itemId,
        [FromBody] ManagedConvertGroupRequestDto body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            var result = await _multipartSubtitleManagerService
                .ConvertGroupAsync(itemId, body, cancellationToken)
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
    /// 下载指定候选字幕并内封到当前分段。
    /// </summary>
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
        catch (FfmpegExecutionException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = ex.Message });
        }
    }

    /// <summary>
    /// 删除当前分段的一条插件内封字幕流。
    /// </summary>
    [HttpPost("Jellyfin.Plugin.SubtitlesTools/Items/{itemId:guid}/parts/{partId}/delete-embedded-subtitle")]
    [ProducesResponseType(typeof(ManagedDeleteEmbeddedSubtitleResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedDeleteEmbeddedSubtitleResponseDto>> DeleteEmbeddedSubtitle(
        [FromRoute] Guid itemId,
        [FromRoute] string partId,
        [FromBody] ManagedDeleteEmbeddedSubtitleRequestDto body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            var result = await _multipartSubtitleManagerService
                .DeleteEmbeddedSubtitleAsync(itemId, partId, body, cancellationToken)
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
        catch (FfmpegExecutionException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = ex.Message });
        }
    }

    /// <summary>
    /// 为所有分段分别搜索并内封第一名字幕候选。
    /// </summary>
    [HttpPost("Jellyfin.Plugin.SubtitlesTools/Items/{itemId:guid}/download-best")]
    [ProducesResponseType(typeof(ManagedBatchOperationResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedBatchOperationResponseDto>> DownloadBest(
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
        catch (FfmpegExecutionException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = ex.Message });
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

    /// <summary>
    /// 获取或设置是否自动预计算哈希。
    /// </summary>
    public bool EnableAutoHashPrecompute { get; set; }

    /// <summary>
    /// 获取或设置哈希预计算并发数。
    /// </summary>
    public int HashPrecomputeConcurrency { get; set; }

    /// <summary>
    /// 获取或设置是否自动转换为 MKV。
    /// </summary>
    public bool EnableAutoVideoConvertToMkv { get; set; }

    /// <summary>
    /// 获取或设置视频转换并发数。
    /// </summary>
    public int VideoConvertConcurrency { get; set; }

    /// <summary>
    /// 获取或设置 FFmpeg 路径。
    /// </summary>
    public string? FfmpegExecutablePath { get; set; }
}
