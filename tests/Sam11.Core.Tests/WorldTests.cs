using Sam11.Core.World;

namespace Sam11.Core.Tests;

public class WorldMapTests
{
    [Test]
    public void AllStrongholdsArePlacedWithExpectedShapes()
    {
        var m = TestData.Map;
        Assert.That(m.Strongholds, Has.Count.EqualTo(TestData.Data.Strongholds.Count));
        foreach (var s in TestData.Data.Strongholds.Values)
        {
            var p = m.Strongholds[s.Id];
            int expected = s.Type switch { "major_city" => 19, "city" or "fortress" => 7, _ => -1 };
            if (expected > 0) Assert.That(p.Tiles, Has.Count.EqualTo(expected), s.Id);
            if (s.Type is "major_city" or "city" or "fortress" or "gate")
                Assert.That(p.Tiles.Values, Has.Member(TileKind.Gate), $"{s.Id}에 성문이 없음");
            Assert.That(s.Hex!.ToHex(), Is.EqualTo(p.Center), s.Id);
        }
    }

    [Test]
    public void CitiesHaveFacilitySlotsInsideOwnTerritory()
    {
        var m = TestData.Map;
        foreach (var s in TestData.Data.Strongholds.Values.Where(s => s.IsCity))
        {
            var p = m.Strongholds[s.Id];
            Assert.That(p.Slots.Count, Is.GreaterThanOrEqualTo(s.FacilitySlots * 3 / 4), s.Id);
            foreach (var slot in p.Slots)
                Assert.That(m.Territory[slot.Hex], Is.EqualTo(s.Id));
        }
    }

    [Test]
    public void ConvertsToBattleMap()
    {
        var b = TestData.Map.ToBattleMap();
        var luoyang = TestData.Map.Strongholds["luoyang"].Center;
        Assert.That(b.TerrainAt(luoyang), Is.EqualTo("street"));
    }
}

public class ScenarioTests
{
    [Test]
    public void Loads190Scenario()
    {
        var w = TestData.NewWorld();
        Assert.That(w.Date.ToString(), Is.EqualTo("190년 1월 상순"));
        Assert.That(w.Factions, Has.Count.EqualTo(14));
        Assert.That(w.Cities, Has.Count.EqualTo(TestData.Data.Strongholds.Count));
        Assert.That(w.Officers["cao_cao"].Location, Is.EqualTo("chenliu"));
        Assert.That(w.Cities["luoyang"].Owner, Is.EqualTo("dong_zhuo"));
    }

    [Test]
    public void GatesAreWeakerThanWalls()
    {
        var c = TestData.NewWorld().Cities["luoyang"];
        int wall = c.Structures.Values.First(s => s.Kind == TileKind.Wall).MaxDurability;
        int gate = c.Structures.Values.First(s => s.Kind == TileKind.Gate).MaxDurability;
        Assert.That(gate, Is.LessThan(wall));
    }
}

public class DomesticTests
{
    private static void AdvanceToNextMonth(WorldState w)
    {
        int m = w.Date.Month;
        while (w.Date.Month == m) Domestic.AdvanceTurn(w);
    }

    [Test]
    public void GoldComesInMonthly()
    {
        var w = TestData.NewWorld();
        var c = w.Cities["chenliu"];
        int before = c.Gold, expected = Domestic.MonthlyGold(w, c);
        Domestic.AdvanceTurn(w);
        Assert.That(c.Gold, Is.EqualTo(before), "중순에는 수입 없음");
        AdvanceToNextMonth(w);
        Assert.That(c.Gold, Is.EqualTo(before + expected));
    }

    [Test]
    public void HarvestOnlyInSeptember()
    {
        var w = TestData.NewWorld();
        var c = w.Cities["chenliu"];
        w.Date.Month = 7; w.Date.Turn = 3;
        int food = c.Food, use = Domestic.MonthlyFoodUse(w, c);
        Domestic.AdvanceTurn(w); // 8월: 소비만
        Assert.That(c.Food, Is.EqualTo(food - use));
        w.Date.Turn = 3;
        int before = c.Food, harvest = Domestic.Harvest(w, c);
        Domestic.AdvanceTurn(w); // 9월: 소비 + 수확
        Assert.That(c.Food, Is.EqualTo(Math.Min(Domestic.FoodCap(w, c), before - Domestic.MonthlyFoodUse(w, c) + harvest)));
        Assert.That(w.Log.Any(l => l.Contains("가을 수확")));
    }

    [Test]
    public void RecruitTurnsPopulationIntoTroops()
    {
        var w = TestData.NewWorld();
        var c = w.Cities["chenliu"];
        int pop = c.Population, troops = c.Troops, morale = c.Morale;
        Domestic.Recruit(w, "cao_cao", "chenliu");
        int gained = c.Troops - troops;
        Assert.That(gained, Is.GreaterThan(0));
        Assert.That(c.Population, Is.EqualTo(pop - gained));
        Assert.That(c.Morale, Is.LessThan(morale));
        Assert.Throws<InvalidOperationException>(() => Domestic.Recruit(w, "cao_cao", "chenliu"), "한 턴에 두 번 행동 불가");
    }

    [Test]
    public void OfficerMustBeInCityOfOwnFaction()
    {
        var w = TestData.NewWorld();
        Assert.Throws<InvalidOperationException>(() => Domestic.Patrol(w, "cao_cao", "luoyang"));
        Assert.Throws<InvalidOperationException>(() => Domestic.Patrol(w, "xun_yu", "xuchang")); // 재야
    }

