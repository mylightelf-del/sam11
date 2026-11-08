using Sam11.Core.World;

namespace Sam11.Core.Tests;

public class SiegeTests
{
    /// <summary>진류(조조, 7칸 도시)를 동탁군이 공격하는 전장.</summary>
    private sealed class Field
    {
        public WorldState World = TestData.NewWorld();
        public Battle.Battle Battle;
        public StrongholdPlacement City;
        public HexCoord Gate;
        public HexCoord Outside; // 성문 바로 바깥 칸

        public Field(string cityId = "chenliu", BattleOptions? options = null)
        {
            Battle = SiegeSetup.CreateBattle(World, 1, options);
            City = World.Map.Strongholds[cityId];
            Gate = City.Tiles.First(t => t.Value == TileKind.Gate).Key;
            Outside = Gate.Neighbors().First(n => !City.Tiles.ContainsKey(n) && Battle.UnitAt(n) == null
                                                  && World.Map.TerrainAt(n) is "plain" or "road" or "forest");
        }
    }

    private static readonly Dictionary<string, int> Gear = TestData.PlentyOfEquipment;

    [Test]
    public void EnemyCannotPassIntactWallButOwnerCanStandOnIt()
    {
        var f = new Field();
        var enemy = f.Battle.Deploy("dong_zhuo", new[] { "hua_xiong" }, "fish_scale", 5000, f.Outside);
        var reach = f.Battle.Reachable(enemy);
        Assert.That(f.City.Tiles.Keys.Where(reach.ContainsKey), Is.Empty);

        var own = f.Battle.Deploy("cao_cao", new[] { "xiahou_dun" }, "square", 5000, f.City.Center);
        Assert.That(f.Battle.Reachable(own).ContainsKey(f.Gate));
    }

    [Test]
    public void SiegeFormationHitsStructuresMuchHarder()
    {
        var f = new Field();
        var ram = f.Battle.Deploy("dong_zhuo", new[] { "li_ru" }, "siege", 4000, f.Outside, Gear);
        // 이유: 어린 적성 C라 학익으로 비교 (학익 공격 105)
        var foot = f.Battle.Deploy("dong_zhuo", new[] { "li_ru" }, "crane_wing", 4000, f.Outside.Neighbors().First(n => f.Battle.UnitAt(n) == null && !f.City.Tiles.ContainsKey(n)));
        Assert.That(f.Battle.StructureDamage(ram, 1, false), Is.EqualTo(f.Battle.StructureDamage(foot, 1, false) * 300 / 105.0).Within(2));
    }

    [Test]
    public void BrokenGateBecomesPassage()
    {
        var f = new Field();
        var ram = f.Battle.Deploy("dong_zhuo", new[] { "li_ru", "lu_bu" }, "siege", 4000, f.Outside, Gear);
        int guard = 0;
        while (f.Battle.Structures[f.Gate].Blocking && guard++ < 20)
        {
            f.Battle.BeginTurn("dong_zhuo");
            if (ram.Energy >= 20) f.Battle.UseTacticOnStructure(ram, "ram", f.Gate);
            else f.Battle.AttackStructure(ram, f.Gate);
        }
        Assert.That(f.Battle.Structures[f.Gate].Blocking, Is.False);
        Assert.That(f.Battle.Log.Lines.Any(l => l.Contains("무너졌다")));
        f.Battle.BeginTurn("dong_zhuo");
        Assert.That(f.Battle.Reachable(ram).ContainsKey(f.Gate));
    }

    [Test]
    public void RamCannotTargetUnitsAndOrdinaryTacticsCannotHitWalls()
    {
        var f = new Field(options: new BattleOptions { TacticSuccessOverride = 1 });
        var ram = f.Battle.Deploy("dong_zhuo", new[] { "li_ru" }, "siege", 4000, f.Outside, Gear);
        var foot = f.Battle.Deploy("dong_zhuo", new[] { "hua_xiong" }, "fish_scale", 5000, f.Outside.Neighbors().First(n => f.Battle.UnitAt(n) == null && !f.City.Tiles.ContainsKey(n)));
        var defender = f.Battle.Deploy("cao_cao", new[] { "cao_hong" }, "square", 5000, f.Gate);
        Assert.Throws<InvalidOperationException>(() => f.Battle.UseTactic(ram, "ram", defender));
        Assert.Throws<InvalidOperationException>(() => f.Battle.UseTacticOnStructure(foot, "breach", f.Gate));
    }

