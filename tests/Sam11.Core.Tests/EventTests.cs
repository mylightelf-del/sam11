using Newtonsoft.Json.Linq;
using Sam11.Core.World;

namespace Sam11.Core.Tests;

public class EventTests
{
    private static Game NewGame() => new(TestData.NewWorld(), 1);

    [Test]
    public void ScenarioEventsAreLoaded()
    {
        var w = TestData.NewWorld();
        Assert.That(w.Events.Select(e => e.Id), Is.SupersetOf(new[] { "relocate_changan", "imperial_seal", "chain_stratagem" }));
        Assert.That(w.Events.First(e => e.Id == "imperial_seal").Locked, "옥새는 천도 뒤에 풀린다");
        Assert.That(w.TreasureOwner["red_hare"], Is.EqualTo("lu_bu"));
    }

    [Test]
    public void ConditionsEvaluate()
    {
        var g = NewGame();
        bool C(string json) => EventSystem.Check(g, JToken.Parse(json));
        Assert.That(C("{\"after\": \"190-01\"}"));
        Assert.That(C("{\"after\": \"190-02\"}"), Is.False);
        Assert.That(C("{\"before\": \"190-02\"}"));
        Assert.That(C("{\"ruler_of\": {\"faction\": \"cao_cao\", \"officer\": \"cao_cao\"}}"));
        Assert.That(C("{\"owns\": {\"faction\": \"dong_zhuo\", \"strongholds\": [\"luoyang\", \"changan\"]}}"));
        Assert.That(C("{\"any\": [{\"after\": \"199-01\"}, {\"officer_in\": {\"officer\": \"cao_cao\", \"stronghold\": \"chenliu\"}}]}"));
        Assert.That(C("{\"not\": {\"faction_destroyed\": \"cao_cao\"}}"));
        Assert.Throws<InvalidOperationException>(() => C("{\"moon_phase\": 3}"));
    }

    [Test]
    public void RelocationMovesArmyAndUnlocksImperialSeal()
    {
        var g = NewGame();
        var w = g.World;
        int luoyang = w.Cities["luoyang"].Troops, changan = w.Cities["changan"].Troops;
        var ev = w.Events.First(e => e.Id == "relocate_changan");
        EventSystem.Fire(g, ev, "relocate");
        Assert.That(w.Factions["dong_zhuo"].Capital, Is.EqualTo("changan"));
        Assert.That(w.Cities["changan"].Troops - changan, Is.EqualTo(luoyang - w.Cities["luoyang"].Troops));
        Assert.That(w.Cities["luoyang"].Troops, Is.LessThan(luoyang / 2));
        Assert.That(w.Events.First(e => e.Id == "imperial_seal").Locked, Is.False);
        Assert.That(w.TrustOf("dong_zhuo"), Is.EqualTo(40));
    }

    [Test]
    public void RelocationFiresInMarchOfFirstYear()
    {
        var g = NewGame();
        for (int i = 0; i < 9; i++) g.PlayTurn(); // 190년 4월 상순까지
        Assert.That(g.World.EventsDone, Has.Member("relocate_changan"));
        Assert.That(g.World.EventsDone, Has.No.Member("chain_stratagem"), "연환계는 192년부터");
    }

    [Test]
    public void ChainStratagemSplitsDongZhuoFaction()
    {
        var g = NewGame();
        var w = g.World;
        bool created = false;
        g.ControllerFactory = f => { created = true; return new AI.AiController(); };
        EventSystem.Fire(g, w.Events.First(e => e.Id == "chain_stratagem"));
        Assert.That(w.Officers["dong_zhuo"].Status, Is.EqualTo(OfficerStatus.Dead));
        Assert.That(w.Factions["lu_bu"].Ruler, Is.EqualTo("lu_bu"));
        Assert.That(w.Factions["dong_zhuo"].Ruler, Is.EqualTo("li_jue"));
        Assert.That(w.Cities[w.Factions["lu_bu"].Capital].Owner, Is.EqualTo("lu_bu"));
        Assert.That(w.Officers["gao_shun"].Faction, Is.EqualTo("lu_bu"));
        Assert.That(w.FriendshipOf("lu_bu", "dong_zhuo"), Is.EqualTo(0));
        Assert.That(created, "새 세력에 컨트롤러가 붙는다");
        g.Resolve();
        Assert.That(w.Factions["dong_zhuo"].Alive, "이각이 이어 세력이 남는다");
    }

    [Test]
    public void OnceEventsDoNotRepeat()
    {
        var g = NewGame();
        g.World.Date.Month = 3;
        EventSystem.RunMonthly(g);
        int before = g.World.Log.Count(l => l.Contains("장안 천도"));
        EventSystem.RunMonthly(g);
        Assert.That(g.World.Log.Count(l => l.Contains("장안 천도")), Is.EqualTo(before));
    }

