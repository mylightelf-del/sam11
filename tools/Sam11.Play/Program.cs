// 콘솔 클라이언트 (M4 수직 단면): Unity 화면이 생기기 전에 한 판을 처음부터 끝까지 사람이 둘 수 있게 한다.
// 사용법: dotnet run --project tools/Sam11.Play -- [시나리오] [세력] [시드] [--mods 모드1,모드2]
//         dotnet run --project tools/Sam11.Play -- load <저장 파일>   (저장 때의 모드를 자동으로 켠다)
// 명령은 표준 입력에서 한 줄씩 읽으므로, 파일로 넘겨 대본처럼 돌릴 수도 있다.

using Newtonsoft.Json.Linq;
using Sam11.Core.Common;
using Sam11.Core.Data;
using Sam11.Core.Play;
using Sam11.Core.World;

// --mods a,b 로 모드를 켠다. 저장 파일을 불러오면 저장 때의 모드를 자동으로 켠다.
string dataDir = GameData.FindDataDir(AppContext.BaseDirectory);
var modIds = new List<string>();
var rest = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--mods" && i + 1 < args.Length) modIds.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries));
    else rest.Add(args[i]);
}
JObject? save = rest.Count >= 2 && rest[0] == "load" ? JObject.Parse(File.ReadAllText(rest[1])) : null;
if (save != null) modIds = (save["mods"] as JArray)?.Values<string>().Select(x => x!).ToList() ?? new List<string>();
var mods = ModLoader.Order(ModLoader.Discover(ModLoader.DefaultModsDir(dataDir)), modIds);
var data = GameData.Load(dataDir, mods);
var map = WorldMap.Load(data.DataDir);
if (mods.Count > 0) Console.WriteLine($"모드: {string.Join(", ", mods)}" + (data.ModConflicts.Count > 0 ? $" (충돌 {data.ModConflicts.Count}건)" : ""));

PlayerSession session;
if (save != null)
    session = PlayerSession.Load(data, map, save);
else
{
    string scenario = rest.Count > 0 ? rest[0] : "190";
    string faction = rest.Count > 1 ? rest[1] : "cao_cao";
    ulong seed = rest.Count > 2 ? ulong.Parse(rest[2]) : 1;
    session = PlayerSession.NewGame(data, map, scenario, seed, new GameOptions { Players = { faction } });
}
var ui = new ConsoleUi(session);
ui.Run();

sealed class ConsoleUi
{
    private PlayerSession _s;
    private int _logShown;
    private string? _lastTurnFor;

    public ConsoleUi(PlayerSession s) { _s = s; }

    private WorldState W => _s.World;
    private Game G => _s.Game;

    public void Run()
    {
        Console.WriteLine("삼국지 콘솔판 — '도움말'을 입력하면 명령 목록이 나온다.");
        TurnHeader();
        string? line;
        while (!_s.IsOver && (line = Prompt()) != null)
        {
            line = line.Trim();
            if (line == "" || line.StartsWith("#")) continue;
            try { if (!Handle(line)) break; }
            catch (Exception e) when (e is InvalidOperationException or FormatException or KeyNotFoundException or IOException)
            {
                Console.WriteLine($"✗ {e.Message}");
            }
            TurnHeader();
        }
        if (_s.IsOver) GameOver();
    }

    private string? Prompt()
    {
        Console.Write($"[{W.Date} {W.NameOf(_s.Faction ?? "")}]> ");
        var line = Console.ReadLine();
        if (line != null && Console.IsInputRedirected) Console.WriteLine(line);
        return line;
    }

    private void TurnHeader()
    {
        string key = $"{W.Date}|{_s.Faction}";
        if (_s.Faction == null || key == _lastTurnFor) return;
        _lastTurnFor = key;
        Console.WriteLine();
        Console.WriteLine($"===== {W.Date} {W.NameOf(_s.Faction)} 차례 =====");
        ShowNews();
        foreach (var pe in W.PendingEvents.Where(p => p.Faction == _s.Faction))
        {
            var ev = W.Events.First(e => e.Id == pe.Event);
            Console.WriteLine($"◆ 결정할 일: {ev.Name} — {ev.Text}");
            foreach (var c in ev.Choices) Console.WriteLine($"   선택 {ev.Id} {c.Id} : {c.Text}");
        }
        var props = W.PendingProposals.Where(p => p.To == _s.Faction).ToList();
        for (int i = 0; i < props.Count; i++)
            Console.WriteLine($"✉ 제안 {i}: {W.NameOf(props[i].From)} → {Diplomacy.Describe(props[i])}  (답하기: 수락 {i} / 거절 {i})");
    }

