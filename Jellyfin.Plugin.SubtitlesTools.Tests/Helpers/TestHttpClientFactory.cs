using System;
using System.Net.Http;

namespace Jellyfin.Plugin.SubtitlesTools.Tests.Helpers;

/// <summary>
/// 为测试返回固定的 HttpClient。
/// </summary>
internal sealed class TestHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 初始化工厂。
    /// </summary>
    /// <param name="httpClient">要返回的 HttpClient。</param>
    public TestHttpClientFactory(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public HttpClient CreateClient(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _httpClient;
    }
}
