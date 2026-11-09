using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Common;
using Sam11.Core.Data;

namespace Sam11.Core.World
{
    /// <summary>
    /// 보물 (01 무장 설계서 7장): 무장은 종류별로 한 개씩 장착한다. 능력치 보너스(최대 100)와 특기를 준다.
    /// 무장이 사로잡히거나 쓰러지면 보물은 상대 세력의 보관고로 넘어간다. 하사하면 충성이 오른다.
    /// </summary>
    public static class Treasures
    {
        /// <summary>그 무장이 가진 보물들.</summary>
        public static IEnumerable<TreasureDef> Held(WorldState w, string officerId) =>
            w.TreasureOwner.Where(kv => kv.Value == officerId && w.Data.Treasures.ContainsKey(kv.Key))
                           .Select(kv => w.Data.Treasures[kv.Key]).OrderBy(t => t.Id);

        /// <summary>보물 효과를 반영한 무장 정의. 보물이 없으면 원래 정의 그대로.</summary>
        public static OfficerDef Effective(WorldState w, string officerId)
        {
            var o = w.Data.Officers[officerId];
            var held = Held(w, officerId).ToList();
            if (held.Count == 0) return o;
            int cap = w.Data.Rules.Unit.StatCap;
            int Add(int v, string k) => Math.Min(cap, v + held.Sum(t => t.StatBonus.TryGetValue(k, out var b) ? b : 0));
            var e = new OfficerDef
            {
                Id = o.Id, Name = o.Name, Gender = o.Gender, Birth = o.Birth, Death = o.Death, Hidden = o.Hidden,
                Kin = o.Kin, RulerPersonality = o.RulerPersonality,
                Aptitude = new Dictionary<string, AptitudeRank>(o.Aptitude),
                Traits = o.Traits.Concat(held.SelectMany(t => t.GrantsTraits)).Distinct().ToList(),
                Stats = new StatBlock
                {
                    Ldr = Add(o.Stats.Ldr, "ldr"), War = Add(o.Stats.War, "war"), Int = Add(o.Stats.Int, "int"),
                    Pol = Add(o.Stats.Pol, "pol"), Cha = Add(o.Stats.Cha, "cha"),
                },
            };
            // 적토마처럼 진형 적성을 올려 주는 보물
            foreach (var t in held)
                if (t.Id == "red_hare" && e.Aptitude.TryGetValue("wedge", out var r) && r < AptitudeRank.S) e.Aptitude["wedge"] = AptitudeRank.S;
            return e;
        }

        /// <summary>하사: 세력 보관고나 같은 세력 무장에게서 보물을 내린다. 같은 종류를 이미 가졌으면 그것은 보관고로.</summary>
        public static void Bestow(WorldState w, string treasureId, string officerId)
        {
            var o = w.Officers[officerId];
            if (o.Status != OfficerStatus.Active || o.Faction == null) throw new InvalidOperationException("하사할 수 없는 무장");
            var t = w.Data.Treasures[treasureId];
            bool inVault = w.TreasureVault.TryGetValue(treasureId, out var vf) && vf == o.Faction;
            bool fromOwn = w.TreasureOwner.TryGetValue(treasureId, out var holder) && holder != null
                           && w.Officers.TryGetValue(holder, out var h) && h.Faction == o.Faction;
            if (!inVault && !fromOwn) throw new InvalidOperationException($"{t}는 우리 세력 것이 아님");
            foreach (var same in Held(w, officerId).Where(x => x.Slot == t.Slot && x.Id != treasureId).ToList())
            {
                w.TreasureOwner[same.Id] = null;
                w.TreasureVault[same.Id] = o.Faction;
            }
            w.TreasureVault.Remove(treasureId);
            w.TreasureOwner[treasureId] = officerId;
            o.Loyalty = Math.Min(100, o.Loyalty + w.Data.Rules.Treasure.BestowLoyalty);
            w.Log.Add($"[{w.Date}] {Korean.IGa(o.ToString())} {Korean.EulReul(t.Name.Ko)} 하사받았다 (충성 {o.Loyalty})");
        }

        /// <summary>무장이 사로잡히거나 쓰러지면 보물은 상대 세력 보관고로 (상대가 없으면 원래 세력 보관고로).</summary>
        public static void Seize(WorldState w, string officerId, string? toFaction)
        {
            foreach (var t in Held(w, officerId).ToList())
            {
                w.TreasureOwner[t.Id] = null;
                if (toFaction != null)
                {
                    w.TreasureVault[t.Id] = toFaction;
                    w.Log.Add($"[{w.Date}] {w.NameOf(toFaction)} 세력이 {w.Officers[officerId]}의 {Korean.EulReul(t.Name.Ko)} 손에 넣었다");
                }
            }
        }
    }
}
