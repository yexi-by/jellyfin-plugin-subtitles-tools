using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using Jellyfin.Plugin.SubtitlesTools.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 负责在播放态解析“已记住字幕”是否可以自动切换，并向前端返回可执行的切换目标。
/// </summary>
public sealed class RememberedSubtitleAutoApplyService
{
    private readonly ISessionManager _sessionManager;
    private readonly RememberedSubtitleStoreService _rememberedSubtitleStoreService;
    private readonly MultipartMediaParserService _multipartMediaParserService;
    private readonly ILogger<RememberedSubtitleAutoApplyService> _logger;

    /// <summary>
    /// 初始化播放态自动切换服务。
    /// </summary>
    /// <param name="sessionManager">Jellyfin 会话管理器。</param>
    /// <param name="rememberedSubtitleStoreService">记住字幕存储服务。</param>
    /// <param name="multipartMediaParserService">分段解析服务。</param>
    /// <param name="logger">日志记录器。</param>
    public RememberedSubtitleAutoApplyService(
        ISessionManager sessionManager,
        RememberedSubtitleStoreService rememberedSubtitleStoreService,
        MultipartMediaParserService multipartMediaParserService,
        ILogger<RememberedSubtitleAutoApplyService> logger)
    {
        _sessionManager = sessionManager;
        _rememberedSubtitleStoreService = rememberedSubtitleStoreService;
        _multipartMediaParserService = multipartMediaParserService;
        _logger = logger;
    }

    /// <summary>
    /// 解析当前请求对应播放会话是否存在可自动切换的已记住字幕。
    /// </summary>
    /// <param name="authorizationInfo">当前请求的鉴权信息。</param>
    /// <param name="remoteEndpoint">当前请求远端地址。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>前端可直接消费的自动切换结果。</returns>
    public async Task<RememberedSubtitleAutoApplyResponseDto> ResolveAsync(
        AuthorizationInfo authorizationInfo,
        string remoteEndpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizationInfo);

        if (!(Plugin.Instance?.Configuration.EnableRememberedSubtitleAutoApply ?? PluginConfiguration.DefaultEnableRememberedSubtitleAutoApply))
        {
            return BuildResponse("disabled", "管理员已关闭播放时自动切换记住字幕。");
        }

        if (authorizationInfo.UserId == Guid.Empty
            || string.IsNullOrWhiteSpace(authorizationInfo.Token)
            || string.IsNullOrWhiteSpace(authorizationInfo.DeviceId))
        {
            return BuildResponse("no_playback", "当前请求没有可识别的播放会话。");
        }

