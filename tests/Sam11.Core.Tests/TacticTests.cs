namespace Sam11.Core.Tests;

public class TacticTests
{
    private static Battle.Battle Sure() => TestData.NewBattle(options: new BattleOptions { TacticSuccessOverride = 1.0 });

    [Test]
    public void SuccessfulTacticTriggersChainFromAdjacentAllies()
    {
        var b = Sure();
        var target = b.Deploy("b", new[] { "hua_xiong" }, "square", 7800, new HexCoord(5, 5));
        var lead = b.Deploy("a", new[] { "guan_yu" }, "fish_scale", 3000, new HexCoord(4, 5));
        b.Deploy("a", new[] { "zhang_fei" }, "fish_scale", 3000, new HexCoord(6, 5));
        b.Deploy("a", new[] { "liu_bei" }, "crane_wing", 3000, new HexCoord(5, 4));
        var r = b.UseTactic(lead, "breach", target);
        Assert.That(r.Success);
        Assert.That(target.IsAlive);
        Assert.That(r.ChainLinks, Is.EqualTo(2));
    }

    [Test]
    public void SteadfastBlocksChain()
    {
        var b = Sure();
        var target = b.Deploy("b", new[] { "cao_ren" }, "square", 9000, new HexCoord(5, 5)); // 방원 S
        b.BeginTurn("b");
        b.UseTactic(target, "steadfast", target);
        var lead = b.Deploy("a", new[] { "guan_yu" }, "fish_scale", 9000, new HexCoord(4, 5));
        b.Deploy("a", new[] { "zhang_fei" }, "fish_scale", 8000, new HexCoord(6, 5));
        var r = b.UseTactic(lead, "pierce", target);
        Assert.That(r.Success);
        Assert.That(r.ChainLinks, Is.EqualTo(0));
    }

    [Test]
    public void HoldFastRaisesDefense()
    {
        var b = Sure();
        var def = b.Deploy("b", new[] { "cao_ren" }, "square", 9000, new HexCoord(5, 5));
        var atk = b.Deploy("a", new[] { "zhang_fei" }, "fish_scale", 8000, new HexCoord(4, 5));
        double before = Combat.Compute(b, atk, def, 1, 0, 0, false).Traits;
        b.UseTactic(def, "hold_fast", def);
        double after = Combat.Compute(b, atk, def, 1, 0, 0, false).Traits;
        Assert.That(after, Is.LessThan(before));
    }

    [Test]
    public void ChargePushesTarget()
    {
        var b = Sure();
        var lu = b.Deploy("a", new[] { "lu_bu" }, "wedge", 8000, new HexCoord(3, 5), TestData.PlentyOfEquipment);
        var target = b.Deploy("b", new[] { "liu_bei" }, "crane_wing", 7000, new HexCoord(4, 5));
        b.UseTactic(lu, "charge", target);
        if (target.IsAlive)
            Assert.That(target.Position, Is.EqualTo(new HexCoord(6, 5)));
    }

    [Test]
    public void TacticCostsEnergyAndFailsWithoutIt()
    {
        var b = Sure();
        var lead = b.Deploy("a", new[] { "guan_yu" }, "fish_scale", 9000, new HexCoord(4, 5), energy: 20);
        var target = b.Deploy("b", new[] { "hua_xiong" }, "fish_scale", 7000, new HexCoord(5, 5));
        Assert.Throws<InvalidOperationException>(() => b.UseTactic(lead, "pierce", target)); // 25 필요
        b.UseTactic(lead, "breach", target); // 20
        Assert.That(lead.Energy, Is.EqualTo(0));
    }

    [Test]
    public void FireArrowDoesNothingInRain()
    {
        var b = TestData.NewBattle(options: new BattleOptions { TacticSuccessOverride = 1.0, Weather = "rain" });
        var archer = b.Deploy("a", new[] { "huang_zhong" }, "wild_geese", 8000, new HexCoord(3, 5), TestData.PlentyOfEquipment);
        var target = b.Deploy("b", new[] { "hua_xiong" }, "fish_scale", 7000, new HexCoord(5, 5));
        b.UseTactic(archer, "fire_arrow", target);
        Assert.That(b.Map.IsBurning(target.Position), Is.False);
    }
}
