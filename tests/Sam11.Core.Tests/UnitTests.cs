namespace Sam11.Core.Tests;

public class UnitTests
{
    private static UnitRules Rules => TestData.Data.Rules.Unit;

    [Test]
    public void SubOfficerOnlyFillsLeaderWeakness()
    {
        // 설계서 예시: 주장 무력 60, 부장 무력 95 → 67
        Assert.That(UnitStats.Combine(60, new[] { 95 }, Rules), Is.EqualTo(67));
        // 주장보다 낮은 부장은 보정 없음
        Assert.That(UnitStats.Combine(90, new[] { 50, 70 }, Rules), Is.EqualTo(90));
        // 상한 100
        Assert.That(UnitStats.Combine(98, new[] { 100, 100 }, Rules), Is.LessThanOrEqualTo(100));
    }

    [Test]
    public void MaxTroopsIsLeadershipTimes100PlusRank()
    {
        var d = TestData.Data;
        var o = d.Officers["cao_hong"]; // 통솔 72
        Assert.That(UnitStats.MaxTroops(o, null, Rules), Is.EqualTo(7200));
        Assert.That(UnitStats.MaxTroops(o, d.Ranks["general_four"], Rules), Is.EqualTo(10200));
    }

    [Test]
    public void AptitudeIsBestAmongMembers()
    {
        var b = TestData.NewBattle();
        // 유비 추행 A + 장비 추행 S → S
        var u = b.Deploy("liu", new[] { "liu_bei", "zhang_fei" }, "wedge", 5000, new HexCoord(0, 0), TestData.PlentyOfEquipment);
        Assert.That(u.Aptitude, Is.EqualTo(AptitudeRank.S));
        Assert.That(b.AvailableTactics(u).Count(), Is.EqualTo(3));
    }

    [TestCase("guan_yu", "fish_scale", 3)]   // S
    [TestCase("guan_yu", "crane_wing", 2)]   // A
    [TestCase("li_jue", "crane_wing", 1)]    // B
    public void TacticCountFollowsAptitude(string officer, string formation, int expected)
    {
        var b = TestData.NewBattle();
        var u = b.Deploy("x", new[] { officer }, formation, 3000, new HexCoord(0, 0));
        Assert.That(b.AvailableTactics(u).Count(), Is.EqualTo(expected));
    }

    [Test]
    public void CannotDeployFormationWithAptitudeC()
    {
        var b = TestData.NewBattle();
        // 동탁 공성진 C
        var ex = Assert.Throws<InvalidOperationException>(() =>
            b.Deploy("dong", new[] { "dong_zhuo" }, "siege", 3000, new HexCoord(0, 0), TestData.PlentyOfEquipment));
        Assert.That(ex!.Message, Does.Contain("적성이 C"));
    }

    [Test]
    public void EquipmentFormationNeedsEquipment()
    {
        var b = TestData.NewBattle();
        var noHorses = new Dictionary<string, int> { ["horse"] = 1000 };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            b.Deploy("lu", new[] { "lu_bu" }, "wedge", 5000, new HexCoord(0, 0), noHorses));
        Assert.That(ex!.Message, Does.Contain("병장기"));
        // 기본 진형은 병장기 없이 가능
        Assert.DoesNotThrow(() => b.Deploy("lu", new[] { "lu_bu" }, "fish_scale", 5000, new HexCoord(0, 0)));
    }

    [Test]
    public void CannotExceedMaxTroops()
    {
        var b = TestData.NewBattle();
        Assert.Throws<InvalidOperationException>(() =>
            b.Deploy("x", new[] { "cao_hong" }, "fish_scale", 8000, new HexCoord(0, 0)));
    }
}
