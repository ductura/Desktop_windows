namespace Ductura.Core;

/// <summary>Argumenti pokretanja. --smoke služi funkcionalnom testu: aplikacija se pokrene, zapiše potvrdu i izađe.</summary>
public sealed record LaunchOptions(bool Smoke, string? SmokeOutput, string? OpenDocumentId)
{
    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        bool smoke = false;
        string? output = null, doc = null;
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--smoke": smoke = true; break;
                case "--smoke-out" when i + 1 < args.Count: output = args[++i]; smoke = true; break;
                default:
                    if (args[i].StartsWith("ductura://doc/", StringComparison.OrdinalIgnoreCase))
                        doc = args[i]["ductura://doc/".Length..].TrimEnd('/');
                    break;
            }
        }
        return new LaunchOptions(smoke, output, string.IsNullOrWhiteSpace(doc) ? null : doc);
    }
}
