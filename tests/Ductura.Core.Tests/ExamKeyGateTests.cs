using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

// PROPOSAL P-4 exam mode in the desktop app (status PRIJEDLOG): invented input only.
public class ExamKeyGateTests
{
    private const uint Injected = ExamKeyGate.InjectedFlag;
    private const uint FromKeyboard = 0;

    private static WebAddress Web()
    {
        Assert.True(WebAddress.TryCreate("https://ductura.org/app/", "ductura.org", out var web));
        return web!;
    }

    [Fact]
    public void Izvan_ispita_nista_ne_blokira()
    {
        var gate = new ExamKeyGate();
        Assert.False(gate.Block(Injected, released: false, foreground: true));
        Assert.Equal(0, gate.BlockedKeys);
    }

    [Fact]
    public void U_ispitu_blokira_tipku_koju_je_poslao_program()
    {
        var gate = new ExamKeyGate();
        gate.Set(on: true, accommodation: false);
        Assert.True(gate.Block(Injected, released: false, foreground: true));
        // The release of the same key is blocked too, but it is not a second key.
        Assert.True(gate.Block(Injected, released: true, foreground: true));
        Assert.Equal(1, gate.BlockedKeys);
    }

    [Fact]
    public void U_ispitu_propusta_tipkovnicu()
    {
        var gate = new ExamKeyGate();
        gate.Set(on: true, accommodation: false);
        Assert.False(gate.Block(FromKeyboard, released: false, foreground: true));
        Assert.Equal(0, gate.BlockedKeys);
    }

    [Fact]
    public void Ne_dira_tipke_kad_Ductura_nije_u_prednjem_planu()
    {
        // Other programs on the computer may send keys to their own windows.
        var gate = new ExamKeyGate();
        gate.Set(on: true, accommodation: false);
        Assert.False(gate.Block(Injected, released: false, foreground: false));
    }

    [Fact]
    public void Uz_prilagodbu_propusta_i_tipke_programa()
    {
        // The on-screen keyboard and dictation tools send every key through SendInput (P-3 §4).
        var gate = new ExamKeyGate();
        gate.Set(on: true, accommodation: true);
        Assert.False(gate.Block(Injected, released: false, foreground: true));
    }

    [Fact]
    public void Kraj_ispita_vraca_propustanje_a_broj_ostaje()
    {
        var gate = new ExamKeyGate();
        gate.Set(on: true, accommodation: false);
        gate.Block(Injected, released: false, foreground: true);
        gate.Set(on: false, accommodation: false);
        Assert.False(gate.Block(Injected, released: false, foreground: true));
        Assert.Equal(1, gate.BlockedKeys);
        Assert.Equal("{\"type\":\"ductura-keys-blocked\",\"count\":1}", gate.BlockedJson());
    }

    [Fact]
    public void Poruka_stranice_ukljucuje_ispit()
    {
        var m = ExamMessage.Parse("{\"type\":\"ductura-exam\",\"on\":true,\"accommodation\":false}",
            "https://ductura.org/app/#/doc/abc", Web());
        Assert.Equal(new ExamMessage(true, false), m);
    }

    [Theory]
    // Another site, a look-alike host, plain http, a path outside the app.
    [InlineData("https://evil.example/app/")]
    [InlineData("https://ductura.org.evil.example/app/")]
    [InlineData("http://ductura.org/app/")]
    [InlineData("https://ductura.org/other/")]
    [InlineData("")]
    public void Poruka_s_druge_adrese_ne_vrijedi(string source)
    {
        Assert.Null(ExamMessage.Parse("{\"type\":\"ductura-exam\",\"on\":false,\"accommodation\":false}", source, Web()));
    }

    [Theory]
    [InlineData("{\"type\":\"ductura-exam\",\"on\":\"false\",\"accommodation\":false}")]
    [InlineData("{\"type\":\"ductura-exam\",\"on\":false}")]
    [InlineData("{\"type\":\"other\",\"on\":false,\"accommodation\":false}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Neispravna_poruka_ne_vrijedi(string json)
    {
        Assert.Null(ExamMessage.Parse(json, "https://ductura.org/app/", Web()));
    }

    [Fact]
    public void Uklanja_varijable_kojima_program_otvara_WebView2_za_daljinsko_upravljanje()
    {
        // WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222 lets a program type into the page
        // through the DevTools protocol, past any keyboard hook.
        var env = new Dictionary<string, string?>
        {
            ["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] = "--remote-debugging-port=9222",
            ["WEBVIEW2_BROWSER_EXECUTABLE_FOLDER"] = @"C:\fake",
            ["WEBVIEW2_USER_DATA_FOLDER"] = @"C:\fake-data",
            ["WEBVIEW2_RELEASE_CHANNEL_PREFERENCE"] = "1",
            ["WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER"] = "x",
            ["PATH"] = @"C:\Windows",
        };
        var removed = WebViewHardening.Scrub(name => env.GetValueOrDefault(name), (name, value) => env[name] = value);
        Assert.Equal(5, removed.Count);
        Assert.All(env.Where(e => e.Key.StartsWith("WEBVIEW2_")), e => Assert.Null(e.Value));
        Assert.Equal(@"C:\Windows", env["PATH"]);
    }

    [Fact]
    public void Brojac_s_blokiranim_tipkama()
    {
        var t = new KeyOriginTally();
        t.Record(IntPtr.Zero, released: false);
        Assert.Equal("{\"deviceKeys\":0,\"programKeys\":1,\"blockedKeys\":3}", t.ToJson(blockedKeys: 3));
    }

    [Fact]
    public void Kuka_broji_tipke_koje_propusti()
    {
        // While the hook is on, Raw Input is off (Windows stops calling the hook otherwise), so the hook counts.
        var t = new KeyOriginTally();
        t.RecordHookKey(hookFlags: 0, released: false);
        t.RecordHookKey(hookFlags: 0, released: true);
        t.RecordHookKey(hookFlags: ExamKeyGate.InjectedFlag, released: false);
        Assert.Equal(1, t.DeviceKeys);
        Assert.Equal(1, t.ProgramKeys);
    }

    [Fact]
    public void Brojac_je_siguran_s_dvije_niti()
    {
        var t = new KeyOriginTally();
        Parallel.For(0, 10000, i => { if (i % 2 == 0) t.Record(IntPtr.Zero, false); else t.RecordHookKey(0, false); });
        Assert.Equal(5000, t.ProgramKeys);
        Assert.Equal(5000, t.DeviceKeys);
    }

    [Fact]
    public void Opcija_ispitnih_vrata()
    {
        Assert.True(LaunchOptions.Parse(["--exam-gate"]).ExamGate);
        Assert.False(LaunchOptions.Parse([]).ExamGate);
    }
}
