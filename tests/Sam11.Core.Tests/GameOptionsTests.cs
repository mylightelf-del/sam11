using Sam11.Core.World;

namespace Sam11.Core.Tests;

public class GameOptionsTests
{
    [Test]
    public void GoalVictoryEndsGameForPlayerOnly()
    {
        var w = TestData.NewWorld();
        w.Options.Players.Add("sun_jian");
        var g = new Game(w, 1);
        Assert.That(w.Factions["sun_jian"].Goals.Select(x => x.Id), Has.Member("sun_190_a"));
        w.Cities["luoyang"].Owner = "sun_jian";
        g.Field.Strongholds["luoyang"].Owner = "sun_jian";
        for (int i = 0; i < 3 && !g.IsOver; i++) g.PlayTurn();
        Assert.That(g.Outcome, Is.EqualTo(GameOutcome.Goal));
        Assert.That(g.Winner, Is.EqualTo("sun_jian"));
        Assert.That(g.AchievedGoal!.Id, Is.EqualTo("sun_190_a"));
    }

    [Test]
    public void GoalVictoryCanBeTurnedOffAndAiGoalsDoNotEndGame()
    {
        var w = TestData.NewWorld();
        w.Options.GoalVictory = false;
        w.Options.Players.Add("sun_jian");
        var g = new Game(w, 1);
        w.Cities["luoyang"].Owner = "sun_jian";
        g.Field.Strongholds["luoyang"].Owner = "sun_jian";
        for (int i = 0; i < 3; i++) g.PlayTurn();
        Assert.That(g.IsOver, Is.False);

        var auto = TestData.NewWorld();
        var g2 = new Game(auto, 1);
        auto.Cities["luoyang"].Owner = "sun_jian";
        g2.Field.Strongholds["luoyang"].Owner = "sun_jian";
        for (int i = 0; i < 3; i++) g2.PlayTurn();
        Assert.That(g2.IsOver, Is.False, "플레이어가 없으면 목표 승리는 없다");
    }

    [Test]
    public void PlayerDefeatEndsGame()
    {
        var w = TestData.NewWorld();
        w.Options.Players.Add("liu_bei");
        var g = new Game(w, 1);
        foreach (var o in w.Officers.Values.Where(o => o.Faction == "liu_bei")) { o.Status = OfficerStatus.Dead; o.Location = ""; }
        g.PlayTurn();
        Assert.That(w.Factions["liu_bei"].Alive, Is.False);
        Assert.That(g.Outcome, Is.EqualTo(GameOutcome.Defeat));
    }

    [Test]
    public void LifespanKillsOldOfficersUnlessOff()
    {
        int Dead(LifespanMode mode)
        {
            var w = TestData.NewWorld();
            w.Options.Lifespan = mode;
            var g = new Game(w, 1);
            // 모든 무장의 사망 연도를 지난 것으로 보이게 15년 뒤로
            w.Date.Year += 15;
            for (int i = 0; i < 12; i++) g.PlayTurn();
            return w.Officers.Values.Count(o => o.Status == OfficerStatus.Dead && o.Def.Death <= 205);
        }
        Assert.That(Dead(LifespanMode.Historical), Is.GreaterThan(20));
        Assert.That(Dead(LifespanMode.Off), Is.LessThan(Dead(LifespanMode.Historical)));
    }

    [Test]
    public void FictionalLifespanShiftsDeathYearsDeterministically()
    {
        var w = TestData.NewWorld();
        var o = w.Officers.Values.First(x => x.Def.Death != null);
        int a = Game.DeathYear(o, LifespanMode.Fictional), b = Game.DeathYear(o, LifespanMode.Fictional);
        Assert.That(a, Is.EqualTo(b));
        Assert.That(a - o.Def.Death!.Value, Is.InRange(-5, 15));
        var shifts = w.Officers.Values.Where(x => x.Def.Death != null).Select(x => Game.DeathYear(x, LifespanMode.Fictional) - x.Def.Death!.Value).Distinct().Count();
        Assert.That(shifts, Is.GreaterThan(5));
    }

    [Test]
    public void AllScenarioGoalsEvaluate()
    {
        foreach (var dir in Directory.GetDirectories(Path.Combine(TestData.Data.DataDir, "scenarios")))
        {
            var w = ScenarioLoader.Load(TestData.Data, TestData.Map, Path.GetFileName(dir), 1);
            var g = new Game(w, 1);
            foreach (var f in w.Factions.Values)
                foreach (var goal in f.Goals)
                    Assert.DoesNotThrow(() => EventSystem.Check(g, goal.Conditions, f.Id), $"{Path.GetFileName(dir)} {goal.Id}");
        }
    }
}

public class AchievementTests
{
    [Test]
    public void AchievementsLoadAndEvaluateInEveryScenario()
    {
        var defs = AchievementTracker.LoadDefs(TestData.Data);
        Assert.That(defs.Count, Is.GreaterThanOrEqualTo(10));
        Assert.That(defs.Select(d => d.Id).Distinct().Count(), Is.EqualTo(defs.Count));
        foreach (var sc in TestData.Data.ScenarioIds())
        {
            var w = ScenarioLoader.Load(TestData.Data, TestData.Map, sc, 1);
            var g = new Game(w, 1);
            var t = new AchievementTracker(TestData.Data, sc, w);
            foreach (var f in w.Factions.Keys) Assert.DoesNotThrow(() => t.Check(g, f), sc);
        }
    }

    [Test]
    public void UnlocksOnceWhenConditionMet()
    {
        var s = Sam11.Core.Play.PlayerSession.NewGame(TestData.Data, TestData.Map, "190", 1, new GameOptions { Players = { "cao_cao" } });
        var w = s.World;
        foreach (var id in new[] { "xuchang", "puyang" })
        {
            w.Cities[id].Owner = "cao_cao";
            s.Game.Field.Strongholds[id].Owner = "cao_cao";
        }
        for (int i = 0; i < 4; i++) s.Execute(new Newtonsoft.Json.Linq.JObject { ["type"] = "end_turn" });
        Assert.That(s.NewAchievements.Select(a => a.Id), Has.Member("first_conquest"));
        int count = s.NewAchievements.Count(a => a.Id == "first_conquest");
        for (int i = 0; i < 4; i++) s.Execute(new Newtonsoft.Json.Linq.JObject { ["type"] = "end_turn" });
        Assert.That(s.NewAchievements.Count(a => a.Id == "first_conquest"), Is.EqualTo(count), "한 번만");
        Assert.That(s.Save().Value<string>("version"), Is.EqualTo(Sam11.Core.GameInfo.Version));
    }
}
