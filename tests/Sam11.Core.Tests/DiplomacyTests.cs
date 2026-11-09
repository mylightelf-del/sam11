using Sam11.Core.AI;
using Sam11.Core.World;

namespace Sam11.Core.Tests;

public class DiplomacyTests
{
    private static Game NewGame(ulong seed = 1) => new(TestData.NewWorld(seed), seed);

    private static bool ProposeUntil(Game g, Proposal p, int tries = 40)
    {
        for (int i = 0; i < tries; i++)
        {
            g.World.Officers[p.Envoy].Acted = false;
            if (Diplomacy.Propose(g, p)) return true;
        }
        return false;
    }

    [Test]
    public void AllianceStopsHostilityAndBreakingCostsTrust()
    {
        var g = NewGame();
        var w = g.World;
        Assert.That(g.Field.Hostile("dong_zhuo", "ma_teng"));
        w.ChangeFriendship("dong_zhuo", "ma_teng", 25); // 동맹에는 우호도 70 이상이 필요
        Assert.That(ProposeUntil(g, new Proposal { Type = DiploType.Alliance, From = "dong_zhuo", To = "ma_teng", Envoy = "li_ru" }));
        Assert.That(g.Field.Hostile("dong_zhuo", "ma_teng"), Is.False);
        int trust = w.TrustOf("dong_zhuo");
        Diplomacy.Break(g, "dong_zhuo", "ma_teng");
        Assert.That(g.Field.Hostile("dong_zhuo", "ma_teng"));
        Assert.That(w.TrustOf("dong_zhuo"), Is.EqualTo(trust - 30));
    }

    [Test]
    public void CannotAttackUnitsOfAFactionAtPeace()
    {
        var g = NewGame();
        g.World.AddPact(PactKind.Truce, "cao_cao", "dong_zhuo", 6);
        var a = g.Field.Deploy("cao_cao", new[] { "xiahou_dun" }, "fish_scale", 3000, new HexCoord(60, 60));
        var b = g.Field.Deploy("dong_zhuo", new[] { "hua_xiong" }, "fish_scale", 3000, new HexCoord(61, 60));
        Assert.Throws<InvalidOperationException>(() => g.Field.Attack(a, b));
    }

    [Test]
    public void GiftRaisesFriendshipAndCostsGold()
    {
        var g = NewGame();
        var w = g.World;
        var cap = w.Cities[w.Factions["yuan_shao"].Capital];
        cap.Gold = 5000;
        Assert.That(Diplomacy.Propose(g, new Proposal { Type = DiploType.Gift, From = "yuan_shao", To = "han_fu", Envoy = "xun_chen", Gold = 2000 }));
        Assert.That(cap.Gold, Is.EqualTo(3000));
        Assert.That(w.FriendshipOf("yuan_shao", "han_fu"), Is.EqualTo(70));
    }

    [Test]
    public void VassalPaysTributeAndCanBeAbsorbed()
    {
        var g = NewGame();
        var w = g.World;
        w.Vassals["kong_rong"] = "tao_qian";
        int gold = w.Cities["xiapi"].Gold;
        Diplomacy.Monthly(g);
        Assert.That(w.Cities["xiapi"].Gold, Is.GreaterThan(gold));
        Assert.That(g.Field.Hostile("kong_rong", "tao_qian"), Is.False);
        for (int i = 0; i < 40; i++) w.ChangeFriendship("kong_rong", "tao_qian", 1);
        Assert.That(ProposeUntil(g, new Proposal { Type = DiploType.Absorb, From = "tao_qian", To = "kong_rong", Envoy = "mi_zhu" }));
        Assert.That(w.Factions["kong_rong"].Alive, Is.False);
        Assert.That(w.Cities["beihai"].Owner, Is.EqualTo("tao_qian"));
        Assert.That(w.Officers["kong_rong"].Faction, Is.EqualTo("tao_qian"));
    }

    [Test]
    public void AllianceNeedsEnoughFriendship()
    {
        var g = NewGame();
        var w = g.World;
        Assert.That(w.FriendshipOf("liu_bei", "gongsun_du"), Is.EqualTo(50));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Diplomacy.Propose(g, new Proposal { Type = DiploType.Alliance, From = "liu_bei", To = "gongsun_du", Envoy = "jian_yong" }));
        Assert.That(ex!.Message, Does.Contain("우호도"));
        w.ChangeFriendship("liu_bei", "gongsun_du", 20);
        w.AddPact(PactKind.Alliance, "liu_bei", "kong_rong", 12);
        w.AddPact(PactKind.Alliance, "liu_bei", "tao_qian", 12);
        // 우호도만 충분하면 동맹 수에는 제한이 없다
        Assert.That(ProposeUntil(g, new Proposal { Type = DiploType.Alliance, From = "liu_bei", To = "gongsun_du", Envoy = "jian_yong" }));
    }

    [Test]
    public void CoalitionFormsAgainstDominantFaction()
    {
        var g = NewGame();
        var w = g.World;
        w.Alliances.Clear();
        // 동탁에게 대도시들을 몰아줘 국력이 2위의 2배를 넘게 한다
        foreach (var id in new[] { "xuchang", "ye", "nanpi", "wan", "chenliu", "henei" })
        {
            w.Cities[id].Owner = "dong_zhuo";
            w.Cities[id].Troops = 30000;
        }
        Diplomacy.UpdateCoalition(g);
        var c = w.Alliances.FirstOrDefault(a => a.Kind == PactKind.Coalition);
        Assert.That(c, Is.Not.Null);
        Assert.That(c!.Target, Is.EqualTo("dong_zhuo"));
        Assert.That(c.Members.Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void AiAvoidsAllianceWithFarStrongerFaction()
    {
        var g = NewGame();
        var ai = new AiController();
        // 공융(1도시)은 자신을 삼킬 만큼 강한 동탁과는 손잡지 않는다
        Assert.That(ai.Wants(g, new Proposal { Type = DiploType.Alliance, From = "dong_zhuo", To = "kong_rong" }), Is.False);
        // 반대로 약한 공융이 도겸에게 청하면 도겸은 받는다
        Assert.That(ai.Wants(g, new Proposal { Type = DiploType.Alliance, From = "kong_rong", To = "tao_qian" }), Is.True);
    }
}

public class DiplomacyRegressionTests
{
    [Test]
    public void DeadVassalDoesNotCrashAi()
    {
        var w = TestData.NewWorld();
        var g = new Sam11.Core.World.Game(w, 1);
        w.Vassals["liu_bei"] = "cao_cao";
        w.Factions["liu_bei"].Alive = false;
        var ai = new Sam11.Core.AI.AiController(Sam11.Core.AI.AiConfig.For(Sam11.Core.AI.Difficulty.Normal));
        Assert.DoesNotThrow(() => ai.TakeTurn(g, "cao_cao"));
    }
}
