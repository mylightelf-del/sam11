using Newtonsoft.Json.Linq;
using Sam11.Core.Data;
using Sam11.Core.Play;
using Sam11.Core.World;

namespace Sam11.Core.Tests;

public class ModTests
{
    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "sam11_mods_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private void Write(string mod, string rel, object json)
    {
        string path = Path.Combine(_root, mod, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JToken.FromObject(json).ToString());
    }

    private void Mod(string id, params string[] loadAfter) =>
        Write(id, "mod.json", new { id, name = id + " 모드", version = "1.0", load_after = loadAfter });

    private GameData Load(params string[] ids) =>
        GameData.Load(TestData.Data.DataDir, ModLoader.Order(ModLoader.Discover(_root), ids));

    private static object NewOfficer(string id, string name) => new
    {
        id, name = new { ko = name }, gender = "female", birth = 170,
        stats = new { ldr = 70, war = 80, @int = 75, pol = 60, cha = 85 },
        hidden = new { loyalty = 12, ambition = 5, compat = 30 },
        aptitude = new { fish_scale = "A", crane_wing = "B", square = "B", long_snake = "A", wedge = "B", wild_geese = "C", siege = "C", naval = "C" },
        traits = new[] { "inspire" },
    };

    [Test]
    public void BaseDataIsValid()
    {
        Assert.That(DataValidator.Validate(TestData.Data, TestData.Map), Is.Empty);
    }

    [Test]
    public void ModOverridesFieldsAddsAndRemovesEntries()
    {
        Mod("tweak");
        Write("tweak", "base/officers.json", new { officers = new object[] { new { id = "cao_cao", stats = new { war = 99 } }, NewOfficer("mod_hero", "모드영웅") } });
        Write("tweak", "base/rules.json", new { traits = new { max = 5 }, economy = new { food_per_soldier_month = 0.5 } });
        var d = Load("tweak");
        Assert.That(d.Officers["cao_cao"].Stats.War, Is.EqualTo(99));
        Assert.That(d.Officers["cao_cao"].Stats.Ldr, Is.EqualTo(TestData.Data.Officers["cao_cao"].Stats.Ldr), "적지 않은 필드는 그대로");
        Assert.That(d.Officers.ContainsKey("mod_hero"));
        Assert.That(d.Rules.Traits.Max, Is.EqualTo(5));
        Assert.That(d.Rules.Combat, Is.Not.Null);
        Assert.That(d.Mods, Is.EqualTo(new[] { "tweak" }));
        Assert.That(TestData.Data.Officers["cao_cao"].Stats.War, Is.Not.EqualTo(99), "기본 데이터는 그대로");

        Mod("cut");
        Write("cut", "base/officers.json", new { officers = new[] { new { id = "mod_hero", remove = true } } });
        var d2 = Load("tweak", "cut");
        Assert.That(d2.Officers.ContainsKey("mod_hero"), Is.False);
    }

    [Test]
    public void LoadOrderFollowsLoadAfterAndConflictsAreReported()
    {
        Mod("a");
        Mod("b", "a");
        Write("a", "base/officers.json", new { officers = new[] { new { id = "lu_bu", stats = new { war = 90 } } } });
        Write("b", "base/officers.json", new { officers = new[] { new { id = "lu_bu", stats = new { war = 95 } } } });
        var d = Load("b", "a");
        Assert.That(d.Mods, Is.EqualTo(new[] { "a", "b" }), "b는 a 뒤에");
        Assert.That(d.Officers["lu_bu"].Stats.War, Is.EqualTo(95));
        Assert.That(d.ModConflicts.Single().Mods, Is.EqualTo(new[] { "a", "b" }));
        Assert.Throws<InvalidDataException>(() => Load("missing"));
        Mod("x", "y"); Mod("y", "x");
        Assert.Throws<InvalidDataException>(() => Load("x", "y"));
    }