    [Test]
    public void BuildingCompletesAfterTurnsAndAddsIncome()
    {
        var w = TestData.NewWorld();
        var c = w.Cities["chenliu"];
        int income = Domestic.MonthlyGold(w, c);
        Domestic.Build(w, "cao_cao", "chenliu", 0, "market");
        Assert.That(Domestic.MonthlyGold(w, c), Is.EqualTo(income), "공사 중에는 효과 없음");
        int turns = c.Facilities[0].BuildTurnsLeft;
        for (int i = 0; i < turns; i++) Domestic.AdvanceTurn(w);
        Assert.That(c.Facilities[0].Active);
        Assert.That(Domestic.MonthlyGold(w, c), Is.GreaterThan(income));
    }

    [Test]
    public void MintNextToMarketBoostsIncome()
    {
        var w = TestData.NewWorld();
        var c = w.Cities["chenliu"];
        var slots = c.Placement.Slots;
        var pair = (from a in slots from b in slots
                    where a.Index < b.Index && a.Hex.DistanceTo(b.Hex) == 1 select (a, b)).First();
        var far = slots.First(s => s.Hex.DistanceTo(pair.a.Hex) > 1);
        c.Facilities[pair.a.Index] = new FacilityState { FacilityId = "market", Level = 1 };
        double alone = Domestic.FacilityTotal(w, c, "market", "gold_income");
        c.Facilities[far.Index] = new FacilityState { FacilityId = "mint", Level = 1 };
        Assert.That(Domestic.FacilityTotal(w, c, "market", "gold_income"), Is.EqualTo(alone), "떨어진 조폐는 효과 없음");
        c.Facilities.Remove(far.Index);
        c.Facilities[pair.b.Index] = new FacilityState { FacilityId = "mint", Level = 1 };
        Assert.That(Domestic.FacilityTotal(w, c, "market", "gold_income"), Is.EqualTo(alone * 1.5));
    }

    [Test]
    public void ShipyardNeedsWatersideSlot()
    {
        var w = TestData.NewWorld();
        var c = w.Cities["chenliu"];
        var dry = c.Placement.Slots.FirstOrDefault(s => !s.Waterside);
        Assume.That(dry, Is.Not.Null);
        var ex = Assert.Throws<InvalidOperationException>(() => Domestic.Build(w, "cao_cao", "chenliu", dry!.Index, "shipyard"));
        Assert.That(ex!.Message, Does.Contain("물가"));
    }

    [Test]
    public void ProductionNeedsFacility()
    {
        var w = TestData.NewWorld();
        var c = w.Cities["chenliu"];
        Assert.Throws<InvalidOperationException>(() => Domestic.Produce(w, "cao_cao", "chenliu", "horse"));
        c.Facilities[0] = new FacilityState { FacilityId = "stable", Level = 1 };
        int horses = c.Equipment["horse"];
        Domestic.Produce(w, "cao_cao", "chenliu", "horse");
        Assert.That(c.Equipment["horse"], Is.GreaterThan(horses + 1000));
    }

    [Test]
    public void FoodIsCheapAfterHarvestAndDearBefore()
    {
        var w = TestData.NewWorld();
        w.Date.Month = 8;
        double august = Domestic.FoodPerGold(w);
        w.Date.Month = 10;
        Assert.That(Domestic.FoodPerGold(w), Is.GreaterThan(august));
    }

    [Test]
    public void StarvingGarrisonDeserts()
    {
        var w = TestData.NewWorld();
        var c = w.Cities["chenliu"];
        c.Food = 0;
        int troops = c.Troops;
        AdvanceToNextMonth(w);
        Assert.That(c.Troops, Is.LessThan(troops));
        Assert.That(w.Log.Any(l => l.Contains("탈영")));
    }

    [Test]
    public void AgricultureGovernorBoostsHarvest()
    {
        var w = TestData.NewWorld();
        var c = w.Cities["xiapi"];
        c.Governor = "tao_qian"; // 인덕
        int plain = Domestic.Harvest(w, c);
        c.Governor = "chen_deng"; // 농정 +15%
        Assert.That(Domestic.Harvest(w, c), Is.EqualTo((int)Math.Round(plain * 1.15)).Within(1));
    }

    [Test]
    public void PopulationGrowsWithMoraleUpToCap()
    {
        var w = TestData.NewWorld();
        var c = w.Cities["chenliu"];
        c.Morale = 90;
        int pop = c.Population;
        AdvanceToNextMonth(w);
        Assert.That(c.Population, Is.GreaterThan(pop));
        c.Population = Domestic.PopulationCap(w, c);
        AdvanceToNextMonth(w);
        Assert.That(c.Population, Is.EqualTo(Domestic.PopulationCap(w, c)));
    }
}

public class RearSupplyTests
{
    [Test]
    public void GateDrawsFoodFromLinkedFriendlyStronghold()
    {
        var w = TestData.NewWorld();
        var gate = w.Cities["hulao"];
        var city = w.Cities["luoyang"];
        gate.Food = 0;
        int before = city.Food;
        Domestic.RearSupply(w);
        Assert.That(gate.Food, Is.GreaterThan(0));
        Assert.That(city.Food, Is.LessThan(before));
    }

    [Test]
    public void IsolatedGateGetsNothing()
    {
        var w = TestData.NewWorld();
        var gate = w.Cities["hulao"];
        gate.Food = 0;
        foreach (var id in gate.Def.Links.Append(gate.Placement.TerritoryOf!))
            w.Cities[id].Owner = "cao_cao"; // 주변이 모두 적에게 넘어감
        Domestic.RearSupply(w);
        Assert.That(gate.Food, Is.EqualTo(0));
    }
}
