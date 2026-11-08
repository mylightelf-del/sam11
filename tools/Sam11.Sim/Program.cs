// 전투 코어 시연: 호로관 앞 연합군 대 동탁군.
// 여기의 AI는 시연용 임시 AI다 (가까운 적에게 접근, 쓸 수 있는 가장 센 전법 사용). 본 AI는 M3에서 만든다.
// 사용법: dotnet run --project tools/Sam11.Sim -- [시드] [날씨]
//         dotnet run --project tools/Sam11.Sim -- economy [개월]   (명령 없이 시간만 흘렸을 때의 경제 점검)
//         dotnet run --project tools/Sam11.Sim -- siege [시드]      (전국 맵에서 조조군의 사수관 공략)

using Sam11.Core.Battle;
using Sam11.Core.Common;
using Sam11.Core.Data;
using Sam11.Core.World;

var data = GameData.Load(GameData.FindDataDir(AppContext.BaseDirectory));

if (args.Length > 0 && args[0] == "siege")
{
    SiegeDemo(data, args.Length > 1 ? ulong.Parse(args[1]) : 190);
    return;
}

if (args.Length > 0 && args[0] == "economy")
{
    EconomyReport(data, args.Length > 1 ? int.Parse(args[1]) : 12);
    return;
}

ulong seed = args.Length > 0 ? ulong.Parse(args[0]) : 190;
string weather = args.Length > 1 ? args[1] : "clear";
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

static void EconomyReport(GameData data, int months)
{
    var w = ScenarioLoader.Load(data, WorldMap.Load(data.DataDir), "190", 1);
    var factions = w.Factions.Values.ToList();
    string Row(FactionState f)
    {
        var cs = w.CitiesOf(f.Id).ToList();
        int use = cs.Sum(c => Domestic.MonthlyFoodUse(w, c));
        int starving = cs.Count(c => c.Food < Domestic.MonthlyFoodUse(w, c));
        return $"{data.Officers[f.Ruler].Name.Ko,-5} 금 {cs.Sum(c => c.Gold),7} 군량 {cs.Sum(c => c.Food),8} (월 소비 {use,6}) " +
               $"병력 {cs.Sum(c => c.Troops),7} 인구 {cs.Sum(c => c.Population),8} 민심 {(cs.Count > 0 ? cs.Average(c => c.Morale) : 0),3:F0}" +
               (starving > 0 ? $"  ⚠ 군량 부족 거점 {starving}" : "");
    }
    Console.WriteLine($"=== 경제 점검: {w.Date}부터 {months}개월, 명령 없음 ===");
    foreach (var f in factions) Console.WriteLine("  " + Row(f));
    for (int m = 0; m < months; m++)
    {
        int month = w.Date.Month;
        while (w.Date.Month == month) Domestic.AdvanceTurn(w);
        if (w.Date.Month is 4 or 7 or 10 or 1)
        {
            Console.WriteLine($"--- {w.Date} ---");
            foreach (var f in factions) Console.WriteLine("  " + Row(f));
        }
    }
    var events = w.Log.Where(l => l.Contains("탈영") || l.Contains("수확")).ToList();
    Console.WriteLine($"\n탈영 발생 {events.Count(l => l.Contains("탈영"))}건");
    foreach (var l in events.Where(l => l.Contains("탈영")).Take(12)) Console.WriteLine("  " + l);
}

// 전국 맵 위 공성 시연: 조조군이 진류에서 출진해 사수관을 친다. 시연용 임시 AI.
static void SiegeDemo(GameData data, ulong seed)
{
    var w = ScenarioLoader.Load(data, WorldMap.Load(data.DataDir), "190", seed);
    var chenliu = w.Cities["chenliu"];
    var sishui = w.Cities["sishui"];
    // 시연용 조정: 사수관 수비를 줄이고 진류에 공성병기를 넣는다
    sishui.Troops = 6000;
    chenliu.Equipment["siege_engine"] = 4000;
    chenliu.Troops = 16000;
    Console.WriteLine("=== 사수관 공략 (전국 맵) ===");
    Console.WriteLine("시연용 조정: 사수관 수비 25000 → 6000, 진류 병력 16000·공성병기 4000");

    var b = SiegeSetup.CreateBattle(w, seed);
    var target = w.Map.Strongholds["sishui"];
    b.DeployGarrison("sishui", new[] { "hua_xiong" }, sishui.Troops);
    sishui.Troops = 0;

    var units = new List<Unit>
    {
        SiegeSetup.Sortie(w, b, "chenliu", new[] { "cao_cao", "xiahou_dun" }, "crane_wing", 6000, 2000),
        SiegeSetup.Sortie(w, b, "chenliu", new[] { "yue_jin" }, "fish_scale", 5000, 1500),
        SiegeSetup.Sortie(w, b, "chenliu", new[] { "li_dian" }, "siege", 4000, 1500),
    };
    foreach (var u in units)
        Console.WriteLine($"  출진: {string.Join("·", u.Members.Select(m => m.Name.Ko))} {u.Formation.Name.Ko} {u.Troops}명 at {u.Position}");
    Console.WriteLine($"  사수관 성문 {target.Tiles.Count(t => t.Value == TileKind.Gate)}개, 거리 {units[0].Position.DistanceTo(target.Center)}칸");
    Console.WriteLine();

    int printed = 0;
    for (int round = 1; round <= 40 && b.Captures.Count == 0 && units.Any(u => u.IsAlive); round++)
    {
        b.BeginTurn("cao_cao");
        foreach (var u in units.Where(u => u.Status == UnitStatus.Ready && !u.HasActed))
            SiegeAct(b, u, target);
        b.BeginTurn("dong_zhuo");
        foreach (var d in b.Alive.Where(x => x.Faction == "dong_zhuo" && x.Status == UnitStatus.Ready).ToList())
        {
            var foe = b.Alive.Where(e => e.Faction != d.Faction && b.InRange(d, e)).OrderBy(e => e.Troops).FirstOrDefault();
            if (foe != null) b.Attack(d, foe);
        }
        b.EndRound();
        var lines = b.Log.Lines.Skip(printed).ToList();
        printed = b.Log.Lines.Count;
        var supply = string.Join(", ", units.Where(u => u.IsAlive).Select(u => $"{u.Leader.Name.Ko} 군량 {u.Food}{(b.IsSupplied(u) ? "(보급)" : "")}"));
        Console.WriteLine($"--- {round}라운드 --- {supply}");
        if (Environment.GetEnvironmentVariable("SIM_DEBUG") == "1") foreach (var u in units.Where(x => x.IsAlive)) Console.WriteLine($"    [디버그] {u.Leader.Name.Ko} {u.Position} 이동력 {b.MovePoints(u)} 도달 {b.Reachable(u).Count}칸 성문까지 {u.Position.DistanceTo(target.Center)}");
        foreach (var l in lines) Console.WriteLine("  " + l);
    }

    SiegeSetup.ApplyCaptures(w, b);
    Console.WriteLine();
    Console.WriteLine($"=== 결과: 사수관 소유 {w.Cities["sishui"].Owner} ===");
    foreach (var l in w.Log.Where(l => l.Contains("함락") || l.Contains("포로") || l.Contains("후퇴"))) Console.WriteLine("  " + l);
}

