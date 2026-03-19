using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.SubtitlesTools.Tests.Helpers;

/// <summary>
/// 允许测试精确控制 HTTP 响应。
/// </summary>
internal sealed class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    /// <summary>
    /// 初始化一个可编排响应的消息处理器。
    /// </summary>
    /// <param name="handler">请求处理委托。</param>
    public TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    /// <summary>
    /// 获取最近一次请求。
    /// </summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        return await _handler(request, cancellationToken).ConfigureAwait(false);
    }
}
