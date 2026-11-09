using Sam11.Core.AI;
using Sam11.Core.World;

namespace Sam11.Core.Tests;

public class BattleStratagemTests
{
    /// <summary>성공할 때까지 시드를 바꿔 가며 계략을 시도한다 (확률 시험을 결정론적으로).</summary>
    private static (Battle.Battle b, Unit actor, Unit target, bool ok) TryUntil(string actor, string target, string kind, bool wantSuccess,
        string weather = "clear", string terrain = "plain")
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            var b = TestData.NewBattle(BattleMap.Rectangle(12, 12, terrain), seed, new BattleOptions { Weather = weather });
            var a = b.Deploy("a", new[] { actor }, "square", 3000, new HexCoord(3, 3));
            var t = b.Deploy("b", new[] { target }, "fish_scale", 3000, new HexCoord(5, 3));
            bool ok = Stratagems.Use(b, a, kind, t);
            if (ok == wantSuccess) return (b, a, t, ok);
        }
        throw new AssertionException("원하는 결과가 나오지 않음");
    }

    [Test]
    public void FireBurnsAndHurts()
    {
        var (b, _, t, _) = TryUntil("jia_xu", "zhang_fei", "fire", true);
        Assert.That(b.Map.IsBurning(t.Position));
        Assert.That(t.Troops, Is.LessThan(3000));
    }

    [Test]
    public void NoFireInRain()
    {
        var b = TestData.NewBattle(options: new BattleOptions { Weather = "rain" });
        var a = b.Deploy("a", new[] { "jia_xu" }, "square", 3000, new HexCoord(3, 3));
        var t = b.Deploy("b", new[] { "zhang_fei" }, "fish_scale", 3000, new HexCoord(5, 3));
        Assert.Throws<InvalidOperationException>(() => Stratagems.Use(b, a, "fire", t));
    }

    [Test]
    public void ConfuseAndCalm()
    {
        var (b, _, t, _) = TryUntil("jia_xu", "zhang_fei", "confuse", true);
        Assert.That(t.Status, Is.EqualTo(UnitStatus.Confused));
        var helper = b.Deploy("b", new[] { "liu_bei" }, "square", 3000, t.Position.Neighbors().First(n => b.UnitAt(n) == null));
        Stratagems.Use(b, helper, "calm", t);
        Assert.That(t.Status, Is.EqualTo(UnitStatus.Ready));
    }

    [Test]
    public void SharperMindSeesThroughAndBackfires()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            var b = TestData.NewBattle(seed: seed);
            var a = b.Deploy("a", new[] { "zhang_fei" }, "square", 3000, new HexCoord(3, 3));
            var t = b.Deploy("b", new[] { "jia_xu" }, "fish_scale", 3000, new HexCoord(5, 3));
            if (!Stratagems.Use(b, a, "confuse", t) && a.Status == UnitStatus.Confused)
            {
                Assert.That(b.Log.Lines.Any(l => l.Contains("간파")));
                return;
            }
        }
        Assert.Fail("간파가 한 번도 일어나지 않음");
    }

    [Test]
    public void FalseReportNeedsRoomToMove()
    {
        var w = TestData.NewWorld();
        var g = new Game(w, 1);
        var gate = w.Map.Strongholds["chenliu"].Tiles.First(t => t.Value == World.TileKind.Gate).Key;
        var defender = g.Field.Deploy("cao_cao", new[] { "cao_hong" }, "square", 3000, gate);
        var spot = gate.Neighbors().First(n => g.Field.UnitAt(n) == null && !g.Field.Structures.ContainsKey(n)
                                               && TestData.Data.Terrain[g.Field.Map.TerrainAt(n)].PassableLand);
        var schemer = g.Field.Deploy("dong_zhuo", new[] { "jia_xu" }, "square", 3000, spot);
        Assert.That(Stratagems.CanBeMisled(g.Field, defender), Is.False);
        Assert.Throws<InvalidOperationException>(() => Stratagems.Use(g.Field, schemer, "false_report", defender));
    }

    [Test]
    public void AmbushNeedsCoverHidesAndStrikesHarder()
    {
        var open = TestData.NewBattle();
        var u0 = open.Deploy("a", new[] { "zhang_ren" }, "fish_scale", 3000, new HexCoord(3, 3));
        Assert.Throws<InvalidOperationException>(() => Stratagems.Use(open, u0, "ambush"));

        var b = TestData.NewBattle(BattleMap.Rectangle(12, 12, "forest"));
        var hider = b.Deploy("a", new[] { "zhang_ren" }, "fish_scale", 3000, new HexCoord(3, 3));
        var foe = b.Deploy("b", new[] { "cao_hong" }, "fish_scale", 3000, new HexCoord(4, 3));
        double normal = Combat.Compute(b, hider, foe, 1, 0, 0, false).Total;
        Stratagems.Use(b, hider, "ambush");
        Assert.That(hider.Hidden);
        b.BeginTurn("a");
        var r = b.Attack(hider, foe);
        Assert.That(hider.Hidden, Is.False);
        Assert.That(r.Damage, Is.GreaterThan(normal * 1.3));
    }

    [Test]
    public void HiddenUnitsAreInvisibleToAi()
    {
        var g = new Game(TestData.NewWorld(), 1);
        var center = g.World.Map.Strongholds["chenliu"].Center;
        var spot = center.Neighbors().SelectMany(n => n.Neighbors()).SelectMany(n => n.Neighbors())
                         .First(h => g.Field.UnitAt(h) == null && !g.Field.Structures.ContainsKey(h)
                                     && TestData.Data.Terrain[g.Field.Map.TerrainAt(h)].PassableLand);
        var u = g.Field.Deploy("dong_zhuo", new[] { "zhang_ren" }, "fish_scale", 3000, spot);
        var k = new Knowledge("cao_cao");
        k.Update(g);
        Assert.That(k.VisibleEnemies(g), Has.Member(u));
        u.Hidden = true;
        Assert.That(k.VisibleEnemies(g), Has.No.Member(u));
    }
}

