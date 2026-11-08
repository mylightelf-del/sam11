namespace Sam11.Core.Tests;

/// <summary>실제 data 폴더를 한 번만 읽어 모든 테스트가 공유한다.</summary>
public static class TestData
{
    private static readonly Lazy<GameData> _data = new(() => GameData.Load(GameData.FindDataDir(TestContext.CurrentContext.TestDirectory)));
    public static GameData Data => _data.Value;

    public static Battle.Battle NewBattle(BattleMap? map = null, ulong seed = 1, BattleOptions? options = null) =>
        new(Data, map ?? BattleMap.Rectangle(12, 12), seed, options);

    public static readonly Dictionary<string, int> PlentyOfEquipment = new()
    {
        ["horse"] = 100000, ["crossbow"] = 100000, ["siege_engine"] = 100000, ["ship"] = 100000,
    };
}
