using System.Text.RegularExpressions;
using Sam11.Core.Data;

namespace Sam11.Core.Tests;

public class LocalizationTests
{
    [Test]
    public void EveryLanguageHasEveryKey()
    {
        var langs = Localization.Available(TestData.Data.DataDir);
        Assert.That(langs.Keys, Is.SupersetOf(new[] { "ko", "en" }));
        foreach (var lang in langs.Keys.Where(l => l != "ko"))
            Assert.That(Localization.Load(TestData.Data.DataDir, lang).Missing(), Is.Empty, $"{lang} 번역 누락");
    }

    [Test]
    public void FormatsAndFallsBack()
    {
        var en = Localization.Load(TestData.Data.DataDir, "en");
        Assert.That(en.T("end.goal", "Take Luoyang"), Is.EqualTo("Goal achieved: Take Luoyang"));
        Assert.That(en.T("no.such.key"), Is.EqualTo("no.such.key"));
        var ko = Localization.Load(TestData.Data.DataDir, "ko");
        Assert.That(ko.T("hud.save"), Is.EqualTo("저장"));
        var cao = TestData.Data.Officers["cao_cao"].Name;
        Assert.That(ko.Name(cao), Is.EqualTo("조조"));
        Assert.That(en.Name(cao), Is.EqualTo(cao.En ?? cao.Zh ?? cao.Ko));
    }

    [Test]
    public void UnityScriptKeysExist()
    {
        var ko = Localization.Load(TestData.Data.DataDir, "ko");
        string dir = Path.Combine(Path.GetDirectoryName(TestData.Data.DataDir)!, "Unity", "Assets", "Scripts");
        var used = Directory.GetFiles(dir, "*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "L\\.T\\(\"([a-z_.0-9]+)\"").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        Assert.That(used.Count, Is.GreaterThan(40));
        Assert.That(used.Where(k => !k.EndsWith(".") && !ko.Has(k)), Is.Empty);
        // 앞부분만 쓰는 키 (cmd. + 명령 등)
        foreach (var k in new[] { "cmd.recruit", "cmd.train", "cmd.patrol", "cmd.charity", "equip.horse", "equip.ship", "stratagem.fire",
                                  "stratagem.false_report", "diplo.alliance", "diplo.captiveexchange", "difficulty.hell", "opt.lifespan.fictional", "xun.2" })
            Assert.That(ko.Has(k), k);
    }
}
