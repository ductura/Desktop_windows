using System.Net;
using System.Text;
using Ductura.Core;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Ductura.Core.Tests;

// The start-up health call (DAN-944-2a): a stub handler instead of the network, a fake clock instead of real time.
public class HealthProbeTests
{
    private static readonly Uri Api = new("https://api.example.test/");
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add($"{request.Method} {request.RequestUri}");
            return answer(request, cancel);
        }
    }

    private static Stub Body(HttpStatusCode code, string? json, string type = "application/json") =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(code)
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, type),
        }));

    private static Task<HealthReading> Read(HttpMessageHandler handler, CancellationToken cancel = default) =>
        HealthProbe.ReadAsync(handler, Api, Limit, new FakeTimeProvider(), cancel);

    public static TheoryData<HttpStatusCode, string?> NotAnAnswer => new()
    {
        { HttpStatusCode.InternalServerError, """{"status":"ok","minClientVersion":"1.0.0"}""" },
        { HttpStatusCode.NotFound, null },
        { HttpStatusCode.NoContent, null },
        { HttpStatusCode.MovedPermanently, null },
        { HttpStatusCode.OK, "{\"status\":\"ok\"" },
        { HttpStatusCode.OK, "\"ok\"" },
        { HttpStatusCode.OK, null },
        { HttpStatusCode.OK, "" },
        { HttpStatusCode.OK, "not json" },
        { HttpStatusCode.OK, "[]" },
        { HttpStatusCode.OK, "null" },
        { HttpStatusCode.OK, "{}" },
        { HttpStatusCode.OK, """{"status":"down"}""" },
        { HttpStatusCode.OK, """{"status":"OK"}""" },
        { HttpStatusCode.OK, """{"status":"ok "}""" },
        { HttpStatusCode.OK, """{"minClientVersion":"1.0.0"}""" },
        { HttpStatusCode.OK, """{"status":"ok","minClientVersion":1}""" },
        { HttpStatusCode.OK, """{"status":"ok","minClientVersion":true}""" },
        { HttpStatusCode.OK, """{"status":"ok","minClientVersion":{"v":"1.0.0"}}""" },
        { HttpStatusCode.OK, """{"status":"ok","minClientVersion":["1.0.0"]}""" },
    };

    [Theory]
    [MemberData(nameof(NotAnAnswer))]
    public async Task Odgovor_izvan_ugovora_nije_odgovor(HttpStatusCode code, string? json) =>
        Assert.Equal(new HealthReading(false, null), await Read(Body(code, json)));

    [Fact]
    public async Task Html_stranica_s_kodom_200_nije_odgovor() =>
        Assert.Equal(new HealthReading(false, null),
            await Read(Body(HttpStatusCode.OK, "<html>status ok</html>", "text/html")));

    public static TheoryData<Exception> HandlerErrors => new()
    {
        new HttpRequestException("no route"), new IOException("reset"),
        new System.Security.Authentication.AuthenticationException("tls"), new TaskCanceledException("handler"),
    };

    [Theory]
    [MemberData(nameof(HandlerErrors))]
    public async Task Greska_mreze_nije_odgovor(Exception error) =>
        Assert.Equal(new HealthReading(false, null), await Read(new Stub((_, _) => throw error)));

    [Theory]
    [InlineData("http://api.example.test/")]
    [InlineData("https://user@api.example.test/")]
    [InlineData("https://api.example.test/?a=1")]
    [InlineData("https://api.example.test/#f")]
    [InlineData("file:///api/")]
    public async Task Neispravna_adresa_API_baca_bez_zahtjeva(string api)
    {
        var stub = Body(HttpStatusCode.OK, "{\"status\":\"ok\"}");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            HealthProbe.ReadAsync(stub, new Uri(api), Limit, new FakeTimeProvider(), default));
        Assert.Empty(stub.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(-1)]
    public async Task Rok_mora_biti_pozitivan_i_konacan(int ms)
    {
        var limit = ms == -1 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(ms);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            HealthProbe.ReadAsync(Body(HttpStatusCode.OK, "{}"), Api, limit, new FakeTimeProvider(), default));
    }

    [Fact]
    public async Task Vec_otkazan_pozivatelj_baca_bez_zahtjeva()
    {
        var stub = Body(HttpStatusCode.OK, "{\"status\":\"ok\"}");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Read(stub, new CancellationToken(true)));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Otkazivanje_pozivatelja_baca_i_kad_handler_baci_drugu_iznimku()
    {
        using var cancel = new CancellationTokenSource();
        var stub = new Stub((_, _) => { cancel.Cancel(); throw new HttpRequestException("closed"); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Read(stub, cancel.Token));
    }

    [Fact]
    public async Task Istek_roka_po_lazom_satu_nije_odgovor()
    {
        var time = new FakeTimeProvider();
        var fired = false;
        var stub = new Stub(async (_, cancel) =>
        {
            cancel.Register(() => fired = true);
            await Task.Delay(Timeout.Infinite, cancel);
            return new HttpResponseMessage();
        });
        var reading = HealthProbe.ReadAsync(stub, Api, Limit, time, default);
        time.Advance(Limit - TimeSpan.FromMilliseconds(1));
        Assert.False(fired);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(fired);
        // WaitAsync only guards against a hang; the limit itself is the fake clock.
        Assert.Equal(new HealthReading(false, null), await reading.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Otkazivanje_pozivatelja_baca_i_kad_je_rok_istekao()
    {
        var time = new FakeTimeProvider();
        using var cancel = new CancellationTokenSource();
        var stub = new Stub(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(); });
        var reading = HealthProbe.ReadAsync(stub, Api, Limit, time, cancel.Token);
        cancel.Cancel();
        time.Advance(Limit);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData("""{"status":"ok","minClientVersion":"1.0.0"}""", "1.0.0")]
    [InlineData("""{"status":"ok","minClientVersion":"bilo što"}""", "bilo što")]
    [InlineData("""{"status":"ok"}""", null)]
    [InlineData("""{"status":"ok","minClientVersion":null}""", null)]
    [InlineData("""{"status":"ok","state":"degraded","apiVersion":"1.18.0","minClientVersion":"1.0.0"}""", "1.0.0")]
    [InlineData("""{"status":"ok","toString":"x","__proto__":{},"minClientVersion":"1.0.0"}""", "1.0.0")]
    public async Task Ispravan_odgovor_daje_minClientVersion(string json, string? min) =>
        Assert.Equal(new HealthReading(true, min), await Read(Body(HttpStatusCode.OK, json)));

    [Theory]
    [InlineData("https://api.example.test/")]
    [InlineData("https://api.example.test")]
    [InlineData("https://api.example.test/srv/", "/srv")]
    [InlineData("https://api.example.test/srv", "/srv")]
    public async Task Zahtjev_ide_na_health_metodom_GET(string api, string path = "")
    {
        var stub = Body(HttpStatusCode.OK, """{"status":"ok"}""");
        await HealthProbe.ReadAsync(stub, new Uri(api), Limit, new FakeTimeProvider(), default);
        Assert.Equal([$"GET https://api.example.test{path}/api/v1/health"], stub.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.OK)]
    public async Task Svaki_2xx_s_ispravnim_tijelom_je_odgovor(HttpStatusCode code) =>
        Assert.Equal(new HealthReading(true, "1.0.0"),
            await Read(Body(code, """{"status":"ok","minClientVersion":"1.0.0"}""")));

    [Fact]
    public async Task Ponovljeni_kljuc_cita_zadnji_kao_JSON_parse() =>
        Assert.Equal(new HealthReading(true, null), await Read(Body(HttpStatusCode.OK, """{"status":"down","status":"ok"}""")));
}
