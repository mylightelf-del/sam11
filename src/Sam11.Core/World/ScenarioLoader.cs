using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sam11.Core.Data;

namespace Sam11.Core.World
{
    /// <summary>data/scenarios/&lt;id&gt;/scenario.json으로 게임 시작 상태를 만든다.</summary>
    public static class ScenarioLoader
    {
        /// <summary>구조물 내구 (안). 성문은 성벽보다 약하다 (04 설계서 11.1).</summary>
        public static int StructureDurability(string strongholdType, TileKind kind)
        {
            int baseWall = strongholdType switch
            {
                "major_city" => 3000,
                "city" => 2000,
                "gate" => 3500,
                "fortress" => 2500,
                "port" => 800,
                _ => 500,
            };
            return kind switch
            {
                TileKind.Wall => baseWall,
                TileKind.Gate => baseWall / 2,
                TileKind.Center => strongholdType == "port" || strongholdType == "ferry" ? baseWall : 0,
                _ => 0,
            };
        }

        /// <summary>
        /// 모드 무장 배치 (07 설계서 5장). scenario가 "*"이면 모든 시나리오에 재야로, 시나리오를 콕 집으면 적힌 세력으로.
        /// 시작 때 15세가 안 되면 미등장. 이미 그 시나리오에 있는 무장, 없는 세력·거점은 건너뛴다.
        /// </summary>
        private static void PlaceModOfficers(GameData data, WorldState w, string scenarioId)
        {
            // 시나리오를 콕 집은 배치가 "*"보다 먼저
            foreach (var p in data.Placements.OrderBy(x => (x.Value<string>("scenario") ?? "*") == "*"))
            {
                string sc = p.Value<string>("scenario") ?? "*";
                if (sc != "*" && sc != scenarioId) continue;
                string id = p.Value<string>("officer")!;
                if (w.Officers.ContainsKey(id) || !data.Officers.TryGetValue(id, out var def)) continue;
                string loc = p.Value<string>("location")!;
                if (!w.Cities.ContainsKey(loc)) continue;
                string? faction = sc == "*" ? null : p.Value<string>("faction");
                if (faction != null && (!w.Factions.ContainsKey(faction) || w.Cities[loc].Owner != faction)) faction = null;
                bool born = w.Date.Year - def.Birth >= 15;
                if (def.Death != null && def.Death < w.Date.Year) continue;
                w.Officers[id] = new OfficerState
                {
                    Def = def,
                    Faction = born ? faction : null,
                    Location = loc,
                    Status = !born ? OfficerStatus.Unborn : faction != null ? OfficerStatus.Active : OfficerStatus.Free,
                    Loyalty = born && faction != null ? p.Value<int?>("loyalty") ?? 80 : 0,
                };
            }
        }

        public static WorldState Load(GameData data, WorldMap map, string scenarioId, ulong seed)
        {
            string scenarioDir = data.ScenarioDir(scenarioId);
            var root = GameData.ReadObject(Path.Combine(scenarioDir, "scenario.json"));
            var w = new WorldState(data, map, seed);
            var start = root["start"]!;
            w.Date.Year = start.Value<int>("year");
            w.Date.Month = start.Value<int>("month");
            w.Date.Turn = start.Value<int>("turn");

            foreach (var f in root["factions"]!)
            {
                var fs = new FactionState
                {
                    Id = f.Value<string>("id")!,
                    Ruler = f.Value<string>("ruler")!,
                    Color = f.Value<string>("color")!,
                    Title = f.Value<int>("title"),
                    Capital = f.Value<string>("capital")!,
                };
                foreach (var goal in (f["goals"] as JArray) ?? new JArray())
                    fs.Goals.Add(new FactionGoal
                    {
                        Id = goal.Value<string>("id") ?? "",
                        Description = goal.Value<string>("description") ?? "",
                        Conditions = goal["conditions"] ?? new JObject(),
                    });
                w.Factions[fs.Id] = fs;
            }

            foreach (var c in root["cities"]!)
            {
                string id = c.Value<string>("stronghold")!;
                var def = data.Strongholds[id];
                var cs = new CityState
                {
                    Def = def,
                    Placement = map.Strongholds[id],
                    Owner = c.Value<string>("owner"),
                    Governor = c.Value<string>("governor"),
                    Population = c.Value<int>("population"),
                    Morale = c.Value<int>("morale"),
                    Gold = c.Value<int>("gold"),
                    Food = c.Value<int>("food"),
                    Troops = c.Value<int>("troops"),
                    Training = c.Value<int>("training"),
                };
                foreach (var e in (JObject)c["equipment"]!)
                    cs.Equipment[e.Key] = e.Value!.Value<int>();
                foreach (var t in cs.Placement.Tiles)
                {
                    int dur = StructureDurability(def.Type, t.Value);
                    if (dur > 0)
                        cs.Structures[t.Key] = new StructureState { Kind = t.Value, Durability = dur, MaxDurability = dur };
                }
                w.Cities[id] = cs;
            }
            foreach (var id in data.Strongholds.Keys.Where(id => !w.Cities.ContainsKey(id)))
                throw new InvalidDataException($"시나리오 {scenarioId}에 거점 {id}의 상태가 없음");

            foreach (var o in root["officers"]!)
            {
                string id = o.Value<string>("officer")!;
                var os = new OfficerState
                {
                    Def = data.Officers[id],
                    Faction = o.Value<string>("faction"),
                    Location = o.Value<string>("location")!,
                    Status = (OfficerStatus)Enum.Parse(typeof(OfficerStatus), o.Value<string>("status")!, true),
                    Loyalty = o.Value<int?>("loyalty") ?? 0,
                    Rank = o.Value<string>("rank"),
                };
                w.Officers[id] = os;
            }
            foreach (var t in (root["treasures"] as JArray) ?? new JArray())
                w.TreasureOwner[t.Value<string>("treasure")!] = t.Value<string>("owner");
            string? eventsFile = root.Value<string>("events");
            if (eventsFile != null)
            {
                var evRoot = GameData.ReadObject(Path.Combine(scenarioDir, eventsFile));
                foreach (var e in evRoot["events"]!) w.Events.Add(EventDef.Parse(e));
            }
            // 모드가 더하는 이벤트 (같은 id면 바꾼다)
            foreach (var patch in data.ScenarioEventPatches(scenarioId))
                foreach (var e in GameData.ReadObject(patch)["events"]!)
                {
                    var ev = EventDef.Parse(e);
                    int i = w.Events.FindIndex(x => x.Id == ev.Id);
                    if (i >= 0) w.Events[i] = ev;
                    else w.Events.Add(ev);
                }
            PlaceModOfficers(data, w, scenarioId);
            foreach (var d in (root["diplomacy"] as JArray) ?? new JArray())
            {
                if (d.Value<string>("type") == "friendship")
                {
                    var pair = d["factions"]!.Values<string>().ToList();
                    w.ChangeFriendship(pair[0]!, pair[1]!, d.Value<int>("value") - w.FriendshipOf(pair[0]!, pair[1]!));
                    continue;
                }
                if (d.Value<string>("type") != "alliance") continue;
                int months = d.Value<int?>("months") ?? 12;
                int total = w.Date.Year * 12 + (w.Date.Month - 1) + months;
                var a = new Alliance { Name = d.Value<string>("name") ?? "", UntilYear = total / 12, UntilMonth = total % 12 + 1 };
                foreach (var m in d["factions"]!) a.Members.Add(m.Value<string>()!);
                w.Alliances.Add(a);
            }
            return w;
        }
    }
}