    [Test]
    public void OfficerJoinRecruitsFreeOfficerButNotRulers()
    {
        var g = NewGame();
        var w = g.World;
        var free = w.Officers.Values.Where(o => o.Status == OfficerStatus.Free).OrderBy(o => o.Id).First();
        EventSystem.Apply(g, JArray.Parse($"[{{\"type\": \"officer_join\", \"officer\": \"{free.Id}\", \"faction\": \"cao_cao\"}}]"));
        Assert.That(free.Status, Is.EqualTo(OfficerStatus.Active));
        Assert.That(free.Faction, Is.EqualTo("cao_cao"));
        Assert.That(free.Location, Is.EqualTo(w.Factions["cao_cao"].Capital));

        // 다른 세력 무장은 allow_defect가 있어야 옮기고, 군주는 옮기지 않는다
        var ruler = w.Officers[w.Factions["dong_zhuo"].Ruler];
        var general = w.Officers.Values.Where(o => o.Faction == "dong_zhuo" && o.Status == OfficerStatus.Active && o.Id != ruler.Id).OrderBy(o => o.Id).First();
        EventSystem.Apply(g, JArray.Parse($"[{{\"type\": \"officer_join\", \"officer\": \"{general.Id}\", \"faction\": \"cao_cao\"}}]"));
        Assert.That(general.Faction, Is.EqualTo("dong_zhuo"));
        EventSystem.Apply(g, JArray.Parse($"[{{\"type\": \"officer_join\", \"officer\": \"{general.Id}\", \"faction\": \"cao_cao\", \"allow_defect\": true}}, {{\"type\": \"officer_join\", \"officer\": \"{ruler.Id}\", \"faction\": \"cao_cao\", \"allow_defect\": true}}]"));
        Assert.That(general.Faction, Is.EqualTo("cao_cao"));
        Assert.That(ruler.Faction, Is.EqualTo("dong_zhuo"));
    }

    [Test]
    public void TransferCityKeepsTroopsAndCanEndFaction()
    {
        var g = NewGame();
        var w = g.World;
        var city = w.Cities.Values.Where(c => c.Owner == "dong_zhuo" && c.Id != w.Factions["dong_zhuo"].Capital).OrderBy(c => c.Id).First();
        int troops = city.Troops;
        var locals = w.OfficersAt(city.Id).Where(o => o.Faction == "dong_zhuo").ToList();
        EventSystem.Apply(g, JArray.Parse($"[{{\"type\": \"transfer_city\", \"stronghold\": \"{city.Id}\", \"to\": \"cao_cao\"}}]"));
        Assert.That(city.Owner, Is.EqualTo("cao_cao"));
        Assert.That(g.Field.Strongholds[city.Id].Owner, Is.EqualTo("cao_cao"));
        Assert.That(city.Troops, Is.EqualTo(troops), "항복은 병력을 그대로 넘긴다");
        Assert.That(locals.All(o => o.Faction == "cao_cao" && o.Location == city.Id));

        // 마지막 거점을 바치면 세력이 끝나고 군주는 새 주인의 신하가 된다
        var lone = w.Factions.Values.First(f => f.Alive && f.Id != "cao_cao" && w.Cities.Values.Count(c => c.Owner == f.Id) == 1);
        var rulerId = lone.Ruler;
        EventSystem.Apply(g, JArray.Parse($"[{{\"type\": \"transfer_city\", \"stronghold\": \"{lone.Capital}\", \"to\": \"cao_cao\"}}]"));
        Assert.That(lone.Alive, Is.False);
        Assert.That(w.Officers[rulerId].Faction, Is.EqualTo("cao_cao"));
        Assert.That(w.Officers[rulerId].Status, Is.EqualTo(OfficerStatus.Active));
    }

    [Test]
    public void SplitWithLostSeatNeverTakesCapitalAndPactHolds()
    {
        var g = NewGame();
        var w = g.World;
        var dz = w.Factions["dong_zhuo"];
        var general = w.Officers.Values.Where(o => o.Faction == "dong_zhuo" && o.Status == OfficerStatus.Active && o.Id != dz.Ruler && o.Location == dz.Capital)
                       .OrderBy(o => o.Id).First();
        var lost = w.Cities.Values.First(c => c.Owner != "dong_zhuo").Id;
        EventSystem.Apply(g, JArray.Parse($"[{{\"type\": \"split_faction\", \"new_faction\": \"rebel\", \"ruler\": \"{general.Id}\", \"officers\": [\"{general.Id}\"], \"seat\": \"{lost}\", \"friendship\": 0}}, {{\"type\": \"pact\", \"kind\": \"truce\", \"a\": \"dong_zhuo\", \"b\": \"rebel\", \"months\": 12}}]"));
        Assert.That(w.Factions.ContainsKey("rebel"));
        Assert.That(w.Cities[dz.Capital].Owner, Is.EqualTo("dong_zhuo"), "본거지는 가져가지 않는다");
        Assert.That(w.Cities.Values.Count(c => c.Owner == "rebel"), Is.EqualTo(1));
        Assert.That(w.Alliances.Any(a => a.Kind == PactKind.Truce && a.Members.SetEquals(new[] { "dong_zhuo", "rebel" })));
    }
}