        SessionInfo? session;
        try
        {
            session = await _sessionManager.GetSessionByAuthenticationToken(
                authorizationInfo.Token,
                authorizationInfo.DeviceId,
                remoteEndpoint).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "通过当前令牌解析播放会话失败。user_id={UserId}", authorizationInfo.UserId);
            return BuildResponse("no_playback", "当前请求没有可识别的播放会话。");
        }

        if (session is null || session.NowPlayingItem is null)
        {
            return BuildResponse("no_playback", "当前没有正在播放的媒体。");
        }

        if (!SupportsSetSubtitleStreamIndex(session))
        {
            return BuildResponse("unsupported_client", "当前客户端不支持远程切换字幕流。", session);
        }

        var mediaPath = session.FullNowPlayingItem?.Path ?? session.NowPlayingItem.Path;
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            return BuildResponse("unsupported_media", "当前播放项没有可解析的本地媒体路径。", session);
        }

        if (string.Equals(Path.GetExtension(mediaPath), ".strm", StringComparison.OrdinalIgnoreCase))
        {
            return BuildResponse("unsupported_media", "当前版本不处理 .strm 媒体。", session);
        }

        var mediaFile = new FileInfo(mediaPath);
        if (!mediaFile.Exists)
        {
            return BuildResponse("unsupported_media", "当前媒体文件不存在或 Jellyfin 无法读取。", session);
        }

        var group = _multipartMediaParserService.Parse(mediaFile.FullName);
        var currentPart = group.Parts.FirstOrDefault(item => PathsEqual(item.MediaFile.FullName, mediaFile.FullName));
        if (currentPart is null)
        {
            return BuildResponse("unsupported_media", "当前播放项无法映射到分段信息。", session);
        }

        var rememberedRecord = await _rememberedSubtitleStoreService
            .GetAsync(authorizationInfo.UserId, currentPart.MediaFile.FullName, cancellationToken)
            .ConfigureAwait(false);
        if (rememberedRecord is null)
        {
            return BuildResponse("no_memory", "当前分段没有记住字幕。", session, currentPart.Id, mediaFile.FullName);
        }

        var rememberedFile = new FileInfo(Path.Combine(mediaFile.DirectoryName!, rememberedRecord.SubtitleFileName));
        if (!rememberedFile.Exists)
        {
            return BuildResponse(
                "missing_file",
                "已记住的字幕文件不存在，无法自动切换。",
                session,
                currentPart.Id,
                mediaFile.FullName,
                rememberedRecord.SubtitleFileName);
        }

        var targetStream = FindTargetStream(session.NowPlayingItem, rememberedRecord.SubtitleFileName);
        if (targetStream is null)
        {
            return BuildResponse(
                "not_found_in_streams",
                "当前播放会话尚未识别到已记住的字幕流。",
                session,
                currentPart.Id,
                mediaFile.FullName,
                rememberedRecord.SubtitleFileName,
                currentSubtitleStreamIndex: session.PlayState?.SubtitleStreamIndex);
        }

        if (session.PlayState?.SubtitleStreamIndex == targetStream.Index)
        {
            return BuildResponse(
                "already_selected",
                "当前播放会话已经使用已记住字幕。",
                session,
                currentPart.Id,
                mediaFile.FullName,
                rememberedRecord.SubtitleFileName,
                targetStream.Index,
                session.PlayState?.SubtitleStreamIndex);
        }

        return BuildResponse(
            "ready",
            "已找到可自动切换的记住字幕。",
            session,
            currentPart.Id,
            mediaFile.FullName,
            rememberedRecord.SubtitleFileName,
            targetStream.Index,
            session.PlayState?.SubtitleStreamIndex);
    }

    private static MediaStream? FindTargetStream(BaseItemDto nowPlayingItem, string subtitleFileName)
    {
        var fileName = Path.GetFileName(subtitleFileName);
        return nowPlayingItem.MediaSources?
            .SelectMany(item => item.MediaStreams ?? [])
            .FirstOrDefault(item =>
                item.IsExternal
                && item.Type == MediaStreamType.Subtitle
                && !string.IsNullOrWhiteSpace(item.Path)
                && string.Equals(Path.GetFileName(item.Path), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SupportsSetSubtitleStreamIndex(SessionInfo session)
    {
        return session.SupportedCommands?.Any(item => item == GeneralCommandType.SetSubtitleStreamIndex) ?? false;
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }

    private static RememberedSubtitleAutoApplyResponseDto BuildResponse(
        string status,
        string message,
        SessionInfo? session = null,
        string? partId = null,
        string? mediaPath = null,
        string? subtitleFileName = null,
        int? targetSubtitleStreamIndex = null,
        int? currentSubtitleStreamIndex = null)
    {
        var itemId = session?.FullNowPlayingItem?.Id.ToString("D")
            ?? session?.NowPlayingItem?.Id.ToString("D")
            ?? string.Empty;
        return new RememberedSubtitleAutoApplyResponseDto
        {
            Status = status,
            Message = message,
            PlaybackKey = string.IsNullOrWhiteSpace(session?.Id)
                ? string.Empty
                : $"{session.Id}|{mediaPath ?? string.Empty}",
            SessionId = session?.Id ?? string.Empty,
            ItemId = itemId,
            PartId = partId ?? string.Empty,
            SubtitleFileName = subtitleFileName ?? string.Empty,
            TargetSubtitleStreamIndex = targetSubtitleStreamIndex,
            CurrentSubtitleStreamIndex = currentSubtitleStreamIndex
        };
    }
}
