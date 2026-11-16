using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

// The web address comes from package configuration (DAN-883); the check is
// explicit per rule, because System.Uri alone trims, lowercases and keeps ports.
public class WebAddressTests
{
    private const string Hosts = "ductura.org";

    private static bool Ok(string? url, string? hosts = Hosts) => WebAddress.TryCreate(url, hosts, out _);

    [Theory]
    // 1 empty
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    // 2 scheme is not https, relative, Windows paths
    [InlineData("http://ductura.org/app/")]
    [InlineData("file:///C:/app/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://ductura.org/app/")]
    [InlineData("ws://ductura.org/app/")]
    [InlineData("wss://ductura.org/app/")]
    [InlineData("/app/")]
    [InlineData("ductura.org/app/")]
    [InlineData(@"C:\app\")]
    [InlineData(@"\\server\share\")]
    // 3 host outside the list
    [InlineData("https://evil.org/app/")]
    [InlineData("https://x.ductura.org/app/")]
    [InlineData("https://ductura.org.evil.com/app/")]
    [InlineData("https://ductura.org./app/")]
    [InlineData("https://127.0.0.1/app/")]
    [InlineData("https://0x7f.1/app/")]
    [InlineData("https://[::1]/app/")]
    [InlineData("https://user@ductura.org/app/")]
    [InlineData("https://ductura.org@evil.com/app/")]
    [InlineData("https://toString/app/")]
    [InlineData("https://tostring/app/")]
    // 4 not canonical or unsafe form
    [InlineData("HTTPS://ductura.org/app/")]
    [InlineData("https://DUCTURA.org/app/")]
    [InlineData("https://ductura.org:443/app/")]
    [InlineData("https://ductura.org:8443/app/")]
    [InlineData("https://ductura.org:/app/")]
    [InlineData(@"https://ductura.org\app\")]
    [InlineData("https://ductura.org/a/../app/")]
    [InlineData("https://ductura.org/%2e%2e/app/")]
    [InlineData("https://ductura.org//app/")]
    [InlineData("https://ductura.org/app/?")]
    [InlineData("https://ductura.org/app/?x=1")]
    [InlineData("https://ductura.org/app/#")]
    [InlineData("https://ductura.org/app/#/doc/x")]
    [InlineData("https://ductura.org/app")]
    // 5 control and edge characters
    [InlineData(" https://ductura.org/app/")]
    [InlineData("https://ductura.org/app/ ")]
    [InlineData("https://ductura.org/app/\r")]
    [InlineData("https://ductura.org/app/\n")]
    [InlineData("https://ductura.org/app/\r\n")]
    [InlineData("https://ductura.org/app/\t")]
    [InlineData("https://ductura.org/a\tpp/")]
    [InlineData("https://ductura.org/a\u007fpp/")]
    public void Odbija_adresu(string? url) => Assert.False(Ok(url));

    // Characters xUnit may not carry through InlineData.
    [Fact]
    public void Odbija_NBSP_NUL_i_ne_ASCII_host()
    {
        foreach (var url in new[]
        {
            "https://ductura.org/a\u00A0pp/", "\u00A0https://ductura.org/app/", "https://ductura.org/app/\0",
            "https://d\u00FActura.org/app/", "https://ductura.org\u3002org/app/", "https://ductura.org/app\uD800/",
        })
            Assert.False(Ok(url), url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(";")]
    [InlineData("ductura.org;")]
    [InlineData(";ductura.org")]
    [InlineData("a.test;;ductura.org")]
    public void Odbija_prazan_popis_ili_prazan_unos(string? hosts) => Assert.False(Ok("https://ductura.org/app/", hosts));

    // One bad entry next to the right host refuses the whole list (fail closed).
    [Theory]
    [InlineData("Ductura.org")]
    [InlineData(" ductura.org")]
    [InlineData("ductura.org ")]
    [InlineData("ductura .org")]
    [InlineData("ductura.org.")]
    [InlineData(".ductura.org")]
    [InlineData("ductura..org")]
    [InlineData("ductura.org:443")]
    [InlineData("127.0.0.1")]
    [InlineData("-ductura.org")]
    [InlineData("ductura-.org")]
    [InlineData("*.ductura.org")]
    [InlineData("ductura.org\r\n")]
    [InlineData("ductura.org\t")]
    [InlineData("ductura.org\u00A0")]
    [InlineData("ductura.org\u007f")]
    public void Odbija_popis_s_neispravnim_unosom(string entry) => Assert.False(Ok("https://ductura.org/app/", $"ductura.org;{entry}"));

    // An IP or non-ASCII host never passes, even when the same text is on the list.
    [Theory]
    [InlineData("https://127.0.0.1/app/", "127.0.0.1")]
    [InlineData("https://0x7f.1/app/", "0x7f.1")]
    [InlineData("https://[::1]/app/", "[::1]")]
    [InlineData("https://d\u00FActura.org/app/", "d\u00FActura.org")]
    public void Odbija_IP_i_ne_ASCII_host_i_kad_je_na_popisu(string url, string hosts) => Assert.False(Ok(url, hosts));

    [Theory]
    [InlineData("https://ductura.org/app/", "ductura.org")]
    [InlineData("https://ductura.org/", "ductura.org")]
    [InlineData("https://ductura.org/app/", "a.test;ductura.org")]
    [InlineData("https://a.test/app/", "a.test;ductura.org")]
    [InlineData("https://web-1.a.test/x/y/", "web-1.a.test")]
    public void Prihvaca_ispravnu_adresu_s_popisa(string url, string hosts)
    {
        Assert.True(WebAddress.TryCreate(url, hosts, out var web));
        Assert.Equal(url, web!.Url);
    }

    [Fact]
    public void Isporucena_konfiguracija_daje_put_app()
    {
        var web = WebAddress.FromAssembly(typeof(WebAddress).Assembly);
        Assert.NotNull(web);
        Assert.Equal("/app/", new Uri(web!.Url).AbsolutePath);
        Assert.Equal("https", new Uri(web.Url).Scheme);
    }

    [Fact]
    public void Sklop_bez_konfiguracije_nema_adresu() =>
        Assert.Null(WebAddress.FromAssembly(typeof(FactAttribute).Assembly));
}
