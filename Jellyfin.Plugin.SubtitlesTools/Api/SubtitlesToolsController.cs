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
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SubtitlesTools.Api;

/// <summary>
/// 提供插件配置测试、分段纳管、字幕搜索、下载内封与内封字幕删除所需的接口。
/// 当前版本会先纳管并修复视频兼容性，再执行字幕搜索、下载和内封，不再管理外挂字幕。
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
    /// 使用当前表单配置测试 Python 服务端和 FFmpeg 可用性。
    /// </summary>
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
                EnableAutoVideoConvertToMkv = body.EnableAutoVideoConvertToMkv,
                VideoConvertConcurrency = PluginConfiguration.NormalizeVideoConvertConcurrency(body.VideoConvertConcurrency),
                FfmpegExecutablePath = PluginConfiguration.NormalizeFfmpegExecutablePath(body.FfmpegExecutablePath),
                QsvRenderDevicePath = PluginConfiguration.NormalizeQsvRenderDevicePath(body.QsvRenderDevicePath)
            };

            var result = await _apiClient.CheckHealthAsync(configuration, CancellationToken.None).ConfigureAwait(false);
            var ffmpegValidation = _ffmpegProcessService.ValidateExecutables();
            var supportsQsv = await _ffmpegProcessService.SupportsEncoderAsync("h264_qsv", CancellationToken.None).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows() && !QsvRenderDeviceExists(configuration.QsvRenderDevicePath))
            {
                return BadRequest(new { Message = $"未找到 Intel QSV 渲染设备：{configuration.QsvRenderDevicePath}" });
            }

            return Ok(new
            {
                Message = "连接成功。",
                ServiceBaseUrl = result.ServiceBaseUrl,
                TimeoutSeconds = result.TimeoutSeconds,
                Health = result.Health,
                Ffmpeg = new
                {
                    ffmpegPath = ffmpegValidation.FfmpegPath,
                    ffprobePath = ffmpegValidation.FfprobePath
                },
                Video = new
                {
                    qsvRenderDevicePath = configuration.QsvRenderDevicePath,
                    supportsH264Qsv = supportsQsv
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
    /// 检查 Linux NAS 上的 QSV 渲染设备节点是否存在。
    /// 这里读取的是管理员在插件配置页里填写的本机设备路径，只用于存在性校验，不参与其它文件系统操作。
    /// </summary>
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "仅校验管理员配置的 QSV 设备节点是否存在，不执行读写或枚举。")]
    private static bool QsvRenderDeviceExists(string renderDevicePath)
    {
        return System.IO.File.Exists(renderDevicePath);
    }

    /// <summary>
    /// 获取媒体项的分段结构、纳管状态与已内封字幕流。
    /// </summary>
    [HttpGet("Jellyfin.Plugin.SubtitlesTools/Items/{itemId:guid}/parts")]
    [ProducesResponseType(typeof(ManagedItemPartsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedItemPartsResponseDto>> GetItemParts([FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _multipartSubtitleManagerService.GetItemPartsAsync(itemId, cancellationToken).ConfigureAwait(false);
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
    /// 为指定分段先纳管，再搜索字幕候选。
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
            var result = await _multipartSubtitleManagerService.SearchPartAsync(itemId, partId, cancellationToken).ConfigureAwait(false);
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
    /// 手动把当前分段纳管为 MKV。
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
            var result = await _multipartSubtitleManagerService.ConvertPartAsync(itemId, partId, cancellationToken).ConfigureAwait(false);
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
    /// 一键把整组分段纳管为 MKV。
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
            var result = await _multipartSubtitleManagerService.ConvertGroupAsync(itemId, body, cancellationToken).ConfigureAwait(false);
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
            var result = await _multipartSubtitleManagerService.DownloadPartAsync(itemId, partId, body, cancellationToken).ConfigureAwait(false);
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
    /// 删除当前分段中的一条插件内封字幕流。
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
            var result = await _multipartSubtitleManagerService.DeleteEmbeddedSubtitleAsync(itemId, partId, body, cancellationToken).ConfigureAwait(false);
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
            var result = await _multipartSubtitleManagerService.DownloadBestAsync(itemId, body, cancellationToken).ConfigureAwait(false);
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
    /// 获取或设置是否在新视频入库后自动纳管并修复安卓硬解兼容。
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

    /// <summary>
    /// 获取或设置 Intel QSV 渲染设备路径。
    /// </summary>
    public string? QsvRenderDevicePath { get; set; }
}
