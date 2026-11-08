using Sam11.Core.AI;
using Sam11.Core.World;

namespace Sam11.Core.Tests;

public class GameLoopTests
{
    private static Game NewGame(ulong seed = 1) => new(TestData.NewWorld(seed), seed);

    private static HexCoord FreeNear(Game g, HexCoord c, int dist) =>
        g.Field.Map.Cells.Where(h => h.DistanceTo(c) == dist && g.Field.UnitAt(h) == null && !g.Field.Structures.ContainsKey(h)
                                     && TestData.Data.Terrain.TryGetValue(g.Field.Map.TerrainAt(h), out var t) && t.PassableLand)
                         .OrderBy(h => h.Q).ThenBy(h => h.R).First();

    [Test]
    public void TurnAdvancesDateAndMonthlyIncome()
    {
        var g = NewGame();
        int gold = g.World.Cities["chenliu"].Gold;
        for (int i = 0; i < 3; i++) g.PlayTurn();
        Assert.That(g.World.Date.Month, Is.EqualTo(2));
        Assert.That(g.World.Cities["chenliu"].Gold, Is.GreaterThan(gold));
    }

    [Test]
    public void GarrisonTurnsOutWhenEnemyApproachesAndStandsDownAfter()
    {
        var g = NewGame();
        var city = g.World.Cities["chenliu"];
        int troops = city.Troops;
        var enemy = g.Field.Deploy("dong_zhuo", new[] { "hua_xiong" }, "fish_scale", 3000, FreeNear(g, city.Placement.Center, 4));
        g.SyncGarrisons();
        Assert.That(g.Garrisons.ContainsKey("chenliu"));
        Assert.That(city.Troops, Is.EqualTo(0));
        Assert.That(city.FieldGarrison, Is.EqualTo(troops));
        enemy.Status = UnitStatus.Withdrawn;
        g.SyncGarrisons();
        Assert.That(g.Garrisons.ContainsKey("chenliu"), Is.False);
        Assert.That(city.Troops, Is.EqualTo(troops));
    }

    [Test]
    public void GarrisonFightsOnlyWithItsCommandersShare()
    {
        var g = NewGame();
        var city = g.World.Cities["luoyang"]; // 병력 8만
        g.Field.Deploy("cao_cao", new[] { "cao_hong" }, "fish_scale", 3000, FreeNear(g, city.Placement.Center, 4));
        g.SyncGarrisons();
        var gar = g.Garrisons["luoyang"];
        Assert.That(gar.Troops, Is.EqualTo(80000));
        Assert.That(gar.FightingTroops, Is.EqualTo(gar.MaxTroops));
        Assert.That(gar.FightingTroops, Is.LessThan(10000));
    }

    [Test]
    public void NeutralCityCanBeTaken()
    {
        var g = NewGame();
        var city = g.World.Cities["lingling"]; // 무주지
        Assert.That(city.Owner, Is.Null);
        city.Troops = 0; // 비어 있는 무주지
        foreach (var h in city.Placement.Tiles.Keys.Where(h => g.Field.Structures[h].Blocking)) g.Field.Structures[h].Durability = 0;
        var u = g.Field.Deploy("sun_jian", new[] { "sun_jian" }, "fish_scale", 5000, city.Placement.Center);
        g.Field.CheckCaptures();
        g.Resolve();
        Assert.That(city.Owner, Is.EqualTo("sun_jian"));
        Assert.That(g.World.Log.Any(l => l.Contains("영릉 함락") && l.Contains("무주지")));
    }

    [Test]
    public void CapturedOfficerBecomesCaptiveAndRulerIsSucceeded()
    {
        var g = NewGame(3);
        var sun = g.World.Officers["sun_jian"];
        var u = g.Field.Deploy("sun_jian", new[] { "sun_jian" }, "fish_scale", 100, FreeNear(g, g.World.Cities["changsha"].Placement.Center, 6));
        u.Home = "changsha";
        g.Field.Fates.Add(new OfficerFate { OfficerId = "sun_jian", Outcome = DefeatOutcome.Capture, Faction = "sun_jian", By = "liu_biao", Position = u.Position });
        g.Resolve();
        Assert.That(sun.Status, Is.EqualTo(OfficerStatus.Captive));
        Assert.That(sun.Faction, Is.EqualTo("liu_biao"));
        // 아들 손책이 뒤를 잇는다
        Assert.That(g.World.Factions["sun_jian"].Ruler, Is.EqualTo("sun_ce"));
    }

    [Test]
    public void FactionWithoutLandOrArmyIsDestroyed()
    {
        var g = NewGame();
        g.World.Cities["xiangping"].Owner = "cao_cao";
        g.Field.Strongholds["xiangping"].Owner = "cao_cao";
        g.Resolve();
        Assert.That(g.World.Factions["gongsun_du"].Alive, Is.False);
        Assert.That(g.World.Officers["gongsun_kang"].Status, Is.EqualTo(OfficerStatus.Free));
    }

    [Test]
    public void AlliesDoNotBlockEachOther()
    {
        var g = NewGame();
        Assert.That(g.World.AreAllied("cao_cao", "yuan_shao"), "반동탁 연합");
        Assert.That(g.Field.Hostile("cao_cao", "yuan_shao"), Is.False);
        Assert.That(g.Field.Hostile("cao_cao", "dong_zhuo"));
        g.World.Date.Year = 191; g.World.Date.Month = 2; // 12개월 뒤 연합 해체
        Assert.That(g.Field.Hostile("cao_cao", "yuan_shao"));
    }

