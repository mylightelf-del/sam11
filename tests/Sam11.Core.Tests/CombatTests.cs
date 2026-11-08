namespace Sam11.Core.Tests;

public class CombatTests
{
    private static (Battle.Battle b, Unit atk, Unit def) Duel(string atkFormation, string defFormation,
        BattleMap? map = null, HexCoord? atkPos = null, HexCoord? defPos = null)
    {
        var b = TestData.NewBattle(map);
        // 같은 무장끼리 붙여 능력치 영향을 없앤다 (서영: 기본 진형 전부 A)
        var atk = b.Deploy("a", new[] { "zhang_liao" }, atkFormation, 5000, atkPos ?? new HexCoord(2, 2), TestData.PlentyOfEquipment);
        var def = b.Deploy("b", new[] { "zhang_liao" }, defFormation, 5000, defPos ?? new HexCoord(3, 2), TestData.PlentyOfEquipment);
        return (b, atk, def);
    }

    [Test]
    public void AffinityAppliesToDamage()
    {
        var (b, atk, def) = Duel("square", "fish_scale"); // 방원 → 어린 우위
        Assert.That(Combat.Compute(b, atk, def, 1, 0, 0, false).Affinity, Is.EqualTo(1.2));
        Assert.That(Combat.Compute(b, def, atk, 1, 0, 0, false).Affinity, Is.EqualTo(0.8));
    }

    [Test]
    public void FormationStatsApply()
    {
        var (b, atk, def) = Duel("fish_scale", "square"); // 어린 공격 120, 방원 방어 130
        Assert.That(Combat.Compute(b, atk, def, 1, 0, 0, false).Formation, Is.EqualTo(1.2 / 1.3).Within(1e-9));
    }

    [Test]
    public void TerrainModifiersApplyPerFormation()
    {
        var map = BattleMap.Rectangle(12, 12);
        map.Set(new HexCoord(2, 2), "forest");
        var (b, atk, def) = Duel("wedge", "wedge", map);
        // 추행: 숲 70 / 평지 120
        Assert.That(Combat.Compute(b, atk, def, 1, 0, 0, false).Terrain, Is.EqualTo(0.7 / 1.2).Within(1e-9));
    }

    [Test]
    public void HighGroundGivesBonus()
    {
        var map = BattleMap.Rectangle(12, 12);
        map.Set(new HexCoord(2, 2), "mountain");
        var (b, atk, def) = Duel("wild_geese", "fish_scale", map);
        // 안행 산 120 / 어린 평지 100, 고지 +10%
        Assert.That(Combat.Compute(b, atk, def, 1, 0, 0, false).Terrain, Is.EqualTo(1.2 * 1.1).Within(1e-9));
    }

    [Test]
    public void TraitModifiersApplyOnlyToMatchingFormation()
    {
        var b = TestData.NewBattle();
        // 공손찬: 기병술(추행 +15%) + 신궁(안행 +15%)
        var wedge = b.Deploy("a", new[] { "gongsun_zan" }, "wedge", 5000, new HexCoord(2, 2), TestData.PlentyOfEquipment);
        var square = b.Deploy("a", new[] { "gongsun_zan" }, "square", 5000, new HexCoord(5, 5));
        var target = b.Deploy("b", new[] { "gongsun_yue" }, "long_snake", 5000, new HexCoord(3, 2));
        Assert.That(Combat.Compute(b, wedge, target, 1, 0, 0, false).Traits, Is.EqualTo(1.15).Within(1e-9));
        Assert.That(Combat.Compute(b, square, target, 1, 0, 0, false).Traits, Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void ChainBonusIsCapped()
    {
        var (b, atk, def) = Duel("fish_scale", "fish_scale");
        Assert.That(Combat.Compute(b, atk, def, 1, 1, 0, false).Chain, Is.EqualTo(1.1).Within(1e-9));
        Assert.That(Combat.Compute(b, atk, def, 1, 9, 0, false).Chain, Is.EqualTo(1.5).Within(1e-9));
    }

    [Test]
    public void MeleeAttackTakesCounterRangedDoesNot()
    {
        var (b, atk, def) = Duel("fish_scale", "long_snake");
        Assert.That(b.Attack(atk, def).CounterDamage, Is.GreaterThan(0));

        var (b2, archer, target) = Duel("wild_geese", "long_snake", atkPos: new HexCoord(1, 2));
        Assert.That(b2.Attack(archer, target).CounterDamage, Is.EqualTo(0));
    }

    [Test]
    public void SameSeedGivesSameBattle()
    {
        string Run(ulong seed)
        {
            var b = TestData.NewBattle(seed: seed);
            var x = b.Deploy("a", new[] { "lu_bu" }, "fish_scale", 8000, new HexCoord(2, 2));
            var y = b.Deploy("b", new[] { "guan_yu", "zhang_fei" }, "long_snake", 9000, new HexCoord(3, 2));
            for (int i = 0; i < 5 && x.IsAlive && y.IsAlive; i++)
            {
                b.BeginTurn("a");
                if (x.Status == UnitStatus.Ready)
                {
                    if (x.Energy >= 20) b.UseTactic(x, "breach", y); else b.Attack(x, y);
                }
                b.BeginTurn("b"); if (y.IsAlive && y.Status == UnitStatus.Ready && x.IsAlive) b.Attack(y, x);
                b.EndRound();
            }
            return string.Join("\n", b.Log.Lines);
        }
        Assert.That(Run(42), Is.EqualTo(Run(42)));
        Assert.That(Run(42), Is.Not.EqualTo(Run(43)));
    }
}
