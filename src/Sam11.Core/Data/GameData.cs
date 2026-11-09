using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Sam11.Core.Data
{
    /// <summary>data/base의 JSON을 읽어 정의 객체로 들고 있는 저장소.</summary>
    public sealed class GameData
    {
        public Dictionary<string, OfficerDef> Officers { get; } = new Dictionary<string, OfficerDef>();
        public Dictionary<string, FormationDef> Formations { get; } = new Dictionary<string, FormationDef>();
        public Dictionary<string, TacticDef> Tactics { get; } = new Dictionary<string, TacticDef>();
        public Dictionary<string, TraitDef> Traits { get; } = new Dictionary<string, TraitDef>();
        public Dictionary<string, TerrainDef> Terrain { get; } = new Dictionary<string, TerrainDef>();
        public Dictionary<string, WeatherDef> Weather { get; } = new Dictionary<string, WeatherDef>();
        public Dictionary<string, RankDef> Ranks { get; } = new Dictionary<string, RankDef>();
        public List<RulerTitleDef> RulerTitles { get; } = new List<RulerTitleDef>();
        public Dictionary<string, StrongholdDef> Strongholds { get; } = new Dictionary<string, StrongholdDef>();
        public Dictionary<string, FacilityDef> Facilities { get; } = new Dictionary<string, FacilityDef>();
        public Dictionary<string, TreasureDef> Treasures { get; } = new Dictionary<string, TreasureDef>();

        /// <summary>불러온 data 폴더 경로 (맵·시나리오 로딩에 쓴다).</summary>
        public string DataDir { get; private set; } = "";
        /// <summary>불러온 모드 (불러온 순서).</summary>
        public List<string> Mods { get; } = new List<string>();
        public Rules Rules { get; private set; } = new Rules();

        public static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
            Converters = { new StringEnumConverter() },
            MissingMemberHandling = MissingMemberHandling.Ignore,
        });

        /// <summary>불러온 모드 정보 (불러온 순서).</summary>
        public List<ModInfo> ModInfos { get; } = new List<ModInfo>();
        /// <summary>두 모드 이상이 같은 항목을 바꾼 곳.</summary>
        public List<ModConflict> ModConflicts { get; } = new List<ModConflict>();
        /// <summary>모드가 시나리오에 넣는 무장 (신무장 편집기 결과 등).</summary>
        public List<JObject> Placements { get; } = new List<JObject>();

        /// <summary>data 폴더(base, scenarios를 담은 폴더)에서 불러온다. 모드를 주면 그 순서대로 덮어쓴다.</summary>
        public static GameData Load(string dataDir, IReadOnlyList<ModInfo>? mods = null)
        {
            var d = new GameData();
            mods ??= Array.Empty<ModInfo>();
            string b = Path.Combine(dataDir, "base");
            var files = new Dictionary<string, JObject>();
            JObject F(string file)
            {
                if (!files.TryGetValue(file, out var j)) files[file] = j = ModLoader.MergeFile(b, file, mods, d.ModConflicts);
                return j;
            }
            Fill(d.Officers, ReadList<OfficerDef>(F("officers.json"), "officers.json", "officers"), o => o.Id);
            Fill(d.Formations, ReadList<FormationDef>(F("formations.json"), "formations.json", "formations"), f => f.Id);
            Fill(d.Tactics, ReadList<TacticDef>(F("tactics.json"), "tactics.json", "tactics"), t => t.Id);
            Fill(d.Traits, ReadList<TraitDef>(F("traits.json"), "traits.json", "traits"), t => t.Id);
            Fill(d.Terrain, ReadList<TerrainDef>(F("terrain.json"), "terrain.json", "terrain"), t => t.Id);
            Fill(d.Weather, ReadList<WeatherDef>(F("weather.json"), "weather.json", "weather"), w => w.Id);
            Fill(d.Ranks, ReadList<RankDef>(F("ranks.json"), "ranks.json", "ranks"), r => r.Id);
            d.RulerTitles.AddRange(ReadList<RulerTitleDef>(F("ranks.json"), "ranks.json", "ruler_titles").OrderBy(t => t.Level));
            Fill(d.Strongholds, ReadList<StrongholdDef>(F("strongholds.json"), "strongholds.json", "strongholds"), s => s.Id);
            Fill(d.Facilities, ReadList<FacilityDef>(F("facilities.json"), "facilities.json", "facilities"), f => f.Id);
            Fill(d.Treasures, ReadList<TreasureDef>(F("treasures.json"), "treasures.json", "treasures"), t => t.Id);
            d.DataDir = dataDir;
            d.Rules = F("rules.json").ToObject<Rules>(Serializer) ?? throw new InvalidDataException("rules.json을 읽을 수 없음");
            d.Rules.Morale.Bands = d.Rules.Morale.Bands.OrderByDescending(x => x.Min).ToList();
            foreach (var m in mods)
            {
                d.Mods.Add(m.Id);
                d.ModInfos.Add(m);
                string pl = Path.Combine(m.Dir, "placements.json");
                if (File.Exists(pl))
                    foreach (var p in (ReadObject(pl)["placements"] as JArray) ?? new JArray())
                        d.Placements.Add((JObject)p);
            }
            return d;
        }

        /// <summary>시나리오 파일 경로: 그 시나리오를 가진 마지막 모드, 없으면 기본 data.</summary>
        public string ScenarioDir(string scenarioId)
        {
            foreach (var m in Enumerable.Reverse(ModInfos))
            {
                string dir = Path.Combine(m.Dir, "scenarios", scenarioId);
                if (File.Exists(Path.Combine(dir, "scenario.json"))) return dir;
            }
            return Path.Combine(DataDir, "scenarios", scenarioId);
        }

        /// <summary>시나리오 파일을 이어서 바꾸는 모드의 events.json (시나리오를 정한 모드보다 뒤에 불러온 것).</summary>
        public IEnumerable<string> ScenarioEventPatches(string scenarioId)
        {
            string owner = ScenarioDir(scenarioId);
            bool after = !ModInfos.Any(m => owner.StartsWith(m.Dir, StringComparison.Ordinal));
            foreach (var m in ModInfos)
            {
                string dir = Path.Combine(m.Dir, "scenarios", scenarioId);
                if (dir == owner) { after = true; continue; }
                string ev = Path.Combine(dir, "events.json");
                if (after && File.Exists(ev) && !File.Exists(Path.Combine(dir, "scenario.json"))) yield return ev;
            }
        }

        /// <summary>쓸 수 있는 시나리오 id (기본 + 모드).</summary>
        public IEnumerable<string> ScenarioIds() =>
            new[] { Path.Combine(DataDir, "scenarios") }.Concat(ModInfos.Select(m => Path.Combine(m.Dir, "scenarios")))
                .Where(Directory.Exists).SelectMany(Directory.GetDirectories)
                .Where(d => File.Exists(Path.Combine(d, "scenario.json"))).Select(Path.GetFileName).Distinct().OrderBy(x => x, StringComparer.Ordinal)!;

        /// <summary>무장 얼굴 그림 (모드의 faces 폴더, 마지막 모드 우선). 없으면 null.</summary>
        public string? FacePath(string officerId)
        {
            foreach (var m in Enumerable.Reverse(ModInfos))
            {
                string p = Path.Combine(m.Dir, "faces", officerId + ".png");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>현재 폴더에서 위로 올라가며 data 폴더를 찾는다 (테스트·도구용).</summary>
        public static string FindDataDir(string? start = null)
        {
            var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory());
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "data", "base", "rules.json");
                if (File.Exists(candidate))
                    return Path.Combine(dir.FullName, "data");
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("data/base/rules.json을 찾을 수 없음");
        }

        internal static JObject ReadObject(string path)
        {
            using var reader = new JsonTextReader(new StreamReader(path));
            return JObject.Load(reader);
        }

        private static List<T> ReadList<T>(JObject root, string file, string key)
        {
            var arr = root[key] as JArray ?? throw new InvalidDataException($"{file}에 '{key}' 배열이 없음");
            return arr.Select(t => t.ToObject<T>(Serializer)
                                   ?? throw new InvalidDataException($"{file} 항목을 읽을 수 없음")).ToList();
        }

        private static void Fill<T>(Dictionary<string, T> target, IEnumerable<T> items, Func<T, string> id)
        {
            foreach (var item in items)
                target.Add(id(item), item);
        }

        public bool IsStrongAgainst(string attackerFormation, string defenderFormation) =>
            Formations[attackerFormation].StrongAgainst.Contains(defenderFormation);

        /// <summary>상성 배율 (우위 1.2 / 열세 0.8 / 무관 1.0).</summary>
        public double AffinityMultiplier(string attackerFormation, string defenderFormation)
        {
            if (IsStrongAgainst(attackerFormation, defenderFormation))
                return Rules.Affinity.Advantage;
            if (IsStrongAgainst(defenderFormation, attackerFormation))
                return Rules.Affinity.Disadvantage;
            return 1.0;
        }
    }
}
