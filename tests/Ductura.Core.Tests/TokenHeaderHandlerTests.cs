using System.Net;
using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

public class TokenHeaderHandlerTests
{
    private static readonly WebAddress Web = WebAddress.TryCreate("https://ductura.example/app/", "ductura.example", out var web)
        ? web!
        : throw new InvalidOperationException("Invalid test web address.");

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
        using var http = new HttpClient(new TokenHeaderHandler(Web, () => token, capture));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        prepare?.Invoke(request);
        using var _ = await http.SendAsync(request);
        return capture.Seen!;
    }

    [Fact]
    public async Task Https_request_carries_bearer_header_and_no_cookie()
    {
        var seen = await Send("https://ductura.example/api/v1/me", "demo-token._~+/==", r =>
        {
            r.Headers.Add("Cookie", "s=1");
            r.Headers.Add("Authorization", "Bearer old");
        });
        Assert.Equal("Bearer demo-token._~+/==", seen.Headers.Authorization!.ToString());
        Assert.False(seen.Headers.Contains("Cookie"));
    }

    [Theory]
    [InlineData("http://ductura.example/api/v1/me")]
    [InlineData("https://other.example/api/v1/me")]
    [InlineData("https://sub.ductura.example/api/v1/me")]
    [InlineData("https://ductura.example:444/api/v1/me")]
    [InlineData("https://demo@ductura.example/api/v1/me")]
    [InlineData("https://ductura.example/api/v10/me")]
    [InlineData("https://ductura.example/api/v1/../me")]
    [InlineData("https://ductura.example/app/")]
    public async Task Other_origins_and_paths_receive_no_credentials(string url)
    {
        var seen = await Send(url, "demo-token", r =>
        {
            r.Headers.Add("Cookie", "s=1");
            r.Headers.Add("Authorization", "Bearer old");
        });
        Assert.Null(seen.Headers.Authorization);
        Assert.False(seen.Headers.Contains("Cookie"));
    }

    [Fact]
    public async Task An_explicit_host_header_prevents_sending_the_token()
    {
        var seen = await Send("https://ductura.example/api/v1/me", "demo-token", r => r.Headers.Host = "other.example");
        Assert.Null(seen.Headers.Authorization);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a\r\nX: y")]
    [InlineData("a,b")]
    [InlineData("a=b")]
    [InlineData("=")]
    [InlineData("č")]
    public async Task Missing_or_malformed_token_sends_no_authorization(string? token)
    {
        var seen = await Send("https://ductura.example/api/v1/me", token, r => r.Headers.TryAddWithoutValidation("Authorization", "Bearer old"));
        Assert.Null(seen.Headers.Authorization);
    }

    [Fact]
    public void Default_transport_disables_cookies_and_redirects()
    {
        using var handler = new TokenHeaderHandler(Web, () => "demo-token");
        var transport = Assert.IsType<HttpClientHandler>(handler.InnerHandler);
        Assert.False(transport.UseCookies);
        Assert.False(transport.AllowAutoRedirect);
    }

    [Fact]
    public void Transport_with_cookies_or_redirects_enabled_is_refused()
    {
        using var cookies = new HttpClientHandler { AllowAutoRedirect = false };
        using var redirects = new SocketsHttpHandler { UseCookies = false };
        Assert.Throws<ArgumentException>(() => new TokenHeaderHandler(Web, () => "t", cookies));
        Assert.Throws<ArgumentException>(() => new TokenHeaderHandler(Web, () => "t", redirects));
    }
}
