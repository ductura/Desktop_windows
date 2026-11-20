using System.Net;
using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

public class TokenHeaderHandlerTests
{
    private sealed class Capture : HttpMessageHandler
    {
        public HttpRequestMessage? Seen;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Seen = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static async Task<HttpRequestMessage> Send(string url, string? token, Action<HttpRequestMessage>? prepare = null)
    {
        var capture = new Capture();
        using var http = new HttpClient(new TokenHeaderHandler(() => token, capture));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        prepare?.Invoke(request);
        using var _ = await http.SendAsync(request);
        return capture.Seen!;
    }

    [Fact]
    public async Task Https_request_carries_bearer_header_and_no_cookie()
    {
        var seen = await Send("https://ductura.org/api/v1/me", "demo-token", r => r.Headers.Add("Cookie", "s=1"));
        Assert.Equal("Bearer demo-token", seen.Headers.Authorization!.ToString());
        Assert.False(seen.Headers.Contains("Cookie"));
    }

    [Fact]
    public async Task Plain_http_never_gets_the_token()
    {
        var seen = await Send("http://ductura.org/api/v1/me", "demo-token");
        Assert.Null(seen.Headers.Authorization);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a\r\nX: y")]
    public async Task Missing_or_malformed_token_sends_no_authorization(string? token)
    {
        var seen = await Send("https://ductura.org/api/v1/me", token, r => r.Headers.TryAddWithoutValidation("Authorization", "Bearer old"));
        Assert.Null(seen.Headers.Authorization);
    }

    [Fact]
    public void Transport_with_cookies_enabled_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new TokenHeaderHandler(() => "t", new HttpClientHandler()));
        Assert.Throws<ArgumentException>(() => new TokenHeaderHandler(() => "t", new SocketsHttpHandler()));
        using var ok1 = new TokenHeaderHandler(() => "t", new HttpClientHandler { UseCookies = false });
        using var ok2 = new TokenHeaderHandler(() => "t", new SocketsHttpHandler { UseCookies = false });
    }
}
