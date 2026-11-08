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
        public static Battle.Battle CreateBattle(WorldState w, ulong seed, BattleOptions? options = null)
        {
            var b = new Battle.Battle(w.Data, w.Map.ToBattleMap(), seed, options);
            foreach (var c in w.Cities.Values)
            {
                b.Strongholds[c.Id] = new StrongholdOnField
                {
                    Id = c.Id, Name = c.Def.Name.Ko, Owner = c.Owner, Center = c.Placement.Center,
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
                // 관문은 성문 칸 자체가 중심이라, 성내 칸이 있으면 그 칸을 정청으로 본다
                if (c.Def.Type == "gate")
                {
                    var inner = c.Placement.Tiles.FirstOrDefault(t => t.Value == TileKind.Street);
                    if (inner.Value == TileKind.Street)
                        b.Strongholds[c.Id].Center = inner.Key;
                }
            }
            return b;
        }

        /// <summary>
        /// 전투 중 함락된 거점을 세계 상태에 반영한다.
        /// 소유 세력이 바뀌고, 수비 병력은 0, 민심이 떨어진다. 그 거점에 있던 옛 세력 무장은
        /// 길로 이어진 아군 거점으로 물러나고, 갈 곳이 없으면 포로가 된다.
        /// 무너진 성벽·성문 내구도 함께 기록한다.
        /// </summary>
        public static void ApplyCaptures(WorldState w, Battle.Battle b)
        {
            foreach (var cap in b.Captures)
            {
                var c = w.Cities[cap.Stronghold];
                string? from = c.Owner;
                c.Owner = cap.To;
                c.Troops = 0;
                c.Governor = null;
                c.Morale = Math.Max(0, c.Morale - w.Data.Rules.Siege.CaptureMoraleLoss);
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
                        o.Status = OfficerStatus.Captive;
                        o.Faction = cap.To;
                        w.Log.Add($"[{w.Date}] {o}, {c} 함락으로 포로가 됨");
                    }
                }
                w.Log.Add($"[{w.Date}] {c} 함락: {w.Factions[cap.To].Ruler} 세력이 차지");
            }
            foreach (var kv in b.Structures)
            {
                var c = w.Cities[kv.Value.Stronghold];
                if (c.Structures.TryGetValue(kv.Key, out var st))
                    st.Durability = kv.Value.Durability;
            }
        }
    }
}
