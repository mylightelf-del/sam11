using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sam11.Core.AI;
using Sam11.Core.Data;

namespace Sam11.Core.World
{
    public sealed class AchievementDef
    {
        public string Id = "";
        public LocalizedText Name = new LocalizedText();
        public LocalizedText Description = new LocalizedText();
        public JToken? Conditions;
        /// <summary>게임 결과 조건: unification / goal.</summary>
        public string? Outcome;
        public Difficulty? MinDifficulty;
        public string? Scenario;
        public int SurviveYears;
        public int? MaxStartCities;
    }

    /// <summary>
    /// 업적 (Steam 업적과 1:1). data/base/achievements.json. 판정은 코어, 기록·표시는 플랫폼(Steam 또는 로컬)이 한다.
    /// 플레이어 세력 기준으로 매달·게임 끝에 확인하고, 처음 이룬 것만 돌려준다.
    /// </summary>
    public sealed class AchievementTracker
    {
        public static List<AchievementDef> LoadDefs(GameData data)
        {
            var list = new List<AchievementDef>();
            string file = Path.Combine(data.DataDir, "base", "achievements.json");
            if (!File.Exists(file)) return list;
            foreach (var a in GameData.ReadObject(file)["achievements"]!)
                list.Add(new AchievementDef
                {
                    Id = a.Value<string>("id")!,
                    Name = a["name"]!.ToObject<LocalizedText>(GameData.Serializer)!,
                    Description = a["description"]?.ToObject<LocalizedText>(GameData.Serializer) ?? new LocalizedText(),
                    Conditions = a["conditions"],
                    Outcome = a.Value<string>("outcome"),
                    MinDifficulty = a.Value<string>("min_difficulty") is string d ? Enum.Parse<Difficulty>(d, true) : (Difficulty?)null,
                    Scenario = a.Value<string>("scenario"),
                    SurviveYears = a.Value<int?>("survive_years") ?? 0,
                    MaxStartCities = a.Value<int?>("max_start_cities"),
                });
            return list;
        }

        private readonly List<AchievementDef> _defs;
        private readonly string _scenario;
        private readonly int _startYear;
        private readonly Dictionary<string, int> _startCities = new Dictionary<string, int>();
        public HashSet<string> Unlocked { get; } = new HashSet<string>();

        public AchievementTracker(GameData data, string scenario, WorldState w, IEnumerable<string>? alreadyUnlocked = null)
        {
            _defs = LoadDefs(data);
            _scenario = scenario;
            _startYear = w.Date.Year;
            foreach (var f in w.Factions.Keys) _startCities[f] = w.Cities.Values.Count(c => c.Owner == f && c.Def.IsCity);
            if (alreadyUnlocked != null) Unlocked.UnionWith(alreadyUnlocked);
        }

        public IReadOnlyList<AchievementDef> Defs => _defs;

        /// <summary>새로 이룬 업적 (한 번 이룬 것은 다시 나오지 않는다).</summary>
        public List<AchievementDef> Check(Game g, string faction)
        {
            var w = g.World;
            var fresh = new List<AchievementDef>();
            if (!w.Factions.TryGetValue(faction, out var f)) return fresh;
            foreach (var a in _defs)
            {
                if (Unlocked.Contains(a.Id)) continue;
                if (a.Scenario != null && a.Scenario != _scenario) continue;
                if (a.MinDifficulty != null && w.Options.Difficulty < a.MinDifficulty) continue;
                bool ok = true;
                if (a.Outcome != null)
                    ok = g.Winner == faction && (a.Outcome == "unification" ? g.Outcome == GameOutcome.Unification : g.Outcome == GameOutcome.Goal);
                if (ok && a.SurviveYears > 0)
                    ok = f.Alive && w.Date.Year - _startYear >= a.SurviveYears
                         && (a.MaxStartCities == null || _startCities.GetValueOrDefault(faction) <= a.MaxStartCities);
                if (ok && a.Conditions != null) ok = f.Alive && EventSystem.Check(g, a.Conditions, faction);
                if (!ok) continue;
                Unlocked.Add(a.Id);
                fresh.Add(a);
            }
            return fresh;
        }
    }
}
