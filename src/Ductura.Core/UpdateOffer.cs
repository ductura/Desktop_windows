using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ductura.Core;

/// <summary>Paket koji se nudi: verzija, adresa (doslovno iz manifesta; relativna se ne razrješava) i SHA-256.</summary>
public sealed record PackageOffer(string Version, string Url, string Sha256);

/// <summary>
/// Ponuda novije verzije iz manifesta preuzimanja (DAN-1072, ruta <c>downloads.manifest.get</c>, S-1H.5).
/// Čista funkcija bez mreže; preuzimanje, provjera paketa i prikaz su zasebni koraci.
/// </summary>
public static partial class UpdateOffer
{
    private static readonly string[] Keys = ["system", "architecture", "version", "url", "sha256"];

    /// <summary>
    /// Redak <c>windows</c> za <paramref name="architecture"/> kad je njegova verzija novija od instalirane
    /// (<see cref="Packaging.IsNewer"/>); inače null. Status različit od 200 i tijelo izvan ugovora (višak, manjak ili dupli
    /// ključ, krivi tip, vrijednost izvan uzorka, par sustav/arhitektura dvaput) ne nude ništa, ma u kojem retku bila greška.
    /// Instalirana verzija mora biti strogi major.minor.patch, jer <c>Version.TryParse</c> prima i "1.0" i " 1.0.0".
    /// </summary>
    public static PackageOffer? Decide(int status, string? body, string? installedVersion, string architecture)
    {
        if (architecture is not ("x64" or "arm64"))
            throw new ArgumentOutOfRangeException(nameof(architecture), architecture, "Podržani su x64 i arm64.");
        if (status != 200 || body is null || installedVersion is null || !VersionPattern().IsMatch(installedVersion)) return null;
        List<PackageOffer>? rows;
        try { rows = Read(body, architecture); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or RegexMatchTimeoutException) { return null; }
        return rows is [var row] && Packaging.IsNewer(row.Version, installedVersion) ? row : null;
    }

    // Windows rows for the architecture, or null when the body is outside the contract. Keys are walked with TryAdd
    // because TryGetProperty silently takes one of two duplicates. Four rows at most follows from the unique pairs.
    private static List<PackageOffer>? Read(string body, string architecture)
    {
        using var doc = JsonDocument.Parse(body);
        if (Fields(doc.RootElement, ["packages"]) is not { } root || root["packages"].ValueKind != JsonValueKind.Array) return null;
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<PackageOffer>();
        foreach (var item in root["packages"].EnumerateArray())
        {
            if (Fields(item, Keys) is not { } f || f.Values.Any(v => v.ValueKind != JsonValueKind.String)) return null;
            var (system, arch, version, url, sha) = (f["system"].GetString()!, f["architecture"].GetString()!,
                f["version"].GetString()!, f["url"].GetString()!, f["sha256"].GetString()!);
            if (system is not ("windows" or "macos") || arch is not ("x64" or "arm64") || !pairs.Add($"{system}/{arch}")) return null;
            if (!VersionPattern().IsMatch(version) || url.Length > 2048 || !UrlPattern().IsMatch(url) || !Sha256Pattern().IsMatch(sha)) return null;
            if (system == "windows" && arch == architecture) result.Add(new PackageOffer(version, url, sha));
        }
        return result;
    }

    private static Dictionary<string, JsonElement>? Fields(JsonElement element, string[] keys)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in element.EnumerateObject())
            if (Array.IndexOf(keys, p.Name) < 0 || !fields.TryAdd(p.Name, p.Value)) return null;
        return fields.Count == keys.Length ? fields : null;
    }

    // Patterns of openapi/v1.json; [0-9] instead of \d (non-ASCII digits in .NET) and \z instead of $.
    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    // Verbatim from the contract (ductura/server 6f7a594, downloads.manifest.get); (?![\s\S]) is the end anchor.
    [GeneratedRegex(@"^(?![\s\S]*(?:%2[eE]|%00|/\.\.?(?:[/?]|(?![\s\S]))))(?:https://(?=[a-z0-9.-]{1,253}/)(?!(?:[a-z0-9.-]*\.)?(?:localhost|local|internal|home\.arpa)/)(?![a-z0-9]{2}--)[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.(?![a-z0-9]{2}--)[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*\.(?![a-z0-9]{2}--)[a-z](?:[a-z0-9-]{0,61}[a-z0-9])?)?/(?![/?])(?:[A-Za-z0-9._~!$&'()*+,;=:@/?-]|%[0-9A-Fa-f]{2})+(?![\s\S])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UrlPattern();
}
