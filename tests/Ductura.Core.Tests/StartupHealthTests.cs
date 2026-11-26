using System.Net;
using System.Text;
using Ductura.Core;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Ductura.Core.Tests;

// Start-up health call through TokenHeaderHandler (DAN-1548): a stub transport, no network.
public class StartupHealthTests
{
    private static readonly WebAddress Web = WebAddress.TryCreate("https://ductura.example/app/", "ductura.example", out var web)
        ? web!
        : throw new InvalidOperationException("Invalid test web address.");

    private sealed class Stub(HttpStatusCode code, string? json) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Seen { get; } = [];
        public string? Authorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Seen.Add(request);
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(code)
            {
                Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static Task<VersionVerdict> Check(Stub stub, string? token, string version = "1.2.0") =>
        StartupHealth.CheckAsync(new TokenHeaderHandler(Web, () => token, stub), Web, version, new FakeTimeProvider(), default);

    [Fact]
    public async Task Health_request_goes_to_api_v1_health_on_the_web_origin_with_the_bearer_header()
    {
        var stub = new Stub(HttpStatusCode.OK, """{"status":"ok","minClientVersion":"1.0.0"}""");
        Assert.Equal(VersionVerdict.Current, await Check(stub, "demo-token"));
        var request = Assert.Single(stub.Seen);
        Assert.Equal("https://ductura.example/api/v1/health", request.RequestUri!.ToString());
        Assert.Equal("Bearer demo-token", stub.Authorization);
    }

    [Fact]
    public async Task Without_a_token_no_authorization_header_is_sent()
    {
        var stub = new Stub(HttpStatusCode.OK, """{"status":"ok"}""");
        Assert.Equal(VersionVerdict.Current, await Check(stub, null));
        Assert.Null(stub.Authorization);
    }

    [Fact]
    public async Task Old_client_must_update()
    {
        var stub = new Stub(HttpStatusCode.OK, """{"status":"ok","minClientVersion":"2.0.0"}""");
        Assert.Equal(VersionVerdict.UpdateRequired, await Check(stub, "t"));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{"status":"ok"}""")]
    [InlineData(HttpStatusCode.OK, """{"status":"down"}""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task Bad_answer_means_server_unavailable(HttpStatusCode code, string body)
    {
        Assert.Equal(VersionVerdict.ServerUnavailable, await Check(new Stub(code, body), "t"));
    }

    [Fact]
    public async Task Transport_failure_means_server_unavailable()
    {
        var failing = new Failing();
        var verdict = await StartupHealth.CheckAsync(
            new TokenHeaderHandler(Web, () => "t", failing), Web, "1.0.0", new FakeTimeProvider(), default);
        Assert.Equal(VersionVerdict.ServerUnavailable, verdict);
    }

    private sealed class Failing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) =>
            throw new HttpRequestException("down");
    }

    [Fact]
    public async Task Cancellation_throws()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StartupHealth.CheckAsync(
            new TokenHeaderHandler(Web, () => "t", new Stub(HttpStatusCode.OK, "{}")), Web, "1.0.0", new FakeTimeProvider(), cancel.Token));
    }

    [Fact]
    public void Api_base_is_the_origin_without_the_app_path()
    {
        Assert.Equal("https://ductura.example/", StartupHealth.ApiBase(Web).ToString());
    }

    [Fact]
    public void Update_required_shows_only_the_croatian_message()
    {
        var view = StartupHealth.Decide(VersionVerdict.UpdateRequired, "https://ductura.example/app/");
        Assert.Null(view.WebViewUrl);
        Assert.Equal(ClientVersion.UpdateRequiredText, view.Message);
    }

    [Fact]
    public void Unavailable_server_opens_the_web_with_a_notice()
    {
        var view = StartupHealth.Decide(VersionVerdict.ServerUnavailable, "https://ductura.example/app/");
        Assert.Equal("https://ductura.example/app/", view.WebViewUrl);
        Assert.Null(view.Message);
        Assert.Equal(StartupHealth.ServerUnavailableText, view.Notice);
    }

    [Fact]
    public void Current_opens_the_web_without_a_notice()
    {
        var view = StartupHealth.Decide(VersionVerdict.Current, "https://ductura.example/app/");
        Assert.Equal("https://ductura.example/app/", view.WebViewUrl);
        Assert.Null(view.Notice);
    }
}
