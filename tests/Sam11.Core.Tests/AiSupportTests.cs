using Newtonsoft.Json.Linq;
using Sam11.Core.AI;
using Sam11.Core.World;

namespace Sam11.Core.Tests;

public class TreasureTests
{
    [Test]
    public void TreasuresRaiseStatsAndGrantTraitsInBattle()
    {
        var w = TestData.NewWorld();
        var g = new Game(w, 1);
        var plain = TestData.Data.Officers["lu_bu"];
        var eff = Treasures.Effective(w, "lu_bu"); // 방천화극 + 적토마
        Assert.That(eff.Stats.War, Is.EqualTo(100), "상한 100");
        Assert.That(eff.Stats.Ldr, Is.GreaterThanOrEqualTo(plain.Stats.Ldr));
        Assert.That(eff.Traits, Does.Contain("forced_march"));
        var u = g.Field.Deploy("dong_zhuo", new[] { "lu_bu" }, "fish_scale", 3000, new HexCoord(60, 60));
        Assert.That(u.Traits.Select(t => t.Id), Does.Contain("forced_march"), "전장 부대도 보물 특기를 받는다");
    }

    [Test]
    public void BestowRaisesLoyaltyAndReturnsSameSlotToVault()
    {
        var w = TestData.NewWorld();
        w.TreasureVault["blue_steel"] = "dong_zhuo";
        int loyalty = w.Officers["lu_bu"].Loyalty;
        Treasures.Bestow(w, "blue_steel", "lu_bu"); // 무기: 방천화극은 보관고로
        Assert.That(w.TreasureOwner["blue_steel"], Is.EqualTo("lu_bu"));
        Assert.That(w.TreasureVault["sky_piercer"], Is.EqualTo("dong_zhuo"));
        Assert.That(w.Officers["lu_bu"].Loyalty, Is.EqualTo(loyalty + 15));
        Assert.Throws<InvalidOperationException>(() => Treasures.Bestow(w, "green_dragon", "lu_bu"), "남의 세력 보물");
    }

    [Test]
    public void CaptorSeizesTreasures()
    {
        var w = TestData.NewWorld();
        var g = new Game(w, 1);
        g.Field.Fates.Add(new OfficerFate { OfficerId = "guan_yu", Outcome = DefeatOutcome.Capture, Faction = "liu_bei", By = "dong_zhuo" });
        g.Resolve();
        Assert.That(w.TreasureOwner["green_dragon"], Is.Null);
        Assert.That(w.TreasureVault["green_dragon"], Is.EqualTo("dong_zhuo"));
    }
}

public class AiSupportTests
{
    [Test]
    public void EventChoiceLeansOnPersonality()
    {
        var w = TestData.NewWorld();
        var g = new Game(w, 1);
        var ai = new AiController();
        g.Controllers["sun_jian"] = ai;
        var ev = w.Events.First(e => e.Id == "imperial_seal"); // 숨기기(신용 변화 없음, 우호 -30) / 바치기(우호 +20, 신용 +5)
        int keep = 0;
        for (ulong s = 0; s < 400; s++)
        {
            var w2 = TestData.NewWorld(s);
            if (ai.Choose(new Game(w2, s), ev, "sun_jian").Id == "keep") keep++;
        }
        double keepRate = keep / 400.0;
        // 데이터 가중치는 7:3. 손견(신의 6, 외교 4)은 바치기를 조금 덜 좋아하고 숨기기를 조금 더 고른다
        Assert.That(keepRate, Is.InRange(0.6, 0.9));
    }

    [Test]
    public void FaithfulRulerReleasesCaptives()
    {
        var w = TestData.NewWorld();
        var g = new Game(w, 1);
        var ai = new AiController();
        g.Controllers["liu_bei"] = ai; // 신의 10
        var c = w.Officers["hua_xiong"];
        c.Status = OfficerStatus.Captive; c.Faction = "liu_bei"; c.PreviousFaction = "dong_zhuo"; c.CaptiveMonths = 3;
        g.PlayTurn();
        Assert.That(c.Status, Is.EqualTo(OfficerStatus.Free));
        Assert.That(ai.Stats.Releases, Is.EqualTo(1));
    }

    [Test]
    public void CruelRulerExecutesOnlyWithDeathOption()
    {
        var w = TestData.NewWorld();
        var g = new Game(w, 1);
        g.Controllers["dong_zhuo"] = new AiController(); // 공격성 9, 신의 2
        var c = w.Officers["guan_yu"];
        c.Status = OfficerStatus.Captive; c.Faction = "dong_zhuo"; c.PreviousFaction = "liu_bei"; c.CaptiveMonths = 3;
        g.PlayTurn();
        Assert.That(c.Status, Is.EqualTo(OfficerStatus.Dead));

        var w2 = TestData.NewWorld();
        var g2 = new Game(w2, 1);
        g2.Field.Options.DeathEnabled = false;
        g2.Controllers["dong_zhuo"] = new AiController();
        var c2 = w2.Officers["guan_yu"];
        c2.Status = OfficerStatus.Captive; c2.Faction = "dong_zhuo"; c2.PreviousFaction = "liu_bei"; c2.CaptiveMonths = 3;
        g2.PlayTurn();
        Assert.That(c2.Status, Is.Not.EqualTo(OfficerStatus.Dead), "전사 옵션 OFF면 처단하지 않는다");
    }

    [Test]
    public void AiInterceptsInvaderInsideItsTerritory()
    {
        var w = TestData.NewWorld();
        var g = new Game(w, 1);
        var ai = new AiController();
        g.Controllers["cao_cao"] = ai;
        var city = w.Map.Strongholds["chenliu"];
        var spot = w.Map.Territory.Where(kv => kv.Value == "chenliu" && kv.Key.DistanceTo(city.Center) is >= 5 and <= 7
                                               && g.Field.UnitAt(kv.Key) == null && !g.Field.Structures.ContainsKey(kv.Key)
                                               && TestData.Data.Terrain[g.Field.Map.TerrainAt(kv.Key)].PassableLand)
                                  .Select(kv => kv.Key).First();
        g.Field.Deploy("dong_zhuo", new[] { "niu_fu" }, "fish_scale", 2000, spot);
        w.Cities["chenliu"].Troops = 20000;
        g.PlayTurn();
        Assert.That(ai.Stats.Intercepts, Is.GreaterThanOrEqualTo(1));
    }
}