    /// <summary>지난번 이후 로그 중 우리와 관계있거나 큰 사건만.</summary>
    private void ShowNews(bool all = false)
    {
        var me = W.NameOf(_s.Faction ?? "");
        var mine = W.Officers.Values.Where(o => o.Faction == _s.Faction).Select(o => o.Def.Name.Ko).ToHashSet();
        foreach (var l in W.Log.Skip(_logShown))
            if (all || l.Contains("★") || l.Contains("◆") || l.Contains(me) || l.Contains("함락") || mine.Any(l.Contains))
                Console.WriteLine("  " + l);
        _logShown = W.Log.Count;
    }

    private bool Handle(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0];
        string Arg(int i) => i < parts.Length ? parts[i] : throw new InvalidOperationException("인자가 모자람 (도움말 참고)");
        switch (cmd)
        {
            case "도움말": case "help": Help(); return true;
            case "종료": case "quit": return false;
            case "현황": case "status": Status(); return true;
            case "세력": Standings(); return true;
            case "도시": case "city": City(Arg(1)); return true;
            case "무장": case "officer": Officer(Arg(1)); return true;
            case "부대": case "units": Units(); return true;
            case "지도": case "map": Map(int.Parse(Arg(1)), int.Parse(Arg(2)), parts.Length > 3 ? int.Parse(parts[3]) : 6); return true;
            case "이동범위": Reach(int.Parse(Arg(1))); return true;
            case "로그": case "log": ShowNews(all: true); return true;
            case "군단": case "corps":
                if (_s.Corps.Count == 0) Console.WriteLine("  군단 없음 (만들기: corps_set corps=<이름> cities=a,b)");
                foreach (var kv in _s.Corps) Console.WriteLine($"  {kv.Key}: {string.Join(", ", kv.Value.Select(id => W.Cities[id].ToString()))}");
                return true;
            case "끝": case "end": Run("end_turn"); return true;
            case "위임": case "auto": Run("delegate"); return true;
            case "자동": case "autoplay":
            {
                int n = parts.Length > 1 ? int.Parse(parts[1]) : 1;
                for (int i = 0; i < n && !_s.IsOver; i++)
                {
                    Exec(new JObject { ["type"] = "delegate" }, quiet: true);
                    Exec(new JObject { ["type"] = "end_turn" }, quiet: true);
                    if (W.Date.IsMonthStart && W.Date.Month == 1) Standings();
                }
                ShowNews();
                return true;
            }
            case "선택": Exec(new JObject { ["type"] = "event_choice", ["event"] = Arg(1), ["choice"] = Arg(2) }); return true;
            case "수락": Exec(new JObject { ["type"] = "respond", ["index"] = int.Parse(Arg(1)), ["accept"] = true }); return true;
            case "거절": Exec(new JObject { ["type"] = "respond", ["index"] = int.Parse(Arg(1)), ["accept"] = false }); return true;
            case "저장": case "save":
                File.WriteAllText(Arg(1), _s.Save().ToString());
                Console.WriteLine($"저장: {Arg(1)} (명령 {_s.Commands.Count}개)");
                return true;
            case "불러오기": case "load":
                _s = PlayerSession.Load(_s.Data, W.Map, JObject.Parse(File.ReadAllText(Arg(1))));
                _logShown = W.Log.Count;
                _lastTurnFor = null;
                Console.WriteLine($"불러옴: {W.Date}");
                return true;
            default:
                // 그 밖에는 엔진 명령: "recruit officer=cao_cao city=chenliu" 또는 JSON 한 줄
                Exec(line.StartsWith("{") ? JObject.Parse(line) : ParseCommand(parts));
                return true;
        }
    }

    private void Run(string type) => Exec(new JObject { ["type"] = type });

    private void Exec(JObject cmd, bool quiet = false)
    {
        var r = _s.Execute(cmd);
        if (!quiet || !r.Ok) Console.WriteLine(r.Ok ? "✓ " + r.Message : "✗ " + r.Message);
    }

    /// <summary>"type key=value ..." → JSON. 쉼표가 있으면 목록, 숫자·참거짓은 그 타입으로.</summary>
    public static JObject ParseCommand(string[] parts)
    {
        var o = new JObject { ["type"] = parts[0] };
        foreach (var p in parts.Skip(1))
        {
            int eq = p.IndexOf('=');
            if (eq <= 0) throw new FormatException($"'{p}': 키=값 형식이어야 함");
            string k = p[..eq], v = p[(eq + 1)..];
            o[k] = k == "officers" || v.Contains(',') ? new JArray(v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                 : int.TryParse(v, out var n) ? n
                 : bool.TryParse(v, out var bl) ? bl
                 : v;
        }
        return o;
    }

    // ------------------------------------------------------------------ 보기

    private void Help() => Console.WriteLine("""
        보기:  현황 | 세력 | 도시 <id> | 무장 <id> | 부대 | 지도 <q> <r> [반경] | 이동범위 <부대번호> | 로그
        차례:  끝 (차례 마침) | 위임 (이번 차례를 AI에게) | 자동 <턴수> (위임+끝 반복)
        군단:  군단 (목록) | corps_set corps=<이름> cities=a,b (도시를 맡김, 차례를 마칠 때 군단이 움직임) | corps_remove corps=<이름>
        답:    선택 <이벤트> <선택지> | 수락 <번호> | 거절 <번호>
        파일:  저장 <파일> | 불러오기 <파일> | 종료
        명령 (키=값):
          recruit|train|patrol|charity officer=<id> city=<id>
          build officer= city= slot=<번호> facility=<id>      produce officer= city= equipment=horse|crossbow|siege_engine|ship
          buy_food officer= city= gold=<금>    sell_food officer= city= food=<군량>
          move_officer officer= from= to=     transport officer= from= to= gold= food= troops=
          employ officer= city= target=<무장>  appoint officer= rank=<관직>  tax level=low|mid|high
          sortie city= officers=a,b,c formation=<진형> troops=<병력> food=<군량>
          move unit=<번호> q= r=   attack unit= target=<부대번호>   tactic unit= tactic=<전법> target=
          strike unit= q= r= [tactic=]   stratagem unit= kind=fire|confuse|false_report|ambush|calm [target=]
          duel unit= target=   withdraw unit= city=
          propose kind=alliance|truce|gift|vassal|absorb|aid|ransom|captiveexchange to=<세력> envoy=<무장> [gold=]
          mission agent= kind=rumor|sow_discord|bribe|insider|spy city= [target=]
          bestow treasure= officer=   release officer=   execute officer=   break_pact other=<세력>
        JSON 한 줄도 그대로 받는다: {"type":"train","officer":"cao_cao","city":"chenliu"}
        """);

    private void Status()
    {
        string me = _s.Faction!;
        var f = W.Factions[me];
        Console.WriteLine($"{W.NameOf(me)} — 군주 {W.Officers[f.Ruler]}, 본거지 {W.Cities[f.Capital]}, 신용 {W.TrustOf(me)}, 세율 {f.Tax}");
        foreach (var c in W.Cities.Values.Where(c => c.Owner == me).OrderByDescending(c => c.Def.IsCity).ThenBy(c => c.Id))
        {
            var here = W.OfficersAt(c.Id).Where(o => o.Faction == me && o.Status == OfficerStatus.Active).ToList();
            Console.WriteLine($"  {c.Id,-12} {c,-6} 병력 {c.Troops + c.FieldGarrison,6} 금 {c.Gold,6} 군량 {c.Food,7} 인구 {c.Population,7} 민심 {c.Morale,3} 훈련 {c.Training,3}  무장 {here.Count(o => !o.Acted)}/{here.Count}");
        }
        var goals = f.Goals.Select(g => g.Description).ToList();
        if (goals.Count > 0) Console.WriteLine("  목표: " + string.Join(" / ", goals));
        var pacts = W.Alliances.Where(a => a.Active(W.Date) && a.Members.Contains(me))
                     .Select(a => $"{a.Name}({string.Join(",", a.Members.Where(m => m != me).Select(W.NameOf))}, {a.UntilYear}년 {a.UntilMonth}월까지)");
        Console.WriteLine("  약속: " + (pacts.Any() ? string.Join(" ", pacts) : "없음"));
    }

    private void Standings()
    {
        Console.WriteLine($"--- {W.Date} 세력 현황 ---");
        foreach (var f in W.Factions.Values.Where(f => f.Alive).OrderByDescending(f => W.Cities.Values.Count(c => c.Owner == f.Id)))
        {
            var cs = W.Cities.Values.Where(c => c.Owner == f.Id).ToList();
            int field = G.Field.Alive.Where(u => u.Faction == f.Id && !u.IsGarrison).Sum(u => u.Troops);
            string rel = f.Id == _s.Faction ? "(우리)" : $"우호 {W.FriendshipOf(_s.Faction ?? f.Id, f.Id)}";
            Console.WriteLine($"  {f.Id,-12} {W.NameOf(f.Id),-5} 도시 {cs.Count(c => c.Def.IsCity),2} 거점 {cs.Count,2} 병력 {cs.Sum(c => c.Troops + c.FieldGarrison) + field,7} {rel}");
        }
    }

    private void City(string id)
    {
        var c = W.Cities[id];
        Console.WriteLine($"{c} ({c.Id}) 소유 {W.NameOf(c.Owner ?? "")}  중심 {c.Placement.Center}  이웃 {string.Join(",", c.Def.Links)}");
        Console.WriteLine($"  병력 {c.Troops} (전장 수비 {c.FieldGarrison}) 훈련 {c.Training}  금 {c.Gold} 군량 {c.Food}  인구 {c.Population} 민심 {c.Morale}");
        Console.WriteLine($"  병장기: {string.Join(" ", c.Equipment.Select(kv => $"{kv.Key} {kv.Value}"))}");
        if (c.Owner != _s.Faction) return;
        for (int i = 0; i < c.Placement.Slots.Count; i++)
        {
            string what = c.Facilities.TryGetValue(i, out var fs)
                ? $"{W.Data.Facilities[fs.FacilityId].Name.Ko} {fs.Level}단계" + (fs.BuildTurnsLeft > 0 ? $" (공사 {fs.BuildTurnsLeft}턴)" : "")
                : "빈터";
            Console.WriteLine($"  슬롯 {i}{(c.Placement.Slots[i].Waterside ? "(물가)" : "")}: {what}");
        }
        foreach (var o in W.OfficersAt(id).Where(o => o.Status == OfficerStatus.Active || o.Status == OfficerStatus.Free))
            Console.WriteLine($"  {(o.Faction == _s.Faction ? (o.Acted ? "·" : "●") : "○")} {OfficerLine(o)}");
    }

    private string OfficerLine(OfficerState o)
    {
        var s = o.Def.Stats;
        string status = o.Status == OfficerStatus.Free ? " [재야]" : o.InjuredTurns > 0 ? $" [부상 {o.InjuredTurns}]" : "";
        return $"{o.Id,-14} {o,-4} 통{s.Ldr,3} 무{s.War,3} 지{s.Int,3} 정{s.Pol,3} 매{s.Cha,3}  충성 {o.Loyalty,3}{status}";
    }

    private void Officer(string id)
    {
        var o = W.Officers[id];
        Console.WriteLine(OfficerLine(o));
        var d = o.Def;
        Console.WriteLine($"  소속 {W.NameOf(o.Faction ?? "")} 위치 {o.Location} 관직 {o.Rank ?? "-"} 공적 {o.Merit}");
        Console.WriteLine($"  특기: {string.Join(", ", d.Traits.Select(t => W.Data.Traits.TryGetValue(t, out var td) ? td.Name.Ko : t))}");
        Console.WriteLine($"  진형 적성: {string.Join(" ", d.Aptitude.Select(kv => $"{kv.Key}:{kv.Value}"))}");
        var held = Treasures.Held(W, id).Select(t => t.Name.Ko).ToList();
        if (held.Count > 0) Console.WriteLine($"  보물: {string.Join(", ", held)}");
    }

    private void Units()
    {
        string me = _s.Faction!;
        if (!G.Field.Alive.Any(u => u.Faction == me && !u.IsGarrison)) Console.WriteLine("  (출진한 우리 부대 없음)");
        foreach (var u in G.Field.Alive.Where(u => !u.IsGarrison).OrderBy(u => u.Faction != me).ThenBy(u => u.Id))
        {
            if (u.Faction != me && u.Hidden) continue;
            bool mineU = u.Faction == me;
            // 우리 부대에서 10칸 안의 적만 보인다
            if (!mineU && !G.Field.Alive.Any(m => m.Faction == me && m.Position.DistanceTo(u.Position) <= 10)
                && !W.Cities.Values.Any(c => c.Owner == me && c.Placement.Center.DistanceTo(u.Position) <= 10)) continue;
            Console.WriteLine($"  {(mineU ? "▶" : "  ")} {u.Id,4}번 {W.NameOf(u.Faction),-5} {u.Label,-8} {u.Position,-10} {u.Formation.Name.Ko,-4} 병력 {u.Troops,6} 사기 {u.Morale,3} 기력 {u.Energy,3} 군량 {u.Food,6}{(u.HasActed ? " (행동함)" : "")}{(u.Status == Sam11.Core.Battle.UnitStatus.Confused ? " (혼란)" : "")}");
        }
    }

    private void Reach(int id)
    {
        var u = G.Field.Units.First(x => x.Id == id && x.IsAlive);
        var r = G.Field.Reachable(u);
        Console.WriteLine($"{u.Label} 이동 가능 {r.Count}칸: " + string.Join(" ", r.Keys.OrderBy(h => h.DistanceTo(u.Position)).Take(60)));
        var tactics = G.Field.AvailableTactics(u).Select(t => $"{t.Id}({t.Name.Ko}, 기력 {t.EnergyCost})");
        Console.WriteLine($"  전법: {string.Join(" ", tactics)}");
    }

    private void Map(int q0, int r0, int radius)
    {
        var b = G.Field;
        for (int r = r0 - radius; r <= r0 + radius; r++)
        {
            var sb = new System.Text.StringBuilder(r % 2 != 0 ? " " : "");
            for (int q = q0 - radius; q <= q0 + radius; q++)
            {
                var h = new HexCoord(q, r);
                if (!b.Map.Contains(h)) { sb.Append("  "); continue; }
                var u = b.UnitAt(h);
                char ch;
                if (u != null && !(u.Hidden && u.Faction != _s.Faction)) ch = u.Faction == _s.Faction ? '@' : u.IsGarrison ? 'G' : 'x';
                else if (b.Structures.TryGetValue(h, out var st)) ch = st.Kind switch { Sam11.Core.Battle.StructureKind.Gate => 'D', Sam11.Core.Battle.StructureKind.Wall => '#', _ => 'C' };
                else ch = b.Map.TerrainAt(h) switch
                {
                    "plain" => '.', "road" => '=', "street" => '=', "forest" => 'T', "mountain" => '^', "river" => '~', "sea" => '~', "ford" => 'f', "marsh" => ',', "high_ground" => 'n', _ => '?',
                };
                sb.Append(ch).Append(' ');
            }
            Console.WriteLine($"{r,4} {sb}");
        }
        Console.WriteLine("     @우리 x적 G수비대 #성벽 D성문 C성 .평지 =길 T숲 ^산 ~물 ,습지 n고지 f여울");
    }

    private void GameOver()
    {
        ShowNews();
        Console.WriteLine();
        Console.WriteLine(G.Outcome switch
        {
            GameOutcome.Unification => $"★★ {W.NameOf(G.Winner!)} 세력이 천하를 통일했다 ({W.Date})",
            GameOutcome.Goal => $"★★ 목표 달성: {G.AchievedGoal!.Description} ({W.Date})",
            GameOutcome.Defeat => $"패배: 우리 세력이 멸망했다 ({W.Date})",
            _ => "끝",
        });
        Standings();
    }
}