public class MissionTests
{
    private static WorldState RunUntil(Func<WorldState, bool> done, Action<WorldState> start, int seeds = 60)
    {
        for (ulong seed = 0; seed < (ulong)seeds; seed++)
        {
            var w = TestData.NewWorld(seed);
            start(w);
            for (int i = 0; i < 8; i++) { Missions.Tick(w); foreach (var o in w.Officers.Values) o.Acted = false; }
            if (done(w)) return w;
        }
        throw new AssertionException("임무가 한 번도 성공하지 않음");
    }

    [Test]
    public void RumorLowersMorale()
    {
        var w = RunUntil(w => w.Cities["chenliu"].Morale < 60, w => Missions.Start(w, "jia_xu", "rumor", "chenliu"));
        Assert.That(w.Cities["chenliu"].Morale, Is.LessThan(60));
    }

    [Test]
    public void CannotSchemeAgainstAllies()
    {
        var w = TestData.NewWorld();
        Assert.Throws<InvalidOperationException>(() => Missions.Start(w, "chen_gong", "bribe", null, "chen_gui"));
    }

    [Test]
    public void AgentIsBusyAndPaysGold()
    {
        var w = TestData.NewWorld();
        int gold = w.Cities.Values.Where(c => c.Owner == "dong_zhuo").Max(c => c.Gold);
        Missions.Start(w, "jia_xu", "spy", "chenliu");
        Assert.That(w.Cities.Values.Where(c => c.Owner == "dong_zhuo").Max(c => c.Gold), Is.EqualTo(gold - 300));
        w.Officers["jia_xu"].Acted = false;
        Assert.Throws<InvalidOperationException>(() => Domestic.Patrol(w, "jia_xu", "changan"));
    }

    [Test]
    public void BribeTakesDisloyalOfficer()
    {
        // 조조와 도겸은 반동탁 연합이라 서로 공작할 수 없다: 동탁 쪽 이각을 노린다
        var w = RunUntil(w => w.Officers["li_jue"].Faction == "cao_cao", w =>
        {
            w.Officers["li_jue"].Loyalty = 20;
            w.Cities["chenliu"].Gold = 10000;
            Missions.Start(w, "chen_gong", "bribe", null, "li_jue");
        });
        Assert.That(w.Officers["li_jue"].Location, Is.EqualTo(w.Factions["cao_cao"].Capital));
    }

    [Test]
    public void SpyReportRevealsExactTroops()
    {
        var w = RunUntil(w => w.HasSpyReport("dong_zhuo", "xiapi"), w => Missions.Start(w, "jia_xu", "spy", "xiapi"));
        var g = new Game(w, 1);
        var k = new Knowledge("dong_zhuo");
        k.Update(g);
        Assert.That(k.EstimateTroops(g, w.Cities["xiapi"]), Is.EqualTo(w.Cities["xiapi"].Troops));
    }

    [Test]
    public void InsiderOpensAGateWhenArmyArrives()
    {
        var w = TestData.NewWorld();
        var g = new Game(w, 1);
        w.Insiders["chenliu"] = "dong_zhuo";
        var city = w.Map.Strongholds["chenliu"];
        var spot = city.Center.Neighbors().SelectMany(n => n.Neighbors()).SelectMany(n => n.Neighbors())
                       .First(h => g.Field.UnitAt(h) == null && !g.Field.Structures.ContainsKey(h)
                                   && TestData.Data.Terrain[g.Field.Map.TerrainAt(h)].PassableLand);
        g.Field.Deploy("dong_zhuo", new[] { "hua_xiong" }, "fish_scale", 3000, spot);
        Missions.TriggerInsiders(g);
        Assert.That(city.Tiles.Where(t => t.Value == TileKind.Gate).Any(t => !g.Field.Structures[t.Key].Blocking));
        Assert.That(w.Insiders.ContainsKey("chenliu"), Is.False, "내응은 한 번만");
    }

    [Test]
    public void DisloyalAmbitiousOfficersMayLeave()
    {
        var w = TestData.NewWorld();
        w.Officers["lu_bu"].Loyalty = 5;
        for (int i = 0; i < 60 && w.Officers["lu_bu"].Status == OfficerStatus.Active; i++) Missions.Defections(w);
        Assert.That(w.Officers["lu_bu"].Status, Is.EqualTo(OfficerStatus.Free));
    }
}
