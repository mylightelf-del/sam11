// 모드 도구: 모드 목록·검사, 신무장 만들기 (07 설계서 5장의 편집기를 명령줄로).
// 사용법:
//   dotnet run --project tools/Sam11.ModTool -- list
//   dotnet run --project tools/Sam11.ModTool -- check [모드 id ...]
//   dotnet run --project tools/Sam11.ModTool -- officer <모드 폴더> id=... name=... birth=... ldr= war= int= pol= cha= traits=a,b [키=값 ...]
// 모드 폴더 위치는 --mods-dir로 바꿀 수 있다 (기본: data 옆의 Mods).

using Newtonsoft.Json.Linq;
using Sam11.Core.Data;
using Sam11.Core.World;

string dataDir = GameData.FindDataDir(AppContext.BaseDirectory);
var rest = new List<string>();
string? modsDir = ModLoader.DefaultModsDir(dataDir);
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--mods-dir" && i + 1 < args.Length) modsDir = args[++i];
    else rest.Add(args[i]);
}
if (rest.Count == 0) { Usage(); return 1; }

switch (rest[0])
{
    case "list":
    {
        var mods = ModLoader.Discover(modsDir);
        Console.WriteLine($"모드 폴더: {modsDir ?? "(없음)"}");
        if (mods.Count == 0) Console.WriteLine("  모드 없음");
        foreach (var m in mods)
            Console.WriteLine($"  {m.Id,-20} {m.Name} {m.Version}" + (m.LoadAfter.Count > 0 ? $"  (먼저: {string.Join(",", m.LoadAfter)})" : "") +
                              (m.Description != "" ? $"\n    {m.Description}" : ""));
        return 0;
    }
    case "check":
    {
        var all = ModLoader.Discover(modsDir);
        var ids = rest.Count > 1 ? rest.Skip(1).ToList() : all.Select(m => m.Id).ToList();
        var order = ModLoader.Order(all, ids);
        var data = GameData.Load(dataDir, order);
        var map = WorldMap.Load(dataDir);
        Console.WriteLine($"불러온 순서: {(order.Count == 0 ? "(모드 없음)" : string.Join(" → ", order.Select(m => m.Id)))}");
        Console.WriteLine($"무장 {data.Officers.Count}, 보물 {data.Treasures.Count}, 시나리오 {string.Join(",", data.ScenarioIds())}, 배치 {data.Placements.Count}");
        foreach (var c in data.ModConflicts) Console.WriteLine($"  충돌: {c}");
        var errors = DataValidator.Validate(data, map);
        // 시나리오마다 실제로 불러 본다 (배치·이벤트가 엉키지 않았는지)
        foreach (var sc in data.ScenarioIds())
        {
            try { ScenarioLoader.Load(data, map, sc, 1); }
            catch (Exception e) { errors.Add($"시나리오 {sc}: {e.Message}"); }
        }
        foreach (var e in errors) Console.WriteLine($"  ✗ {e}");
        Console.WriteLine(errors.Count == 0 ? "검사 통과" : $"문제 {errors.Count}건");
        return errors.Count == 0 ? 0 : 1;
    }
    case "officer":
    {
        if (rest.Count < 3) { Usage(); return 1; }
        string modDir = rest[1];
        var kv = rest.Skip(2).Select(a => a.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        string Get(string k, string? def = null) => kv.TryGetValue(k, out var v) ? v : def ?? throw new ArgumentException($"{k}= 가 필요");
        int N(string k, int def) => kv.TryGetValue(k, out var v) ? int.Parse(v) : def;
        List<string> L(string k) => kv.TryGetValue(k, out var v) ? v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() : new List<string>();

        var baseData = GameData.Load(dataDir, ModLoader.Order(ModLoader.Discover(modsDir), ModLoader.Discover(modsDir)
                                     .Where(m => Path.GetFullPath(m.Dir) != Path.GetFullPath(modDir)).Select(m => m.Id)));
        var o = new OfficerDef
        {
            Id = Get("id"),
            Name = new LocalizedText { Ko = Get("name"), Zh = kv.GetValueOrDefault("zh") },
            Gender = Get("gender", "male"),
            Birth = N("birth", 170),
            Death = kv.ContainsKey("death") ? N("death", 0) : null,
            Stats = new StatBlock { Ldr = N("ldr", 50), War = N("war", 50), Int = N("int", 50), Pol = N("pol", 50), Cha = N("cha", 50) },
            Hidden = new HiddenStats { Loyalty = N("loyalty", 8), Ambition = N("ambition", 8), Compat = N("compat", 75) },
            Traits = L("traits"),
        };
        foreach (var f in baseData.Formations.Keys) o.Aptitude[f] = AptitudeRank.C;
        foreach (var a in L("apt"))
        {
            var p = a.Split(':');
            o.Aptitude[p[0]] = Enum.Parse<AptitudeRank>(p[1], true);
        }
        if (kv.Keys.Any(k => k is "father" or "mother" or "spouse" or "siblings"))
            o.Kin = new KinDef { Father = kv.GetValueOrDefault("father"), Mother = kv.GetValueOrDefault("mother"), Spouse = L("spouse"), Siblings = L("siblings") };

        // baseData에는 이 모드가 빠져 있으므로, 같은 모드 안의 무장을 고쳐 쓰는 것은 '이미 있는 id'가 아니다
        var problems = OfficerEditor.Check(baseData, o, statCap: N("cap", OfficerEditor.DefaultStatCap));
        if (problems.Count > 0)
        {
            foreach (var p in problems) Console.WriteLine($"  ✗ {p}");
            return 1;
        }
        string modId = Path.GetFileName(Path.GetFullPath(modDir).TrimEnd(Path.DirectorySeparatorChar));
        var info = File.Exists(Path.Combine(modDir, "mod.json"))
            ? ModLoader.Discover(Path.GetDirectoryName(Path.GetFullPath(modDir).TrimEnd(Path.DirectorySeparatorChar))).First(m => Path.GetFullPath(m.Dir) == Path.GetFullPath(modDir).TrimEnd(Path.DirectorySeparatorChar))
            : new ModInfo { Id = modId, Name = kv.GetValueOrDefault("mod_name", modId), Version = "1.0", Description = "신무장" };
        JObject[]? placement = kv.ContainsKey("location")
            ? new[] { new JObject { ["officer"] = o.Id, ["scenario"] = Get("scenario", "*"), ["location"] = Get("location"),
                                    ["faction"] = kv.GetValueOrDefault("faction"), ["loyalty"] = N("loyalty_start", 80) } }
            : null;
        OfficerEditor.Export(modDir, info, new[] { o }, placement);
        int total = o.Stats.Ldr + o.Stats.War + o.Stats.Int + o.Stats.Pol + o.Stats.Cha;
        Console.WriteLine($"✓ {o.Name.Ko}({o.Id}) 저장: {modDir} (능력치 합 {total})" + (placement != null ? $", 등장 {placement[0]["scenario"]} {placement[0]["location"]}" : ""));
        return 0;
    }
    default:
        Usage();
        return 1;
}

static void Usage() => Console.WriteLine("""
    모드 도구
      list                         모드 폴더의 모드 목록
      check [모드 id ...]          모드를 켜고 데이터·시나리오 검사 (없으면 전부)
      officer <모드 폴더> 키=값... 신무장을 만들어 모드에 넣는다
        필수: id= name=
        선택: gender=male|female birth= death= ldr= war= int= pol= cha= (기본 50)
              loyalty= ambition= (1~15) compat= (0~149) traits=a,b (1~2개)
              apt=fish_scale:S,wedge:A,... (빠진 진형은 C)
              father= mother= spouse=a,b siblings=a,b
              location=<거점> [scenario=<id>|*] [faction=<세력>]  (등장 위치. *는 모든 시나리오에 재야로)
              cap=<능력치 합 상한, 0이면 없음> (기본 400)
      --mods-dir <폴더>            모드 폴더 (기본: data 옆의 Mods)
    """);
