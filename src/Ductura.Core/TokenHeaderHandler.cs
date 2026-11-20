using System.Net.Http.Headers;

namespace Ductura.Core;

/// <summary>
/// Prijava tokom u zaglavlju (ADR S-1E-e): svaki zahtjev nosi <c>Authorization: Bearer</c>, nikad kolačić.
/// Token daje pozivatelj (nakon prijave kroz AAI@EduHr); ovdje se ne sprema ni ne piše u zapis.
/// Zahtjev bez https-a ne dobiva token, a postojeće zaglavlje <c>Authorization</c> i <c>Cookie</c> se uklanjaju.
/// </summary>
public sealed class TokenHeaderHandler : DelegatingHandler
{
    private readonly Func<string?> tokenSource;

    /// <summary>Unutarnji transport s uključenim <c>UseCookies</c> se odbija: sam bi dodavao kolačić nakon ovog rukovatelja.</summary>
    public TokenHeaderHandler(Func<string?> tokenSource, HttpMessageHandler inner) : base(inner)
    {
        if (inner is HttpClientHandler { UseCookies: true } or SocketsHttpHandler { UseCookies: true })
            throw new ArgumentException("The transport must have UseCookies = false (no cookies, ADR S-1E-e).", nameof(inner));
        this.tokenSource = tokenSource;
    }

    /// <summary>Token je neprazan, bez razmaka i kontrolnih znakova (jedan HTTP token68 niz).</summary>
    public static bool IsValidToken(string? token) =>
        !string.IsNullOrEmpty(token) && token.Length <= 4096
        && token.All(c => c is > ' ' and < '\u007f');

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        request.Headers.Remove("Cookie");
        request.Headers.Authorization = null;
        if (request.RequestUri is { Scheme: "https" } && tokenSource() is { } token && IsValidToken(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, cancel);
    }
}
