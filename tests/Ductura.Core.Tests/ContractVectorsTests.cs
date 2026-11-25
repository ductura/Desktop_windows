using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ductura.Core;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Ductura.Core.Tests;

/// <summary>
/// The locked <c>@ductura/test-fakes</c> release (DAN-997): <c>test-fakes.lock.json</c> holds version, URL and sha256;
/// the tarball comes from the public GitHub release without a token and is used only when its sha256 equals the lock.
/// </summary>
public static class TestFakesRelease
{
    public const string LockFile = "test-fakes.lock.json";
    public const string VectorsPath = "package/server/contracts/generated/fake-server/vectors.json";
    public const string HealthRoute = "platform.health.get";

    public sealed record Lock(string Version, string Url, string Sha256);

    private static readonly Regex Version = new(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256 = new(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant);
    private static readonly string[] Keys = ["version", "url", "sha256"];

    public static string UrlFor(string version) =>
        $"https://github.com/ductura/Institution_server/releases/download/test-fakes-v{version}/ductura-test-fakes-{version}.tgz";

    /// <summary>Exactly the keys version, url and sha256, each once and a string; anything else is an error.</summary>
    public static Lock ParseLock(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw Bad("the root must be a JSON object");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (Array.IndexOf(Keys, p.Name) < 0) throw Bad($"unknown key \"{p.Name}\"");
            if (p.Value.ValueKind != JsonValueKind.String) throw Bad($"\"{p.Name}\" must be a string");
            if (!values.TryAdd(p.Name, p.Value.GetString()!)) throw Bad($"duplicate key \"{p.Name}\"");
        }
        foreach (var k in Keys) if (!values.ContainsKey(k)) throw Bad($"missing key \"{k}\"");
        var (version, url, sha) = (values["version"], values["url"], values["sha256"]);
        if (!Version.IsMatch(version)) throw Bad($"version \"{version}\" is not major.minor.patch");
        if (!Sha256.IsMatch(sha)) throw Bad("sha256 must be 64 lowercase hex characters");
        if (!string.Equals(url, UrlFor(version), StringComparison.Ordinal)) throw Bad($"url must be {UrlFor(version)}");
        return new Lock(version, url, sha);
    }

    public static void Verify(byte[] tarball, Lock locked)
    {
        var actual = Convert.ToHexString(SHA256.HashData(tarball)).ToLowerInvariant();
        if (actual != locked.Sha256)
            throw Bad($"sha256 of {locked.Url} is {actual}, the lock expects {locked.Sha256}");
    }

    /// <summary>The vector array from the single vectors.json file in the tarball.</summary>
    public static JsonElement ReadVectors(byte[] tarball)
    {
        using var gzip = new GZipStream(new MemoryStream(tarball), CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        byte[]? vectors = null;
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.Name != VectorsPath) continue;
            if (vectors is not null || entry.DataStream is null) throw Bad($"{VectorsPath} must be one regular file");
            using var copy = new MemoryStream();
            entry.DataStream.CopyTo(copy);
            vectors = copy.ToArray();
        }
        if (vectors is null) throw Bad($"the tarball has no {VectorsPath}");
        return JsonSerializer.Deserialize<JsonElement>(vectors);
    }

    public static IReadOnlyDictionary<string, JsonElement> VectorsForRoute(JsonElement vectors, string route)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var v in vectors.EnumerateArray())
        {
            if (v.GetProperty("route").GetString() != route) continue;
            var name = v.GetProperty("name").GetString()!;
            if (!result.TryAdd(name, v)) throw Bad($"duplicate {route} vector name: {name}");
        }
        if (result.Count == 0) throw Bad($"no {route} vector in {VectorsPath}");
        return result;
    }

    private static readonly Lazy<Task<JsonElement>> Locked = new(async () =>
    {
        var locked = ParseLock(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, LockFile)));
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var tarball = await http.GetByteArrayAsync(locked.Url);
        Verify(tarball, locked);
        return ReadVectors(tarball);
    });

    public static async Task<IReadOnlyDictionary<string, JsonElement>> LockedVectors(string route) =>
        VectorsForRoute(await Locked.Value, route);

    private static InvalidDataException Bad(string why) => new($"{LockFile}: {why}");
}

