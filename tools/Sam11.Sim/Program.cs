// 전투 코어 시연: 호로관 앞 연합군 대 동탁군.
// 여기의 AI는 시연용 임시 AI다 (가까운 적에게 접근, 쓸 수 있는 가장 센 전법 사용). 본 AI는 M3에서 만든다.
// 사용법: dotnet run --project tools/Sam11.Sim -- [시드] [날씨]

using Sam11.Core.Battle;
using Sam11.Core.Common;
using Sam11.Core.Data;

ulong seed = args.Length > 0 ? ulong.Parse(args[0]) : 190;
string weather = args.Length > 1 ? args[1] : "clear";

var data = GameData.Load(GameData.FindDataDir(AppContext.BaseDirectory));
var map = BattleMap.Rectangle(14, 9);
// 호로관 앞: 가운데 도로, 양옆 산과 숲
for (int r = 0; r < 9; r++)
{
    int off = r / 2;
    map.Set(new HexCoord(6 - off, r), r == 4 ? "road" : (r is 0 or 8 ? "mountain" : "plain"));
    map.Set(new HexCoord(7 - off, r), r == 4 ? "road" : (r is 0 or 8 ? "mountain" : "plain"));
    if (r is 1 or 7) map.Set(new HexCoord(9 - off, r), "forest");
}

var battle = new Battle(data, map, seed, new BattleOptions { Weather = weather });
var gear = new Dictionary<string, int> { ["horse"] = 20000, ["crossbow"] = 20000 };

HexCoord At(int col, int row) => new(col - row / 2, row);

battle.Deploy("연합군", new[] { "liu_bei", "guan_yu", "zhang_fei" }, "fish_scale", 7600, At(2, 4));
battle.Deploy("연합군", new[] { "cao_cao", "xiahou_dun" }, "crane_wing", 9700, At(2, 2), gear);
battle.Deploy("연합군", new[] { "sun_jian", "cheng_pu" }, "wedge", 9300, At(2, 6), gear);
battle.Deploy("연합군", new[] { "gongsun_zan" }, "wild_geese", 8400, At(0, 3), gear);

battle.Deploy("동탁군", new[] { "lu_bu", "gao_shun" }, "wedge", 8800, At(11, 4), gear, "general_misc");
battle.Deploy("동탁군", new[] { "hua_xiong" }, "fish_scale", 7800, At(11, 2));
battle.Deploy("동탁군", new[] { "xu_rong", "li_ru" }, "square", 8400, At(11, 6));
battle.Deploy("동탁군", new[] { "zhang_liao" }, "wild_geese", 9400, At(13, 4), gear);

Console.WriteLine($"=== 호로관 전투 (시드 {seed}, 날씨 {battle.CurrentWeather?.Name.Ko}) ===");
foreach (var u in battle.Units)
    Console.WriteLine($"  [{u.Faction}] {string.Join("·", u.Members.Select(m => m.Name.Ko))} — {u.Formation.Name.Ko}(적성 {u.Aptitude}) " +
                      $"병력 {u.Troops} / 통솔 {u.EffLdr} 무력 {u.EffWar} 지력 {u.EffInt}");
Console.WriteLine();

string[] sides = { "연합군", "동탁군" };
int printed = 0;
for (int round = 1; round <= 25 && battle.FactionsAlive.Count() == 2; round++)
{
    Console.WriteLine($"--- {round}라운드 ---");
    foreach (var side in sides)
    {
        battle.BeginTurn(side);
        foreach (var u in battle.Alive.Where(x => x.Faction == side).OrderBy(x => x.Id).ToList())
            if (u.Status == UnitStatus.Ready && !u.HasActed)
                Act(battle, u);
    }
    battle.EndRound();
    foreach (var line in battle.Log.Lines.Skip(printed)) Console.WriteLine("  " + line);
    printed = battle.Log.Lines.Count;
}

Console.WriteLine();
Console.WriteLine("=== 결과 ===");
foreach (var side in sides)
{
    var alive = battle.Alive.Where(u => u.Faction == side).ToList();
    Console.WriteLine($"{side}: 남은 부대 {alive.Count}, 병력 {alive.Sum(u => u.Troops)}");
}
foreach (var f in battle.Fates)
    Console.WriteLine($"  {data.Officers[f.OfficerId].Name.Ko}: {f.Outcome}");

static void Act(Battle b, Unit u)
{
    Unit? Nearest() => b.Alive.Where(e => e.Faction != u.Faction).OrderBy(e => e.Position.DistanceTo(u.Position)).ThenBy(e => e.Troops).FirstOrDefault();

    var target = Nearest();
    if (target == null) return;

    // 사거리 밖이면 적에게 가장 가까운 칸으로 이동
    if (!b.InRange(u, target))
    {
        var reach = b.Reachable(u);
        var dest = reach.Keys.OrderBy(h => h.DistanceTo(target.Position) < u.Formation.Range ? 99 : Math.Abs(h.DistanceTo(target.Position) - u.Formation.Range))
                             .ThenBy(h => reach[h]).First();
        if (dest != u.Position) b.Move(u, dest);
        target = b.Alive.Where(e => e.Faction != u.Faction && b.InRange(u, e)).OrderBy(e => e.Troops).FirstOrDefault();
        if (target == null) { u.HasActed = true; return; }
    }

    // 쓸 수 있는 가장 센 전법, 없으면 일반 공격
    var tactic = b.AvailableTactics(u)
        .Where(t => t.Power > 0 && t.EnergyCost <= u.Energy && !b.TacticTargetsStructureOnly(t) && b.InRange(u, target, b.TacticRange(u, t)))
        .OrderByDescending(t => t.Power).FirstOrDefault();
    if (tactic != null) b.UseTactic(u, tactic.Id, target);
    else b.Attack(u, target);
}
