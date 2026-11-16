using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

// What the app does with the package address (DAN-883): Program.cs only applies these results.
public class StartupTests
{
    private static WebAddress? Web(string url) => WebAddress.TryCreate(url, "ductura.org", out var web) ? web : null;

    [Theory]
    [InlineData("http://ductura.org/app/")]
    [InlineData("https://evil.org/app/")]
    [InlineData("")]
    public void Smoke_s_neispravnom_adresom_vraca_1_bez_potvrde(string url)
    {
        var web = Web(url);
        Assert.Null(web);
        var (exitCode, line) = Startup.Smoke(web, "0.1.0");
        Assert.Equal(1, exitCode);
        Assert.Null(line);
    }

    [Theory]
    [InlineData("http://ductura.org/app/")]
    [InlineData("https://evil.org/app/")]
    public void Neispravna_adresa_ne_daje_poveznicu_nego_poruku(string url)
    {
        var options = LaunchOptions.Parse(["ductura://doc/demo-1"]);
        Assert.Null(Startup.LinkUrl(options, Web(url)));
        Assert.Equal("Adresa Ducture u ovom paketu nije ispravna.", Startup.InvalidAddressText);
    }

    [Fact]
    public void Ispravna_adresa_daje_potvrdu_i_poveznicu_na_rad()
    {
        var web = Web("https://ductura.org/app/");
        Assert.Equal((0, "Ductura OK 0.1.0"), Startup.Smoke(web, "0.1.0"));
        Assert.Equal("https://ductura.org/app/#/doc/demo-1", Startup.LinkUrl(LaunchOptions.Parse(["ductura://doc/demo-1"]), web));
        Assert.Equal("https://ductura.org/app/", Startup.LinkUrl(LaunchOptions.Parse([]), web));
    }

    [Fact]
    public void Isporuceni_paket_prolazi_smoke() =>
        Assert.Equal(0, Startup.Smoke(WebAddress.FromAssembly(typeof(WebAddress).Assembly), "0.1.0").ExitCode);
}
