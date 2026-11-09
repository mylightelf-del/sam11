namespace Sam11.Core.Tests;

public class DataTests
{
    [Test]
    public void LoadsAllBaseData()
    {
        var d = TestData.Data;
        Assert.That(d.Officers.Count, Is.GreaterThanOrEqualTo(280));
        Assert.That(d.Formations, Has.Count.EqualTo(8));
        Assert.That(d.Tactics, Has.Count.EqualTo(24));
        Assert.That(d.Officers["cao_cao"].Stats.Ldr, Is.EqualTo(97));
        Assert.That(d.Officers["lu_bu"].AptitudeFor("wedge"), Is.EqualTo(AptitudeRank.S));
    }

    // 04 군사·전투 설계서 4.2 상성 행렬 (행 = 공격측). +1 우위, -1 열세, 0 무관
    private static readonly string[] Land = { "fish_scale", "crane_wing", "square", "long_snake", "wedge", "wild_geese", "siege" };
    private static readonly int[,] Matrix =
    {
        //        어린 학익 방원 장사 추행 안행 공성
        /*어린*/ { 0,  1, -1,  0,  0, -1,  1 },
        /*학익*/ { -1, 0,  0,  1,  0, -1,  1 },
        /*방원*/ { 1,  0,  0, -1,  1,  0,  1 },
        /*장사*/ { 0, -1,  1,  0,  0,  0,  1 },
        /*추행*/ { 0,  0, -1,  0,  0,  1,  1 },
        /*안행*/ { 1,  1,  0,  0, -1,  0,  1 },
        /*공성*/ { -1, -1, -1, -1, -1, -1, 0 },
    };

    [Test]
    public void AffinityMatchesDesignMatrix()
    {
        var d = TestData.Data;
        for (int a = 0; a < Land.Length; a++)
            for (int b = 0; b < Land.Length; b++)
            {
                double expected = Matrix[a, b] switch { 1 => 1.2, -1 => 0.8, _ => 1.0 };
                Assert.That(d.AffinityMultiplier(Land[a], Land[b]), Is.EqualTo(expected),
                    $"{Land[a]} → {Land[b]}");
            }
    }

    [Test]
    public void EveryCommanderCanDeploySomeFormation()
    {
        foreach (var o in TestData.Data.Officers.Values.Where(o => o.Stats.Ldr >= 50))
            Assert.That(o.Aptitude.Values.Any(r => r != AptitudeRank.C), $"{o.Name.Ko}");
    }

    [Test]
    public void EachFormationHasThreeTacticsOrderedByRank()
    {
        foreach (var f in TestData.Data.Formations.Values)
        {
            var ranks = f.Tactics.Select(t => TestData.Data.Tactics[t].RequiredRank).ToList();
            Assert.That(ranks, Is.EqualTo(new[] { AptitudeRank.B, AptitudeRank.A, AptitudeRank.S }), f.Id);
        }
    }
}
