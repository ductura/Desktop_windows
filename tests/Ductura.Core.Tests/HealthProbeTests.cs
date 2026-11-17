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

    private static Stub Body(HttpStatusCode code, string? json) => new((_, _) => Task.FromResult(new HttpResponseMessage(code)
    {
        Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json"),
    }));

    private static Task<HealthReading> Read(HttpMessageHandler handler, CancellationToken cancel = default) =>
        HealthProbe.ReadAsync(handler, Api, Limit, new FakeTimeProvider(), cancel);

    public static TheoryData<HttpStatusCode, string?> NotAnAnswer => new()
    {
        { HttpStatusCode.InternalServerError, """{"status":"ok","minClientVersion":"1.0.0"}""" },
        { HttpStatusCode.NotFound, null },
        { HttpStatusCode.NoContent, null },
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
    public async Task Greska_mreze_nije_odgovor() =>
        Assert.Equal(new HealthReading(false, null),
            await Read(new Stub((_, _) => throw new HttpRequestException("no route"))));

    [Fact]
    public async Task Istek_roka_po_lazom_satu_nije_odgovor()
    {
        var time = new FakeTimeProvider();
        var stub = new Stub(async (_, cancel) => { await Task.Delay(Timeout.Infinite, cancel); return new HttpResponseMessage(); });
        var reading = HealthProbe.ReadAsync(stub, Api, Limit, time, default);
        time.Advance(Limit - TimeSpan.FromMilliseconds(1));
        await Task.Yield();
        Assert.False(reading.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));
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
    public async Task Zahtjev_ide_na_health_metodom_GET(string api)
    {
        var stub = Body(HttpStatusCode.OK, """{"status":"ok"}""");
        await HealthProbe.ReadAsync(stub, new Uri(api), Limit, new FakeTimeProvider(), default);
        Assert.Equal(["GET https://api.example.test/api/v1/health"], stub.Requests);
    }
}
