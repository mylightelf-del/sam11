using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Battle;
using Sam11.Core.Common;

namespace Sam11.Core.World
{
    /// <summary>세계 상태 ↔ 전투 엔진 연결: 전국 맵 위에 거점 구조물을 올린 전장을 만들고, 함락 결과를 되돌려 쓴다.</summary>
    public static class SiegeSetup
    {
        /// <summary>주인 없는 거점(무주지)의 전장 위 세력 이름.</summary>
        public const string Neutral = "_neutral";

        public static Battle.Battle CreateBattle(WorldState w, ulong seed, BattleOptions? options = null)
        {
            var b = new Battle.Battle(w.Data, w.Map.ToBattleMap(), seed, options);
            b.TerritoryOf = h => w.Map.Territory.TryGetValue(h, out var city) ? city : null;
            foreach (var c in w.Cities.Values)
            {
                b.Strongholds[c.Id] = new StrongholdOnField
                {
                    Id = c.Id, Name = c.Def.Name.Ko, Owner = c.Owner ?? Neutral, Center = c.Placement.Center,
                };
                foreach (var t in c.Placement.Tiles)
                {
                    c.Structures.TryGetValue(t.Key, out var st);
                    var kind = t.Value switch
                    {
                        TileKind.Wall => StructureKind.Wall,
                        TileKind.Gate => StructureKind.Gate,
                        TileKind.Center => StructureKind.Center,
                        _ => StructureKind.Street,
                    };
                    b.Structures[t.Key] = new SiegeStructure
                    {
                        Stronghold = c.Id,
                        Kind = kind,
                        Durability = st?.Durability ?? 0,
                        MaxDurability = st?.MaxDurability ?? 0,
                    };
                }
            }
            return b;
        }

        /// <summary>
        /// 출진: 거점의 병력·병장기·군량을 떼어 부대를 만든다. 부대원은 그 턴에 행동을 마친다.
        /// at을 주지 않으면 거점 둘레의 빈 칸 중 첫 칸에 선다.
        /// </summary>
        public static Unit Sortie(WorldState w, Battle.Battle b, string cityId, IReadOnlyList<string> officerIds,
                                  string formation, int troops, int food, HexCoord? at = null)
        {
            var c = w.Cities[cityId];
            var officers = officerIds.Select(id => w.Officers[id]).ToList();
            if (officers.Count == 0) throw new InvalidOperationException("부대원이 없음");
            foreach (var o in officers)
            {
                if (o.Status != OfficerStatus.Active || o.Faction != c.Owner || o.Location != cityId)
                    throw new InvalidOperationException($"{Korean.EunNeun(o.ToString())} {c}에서 출진할 수 없음");
                if (o.Acted) throw new InvalidOperationException($"{Korean.EunNeun(o.ToString())} 이번 턴에 이미 행동함");
            }
            if (troops <= 0 || troops > c.Troops) throw new InvalidOperationException($"병력 부족: {c.Troops}/{troops}");
            if (food < 0 || food > c.Food) throw new InvalidOperationException($"군량 부족: {c.Food}/{food}");
            var spot = at ?? SortieSpot(b, c);
            string? eq = w.Data.Formations[formation].RequiredEquipment;
            // 출진 부대의 사기는 성의 훈련도를 따른다 (수비대와 같은 공식)
            var u = b.Deploy(c.Owner!, officerIds, formation, troops, spot, c.Equipment, officers[0].Rank, morale: 50 + c.Training / 2);
            c.Troops -= troops;
            c.Food -= food;
            if (eq != null)
            {
                c.Equipment[eq] -= troops;
                u.Equipment[eq] = troops;
            }
            u.Food = food;
            u.Home = cityId;
            foreach (var o in officers)
            {
                o.Acted = true;
                o.Location = "";
            }
            w.Log.Add($"[{w.Date}] {officers[0]} 부대 {c}에서 출진 ({w.Data.Formations[formation].Name.Ko}, {troops}명, 군량 {food})");
            return u;
        }

        private static HexCoord SortieSpot(Battle.Battle b, CityState c)
        {
            var tiles = c.Placement.Tiles.Keys.ToList();
            foreach (var gate in c.Placement.Tiles.Where(t => t.Value == TileKind.Gate).Select(t => t.Key).Concat(tiles))
                foreach (var n in gate.Neighbors())
                    if (!c.Placement.Tiles.ContainsKey(n) && b.UnitAt(n) == null && b.Map.Contains(n)
                        && b.Data.Terrain.TryGetValue(b.Map.TerrainAt(n), out var t) && t.PassableLand)
                        return n;
            throw new InvalidOperationException($"{c} 둘레에 출진할 빈 칸이 없음");
        }