    [Test]
    public void ModEventsAndPlacementsEnterScenarios()
    {
        Mod("story");
        Write("story", "base/officers.json", new { officers = new[] { NewOfficer("mod_hero", "모드영웅") } });
        Write("story", "placements.json", new { placements = new object[] {
            new { officer = "mod_hero", scenario = "*", location = "luoyang" },
            new { officer = "mod_hero", scenario = "190", location = "chenliu", faction = "cao_cao", loyalty = 95 },
        } });
        Write("story", "scenarios/190/events.json", new { events = new[] { new
        {
            id = "mod_event", name = new { ko = "모드 이벤트" }, trigger = new { after = "190-02" }, text = "모드가 더한 이벤트",
            effects = new[] { new { type = "trust", faction = "cao_cao", delta = 5 } },
        } } });
        var d = Load("story");
        var w190 = ScenarioLoader.Load(d, TestData.Map, "190", 1);
        Assert.That(w190.Officers["mod_hero"].Faction, Is.EqualTo("cao_cao"), "시나리오를 콕 집으면 세력 소속");
        Assert.That(w190.Events.Any(e => e.Id == "mod_event"));
        Assert.That(w190.Events.Count, Is.EqualTo(TestData.NewWorld().Events.Count + 1));
        var w194 = ScenarioLoader.Load(d, TestData.Map, "194", 1);
        Assert.That(w194.Officers["mod_hero"].Status, Is.EqualTo(OfficerStatus.Free), "모든 시나리오에는 재야로");
        Assert.That(w194.Officers["mod_hero"].Location, Is.EqualTo("luoyang"));
        var w184 = ScenarioLoader.Load(d, TestData.Map, "184", 1);
        Assert.That(w184.Officers["mod_hero"].Status, Is.EqualTo(OfficerStatus.Unborn), "184년엔 14세");
        Assert.That(DataValidator.Validate(d, TestData.Map), Is.Empty);
    }

    [Test]
    public void ModCanAddWholeScenario()
    {
        Mod("newsc");
        string src = Path.Combine(TestData.Data.DataDir, "scenarios", "190");
        string dst = Path.Combine(_root, "newsc", "scenarios", "190x");
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        var d = Load("newsc");
        Assert.That(d.ScenarioIds(), Has.Member("190x").And.Member("190"));
        var w = ScenarioLoader.Load(d, TestData.Map, "190x", 1);
        Assert.That(w.Factions.ContainsKey("dong_zhuo"));
    }

    [Test]
    public void SaveRemembersMods()
    {
        Mod("tweak");
        Write("tweak", "base/officers.json", new { officers = new[] { new { id = "cao_cao", stats = new { war = 99 } } } });
        var d = Load("tweak");
        var s = PlayerSession.NewGame(d, TestData.Map, "190", 1, new GameOptions { Players = { "cao_cao" } });
        s.Execute(new JObject { ["type"] = "end_turn" });
        var save = s.Save();
        Assert.That(PlayerSession.Load(d, TestData.Map, save).Digest(), Is.EqualTo(s.Digest()));
        var ex = Assert.Throws<InvalidOperationException>(() => PlayerSession.Load(TestData.Data, TestData.Map, save));
        Assert.That(ex!.Message, Does.Contain("모드"));
    }

    [Test]
    public void OfficerEditorChecksAndExports()
    {
        var d = TestData.Data;
        var o = JObject.FromObject(NewOfficer("my_general", "나의장수")).ToObject<OfficerDef>(GameData.Serializer)!;
        Assert.That(OfficerEditor.Check(d, o), Is.Empty);

        o.Stats.War = 100; o.Stats.Ldr = 100;
        Assert.That(OfficerEditor.Check(d, o), Has.Some.Contains("상한"));
        Assert.That(OfficerEditor.Check(d, o, statCap: 0), Is.Empty, "상한 옵션을 끄면 통과");
        o.Stats.War = 80; o.Stats.Ldr = 70;
        o.Traits = new List<string> { "inspire", "no_such_trait", "cavalry" };
        Assert.That(OfficerEditor.Check(d, o), Has.Some.Contains("없는 특기").And.Some.Contains("1~2개"));
        o.Traits = new List<string> { "inspire" };
        var dup = JObject.FromObject(NewOfficer("cao_cao", "가짜")).ToObject<OfficerDef>(GameData.Serializer)!;
        Assert.That(OfficerEditor.Check(d, dup), Has.Some.Contains("이미 있는 id"));
        o.Kin = new KinDef { Father = "cao_cao" };

        string dir = Path.Combine(_root, "my_officers");
        OfficerEditor.Export(dir, new ModInfo { Id = "my_officers", Name = "나의 무장", Version = "1" }, new[] { o },
            new[] { new JObject { ["officer"] = "my_general", ["scenario"] = "*", ["location"] = "xuchang" } });
        // 같은 무장을 다시 내보내면 바뀌기만 한다
        o.Stats.Pol = 61;
        OfficerEditor.Export(dir, new ModInfo { Id = "my_officers", Name = "나의 무장", Version = "1" }, new[] { o });
        var loaded = Load("my_officers");
        Assert.That(loaded.Officers["my_general"].Stats.Pol, Is.EqualTo(61));
        Assert.That(loaded.Officers["my_general"].Kin!.Father, Is.EqualTo("cao_cao"));
        Assert.That(DataValidator.Validate(loaded, TestData.Map), Is.Empty);
        var w = ScenarioLoader.Load(loaded, TestData.Map, "200", 1);
        Assert.That(w.Officers["my_general"].Status, Is.EqualTo(OfficerStatus.Free));
    }
}