static void SiegeAct(Battle b, Unit u, StrongholdPlacement target)
{
    var walls = target.Tiles.Keys.Where(h => b.Structures[h].Blocking).ToList();
    var gates = walls.Where(h => b.Structures[h].Kind == StructureKind.Gate).ToList();

    // 적 부대가 사거리 안이면 공격 (공성진은 성문 우선)
    var foe = b.Alive.Where(e => e.Faction != u.Faction && b.InRange(u, e)).OrderBy(e => e.Troops).FirstOrDefault();
    if (foe != null && u.Formation.Id != "siege")
    {
        var tac = b.AvailableTactics(u).Where(t => t.Power > 0 && t.EnergyCost <= u.Energy && !b.TacticTargetsStructureOnly(t)
                    && !t.Effects.Any(e => e.Type == "target_on_wall") && b.InRange(u, foe, b.TacticRange(u, t)))
                  .OrderByDescending(t => t.Power).FirstOrDefault();
        if (tac != null) b.UseTactic(u, tac.Id, foe); else b.Attack(u, foe);
        return;
    }
    // 성문이 서 있으면 성문으로, 무너졌으면 정청으로
    HexCoord goal = gates.Count > 0 ? gates.OrderBy(g => g.DistanceTo(u.Position)).First() : target.Center;
    if (gates.Count > 0 && u.Formation.Id == "siege")
    {
        // 공성진: 투석(사거리 3)이 닿으면 강 건너에서도 쏜다
        var tac = b.AvailableTactics(u).Where(t => (t.Id == "catapult" || t.Id == "ram") && t.EnergyCost <= u.Energy
                                                   && u.Position.DistanceTo(goal) <= b.TacticRange(u, t))
                   .OrderByDescending(t => t.Power).FirstOrDefault();
        if (tac != null) { b.UseTacticOnStructure(u, tac.Id, goal); return; }
    }
    if (gates.Count > 0 && u.Position.DistanceTo(goal) <= u.Formation.Range)
    {
        b.AttackStructure(u, goal);
        return;
    }
    // 직선거리가 아니라 실제 이동 경로 거리로 목표에 다가간다 (강·산을 돌아가야 할 때)
    var field = PathField(b, u, goal);
    var reach = b.Reachable(u);
    var dest = reach.Keys.Where(field.ContainsKey).OrderBy(h => field[h]).ThenBy(h => reach[h]).DefaultIfEmpty(u.Position).First();
    if (dest != u.Position) b.Move(u, dest);
    if (u.HasActed || !u.IsAlive) return;
    var near = b.Alive.Where(e => e.Faction != u.Faction && b.InRange(u, e)).OrderBy(e => e.Troops).FirstOrDefault();
    if (near != null) b.Attack(u, near);
    else if (gates.Count > 0 && u.Position.DistanceTo(goal) <= u.Formation.Range) b.AttackStructure(u, goal);
}

/// <summary>목표 칸까지의 이동 비용 지도 (부대는 무시, 지형만 본다). 목표 칸 자체는 막혀 있어도 된다.</summary>
static Dictionary<HexCoord, int> PathField(Battle b, Unit u, HexCoord goal)
{
    var dist = new Dictionary<HexCoord, int> { [goal] = 0 };
    var queue = new PriorityQueue<HexCoord, int>();
    queue.Enqueue(goal, 0);
    while (queue.TryDequeue(out var cur, out var d))
    {
        if (d > dist[cur] || d > 400) continue;
        foreach (var n in cur.Neighbors())
        {
            int? step = b.MoveCost2(u, n);
            if (step == null) continue;
            int nd = d + step.Value;
            if (nd < (dist.TryGetValue(n, out var old) ? old : int.MaxValue)) { dist[n] = nd; queue.Enqueue(n, nd); }
        }
    }
    return dist;
}
