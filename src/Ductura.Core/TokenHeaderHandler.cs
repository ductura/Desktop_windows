using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Ductura.Core;

/// <summary>
/// Nativni HTTP transport za ADR S-1E-e. Bearer token šalje samo na /api/v1 na potvrđenom web podrijetlu.
/// Token daje pozivatelj nakon prijave, a transport ne sprema kolačiće i ne slijedi preusmjeravanja.
/// </summary>
public sealed partial class TokenHeaderHandler : DelegatingHandler
{
    private readonly Uri _origin;
    private readonly Func<string?> _tokenSource;

    public TokenHeaderHandler(WebAddress web, Func<string?> tokenSource)
        : this(web, tokenSource, new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
    {
    }

    internal TokenHeaderHandler(WebAddress web, Func<string?> tokenSource, HttpMessageHandler inner) : base(inner)
    {
        if (inner is HttpClientHandler { UseCookies: true } or SocketsHttpHandler { UseCookies: true })
            throw new ArgumentException("The transport must disable automatic cookies.", nameof(inner));
        if (inner is HttpClientHandler { AllowAutoRedirect: true } or SocketsHttpHandler { AllowAutoRedirect: true })
            throw new ArgumentException("The transport must disable automatic redirects.", nameof(inner));
        _origin = new Uri(web.Url);
        _tokenSource = tokenSource;
    }

    [GeneratedRegex(@"\A[A-Za-z0-9._~+/\-]+=*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Token68();

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancel)
    {
        ApplyHeaders(request);
        return base.Send(request, cancel);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        ApplyHeaders(request);
        return base.SendAsync(request, cancel);
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        request.Headers.Remove("Cookie");
        request.Headers.Authorization = null;
        if (request.RequestUri is { IsAbsoluteUri: true } target
            && target.Scheme == _origin.Scheme && target.Host == _origin.Host && target.Port == _origin.Port
            && target.UserInfo.Length == 0 && !request.Headers.Contains("Host")
            && target.AbsolutePath.StartsWith("/api/v1/", StringComparison.Ordinal)
            && _tokenSource() is { Length: > 0 and <= 4096 } token && Token68().IsMatch(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
