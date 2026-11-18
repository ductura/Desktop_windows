using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

/// <summary>
/// Work id vectors (DAN-1000) of the <c>@ductura/contracts</c> release locked in <c>contract-lock.json</c>: the lock holds
/// version and sha256, the URL follows from the version as in <c>scripts/contract-client.mjs</c>, and the tarball is used
/// only when its sha256 equals the lock. Every vector goes through <see cref="LaunchOptions.Parse"/>.
/// </summary>
public static class ContractsRelease
{
    public const string LockFile = "contract-lock.json";
    public const string VectorsPath = "package/vectors/work-id.json";
    public const string Format = "ductura-work-id-vectors-v1";

    public sealed record Lock(string Version, string Sha256);

    /// <summary>One vector; <c>Input</c> is null when the JSON input is not a string (<c>IsString</c> false).</summary>
    public sealed record Vector(string Name, string Check, bool IsString, string? Input, bool Accepted, string? Id);

    private static readonly Regex Version = new(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256 = new(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant);

    public static string UrlFor(string version) =>
        $"https://github.com/ductura/Institution_server/releases/download/contracts-v{version}/ductura-contracts-{version}.tgz";

    /// <summary>Exactly the string keys version and sha256, each once.</summary>
    public static Lock ParseLock(string json)
    {
        var o = Fields(JsonDocument.Parse(json).RootElement, LockFile, ["version", "sha256"], ["version", "sha256"]);
        var version = Text(o["version"], "version");
        var sha = Text(o["sha256"], "sha256");
        if (!Version.IsMatch(version)) throw Bad($"version \"{version}\" is not major.minor.patch");
        if (!Sha256.IsMatch(sha)) throw Bad("sha256 must be 64 lowercase hex characters");
        return new Lock(version, sha);
    }

    public static void Verify(byte[] tarball, Lock locked)
    {
        var actual = Convert.ToHexString(SHA256.HashData(tarball)).ToLowerInvariant();
        if (actual != locked.Sha256) throw Bad($"sha256 of {UrlFor(locked.Version)} is {actual}, the lock expects {locked.Sha256}");
    }

    public static byte[] VectorsFile(byte[] tarball)
    {
        using var tar = new TarReader(new GZipStream(new MemoryStream(tarball), CompressionMode.Decompress));
        byte[]? found = null;
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.Name != VectorsPath) continue;
            if (found is not null || entry.DataStream is null) throw Bad($"{VectorsPath} must be one regular file");
            using var copy = new MemoryStream();
            entry.DataStream.CopyTo(copy);
            found = copy.ToArray();
        }
        return found ?? throw Bad($"the tarball has no {VectorsPath}");
    }

    /// <summary>Strict reader: unknown, missing or duplicate keys, wrong types and duplicate names are errors.</summary>
    public static IReadOnlyList<Vector> ParseVectors(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = Fields(doc.RootElement, "root", ["format", "vectors"], ["format", "vectors"]);
        if (Text(root["format"], "format") != Format) throw Bad($"format must be {Format}");
        if (root["vectors"].ValueKind != JsonValueKind.Array || root["vectors"].GetArrayLength() == 0) throw Bad("vectors must be a non-empty array");
        var result = new List<Vector>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in root["vectors"].EnumerateArray())
        {
            var v = Fields(e, "vector", ["name", "check", "input", "repeat", "accepted", "id"], ["name", "check", "accepted"]);
            var name = Text(v["name"], "name");
            if (!names.Add(name)) throw Bad($"duplicate vector name \"{name}\"");
            var check = Text(v["check"], "check");
            if (check is not ("isWorkId" or "workIdFromPath")) throw Bad($"{name}: unknown check \"{check}\"");
            if (v["accepted"].ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Bad($"{name}: accepted must be a boolean");
            bool accepted = v["accepted"].GetBoolean();
            if (v.ContainsKey("input") == v.ContainsKey("repeat")) throw Bad($"{name}: exactly one of input and repeat");
            string? input;
            if (v.TryGetValue("repeat", out var r))
            {
                var rep = Fields(r, "repeat", ["text", "count"], ["text", "count"]);
                if (!rep["count"].TryGetInt32(out var count) || count is < 1 or > 1024) throw Bad($"{name}: count must be an integer 1 to 1024");
                input = string.Concat(Enumerable.Repeat(Text(rep["text"], "text"), count));
            }
            else input = v["input"].ValueKind == JsonValueKind.String ? JsonString(v["input"]) : null;
            bool needsId = accepted && check == "workIdFromPath";
            if (v.ContainsKey("id") != needsId) throw Bad($"{name}: id belongs to an accepted workIdFromPath vector only");
            result.Add(new Vector(name, check, input is not null, input, accepted, needsId ? Text(v["id"], "id") : null));
        }
        return result;
    }

    /// <summary>
    /// The vectors whose outcome through <see cref="LaunchOptions.Parse"/> differs. An isWorkId input goes in with
    /// % and / escaped, so the parser decodes it back to the input. A launch argument is always a string, so a
    /// non-string input must be refused.
    /// </summary>
    public static IReadOnlyList<string> Mismatches(IEnumerable<Vector> vectors)
    {
        var problems = new List<string>();
        foreach (var v in vectors)
        {
            if (!v.IsString) { if (v.Accepted) problems.Add($"{v.Name}: a non-string input can never be accepted"); continue; }
            var segment = v.Check == "isWorkId" ? v.Input!.Replace("%", "%25").Replace("/", "%2F") : v.Input!;
            var expected = !v.Accepted ? null : v.Check == "isWorkId" ? v.Input : v.Id;
            var actual = LaunchOptions.Parse(["ductura://doc/" + segment]).OpenDocumentId;
            if (!string.Equals(expected, actual, StringComparison.Ordinal)) problems.Add($"{v.Name}: expected {Show(expected)}, got {Show(actual)}");
        }
        return problems;
    }

    /// <summary>A JSON string read from its raw text, because GetString() throws on a lone surrogate (\ud800).</summary>
    public static string JsonString(JsonElement e)
    {
        var raw = e.GetRawText();
        var s = new StringBuilder(raw.Length);
        for (int i = 1; i < raw.Length - 1; i++)
        {
            if (raw[i] != '\\') { s.Append(raw[i]); continue; }
            char c = raw[++i];
            s.Append(c switch
            {
                'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t',
                'u' => (char)Convert.ToUInt16(raw.Substring(i + 1, 4), 16),
                _ => c, // " \ / (JsonDocument already refused any other escape)
            });
            if (c == 'u') i += 4;
        }
        return s.ToString();
    }

    private static readonly Lazy<Task<IReadOnlyList<Vector>>> Locked = new(async () =>
    {
        var locked = ParseLock(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, LockFile)));
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var tarball = await http.GetByteArrayAsync(UrlFor(locked.Version));
        Verify(tarball, locked);
        return ParseVectors(Encoding.UTF8.GetString(VectorsFile(tarball)));
    });

    public static Task<IReadOnlyList<Vector>> LockedVectors() => Locked.Value;

    private static Dictionary<string, JsonElement> Fields(JsonElement e, string what, string[] allowed, string[] required)
    {
        if (e.ValueKind != JsonValueKind.Object) throw Bad($"{what} must be a JSON object");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in e.EnumerateObject())
        {
            if (Array.IndexOf(allowed, p.Name) < 0) throw Bad($"{what}: unknown key \"{p.Name}\"");
            if (!fields.TryAdd(p.Name, p.Value.Clone())) throw Bad($"{what}: duplicate key \"{p.Name}\"");
        }
        foreach (var k in required) if (!fields.ContainsKey(k)) throw Bad($"{what}: missing key \"{k}\"");
        return fields;
    }

    private static string Text(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.String ? JsonString(e) : throw Bad($"\"{key}\" must be a string");

    private static string Show(string? s) => s is null ? "null" :
        "\"" + string.Concat(s.Select(c => c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:x4}")) + "\"";

    private static InvalidDataException Bad(string why) => new($"work id vectors: {why}");
}

public class WorkIdVectorsTests
{
    // Negative cases first: a wrong lock, tarball or file must fail loudly, never pass with fewer vectors.
    [Theory]
    [InlineData("[]")]
    [InlineData("""{"version":"2.5.0"}""")]
    [InlineData("""{"version":"2.5.0","sha256":"SHA","url":"x"}""")]
    [InlineData("""{"version":"2.5.0","sha256":"SHA","__proto__":"x"}""")]
    [InlineData("""{"version":"2.5.0","sha256":"SHA","toString":"x"}""")]
    [InlineData("""{"version":"2.5.0","version":"2.5.0","sha256":"SHA"}""")]
    [InlineData("""{"version":250,"sha256":"SHA"}""")]
    [InlineData("""{"version":"2.5","sha256":"SHA"}""")]
    [InlineData("""{"version":"02.5.0","sha256":"SHA"}""")]
    [InlineData("""{"version":"2.5.0\n","sha256":"SHA"}""")]
    [InlineData("""{"version":"2.5.0\r\n","sha256":"SHA"}""")]
    [InlineData("""{"version":"2.5.0","sha256":"SHA\t"}""")]
    [InlineData("""{"version":"2.5.0","sha256":"SHA "}""")]
    [InlineData("""{"version":"2.5.0","sha256":"SHA\u007f"}""")]
    [InlineData("""{"version":"2.5.0","sha256":"ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789"}""")]
    [InlineData("""{"version":"2.5.0","sha256":"0"}""")]
    public void Lock_izvan_oblika_pada(string json) =>
        Assert.Throws<InvalidDataException>(() => ContractsRelease.ParseLock(json.Replace("SHA", new string('a', 64))));

    [Fact]
    public void Krivi_sha256_rusi_test_s_jasnom_porukom()
    {
        var tarball = Encoding.UTF8.GetBytes("not the release");
        var wrong = new string('0', 64);
        var error = Assert.Throws<InvalidDataException>(() => ContractsRelease.Verify(tarball, new("2.5.0", wrong)));
        var actual = Convert.ToHexString(SHA256.HashData(tarball)).ToLowerInvariant();
        Assert.Equal($"work id vectors: sha256 of {ContractsRelease.UrlFor("2.5.0")} is {actual}, the lock expects {wrong}", error.Message);
    }

    [Fact]
    public void Tarball_bez_vektora_pada()
    {
        using var output = new MemoryStream();
        using (var tar = new TarWriter(new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true)))
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "package/README.md") { DataStream = new MemoryStream([1]) });
        Assert.Throws<InvalidDataException>(() => ContractsRelease.VectorsFile(output.ToArray()));
    }

    private const string Ok = """{"name":"n","check":"isWorkId","input":"doc","accepted":true}""";
    private static string Doc(string vectors) => $$"""{"format":"{{ContractsRelease.Format}}","vectors":[{{vectors}}]}""";

    [Theory]
    [InlineData("""{"format":"ductura-work-id-vectors-v1","vectors":[],"x":1}""")]
    [InlineData("""{"format":"ductura-work-id-vectors-v2","vectors":[{"name":"n","check":"isWorkId","input":"doc","accepted":true}]}""")]
    [InlineData("""{"format":"ductura-work-id-vectors-v1","vectors":[]}""")]
    [InlineData("""{"format":"ductura-work-id-vectors-v1","vectors":{}}""")]
    public void Datoteka_izvan_oblika_pada(string json) =>
        Assert.Throws<InvalidDataException>(() => ContractsRelease.ParseVectors(json));

    [Theory]
    [InlineData("""{"name":"n","check":"isWorkId","input":"doc","accepted":true,"extra":1}""")]
    [InlineData("""{"name":"n","check":"isWorkId","input":"doc","accepted":true,"__proto__":1}""")]
    [InlineData("""{"name":"n","check":"isWorkId","input":"doc","accepted":true,"toString":1}""")]
    [InlineData("""{"name":"n","check":"isWorkId","input":"doc"}""")]
    [InlineData("""{"name":"n","check":"isWorkId","input":"doc","accepted":"true"}""")]
    [InlineData("""{"name":"n","check":"isWorkId","accepted":true}""")]
    [InlineData("""{"name":"n","check":"isWorkId","input":"d","repeat":{"text":"d","count":2},"accepted":true}""")]
    [InlineData("""{"name":"n","check":"isWorkId","repeat":{"text":"d","count":0},"accepted":true}""")]
    [InlineData("""{"name":"n","check":"isWorkId","repeat":{"text":"d","count":1025},"accepted":true}""")]
    [InlineData("""{"name":"n","check":"isWorkId","repeat":{"text":"d","count":1.5},"accepted":true}""")]
    [InlineData("""{"name":"n","check":"isWorkId","repeat":{"text":"d","count":2,"x":1},"accepted":true}""")]
    [InlineData("""{"name":"n","check":"isworkid","input":"doc","accepted":true}""")]
    [InlineData("""{"name":7,"check":"isWorkId","input":"doc","accepted":true}""")]
    [InlineData("""{"name":"n","check":"isWorkId","input":"doc","accepted":true,"id":"doc"}""")]
    [InlineData("""{"name":"n","check":"workIdFromPath","input":"doc","accepted":true}""")]
    [InlineData("""{"name":"n","check":"workIdFromPath","input":"%00","accepted":false,"id":"x"}""")]
    [InlineData("""{"name":"n","check":"isWorkId","input":"doc","accepted":true}""" + "," + Ok)]
    public void Vektor_izvan_oblika_pada(string vectors) =>
        Assert.Throws<InvalidDataException>(() => ContractsRelease.ParseVectors(Doc(vectors)));

    [Fact]
    public void Citac_cuva_usamljeni_surogat_i_escapeove()
    {
        Assert.Single(ContractsRelease.ParseVectors(Doc(Ok))); // the negative cases above fail for their own reason
        var v = ContractsRelease.ParseVectors(Doc("""{"name":"n","check":"isWorkId","input":"\ud800|\udc00|😀|\t\r\n \u007f\/\"\\","accepted":false}"""))[0];
        Assert.Equal("\uD800|\uDC00|😀|\t\r\n \u007F/\"\\", v.Input);
        Assert.Equal(new string('\uD800', 3), ContractsRelease.ParseVectors(Doc("""{"name":"n","check":"isWorkId","repeat":{"text":"\ud800","count":3},"accepted":false}"""))[0].Input);
    }

    // Count and sha256 of the sorted names of the locked release: a vector added, removed or renamed fails the test.
    private const int Count = 48;
    private const string NamesSha256 = "879ab64f7bfaf679886d6a2949a344270927477ddf39c1007ff58546b7c8c50b";

    [Fact]
    public async Task Svaki_vektor_ID_a_rada_prolazi_kroz_LaunchOptions()
    {
        var vectors = await ContractsRelease.LockedVectors();
        Assert.Empty(ContractsRelease.Mismatches(vectors));
        var names = string.Join('\n', vectors.Select(v => v.Name).Order(StringComparer.Ordinal));
        Assert.Equal((Count, NamesSha256), (vectors.Count, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(names))).ToLowerInvariant()));
        foreach (var check in new[] { "isWorkId", "workIdFromPath" })
        {
            Assert.Contains(vectors, v => v.Check == check && v.Accepted);
            Assert.Contains(vectors, v => v.Check == check && !v.Accepted && v.IsString);
        }
    }

    [Fact]
    public async Task Promijenjen_vektor_rusi_provjeru()
    {
        foreach (var v in await ContractsRelease.LockedVectors())
        {
            bool path = v.Check == "workIdFromPath";
            var flipped = v with { Accepted = !v.Accepted, Id = path && !v.Accepted ? "izmijenjeni-id" : null };
            Assert.Single(ContractsRelease.Mismatches([flipped]));
            if (path && v.Accepted) Assert.Single(ContractsRelease.Mismatches([v with { Id = "izmijenjeni-id" }]));
        }
    }
}