        /// <summary>귀환: 아군 거점에 인접(또는 성 안)한 부대가 병력·군량·병장기를 반납하고 무장은 그 거점으로 들어간다.</summary>
        public static void Withdraw(WorldState w, Battle.Battle b, Unit u, string cityId)
        {
            var c = w.Cities[cityId];
            if (c.Owner != u.Faction) throw new InvalidOperationException($"{Korean.EunNeun(c.ToString())} 아군 거점이 아님");
            bool near = c.Placement.Tiles.ContainsKey(u.Position) || c.Placement.Tiles.Keys.Any(t => t.DistanceTo(u.Position) == 1);
            if (!near) throw new InvalidOperationException($"{Korean.EunNeun(u.Label)} {c}에 붙어 있지 않음");
            c.Troops += u.Troops;
            c.Food = Math.Min(Domestic.FoodCap(w, c), c.Food + Math.Max(0, u.Food));
            foreach (var kv in u.Equipment) c.Equipment[kv.Key] += Math.Min(kv.Value, u.Troops);
            foreach (var o in u.Members)
                if (w.Officers.TryGetValue(o.Id, out var os)) os.Location = cityId;
            u.Status = UnitStatus.Withdrawn;
            u.Troops = 0;
            w.Log.Add($"[{w.Date}] {u.Label} {Korean.EuRo(c.ToString())} 귀환");
        }

        /// <summary>
        /// 전투 중 함락된 거점을 세계 상태에 반영한다 (전부 다시 적용하므로 단독 전투용).
        /// 게임 루프에서는 ApplyCapture를 한 건씩 부른다.
        /// </summary>
        public static void ApplyCaptures(WorldState w, Battle.Battle b)
        {
            foreach (var cap in b.Captures)
                ApplyCapture(w, cap);
            SyncStructures(w, b);
        }

        /// <summary>
        /// 함락 한 건: 소유 세력이 바뀌고, 수비 병력은 0, 민심이 떨어진다. 그 거점에 있던 옛 세력 무장은
        /// 길로 이어진 아군 거점으로 물러나고, 갈 곳이 없으면 포로가 된다.
        /// </summary>
        public static void ApplyCapture(WorldState w, CaptureEvent cap)
        {
            var c = w.Cities[cap.Stronghold];
            string? from = c.Owner;
            c.Owner = cap.To;
            c.Troops = 0;
            c.FieldGarrison = 0;
            c.Governor = null;
            c.Morale = Math.Max(0, c.Morale - w.Data.Rules.Siege.CaptureMoraleLoss);
            if (from != null)
                foreach (var o in w.OfficersAt(c.Id).Where(o => o.Faction == from).ToList())
                {
                    var retreat = c.Def.Links.Select(id => w.Cities[id]).FirstOrDefault(x => x.Owner == from);
                    if (retreat != null)
                    {
                        o.Location = retreat.Id;
                        w.Log.Add($"[{w.Date}] {o}, {c} 함락으로 {Korean.EuRo(retreat.ToString())} 후퇴");
                    }
                    else
                    {
                        Treasures.Seize(w, o.Id, cap.To);
                        o.PreviousFaction = o.Faction;
                        o.Status = OfficerStatus.Captive;
                        o.Faction = cap.To;
                        o.CaptiveMonths = 0;
                        w.Log.Add($"[{w.Date}] {o}, {c} 함락으로 포로가 됨");
                    }
                }
            w.Log.Add($"[{w.Date}] {c} 함락: {w.NameOf(cap.To)} 세력이 차지" + (from == null ? " (무주지)" : $" ({w.NameOf(from)} 세력에게서)"));
        }

        /// <summary>전장의 성벽·성문 내구를 세계 상태에 기록한다.</summary>
        public static void SyncStructures(WorldState w, Battle.Battle b)
        {
            foreach (var kv in b.Structures)
            {
                var c = w.Cities[kv.Value.Stronghold];
                if (c.Structures.TryGetValue(kv.Key, out var st))
                    st.Durability = kv.Value.Durability;
            }
        }
    }
}
