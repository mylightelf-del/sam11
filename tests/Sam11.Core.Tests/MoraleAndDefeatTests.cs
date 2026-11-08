namespace Sam11.Core.Tests;

public class MoraleAndDefeatTests
{
    [Test]
    public void ZeroMoraleConfusesAndConfusedUnitCannotAct()
    {
        var b = TestData.NewBattle();
        var u = b.Deploy("a", new[] { "cao_hong" }, "fish_scale", 5000, new HexCoord(3, 3), morale: 5);
        var e = b.Deploy("b", new[] { "hua_xiong" }, "fish_scale", 5000, new HexCoord(4, 3));
        b.LoseMorale(u, 10);
        Assert.That(u.Status, Is.EqualTo(UnitStatus.Confused));
        Assert.Throws<InvalidOperationException>(() => b.Attack(u, e));
    }

    [Test]
    public void LowMoraleReducesDamage()
    {
        var b = TestData.NewBattle();
        var hi = b.Deploy("a", new[] { "cao_hong" }, "fish_scale", 5000, new HexCoord(3, 3), morale: 90);
        var lo = b.Deploy("a", new[] { "cao_hong" }, "fish_scale", 5000, new HexCoord(6, 6), morale: 20);
        var e = b.Deploy("b", new[] { "hua_xiong" }, "fish_scale", 5000, new HexCoord(4, 3));
        Assert.That(Combat.Compute(b, hi, e, 1, 0, 0, false).Morale, Is.EqualTo(1.1));
        Assert.That(Combat.Compute(b, lo, e, 1, 0, 0, false).Morale, Is.EqualTo(0.8));
    }

    [Test]
    public void DeathOptionOffTurnsDeathIntoSeriousInjury()
    {
        var b = TestData.NewBattle(options: new BattleOptions { DeathEnabled = false });
        var u = b.Deploy("a", new[] { "cao_hong" }, "fish_scale", 5000, new HexCoord(3, 3));
        var p = b.FateProbabilities(u.Leader, u, surrounded: true, onFire: true);
        Assert.That(p.Select(x => x.Key), Does.Not.Contain(DefeatOutcome.Death));
        Assert.That(p.Sum(x => x.Value), Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void SurroundedRaisesCaptureChance()
    {
        var b = TestData.NewBattle();
        var u = b.Deploy("a", new[] { "cao_hong" }, "fish_scale", 5000, new HexCoord(3, 3));
        double Capture(bool s) => b.FateProbabilities(u.Leader, u, s, false).First(x => x.Key == DefeatOutcome.Capture).Value;
        Assert.That(Capture(true), Is.GreaterThan(Capture(false)));
    }

    [Test]
    public void DestroyedUnitOfficersGetFates()
    {
        var b = TestData.NewBattle(seed: 7);
        var strong = b.Deploy("a", new[] { "lu_bu" }, "fish_scale", 8800, new HexCoord(3, 3));
        var weak = b.Deploy("b", new[] { "cao_bao" }, "square", 100, new HexCoord(4, 3));
        var r = b.Attack(strong, weak);
        Assert.That(r.TargetDestroyed);
        Assert.That(b.Fates.Select(f => f.OfficerId), Does.Contain("cao_bao"));
    }
}

public class RegressionTests
{
    [Test]
    public void ConfusionSkipsExactlyOneOwnTurn()
    {
        var b = TestData.NewBattle();
        var u = b.Deploy("a", new[] { "zhang_liao" }, "fish_scale", 5000, new HexCoord(3, 3), morale: 1);
        var e = b.Deploy("b", new[] { "hua_xiong" }, "fish_scale", 5000, new HexCoord(4, 3));
        b.LoseMorale(u, 5); // 상대 턴에 혼란
        b.BeginTurn("a");   // 다음 자기 턴: 아직 혼란
        Assert.That(u.Status, Is.EqualTo(UnitStatus.Confused));
        Assert.Throws<InvalidOperationException>(() => b.Attack(u, e));
        b.BeginTurn("a");   // 그다음 자기 턴: 풀리거나(통솔로 사기 수습) 도주
        Assert.That(u.Status, Is.EqualTo(UnitStatus.Ready).Or.EqualTo(UnitStatus.Routed));
    }

    [Test]
    public void AttackLineIsLoggedBeforeDestruction()
    {
        var b = TestData.NewBattle();
        var strong = b.Deploy("a", new[] { "lu_bu" }, "fish_scale", 8800, new HexCoord(3, 3));
        var weak = b.Deploy("b", new[] { "cao_bao" }, "square", 100, new HexCoord(4, 3));
        b.Attack(strong, weak);
        int attackLine = b.Log.Lines.ToList().FindIndex(l => l.Contains("의 공격"));
        int destroyLine = b.Log.Lines.ToList().FindIndex(l => l.Contains("궤멸"));
        Assert.That(attackLine, Is.GreaterThanOrEqualTo(0));
        Assert.That(attackLine, Is.LessThan(destroyLine));
    }

    [Test]
    public void KoreanParticlesFollowBatchim()
    {
        Assert.That(Korean.IGa("유비 부대"), Is.EqualTo("유비 부대가"));
        Assert.That(Korean.IGa("관우"), Is.EqualTo("관우가"));
        Assert.That(Korean.IGa("손견"), Is.EqualTo("손견이"));
        Assert.That(Korean.EunNeun("장료"), Is.EqualTo("장료는"));
    }
}
