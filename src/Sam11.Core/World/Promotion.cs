using System;
using System.Linq;
using Sam11.Core.Common;
using Sam11.Core.Data;

namespace Sam11.Core.World
{
    /// <summary>
    /// 군주 작위와 관직 (01 무장 설계서 6장).
    /// 작위는 도시 수로 오르고, 작위가 높을수록 높은 관직을 내릴 수 있다. 관직은 부대 최대 병력을 늘린다.
    /// </summary>
    public static class Promotion
    {
        /// <summary>도시 수에 맞게 군주 작위를 올린다 (내려가지는 않는다).</summary>
        public static void UpdateTitle(WorldState w, FactionState f)
        {
            int cities = w.Cities.Values.Count(c => c.Owner == f.Id && c.Def.IsCity);
            var earned = w.Data.RulerTitles.Where(t => cities >= t.RequiredCities).Select(t => t.Level).DefaultIfEmpty(0).Max();
            if (earned <= f.Title) return;
            f.Title = earned;
            var name = w.Data.RulerTitles.First(t => t.Level == earned).Name.Ko;
            w.Log.Add($"[{w.Date}] {Korean.IGa(w.NameOf(f.Id))} {Korean.EuRo(name)} 올랐다");
        }

        public static int Tier(WorldState w, OfficerState o) => o.Rank != null && w.Data.Ranks.TryGetValue(o.Rank, out var r) ? r.Tier : 0;

        /// <summary>임명할 수 있는지: 더 높은 관직, 공적, 군주 작위, 정원.</summary>
        public static string? CanAppoint(WorldState w, OfficerState o, string rankId)
        {
            if (o.Status != OfficerStatus.Active || o.Faction == null) return "세력 소속이 아님";
            if (!w.Data.Ranks.TryGetValue(rankId, out var rank)) return $"없는 관직: {rankId}";
            if (rank.Tier <= Tier(w, o)) return "지금 관직보다 높지 않음";
            if (o.Merit < rank.Merit) return $"공적 부족: {o.Merit}/{rank.Merit}";
            var f = w.Factions[o.Faction];
            if (f.Title < rank.RequiredTitle) return "군주 작위가 낮음";
            if (rank.Quota != null && w.Officers.Values.Count(x => x.Faction == o.Faction && x.Status == OfficerStatus.Active && x.Rank == rankId) >= rank.Quota)
                return "정원이 찼음";
            return null;
        }

        public static void Appoint(WorldState w, string officerId, string rankId)
        {
            var o = w.Officers[officerId];
            string? why = CanAppoint(w, o, rankId);
            if (why != null) throw new InvalidOperationException(why);
            o.Rank = rankId;
            o.Loyalty = Math.Min(100, o.Loyalty + 10);
            w.Log.Add($"[{w.Date}] {Korean.IGa(o.ToString())} {Korean.EuRo(w.Data.Ranks[rankId].Name.Ko)} 임명되었다");
        }

        /// <summary>공적이 높은 무장부터, 받을 수 있는 가장 높은 관직을 내린다 (AI용).</summary>
        public static void AppointAll(WorldState w, string faction)
        {
            var ranks = w.Data.Ranks.Values.OrderByDescending(r => r.Tier).ToList();
            foreach (var o in w.Officers.Values.Where(o => o.Faction == faction && o.Status == OfficerStatus.Active)
                                          .OrderByDescending(o => o.Merit).ThenBy(o => o.Id).ToList())
            {
                var best = ranks.FirstOrDefault(r => CanAppoint(w, o, r.Id) == null);
                if (best != null) Appoint(w, o.Id, best.Id);
            }
        }
    }
}
