using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

// The web view handler (DAN-1547): NavigationStarting, FrameNavigationStarting and NewWindowRequested in
// Program.cs apply Navigation.Handle; only Load is loaded. Refusal rows come first.
public class NavigationHandlerTests
{
    private static readonly string[] Hosts = ["ductura.org"];
    private static readonly NavigationKind[] Kinds = [NavigationKind.MainFrame, NavigationKind.Frame, NavigationKind.NewWindow];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://ductura.org/app/")]
    [InlineData("file:///C:/Users/x/doc.html")]
    [InlineData(@"\\server\share\doc.html")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<p>x</p>")]
    [InlineData("blob:https://ductura.org/1234")]
    [InlineData("about:blank")]
    [InlineData("ductura://doc/demo-1")]
    [InlineData("mailto:x@ductura.org")]
    [InlineData("https://ductura.org@evil.org/app/")]
    [InlineData("https://ductura.org/app/\r\n")]
    [InlineData(@"https://ductura.org\app\")]
    public void Odbijeno_odrediste_se_ne_ucitava_ni_ne_otvara(string? url)
    {
        foreach (var kind in Kinds)
        foreach (var user in new[] { true, false })
            Assert.Equal(NavigationAction.Refuse, Navigation.Handle(kind, url, Hosts, user));
    }

    [Theory]
    [InlineData("https://evil.org/prijava")]
    [InlineData("https://ductura.org.evil.com/app/")]
    [InlineData("https://Ductura.org/app/")]
    [InlineData("https://ductura.org:443/app/")]
    public void Tudi_https_host_nikad_u_prikazu_a_u_preglednik_samo_na_klik(string url)
    {
        Assert.Equal(NavigationAction.OpenInBrowser, Navigation.Handle(NavigationKind.MainFrame, url, Hosts, true));
        Assert.Equal(NavigationAction.OpenInBrowser, Navigation.Handle(NavigationKind.NewWindow, url, Hosts, true));
        // A script without a click opens nothing.
        Assert.Equal(NavigationAction.Refuse, Navigation.Handle(NavigationKind.MainFrame, url, Hosts, false));
        Assert.Equal(NavigationAction.Refuse, Navigation.Handle(NavigationKind.NewWindow, url, Hosts, false));
        // A frame never loads another host and never opens the browser.
        Assert.Equal(NavigationAction.Refuse, Navigation.Handle(NavigationKind.Frame, url, Hosts, true));
        Assert.Equal(NavigationAction.Refuse, Navigation.Handle(NavigationKind.Frame, url, Hosts, false));
    }

    [Theory]
    [InlineData("https://ductura.org/app/")]
    [InlineData("https://ductura.org/app/#/doc/demo-1")]
    [InlineData("https://ductura.org/app/?x=1")]
    public void Dopusteno_odrediste_se_ucita_u_prikazu(string url)
    {
        foreach (var kind in Kinds)
        foreach (var user in new[] { true, false })
            Assert.Equal(NavigationAction.Load, Navigation.Handle(kind, url, Hosts, user));
    }

    [Fact]
    public void Nepoznata_vrsta_navigacije_se_odbija()
    {
        Assert.Equal(NavigationAction.Refuse, Navigation.Handle((NavigationKind)99, "https://ductura.org/app/", Hosts, true));
        Assert.Equal(NavigationAction.Refuse, Navigation.Handle((NavigationKind)99, "https://evil.org/", Hosts, true));
    }

    [Fact]
    public void Bez_popisa_hostova_nista_se_ne_ucitava()
    {
        foreach (var kind in Kinds)
        {
            Assert.NotEqual(NavigationAction.Load, Navigation.Handle(kind, "https://ductura.org/app/", null, true));
            Assert.NotEqual(NavigationAction.Load, Navigation.Handle(kind, "https://ductura.org/app/", [], true));
        }
    }

    // The handler takes its hosts from the verified package address, never from the page.
    [Fact]
    public void Hostovi_su_popis_iz_paketa_i_ne_mogu_se_promijeniti()
    {
        Assert.True(WebAddress.TryCreate("https://ductura.org/app/", "ductura.org;prijava.ductura.org", out var web));
        Assert.Equal(["ductura.org", "prijava.ductura.org"], web!.Hosts);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)web.Hosts)[0] = "evil.org");
        Assert.Equal(NavigationAction.Load,
            Navigation.Handle(NavigationKind.MainFrame, "https://prijava.ductura.org/", web.Hosts, false));
        Assert.Equal(NavigationAction.Refuse,
            Navigation.Handle(NavigationKind.Frame, "https://evil.org/", web.Hosts, true));
    }

    // The first address the window opens must pass its own handler, or the shell would never show Ductura.
    [Theory]
    [InlineData()]
    [InlineData("ductura://doc/demo-1")]
    [InlineData("ductura://doc/%2E%2E")]
    public void Pocetna_adresa_prozora_prolazi_rukovatelj(params string[] args)
    {
        Assert.True(WebAddress.TryCreate("https://ductura.org/app/", "ductura.org", out var web));
        var url = Startup.Shell(LaunchOptions.Parse(args), web).WebViewUrl;
        Assert.NotNull(url);
        Assert.Equal(NavigationAction.Load, Navigation.Handle(NavigationKind.MainFrame, url, web!.Hosts, false));
    }
}
