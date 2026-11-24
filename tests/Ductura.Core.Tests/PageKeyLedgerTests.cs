using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

// PROPOSAL P-4 exam mode, keys posted to the window (WM_KEYDOWN / WM_CHAR through PostMessage) never pass the
// low-level keyboard hook. The page counts the keys it received; more than the hook let through is unaccounted.
// Invented counts only.
public class PageKeyLedgerTests
{
    private static WebAddress Web()
    {
        Assert.True(WebAddress.TryCreate("https://ductura.org/app/", "ductura.org", out var web));
        return web!;
    }

    [Fact]
    public void Tipke_s_tipkovnice_nisu_visak()
    {
        var ledger = new PageKeyLedger();
        for (var i = 0; i < 5; i++) { ledger.HookPassed(released: false); ledger.HookPassed(released: true); }
        ledger.PageKeys(5);
        Assert.Equal(5, ledger.HookKeys);
        Assert.Equal(5, ledger.PageKeyCount);
        Assert.Equal(0, ledger.Unaccounted);
        Assert.False(ledger.TakeReport(out _));
    }

    [Fact]
    public void Tipke_koje_je_stranica_primila_mimo_kuke_su_visak()
    {
        // A program posted three WM_CHAR messages to the WebView2 window: the page saw them, the hook did not.
        var ledger = new PageKeyLedger();
        ledger.HookPassed(released: false);
        ledger.PageKeys(1);
        ledger.PageKeys(3);
        Assert.Equal(3, ledger.Unaccounted);
        Assert.True(ledger.TakeReport(out var json));
        Assert.Equal("{\"type\":\"ductura-keys-unaccounted\",\"count\":3}", json);
        // Reported once; only a new excess is reported again.
        Assert.False(ledger.TakeReport(out _));
        ledger.PageKeys(1);
        Assert.True(ledger.TakeReport(out json));
        Assert.Equal("{\"type\":\"ductura-keys-unaccounted\",\"count\":4}", json);
    }

    [Fact]
    public void Manje_tipki_na_stranici_nije_visak()
    {
        // Shortcuts the browser keeps, dead keys and keys pressed while the page is loading never reach it.
        var ledger = new PageKeyLedger();
        for (var i = 0; i < 4; i++) ledger.HookPassed(released: false);
        ledger.PageKeys(1);
        Assert.Equal(0, ledger.Unaccounted);
    }

    [Fact]
    public void Visak_se_ne_moze_sakriti_kasnijim_tipkama()
    {
        // Unaccounted keys stay unaccounted: once the page is ahead, later real keys do not pay them back.
        var ledger = new PageKeyLedger();
        ledger.PageKeys(2);
        for (var i = 0; i < 10; i++) { ledger.HookPassed(released: false); ledger.PageKeys(1); }
        Assert.Equal(2, ledger.Unaccounted);
    }

    [Fact]
    public void Tipke_koje_preglednik_zadrzi_ne_placaju_kasnije_poslane()
    {
        // A hundred shortcuts the browser kept, then 120 posted keys: at most MaxInFlight of them can hide.
        var ledger = new PageKeyLedger();
        for (var i = 0; i < 100; i++) ledger.HookPassed(released: false);
        ledger.PageKeys(120);
        Assert.Equal(120 - PageKeyLedger.MaxInFlight, ledger.Unaccounted);
    }

    [Fact]
    public void Brojac_je_siguran_s_dvije_niti()
    {
        var ledger = new PageKeyLedger();
        Parallel.For(0, 10000, i => { if (i % 2 == 0) ledger.HookPassed(false); else ledger.PageKeys(1); });
        Assert.Equal(5000, ledger.HookKeys);
        Assert.Equal(5000, ledger.PageKeyCount);
    }

    [Fact]
    public void Poruka_stranice_s_brojem_tipki()
    {
        Assert.Equal(7, PageKeysMessage.Parse("{\"type\":\"ductura-page-keys\",\"count\":7}", "https://ductura.org/app/", Web()));
    }

    [Theory]
    [InlineData("{\"type\":\"ductura-page-keys\",\"count\":0}", "https://ductura.org/app/")]
    [InlineData("{\"type\":\"ductura-page-keys\",\"count\":-1}", "https://ductura.org/app/")]
    [InlineData("{\"type\":\"ductura-page-keys\",\"count\":100001}", "https://ductura.org/app/")]
    [InlineData("{\"type\":\"ductura-page-keys\",\"count\":1.5}", "https://ductura.org/app/")]
    [InlineData("{\"type\":\"ductura-page-keys\",\"count\":\"3\"}", "https://ductura.org/app/")]
    [InlineData("{\"type\":\"ductura-exam\",\"count\":3}", "https://ductura.org/app/")]
    [InlineData("not json", "https://ductura.org/app/")]
    [InlineData("{\"type\":\"ductura-page-keys\",\"count\":3}", "https://evil.example/app/")]
    public void Neispravna_poruka_s_brojem_tipki_ne_vrijedi(string json, string source)
    {
        Assert.Null(PageKeysMessage.Parse(json, source, Web()));
    }

    [Fact]
    public void Skripta_broji_samo_prave_tipke_i_prije_stranice()
    {
        var s = PageKeyLedger.Script;
        // Capture phase on window, so the page's own handlers cannot stop it first.
        Assert.Contains("addEventListener('keydown'", s);
        Assert.Contains("addEventListener('keypress'", s);
        Assert.Contains("addEventListener('keyup'", s);
        Assert.Contains(", true)", s);
        // Events a script dispatched are not keys from Windows; they are not this check's concern.
        Assert.Contains("isTrusted", s);
        Assert.Contains("ductura-page-keys", s);
        // No key, no text: only a count leaves the page.
        Assert.DoesNotContain("e.key", s);
        Assert.DoesNotContain("e.code", s);
    }

    [Fact]
    public void Brojac_s_tipkama_stranice()
    {
        var t = new KeyOriginTally();
        Assert.Equal("{\"deviceKeys\":0,\"programKeys\":0,\"blockedKeys\":1,\"pageKeys\":5,\"unaccountedKeys\":4}",
            t.ToJson(blockedKeys: 1, pageKeys: 5, unaccountedKeys: 4));
    }
}
