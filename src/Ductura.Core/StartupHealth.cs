namespace Ductura.Core;

/// <summary>
/// Provjera zdravlja servera pri pokretanju (DAN-1548): <see cref="HealthProbe"/> šalje poziv kroz
/// <see cref="TokenHeaderHandler"/> (nativni transport, ADR S-1E-e), a <see cref="ClientVersion"/> odlučuje o ishodu.
/// Program.cs samo primjenjuje <see cref="Decide"/>.
/// </summary>
public static class StartupHealth
{
    /// <summary>Rok poziva <c>health</c> pri pokretanju; nakon njega server vrijedi kao nedostupan, a web se ipak otvara.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    /// <summary>Obavijest uz otvoreni web kad server nije odgovorio po ugovoru (ne blokira, D-300).</summary>
    public const string ServerUnavailableText = "Poslužitelj Ducture trenutačno ne odgovara. Prikaz se otvara, ali neke stvari možda neće raditi.";

    /// <summary>Adresa API-ja je podrijetlo weba (shema, host, port): ugovorne rute počinju s /api/v1/.</summary>
    public static Uri ApiBase(WebAddress web) => new(new Uri(web.Url).GetLeftPart(UriPartial.Authority) + "/");

    /// <summary>
    /// Poziva <c>health</c> kroz <see cref="TokenHeaderHandler"/>. <paramref name="tokenSource"/> daje token nakon prijave
    /// (prijave u aplikaciji još nema, pa vraća null i zaglavlje se ne šalje). Otkazivanje baca; svaki drugi
    /// neuspjeh je <see cref="VersionVerdict.ServerUnavailable"/>.
    /// </summary>
    public static Task<VersionVerdict> CheckAsync(
        WebAddress web, string? clientVersion, Func<string?> tokenSource, TimeProvider time, CancellationToken cancel) =>
        CheckAsync(new TokenHeaderHandler(web, tokenSource), web, clientVersion, time, cancel);

    internal static async Task<VersionVerdict> CheckAsync(
        TokenHeaderHandler handler, WebAddress web, string? clientVersion, TimeProvider time, CancellationToken cancel)
    {
        using (handler)
        {
            var reading = await HealthProbe.ReadAsync(handler, ApiBase(web), Limit, time, cancel);
            return ClientVersion.Decide(reading.Answered, reading.MinClientVersion, clientVersion);
        }
    }

    /// <summary>
    /// Sadržaj prozora nakon provjere: zastario klijent dobiva samo poruku (web se ne otvara), inače se otvara
    /// <paramref name="url"/>, uz obavijest kad server nije odgovorio.
    /// </summary>
    public static ShellView Decide(VersionVerdict verdict, string url) => verdict switch
    {
        VersionVerdict.UpdateRequired => new ShellView(null, ClientVersion.UpdateRequiredText),
        VersionVerdict.ServerUnavailable => new ShellView(url, null, ServerUnavailableText),
        _ => new ShellView(url, null),
    };
}
