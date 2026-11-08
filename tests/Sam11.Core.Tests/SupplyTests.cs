using Sam11.Core.World;

namespace Sam11.Core.Tests;

public class SupplyTests
{
    private static (WorldState w, Battle.Battle b) Setup()
    {
        var w = TestData.NewWorld();
        return (w, SiegeSetup.CreateBattle(w, 1));
    }

    /// <summary>어느 도시 영역 안의, 비어 있는 평지 칸.</summary>
    private static HexCoord OpenHexIn(WorldState w, Battle.Battle b, string city) =>
        w.Map.Territory.Where(kv => kv.Value == city && w.Map.TerrainAt(kv.Key) == "plain" && b.UnitAt(kv.Key) == null
                                    && !b.Structures.ContainsKey(kv.Key))
                       .Select(kv => kv.Key).OrderBy(h => h.DistanceTo(w.Map.Strongholds[city].Center)).Skip(4).First();

    [Test]
    public void SortieTakesTroopsFoodAndEquipmentFromCity()
    {
        var (w, b) = Setup();
        var c = w.Cities["chenliu"];
        int troops = c.Troops, food = c.Food, horses = c.Equipment["horse"];
        var u = SiegeSetup.Sortie(w, b, "chenliu", new[] { "xiahou_yuan" }, "wedge", 3000, 3000);
        Assert.That(c.Troops, Is.EqualTo(troops - 3000));
        Assert.That(c.Food, Is.EqualTo(food - 3000));
        Assert.That(c.Equipment["horse"], Is.EqualTo(horses - 3000));
        Assert.That(u.Food, Is.EqualTo(3000));
        Assert.That(w.Officers["xiahou_yuan"].Acted);
        Assert.Throws<InvalidOperationException>(() => SiegeSetup.Sortie(w, b, "chenliu", new[] { "xiahou_yuan" }, "square", 100, 0));
    }

    [Test]
    public void NoFoodUsedInsideOwnTerritory()
    {
        var (w, b) = Setup();
        var u = SiegeSetup.Sortie(w, b, "chenliu", new[] { "cao_hong" }, "square", 3000, 900, OpenHexIn(w, b, "chenliu"));
        Assert.That(b.IsSupplied(u));
        b.BeginTurn("cao_cao");
        Assert.That(u.Food, Is.EqualTo(900));
    }

    [Test]
    public void FoodIsUsedInEnemyTerritory()
    {
        var (w, b) = Setup();
        var u = SiegeSetup.Sortie(w, b, "chenliu", new[] { "cao_hong" }, "square", 3000, 900, OpenHexIn(w, b, "luoyang"));
        Assert.That(b.IsSupplied(u), Is.False);
        b.BeginTurn("cao_cao");
        Assert.That(u.Food, Is.EqualTo(900 - 100)); // 3000명 × 0.1 ÷ 3턴
    }

    [Test]
    public void StarvingUnitLosesTroopsAndMorale()
    {
        var (w, b) = Setup();
        var u = SiegeSetup.Sortie(w, b, "chenliu", new[] { "cao_hong" }, "square", 3000, 0, OpenHexIn(w, b, "luoyang"));
        int morale = u.Morale;
        b.BeginTurn("cao_cao");
        Assert.That(u.Troops, Is.LessThan(3000));
        Assert.That(u.Morale, Is.LessThan(morale));
    }

    [Test]
    public void BlockadeCutsSupply()
    {
        var (w, b) = Setup();
        var city = w.Map.Strongholds["chenliu"];
        var inside = SiegeSetup.Sortie(w, b, "chenliu", new[] { "cao_hong" }, "square", 3000, 900, OpenHexIn(w, b, "chenliu"));
        Assert.That(b.IsSupplied(inside));
        // 성 둘레 칸 절반 이상을 동탁군으로 채운다
        var ring = city.Tiles.Keys.SelectMany(h => h.Neighbors()).Where(n => !city.Tiles.ContainsKey(n)).Distinct()
                       .Where(n => TestData.Data.Terrain.TryGetValue(b.Map.TerrainAt(n), out var t) && t.PassableLand).ToList();
        int need = (int)Math.Ceiling(ring.Count * 0.5);
        foreach (var h in ring.Where(h => b.UnitAt(h) == null).Take(need))
            b.Deploy("dong_zhuo", new[] { "niu_fu" }, "square", 1000, h);
        Assert.That(b.IsBlockaded("chenliu"));
        Assert.That(b.IsSupplied(inside), Is.False);
    }

    [Test]
    public void WithdrawReturnsEverythingToCity()
    {
        var (w, b) = Setup();
        var c = w.Cities["chenliu"];
        int troops = c.Troops, horses = c.Equipment["horse"];
        var u = SiegeSetup.Sortie(w, b, "chenliu", new[] { "xiahou_yuan" }, "wedge", 3000, 500);
        SiegeSetup.Withdraw(w, b, u, "chenliu");
        Assert.That(c.Troops, Is.EqualTo(troops));
        Assert.That(c.Equipment["horse"], Is.EqualTo(horses));
        Assert.That(w.Officers["xiahou_yuan"].Location, Is.EqualTo("chenliu"));
        Assert.That(u.Status, Is.EqualTo(UnitStatus.Withdrawn));
    }
}
