using System.Text.Json;
using Ductura.Contracts;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Ductura.Core;

/// <summary>Ishod poziva <c>health</c>: je li server odgovorio po ugovoru i njegov <c>minClientVersion</c> (ulaz za <see cref="ClientVersion"/>).</summary>
public sealed record HealthReading(bool Answered, string? MinClientVersion);

/// <summary>
/// Poziv <c>GET /api/v1/health</c> pri pokretanju (DAN-944-2a) kroz generirani ugovorni klijent (DAN-958).
/// Bez ponovnih pokušaja; rok mjeri dani sat, pa se u testu ne čeka stvarno vrijeme.
/// </summary>
public static class HealthProbe
{
    /// <summary>
    /// Nije odgovor: greška mreže, ne-2xx, prazno tijelo, tijelo koje nije JSON objekt, <c>status</c> različit od
    /// <c>"ok"</c>, <c>minClientVersion</c> koji nije string ni null, istek roka. Dodatni ključevi se toleriraju
    /// (budući minor ugovora smije dodati polje). Otkazivanje pozivatelja se ne guta nego baca.
    /// </summary>
    public static async Task<HealthReading> ReadAsync(
        HttpMessageHandler handler, Uri apiBase, TimeSpan limit, TimeProvider time, CancellationToken cancel)
    {
        using var deadline = new CancellationTokenSource(limit, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, deadline.Token);
        using var http = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        // A given HttpClient gets no Kiota middleware: no retries and no redirects beyond the handler's own.
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: http)
        {
            BaseUrl = apiBase.ToString().TrimEnd('/'),
        };
        var request = new DucturaApiClient(adapter).Api.V1.Health.ToGetRequestInformation();
        try
        {
            // The generated request (path, method, Accept); the body is checked here, because the generated
            // model turns a wrong type into null and so cannot tell it from an absent field.
            await using var body = await adapter.SendPrimitiveAsync<Stream>(request, cancellationToken: linked.Token);
            if (body is null) return NoAnswer;
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: linked.Token);
            return Read(json.RootElement);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancel.IsCancellationRequested)
        {
            return NoAnswer;
        }
    }

    private static readonly HealthReading NoAnswer = new(false, null);

    private static HealthReading Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return NoAnswer;
        if (!root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String
            || status.GetString() != "ok") return NoAnswer;
        if (!root.TryGetProperty("minClientVersion", out var min) || min.ValueKind == JsonValueKind.Null)
            return new HealthReading(true, null);
        return min.ValueKind == JsonValueKind.String ? new HealthReading(true, min.GetString()) : NoAnswer;
    }
}
