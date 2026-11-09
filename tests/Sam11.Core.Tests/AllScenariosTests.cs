using Sam11.Core.AI;
using Sam11.Core.World;

namespace Sam11.Core.Tests;

/// <summary>data/scenarios의 모든 시나리오를 엔진으로 불러와 이벤트 문법을 확인하고 몇 턴 돌려 본다.</summary>
public class AllScenariosTests
{
    public static IEnumerable<string> ScenarioIds() =>
        Directory.GetDirectories(Path.Combine(GameData.FindDataDir(TestContext.CurrentContext.TestDirectory), "scenarios"))
                 .Where(d => File.Exists(Path.Combine(d, "scenario.json")))
                 .Select(Path.GetFileName).OrderBy(x => x)!;

    [TestCaseSource(nameof(ScenarioIds))]
    public void LoadsAndPlays(string id)
    {
        var w = ScenarioLoader.Load(TestData.Data, TestData.Map, id, 1);
        Assert.That(w.Factions.Values.All(f => w.Cities[f.Capital].Owner == f.Id), "본거지 소유");
        var g = new Game(w, 1);
        // 이벤트 조건·결과에 모르는 이름이 있으면 여기서 드러난다
        foreach (var ev in w.Events)
        {
            Assert.DoesNotThrow(() => EventSystem.Check(g, ev.Trigger), $"{id}/{ev.Id} 조건");
            foreach (var c in ev.Choices)
                foreach (var fx in c.Effects)
                    Assert.That(KnownEffects, Does.Contain(fx.Value<string>("type")), $"{id}/{ev.Id}/{c.Id}");
            foreach (var fx in ev.Effects)
                Assert.That(KnownEffects, Does.Contain(fx.Value<string>("type")), $"{id}/{ev.Id}");
        }
        foreach (var f in w.Factions.Keys) g.Controllers[f] = new AiController();
        g.ControllerFactory = f => new AiController();
        for (int i = 0; i < 12; i++) g.PlayTurn();
        Assert.That(g.World.Date.Month, Is.GreaterThan(w.Date.Month - 12));
    }

    private static readonly string[] KnownEffects =
    {
        "move_capital", "city_population", "city_morale", "destroy_facilities", "transfer_resources", "trust", "friendship",
        "unlock_event", "give_treasure", "loyalty", "kill_officer", "split_faction", "officer_join", "transfer_city", "pact", "special",
    };
}
