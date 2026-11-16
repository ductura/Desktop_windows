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
                    if (args[i].StartsWith(DocLinkPrefix, StringComparison.OrdinalIgnoreCase))
                        doc = DocumentIdFromLink(args[i][DocLinkPrefix.Length..]);
                    break;
            }
        }
        return new LaunchOptions(smoke, output, doc);
    }

    private const string DocLinkPrefix = "ductura://doc/";

    // One path segment after the prefix; one trailing '/' (Windows and browsers add it) is dropped.
    // A raw '/' left over means more than one segment, so there is no work id (S-1H.1-c).
    private static string? DocumentIdFromLink(string rest)
    {
        var segment = rest.EndsWith('/') ? rest[..^1] : rest;
        return segment.Contains('/') ? null : WorkId.FromPathSegment(segment);
    }
}
