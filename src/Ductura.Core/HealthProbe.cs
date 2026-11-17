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
    /// Nije odgovor: svaka iznimka poziva (mreža, tijelo koje nije JSON), ne-2xx, vrsta sadržaja koja nije
    /// <c>application/json</c> (i bez tijela: 204, 3xx), tijelo koje nije JSON objekt, <c>status</c> različit od <c>"ok"</c>, <c>minClientVersion</c> koji nije
    /// string ni null, istek roka. Svaki 2xx s ispravnim tijelom je odgovor. Dodatni ključevi se toleriraju (budući
    /// minor ugovora smije dodati polje); kod ponovljenog ključa vrijedi zadnji, kao u JSON.parse.
    /// Otkazivanje pozivatelja uvijek baca, bez obzira na vrstu iznimke.
    /// </summary>
    public static async Task<HealthReading> ReadAsync(
        HttpMessageHandler handler, Uri apiBase, TimeSpan limit, TimeProvider time, CancellationToken cancel)
    {
        if (!apiBase.IsAbsoluteUri || apiBase.Scheme != Uri.UriSchemeHttps || apiBase.UserInfo.Length > 0
            || apiBase.Query.Length > 0 || apiBase.Fragment.Length > 0)
            throw new ArgumentException("API address must be absolute https without user, query or fragment.", nameof(apiBase));
        if (limit <= TimeSpan.Zero || limit == Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Time limit must be positive and finite.");
        cancel.ThrowIfCancellationRequested();
        using var deadline = new CancellationTokenSource(limit, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, deadline.Token);
        // The handler is the caller's (not disposed here); the given clock is the only time limit.
        using var http = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        // A given HttpClient gets no Kiota middleware: no retries and no redirects beyond the handler's own.
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: http)
        {
            BaseUrl = apiBase.GetLeftPart(UriPartial.Path).TrimEnd('/'),
        };
        var request = new DucturaApiClient(adapter).Api.V1.Health.ToGetRequestInformation();
        HealthReading reading;
        try
        {
            reading = await SendAsync(adapter, http, request, linked.Token);
        }
        catch (Exception) when (!cancel.IsCancellationRequested)
        {
            reading = NoAnswer;
        }
        catch (Exception)
        {
            throw new OperationCanceledException(cancel);
        }
        // One exit for every outcome: a caller who cancelled never gets a result, also when the handler
        // cancelled and then answered without throwing.
        cancel.ThrowIfCancellationRequested();
        return reading;
    }

    // The generated request (path, method, Accept), sent as is; status, media type and body are checked
    // here, because the generated model turns a wrong type into null and ignores the media type.
    private static async Task<HealthReading> SendAsync(
        HttpClientRequestAdapter adapter, HttpClient http, Microsoft.Kiota.Abstractions.RequestInformation request, CancellationToken token)
    {
        using var message = await adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(request, token);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode
            || !string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            return NoAnswer;
        await using var body = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: token);
        return Read(json.RootElement);
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
