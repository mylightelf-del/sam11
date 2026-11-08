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
        public Dictionary<string, StrongholdDef> Strongholds { get; } = new Dictionary<string, StrongholdDef>();
        public Dictionary<string, FacilityDef> Facilities { get; } = new Dictionary<string, FacilityDef>();

        /// <summary>불러온 data 폴더 경로 (맵·시나리오 로딩에 쓴다).</summary>
        public string DataDir { get; private set; } = "";
        public Rules Rules { get; private set; } = new Rules();

        public static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
            Converters = { new StringEnumConverter() },
            MissingMemberHandling = MissingMemberHandling.Ignore,
        });

        /// <summary>data 폴더(base, scenarios를 담은 폴더)에서 불러온다.</summary>
        public static GameData Load(string dataDir)
        {
            var d = new GameData();
            string b = Path.Combine(dataDir, "base");
            Fill(d.Officers, ReadList<OfficerDef>(b, "officers.json", "officers"), o => o.Id);
            Fill(d.Formations, ReadList<FormationDef>(b, "formations.json", "formations"), f => f.Id);
            Fill(d.Tactics, ReadList<TacticDef>(b, "tactics.json", "tactics"), t => t.Id);
            Fill(d.Traits, ReadList<TraitDef>(b, "traits.json", "traits"), t => t.Id);
            Fill(d.Terrain, ReadList<TerrainDef>(b, "terrain.json", "terrain"), t => t.Id);
            Fill(d.Weather, ReadList<WeatherDef>(b, "weather.json", "weather"), w => w.Id);
            Fill(d.Ranks, ReadList<RankDef>(b, "ranks.json", "ranks"), r => r.Id);
            Fill(d.Strongholds, ReadList<StrongholdDef>(b, "strongholds.json", "strongholds"), s => s.Id);
            Fill(d.Facilities, ReadList<FacilityDef>(b, "facilities.json", "facilities"), f => f.Id);
            d.DataDir = dataDir;
            d.Rules = ReadObject(Path.Combine(b, "rules.json")).ToObject<Rules>(Serializer)
                      ?? throw new InvalidDataException("rules.json을 읽을 수 없음");
            d.Rules.Morale.Bands = d.Rules.Morale.Bands.OrderByDescending(x => x.Min).ToList();
            return d;
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

        private static List<T> ReadList<T>(string dir, string file, string key)
        {
            var root = ReadObject(Path.Combine(dir, file));
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