    [Test]
    public void WedgeEnteringCitySwitchesToBaseFormation()
    {
        var f = new Field();
        f.Battle.Structures[f.Gate].Durability = 0;
        var rider = f.Battle.Deploy("dong_zhuo", new[] { "lu_bu" }, "wedge", 5000, f.Outside, Gear);
        f.Battle.Move(rider, f.Gate);
        Assert.That(rider.Formation.Category, Is.EqualTo("base"));
        Assert.That(f.Battle.Log.Lines.Any(l => l.Contains("성 안에 들어서며")));
    }

    [Test]
    public void SiegeTowerIgnoresWallDefenseBonus()
    {
        var f = new Field(options: new BattleOptions { TacticSuccessOverride = 1 });
        var archer = f.Battle.Deploy("cao_cao", new[] { "xiahou_yuan" }, "wild_geese", 5000, f.Gate, Gear);
        var tower = f.Battle.Deploy("dong_zhuo", new[] { "li_ru" }, "siege", 4000, f.Outside, Gear);
        double normal = Combat.Compute(f.Battle, tower, archer, 1, 0, 0, false).Terrain;
        double ignoring = Combat.Compute(f.Battle, tower, archer, 1, 0, 0, false, ignoreWallBonus: true).Terrain;
        Assert.That(ignoring, Is.GreaterThan(normal));
    }

    [Test]
    public void CityFallsWhenInteriorIsEmptyAndCenterTaken()
    {
        var f = new Field();
        var garrison = f.Battle.DeployGarrison("chenliu", new[] { "cao_hong" }, 50);
        f.Battle.Structures[f.Gate].Durability = 0;
        var attacker = f.Battle.Deploy("dong_zhuo", new[] { "lu_bu" }, "fish_scale", 8000, f.Gate);
        f.Battle.Attack(attacker, garrison);
        Assert.That(garrison.IsAlive, Is.False);
        f.Battle.BeginTurn("dong_zhuo");
        f.Battle.Move(attacker, f.City.Center);
        Assert.That(f.Battle.Captures.Select(c => c.Stronghold), Has.Member("chenliu"));

        SiegeSetup.ApplyCaptures(f.World, f.Battle);
        var city = f.World.Cities["chenliu"];
        Assert.That(city.Owner, Is.EqualTo("dong_zhuo"));
        Assert.That(city.Structures[f.Gate].Durability, Is.EqualTo(0), "무너진 성문은 세계 상태에도 남는다");
        // 진류와 이어진 조조 거점이 없으므로 조조군 무장은 포로가 된다
        Assert.That(f.World.Officers["cao_cao"].Status, Is.EqualTo(OfficerStatus.Captive));
    }

    [Test]
    public void DefenderInStreetPreventsCaptureOfMajorCity()
    {
        var f = new Field("luoyang");
        var street = f.City.Tiles.First(t => t.Value == TileKind.Street).Key;
        var holdout = f.Battle.Deploy("dong_zhuo", new[] { "hua_xiong" }, "square", 100, street);
        var attacker = f.Battle.Deploy("cao_cao", new[] { "xiahou_dun" }, "fish_scale", 8000, f.City.Center);
        f.Battle.CheckCaptures();
        Assert.That(f.Battle.Captures, Is.Empty, "시가에 수비 부대가 남아 있으면 함락되지 않음");
        attacker.HasActed = false;
        f.Battle.Attack(attacker, holdout);
        Assert.That(holdout.IsAlive, Is.False);
        Assert.That(f.Battle.Captures.Select(c => c.Stronghold), Has.Member("luoyang"));
    }

    [Test]
    public void StreetDefendersGetBonus()
    {
        var f = new Field("luoyang");
        var street = f.City.Tiles.First(t => t.Value == TileKind.Street).Key;
        var inside = f.Battle.Deploy("dong_zhuo", new[] { "hua_xiong" }, "square", 5000, street);
        var attacker = f.Battle.Deploy("cao_cao", new[] { "xiahou_dun" }, "fish_scale", 5000, f.Outside);
        Assert.That(Combat.Compute(f.Battle, attacker, inside, 1, 0, 0, false).Traits, Is.EqualTo(1 / 1.1).Within(1e-9));
    }
}
