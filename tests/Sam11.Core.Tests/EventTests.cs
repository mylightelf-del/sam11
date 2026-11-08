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
        Assert.That(w.Events.Select(e => e.Id), Is.EquivalentTo(new[] { "relocate_changan", "imperial_seal", "chain_stratagem" }));
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
}
