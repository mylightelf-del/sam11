namespace Sam11.Core.Tests;

public class MovementTests
{
    [Test]
    public void WedgeCannotEnterMountain()
    {
        var map = BattleMap.Rectangle(12, 12);
        map.Set(new HexCoord(4, 3), "mountain");
        var b = TestData.NewBattle(map);
        var u = b.Deploy("a", new[] { "lu_bu" }, "wedge", 5000, new HexCoord(3, 3), TestData.PlentyOfEquipment);
        Assert.That(b.Reachable(u).ContainsKey(new HexCoord(4, 3)), Is.False);
        // 보병 진형(방원)은 인접한 산에 들어갈 수 있다
        var foot = b.Deploy("a", new[] { "zhang_liao" }, "square", 5000, new HexCoord(3, 4));
        Assert.That(b.Reachable(foot).ContainsKey(new HexCoord(4, 3)));
    }

    [Test]
    public void ZoneOfControlStopsMovement()
    {
        // 한 줄짜리 좁은 길: 우회로가 없다
        var b = TestData.NewBattle(BattleMap.Rectangle(15, 1));
        var u = b.Deploy("a", new[] { "lu_bu" }, "wedge", 5000, new HexCoord(0, 0), TestData.PlentyOfEquipment);
        b.Deploy("b", new[] { "liu_bei" }, "square", 5000, new HexCoord(4, 0));
        var reach = b.Reachable(u);
        // 적에게 인접한 칸(3,0)까지는 가지만, 적을 지나쳐 뒤로 갈 수 없다
        Assert.That(reach.ContainsKey(new HexCoord(3, 0)));
        Assert.That(reach.ContainsKey(new HexCoord(5, 0)), Is.False);
    }

    [Test]
    public void EnteringEnemyZoneEndsMovement()
    {
        // 넓은 맵: 적에게 인접한 칸에 들어서면 멈추므로, 적 바로 뒤 칸은 인접 칸을 피해 크게 돌아가야만 닿는다
        var b = TestData.NewBattle(BattleMap.Rectangle(15, 5));
        var u = b.Deploy("a", new[] { "lu_bu" }, "wedge", 5000, new HexCoord(0, 2), TestData.PlentyOfEquipment);
        b.Deploy("b", new[] { "liu_bei" }, "square", 5000, new HexCoord(3, 2));
        var reach = b.Reachable(u);
        Assert.That(reach.ContainsKey(new HexCoord(2, 2)));
        Assert.That(reach[new HexCoord(4, 2)], Is.GreaterThan(reach[new HexCoord(2, 2)] + 2));
    }

    [Test]
    public void ForcedMarchAddsMovement()
    {
        var b = TestData.NewBattle();
        var normal = b.Deploy("a", new[] { "cao_hong" }, "fish_scale", 3000, new HexCoord(0, 0));
        var fast = b.Deploy("a", new[] { "xiahou_yuan" }, "fish_scale", 3000, new HexCoord(5, 5)); // 급행
        Assert.That(b.MovePoints(fast), Is.EqualTo(b.MovePoints(normal) + 1));
    }

    [Test]
    public void RainSlowsMovementOffRoad()
    {
        var map = BattleMap.Rectangle(12, 12, "forest");
        var dry = TestData.NewBattle(map);
        var u1 = dry.Deploy("a", new[] { "cao_hong" }, "fish_scale", 3000, new HexCoord(3, 3));
        var wet = TestData.NewBattle(BattleMap.Rectangle(12, 12, "forest"), options: new BattleOptions { Weather = "rain" });
        var u2 = wet.Deploy("a", new[] { "cao_hong" }, "fish_scale", 3000, new HexCoord(3, 3));
        Assert.That(wet.Reachable(u2).Count, Is.LessThan(dry.Reachable(u1).Count));
    }
}
