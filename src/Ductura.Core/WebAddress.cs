using System.Reflection;
using System.Text.RegularExpressions;

namespace Ductura.Core;

/// <summary>
/// Provjerena adresa weba iz konfiguracije paketa (DAN-883): svojstva <c>DucturaWebUrl</c> i
/// <c>DucturaWebHosts</c> u <c>Directory.Build.props</c>, upisana u sklop kao <c>AssemblyMetadata</c>.
/// Nastaje samo kroz <see cref="TryCreate"/>, pa je svaka instanca prošla provjeru.
/// </summary>
public sealed partial class WebAddress
{
    public const string UrlKey = "DucturaWebUrl";
    public const string HostsKey = "DucturaWebHosts";

    /// <summary>Kanonska https adresa s putem koji završava s '/', bez upita i fragmenta.</summary>
    public string Url { get; }

    private WebAddress(string url) => Url = url;

    /// <summary>Adresa iz metapodataka sklopa ili null ako ključ nedostaje ili ne prolazi provjeru.</summary>
    public static WebAddress? FromAssembly(Assembly assembly)
    {
        var meta = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        string? Read(string key) => meta.LastOrDefault(a => a.Key == key)?.Value;
        return TryCreate(Read(UrlKey), Read(HostsKey), out var web) ? web : null;
    }

    /// <summary>
    /// Prolazi samo apsolutna https adresa, jednaka svom kanonskom obliku znak za znak (time i bez
    /// kontrolnih znakova, rubnog razmaka i ne-ASCII znakova),
    /// bez korisnika, porta, upita i fragmenta, s putem koji završava s '/' bez praznog segmenta,
    /// i s hostom koji je ordinalno jedan od hostova s popisa (oznake odvojene s ';').
    /// </summary>
    public static bool TryCreate(string? url, string? hosts, out WebAddress? web)
    {
        web = null;
        if (!TryParseHosts(hosts, out var allowed)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        // System.Uri trims whitespace and CR/LF, escapes control and non-ASCII characters, lowercases,
        // drops :443 and dot segments: refuse any change. A non-ASCII or IP host is never on the list.
        if (!string.Equals(uri.AbsoluteUri, url, StringComparison.Ordinal)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        if (uri.UserInfo.Length > 0 || url.IndexOf(':', "https:".Length) >= 0) return false;
        if (url.Contains('?') || url.Contains('#')) return false;
        if (!uri.AbsolutePath.EndsWith('/') || uri.AbsolutePath.Contains("//")) return false;
        if (!allowed.Contains(uri.Host, StringComparer.Ordinal)) return false;
        web = new WebAddress(url);
        return true;
    }

    // Every entry must be a lowercase LDH host name; one bad or empty entry refuses the whole list.
    private static bool TryParseHosts(string? hosts, out string[] allowed)
    {
        allowed = (hosts ?? "").Split(';');
        return allowed.All(h => HostName().IsMatch(h));
    }

    // At least two labels, the last one not all digits, so an IPv4 address is never a host name.
    [GeneratedRegex(@"\A(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?=[a-z0-9-]*[a-z])[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\z")]
    private static partial Regex HostName();
}
