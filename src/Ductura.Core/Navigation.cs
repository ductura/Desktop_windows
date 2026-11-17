namespace Ductura.Core;

/// <summary>Što prozor radi s navigacijom (DAN-960).</summary>
public enum NavigationDecision
{
    /// <summary>Odbij: adresa se ne otvara nigdje.</summary>
    Block,
    /// <summary>Ostaje u ljusci (WebView2).</summary>
    Shell,
    /// <summary>Otvara se u zadanom pregledniku.</summary>
    Browser,
}

/// <summary>
/// Odluka o svakoj navigaciji ljuske (DAN-960). Čista funkcija: ne čita okruženje i ne baca iznimke.
/// </summary>
public static class Navigation
{
    /// <summary>
    /// <see cref="NavigationDecision.Block"/> za praznu adresu, znak izvan ispisivog ASCII-ja (razmak, CR, LF, tab,
    /// NBSP, DEL, ne-ASCII) ili '\', adresu koja ne počinje s <c>https://</c> (među njima <c>http:</c>,
    /// <c>file:</c>, <c>javascript:</c>) i adresu s '@' u autoritetu (korisnik, i prazan).
    /// <see cref="NavigationDecision.Shell"/> samo kad je autoritet točno host s popisa (ordinalno, bez porta;
    /// unos s velikim slovima nikad ne odgovara). Svaka druga ispravna https adresa je
    /// <see cref="NavigationDecision.Browser"/>. Popis null je prazan popis.
    /// </summary>
    public static NavigationDecision Decide(string? url, IReadOnlyCollection<string>? shellHosts)
    {
        // System.Uri and the browser's URL parser may disagree on unusual input: refuse it before parsing.
        if (string.IsNullOrEmpty(url) || url.Any(c => c is < '!' or > '~' or '\\')) return NavigationDecision.Block;
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return NavigationDecision.Block;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return NavigationDecision.Block;
        // Raw authority, not Uri.UserInfo: "https://@host/" has an empty UserInfo.
        var rest = url["https://".Length..];
        var end = rest.IndexOfAny(['/', '?', '#']);
        var authority = end < 0 ? rest : rest[..end];
        if (authority.Contains('@')) return NavigationDecision.Block;
        return (shellHosts ?? []).Contains(authority, StringComparer.Ordinal)
            ? NavigationDecision.Shell
            : NavigationDecision.Browser;
    }
}