public class ContractVectorsTests
{
    // Negative cases first: a wrong lock or a wrong tarball must fail loudly, never pass with no vectors.
    private static readonly byte[] Tarball = Encoding.UTF8.GetBytes("not the release");
    private static readonly string Good = TestFakesRelease.UrlFor("3.0.1");
    private static string LockJson(string version = "3.0.1", string? url = null, string sha = "") =>
        JsonSerializer.Serialize(new { version, url = url ?? TestFakesRelease.UrlFor(version), sha256 = sha });

    [Fact]
    public void Krivi_sha256_u_locku_rusi_test_s_jasnom_porukom()
    {
        var wrong = new string('0', 64);
        var error = Assert.Throws<InvalidDataException>(() =>
            TestFakesRelease.Verify(Tarball, TestFakesRelease.ParseLock(LockJson(sha: wrong))));
        var actual = Convert.ToHexString(SHA256.HashData(Tarball)).ToLowerInvariant();
        Assert.Equal($"test-fakes.lock.json: sha256 of {Good} is {actual}, the lock expects {wrong}", error.Message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"version":"3.0.1","url":"x"}""")]
    [InlineData("""{"version":"3.0.1","url":"x","sha256":"x","extra":"x"}""")]
    [InlineData("""{"version":"3.0.1","url":"x","sha256":"x","__proto__":"x"}""")]
    [InlineData("""{"version":"3.0.1","version":"3.0.1","url":"x","sha256":"x"}""")]
    [InlineData("""{"version":301,"url":"x","sha256":"x"}""")]
    public void Lock_izvan_oblika_pada(string json) =>
        Assert.Throws<InvalidDataException>(() => TestFakesRelease.ParseLock(json));

    [Theory]
    [InlineData("3.0", null)]
    [InlineData("03.0.1", null)]
    [InlineData("3.0.1\n", null)]
    [InlineData("3.0.١", null)]
    [InlineData("3.0.1", "http://github.com/ductura/Institution_server/releases/download/test-fakes-v3.0.1/ductura-test-fakes-3.0.1.tgz")]
    [InlineData("3.0.1", "https://example.test/releases/download/test-fakes-v3.0.1/ductura-test-fakes-3.0.1.tgz")]
    [InlineData("3.0.1", "https://github.com/ductura/Institution_server/releases/download/test-fakes-v3.0.0/ductura-test-fakes-3.0.1.tgz")]
    [InlineData("3.0.1", " https://github.com/ductura/Institution_server/releases/download/test-fakes-v3.0.1/ductura-test-fakes-3.0.1.tgz")]
    [InlineData("3.0.1", "https://github.com/ductura/Institution_server/releases/download/test-fakes-v3.0.1/ductura-test-fakes-3.0.1.tgz\r\n")]
    [InlineData("3.0.1", "https://github.com/ductura/Institution_server/releases/download/test-fakes-v3.0.1/ductura-test-fakes-3.0.1.tgz\t")]
    [InlineData("3.0.1", "https://github.com/ductura/Institution_server/releases/download/test-fakes-v3.0.1/ductura-test-fakes-3.0.1.tgz ")]
    [InlineData("3.0.1", "https://github.com/ductura/Institution_server/releases/download/test-fakes-v3.0.1/ductura-test-fakes-3.0.1.tgz\u007f")]
    public void Neispravna_verzija_ili_adresa_pada(string version, string? url) =>
        Assert.Throws<InvalidDataException>(() => TestFakesRelease.ParseLock(LockJson(version, url, new string('a', 64))));

    [Theory]
    [InlineData("A")]
    [InlineData("0")]
    [InlineData("g")]
    public void Neispravan_sha256_pada(string fill) =>
        Assert.Throws<InvalidDataException>(() => TestFakesRelease.ParseLock(LockJson(sha: new string(fill[0], fill == "0" ? 63 : 64))));

    private static byte[] TarGz(string name, string content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) });
        }
        return output.ToArray();
    }

    [Fact]
    public void Tarball_bez_vektora_pada() =>
        Assert.Throws<InvalidDataException>(() => TestFakesRelease.ReadVectors(TarGz("package/README.md", "x")));

    [Fact]
    public void Tarball_bez_health_vektora_pada() =>
        Assert.Throws<InvalidDataException>(() => TestFakesRelease.VectorsForRoute(
            TestFakesRelease.ReadVectors(TarGz(TestFakesRelease.VectorsPath, """[{"route":"platform.ready.get","name":"ready"}]""")),
            TestFakesRelease.HealthRoute));

    // Expected outcome per health vector of the locked release: the reading and the verdict for clients 0.1.0 and 1.0.0.
    public static TheoryData<string, bool, string?, VersionVerdict, VersionVerdict> Expected => new()
    {
        { "alive", true, null, VersionVerdict.Current, VersionVerdict.Current },
        { "alive-with-version", true, null, VersionVerdict.Current, VersionVerdict.Current },
        { "alive-current", true, "1.0.0", VersionVerdict.UpdateRequired, VersionVerdict.Current },
        { "degraded", true, "1.0.0", VersionVerdict.UpdateRequired, VersionVerdict.Current },
        { "internal", false, null, VersionVerdict.ServerUnavailable, VersionVerdict.ServerUnavailable },
        { "too-many-requests", false, null, VersionVerdict.ServerUnavailable, VersionVerdict.ServerUnavailable },
    };

    [Fact]
    public async Task Svaki_health_vektor_izdanja_ima_ocekivani_ishod()
    {
        var names = (await TestFakesRelease.LockedVectors(TestFakesRelease.HealthRoute)).Keys.Order(StringComparer.Ordinal);
        Assert.Equal(Expected.Select(row => (string)row[0]).Order(StringComparer.Ordinal), names);
    }

    [Theory]
    [MemberData(nameof(Expected))]
    public async Task Klijent_daje_ocekivani_ishod_za_health_vektor(
        string name, bool answered, string? min, VersionVerdict at010, VersionVerdict at100)
    {
        var response = (await TestFakesRelease.LockedVectors(TestFakesRelease.HealthRoute))[name].GetProperty("response");
        var handler = new VectorHandler(response);
        var reading = await HealthProbe.ReadAsync(handler, new Uri("https://api.example.test/"), TimeSpan.FromSeconds(5), new FakeTimeProvider(), default);
        Assert.Equal(["GET https://api.example.test/api/v1/health"], handler.Requests);
        Assert.Equal(new HealthReading(answered, min), reading);
        Assert.Equal(at010, ClientVersion.Decide(reading.Answered, reading.MinClientVersion, "0.1.0"));
        Assert.Equal(at100, ClientVersion.Decide(reading.Answered, reading.MinClientVersion, "1.0.0"));
    }

    // Answers with the vector's status, content type, headers and body.
    private sealed class VectorHandler(JsonElement response) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add($"{request.Method} {request.RequestUri}");
            var message = new HttpResponseMessage((HttpStatusCode)response.GetProperty("status").GetInt32());
            if (response.TryGetProperty("body", out var body))
                message.Content = new StringContent(body.GetRawText(), Encoding.UTF8, response.GetProperty("contentType").GetString()!);
            if (response.TryGetProperty("headers", out var headers))
                foreach (var h in headers.EnumerateObject()) message.Headers.TryAddWithoutValidation(h.Name, h.Value.GetString());
            return Task.FromResult(message);
        }
    }
}