    [Test]
    public void SameSeedSameHistory()
    {
        string Run(ulong seed)
        {
            var g = NewGame(seed);
            foreach (var f in g.World.Factions.Keys) g.Controllers[f] = new AiController();
            for (int i = 0; i < 18; i++) g.PlayTurn();
            return string.Join("\n", g.World.Log) + string.Join("\n", g.Field.Log.Lines);
        }
        Assert.That(Run(5), Is.EqualTo(Run(5)));
    }
}

public class NewDomesticCommandTests
{
    [Test]
    public void MoveAndTransportBetweenConnectedOwnStrongholds()
    {
        var w = TestData.NewWorld();
        Domestic.Transport(w, "dong_zhuo", "luoyang", "hulao", 100, 1000, 2000);
        Assert.That(w.Cities["hulao"].Troops, Is.EqualTo(40000 + 2000));
        Domestic.MoveOfficer(w, "lu_bu", "luoyang", "changan");
        Assert.That(w.Officers["lu_bu"].Location, Is.EqualTo("changan"));
        Assert.Throws<InvalidOperationException>(() => Domestic.MoveOfficer(w, "li_ru", "luoyang", "chenliu"));
    }

    [Test]
    public void EmployFreeOfficerInSameCity()
    {
        var w = TestData.NewWorld();
        w.Officers["guo_jia"].Location = "chenliu"; // 재야 곽가가 진류에 왔다
        int tries = 0;
        while (w.Officers["guo_jia"].Status == OfficerStatus.Free && tries++ < 30)
        {
            w.Officers["cao_cao"].Acted = false;
            Domestic.Employ(w, "cao_cao", "chenliu", "guo_jia");
        }
        Assert.That(w.Officers["guo_jia"].Faction, Is.EqualTo("cao_cao"));
    }

    [Test]
    public void SonsComeOfAgeInJanuary()
    {
        var w = TestData.NewWorld();
        w.Date.Year = 197; w.Date.Month = 1;
        Domestic.Debut(w);
        Assert.That(w.Officers["sun_quan"].Status, Is.EqualTo(OfficerStatus.Active));
        Assert.That(w.Officers["sun_quan"].Faction, Is.EqualTo("sun_jian"));
    }

    [Test]
    public void FoodTradeIsLimitedByLocalMarket()
    {
        var w = TestData.NewWorld();
        var c = w.Cities["chenliu"];
        c.Gold = 1000000;
        int food = c.Food;
        Domestic.BuyFood(w, "cao_cao", "chenliu", 1000000);
        Assert.That(c.Food - food, Is.LessThanOrEqualTo(Domestic.MaxTradeFood(w, c)));
    }

    [Test]
    public void RecruitStopsAtPopulationTroopCap()
    {
        var w = TestData.NewWorld();
        var c = w.Cities["chenliu"];
        c.Gold = 100000;
        c.Troops = (int)(c.Population * 0.15);
        Assert.Throws<InvalidOperationException>(() => Domestic.Recruit(w, "cao_cao", "chenliu"));
    }
}

public class AiTests
{
    [Test]
    public void AiExpandsIntoEmptyNeighbourWithinMonths()
    {
        var w = TestData.NewWorld();
        var g = new Game(w, 1);
        g.Controllers["sun_jian"] = new AiController(); // 손견만 AI로 움직인다
        for (int i = 0; i < 36 && !w.Cities.Values.Any(c => c.Owner == "sun_jian" && c.Id != "changsha"); i++) g.PlayTurn();
        Assert.That(w.Cities.Values.Count(c => c.Owner == "sun_jian"), Is.GreaterThan(1));
    }

    [Test]
    public void AiNeverPlansAgainstAllies()
    {
        var w = TestData.NewWorld();
        var g = new Game(w, 1);
        var ai = new AiController();
        g.Controllers["cao_cao"] = ai;
        for (int i = 0; i < 9; i++)
        {
            g.PlayTurn();
            foreach (var c in ai.Campaigns)
            {
                string? owner = w.Cities[c.Target].Owner;
                Assert.That(owner == null || !w.AreAllied(owner, "cao_cao"), $"{c.Target}는 동맹 거점");
            }
        }
    }

    [Test]
    public void KnowledgeUsesBorderScoutingAndReputation()
    {
        var w = TestData.NewWorld();
        var g = new Game(w, 1);
        var k = new Knowledge("cao_cao");
        k.Update(g);
        var near = w.Cities["sishui"];   // 진류와 이어진 이웃: 오차 ±20%
        int actual = near.Troops;
        Assert.That(k.EstimateTroops(g, near), Is.InRange((int)(actual * 0.8), (int)(actual * 1.2) + 1));
        var far = w.Cities["chengdu"];   // 멀리 있는 대도시: 평판치
        Assert.That(k.EstimateTroops(g, far), Is.EqualTo(25000));
    }

    [Test]
    public void AiLaunchesOnlyWithEnoughForce()
    {
        var w = TestData.NewWorld();
        var g = new Game(w, 2);
        var ais = w.Factions.Keys.ToDictionary(f => f, f => new AiController());
        foreach (var kv in ais) g.Controllers[kv.Key] = kv.Value;
        for (int i = 0; i < 72; i++) g.PlayTurn();
        var launched = ais.Values.Sum(a => a.Stats.CampaignsLaunched);
        var under = ais.Values.Sum(a => a.Stats.LaunchedUnderRatio);
        Assert.That(launched, Is.GreaterThan(5));
        Assert.That(under, Is.LessThanOrEqualTo(launched / 10), "축차 투입이 10%를 넘음");
    }
}
