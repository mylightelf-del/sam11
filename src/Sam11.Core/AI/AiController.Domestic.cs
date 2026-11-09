using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Battle;
using Sam11.Core.World;

namespace Sam11.Core.AI
{
    /// <summary>내정 AI: 군량, 징병, 집결, 건설, 훈련, 순찰, 등용, 무장 배치.</summary>
    public sealed partial class AiController
    {
        private void RunDomestic(Game g, string faction)
        {
            var w = g.World;
            var gathering = Campaigns.Where(c => c.State == CampaignState.Gathering).ToList();
            foreach (var camp in gathering)
                PrepareCampaign(g, faction, camp);
            ReturnStrandedTroops(g, faction);
            foreach (var c in w.Cities.Values.Where(c => c.Owner == faction && InScope(c.Id)).OrderBy(c => c.Id).ToList())
            {
                var pool = w.OfficersAt(c.Id).Where(o => o.Faction == faction && !o.Acted).ToList();
                if (c.Governor == null || !w.Officers.TryGetValue(c.Governor, out var gov) || gov.Location != c.Id || gov.Faction != faction)
                    c.Governor = w.OfficersAt(c.Id).Where(o => o.Faction == faction).OrderByDescending(o => o.Def.Stats.Pol).Select(o => o.Id).FirstOrDefault();

                ManageFood(g, c, pool);
                if (pool.Count == 0) continue;

                var camp = gathering.FirstOrDefault(x => x.Source == c.Id);
                bool threatened = Threat(g, c) > c.Troops * 0.7;
                int use = Math.Max(1, Domestic.MonthlyFoodUse(w, c));

                if (camp != null || threatened)
                {
                    if (CanFeedMore(g, c)) Try(pool, o => Domestic.Recruit(w, o.Id, c.Id), byCharisma: true);
                    if (c.Training < 70) Try(pool, o => Domestic.Train(w, o.Id, c.Id), byLeadership: true);
                }

                // 원정 거점으로 모자란 만큼만 병력·군량을 보낸다 (계획이 바뀌어도 병력이 엉뚱한 곳에 갇히지 않게)
                var feed = gathering.FirstOrDefault(x => x.Source != c.Id && Connected(w, c.Id).Contains(x.Source));
                if (feed != null)
                {
                    var src = w.Cities[feed.Source];
                    int deficit = (int)(feed.Required * 1.2) - Surplus(g, src, faction);
                    int troops = Math.Min(Surplus(g, c, faction), Math.Max(0, deficit));
                    int food = Math.Max(0, c.Food - use * 4);
                    if (troops >= 1000 || food >= 5000)
                        Try(pool, o => Domestic.Transport(w, o.Id, c.Id, feed.Source, 0, food, troops));
                }

                if (c.Morale < 45)
                {
                    if (c.Food > use * 8) Try(pool, o => Domestic.Charity(w, o.Id, c.Id), byCharisma: true);
                    else Try(pool, o => Domestic.Patrol(w, o.Id, c.Id));
                }

                BuildSomething(g, c, pool);

                // 군비 확충 (빠른 판): 군량이 감당되는 한 인구 상한의 90%까지 병력을 채운다
                int troopCap = (int)(c.Population * w.Data.Rules.Economy.Recruit.MaxTroopRatio * 0.9);
                if (c.Def.IsCity && c.Troops + c.FieldGarrison < troopCap && c.Population > 30000 && CanFeedMore(g, c))
                    Try(pool, o => Domestic.Recruit(w, o.Id, c.Id), byCharisma: true);
                if (c.Training < 60) Try(pool, o => Domestic.Train(w, o.Id, c.Id), byLeadership: true);

                foreach (var eq in Domestic.Production.Keys)
                    if (c.Equipment[eq] < c.Troops / 2 && Domestic.FacilityTotal(w, c, Domestic.Production[eq].Facility, Domestic.Production[eq].Stat) > 0
                        && c.Gold > 1500)
                        Try(pool, o => Domestic.Produce(w, o.Id, c.Id, eq));

                // 등용: 같은 거점의 재야, 우리 세력의 포로
                foreach (var t in w.Officers.Values.Where(t => (t.Status == OfficerStatus.Free && t.Location == c.Id)
                                                               || (t.Status == OfficerStatus.Captive && t.Faction == faction && t.Location == c.Id)).ToList())
                    Try(pool, o => Domestic.Employ(w, o.Id, c.Id, t.Id), byCharisma: true);

                RedistributeOfficers(g, c, pool, faction);
            }
        }

        /// <summary>
        /// 원정 출발지가 아닌 관문·항구 등에 여유 병력이 쌓여 있으면, 무장이 있는 이어진 도시로 되돌려 보낸다.
        /// 거점에 무장이 없으면 이어진 도시의 무장이 와서 옮긴다고 보고, 그 도시 무장의 행동을 쓴다.
        /// </summary>
        private void ReturnStrandedTroops(Game g, string faction)
        {
            var w = g.World;
            foreach (var post in w.Cities.Values.Where(c => c.Owner == faction && InScope(c.Id) && !c.Def.IsCity).OrderBy(c => c.Id).ToList())
            {
                if (Campaigns.Any(x => x.Source == post.Id)) continue;
                int surplus = Surplus(g, post, faction);
                if (surplus < 3000) continue;
                var home = post.Def.Links.Select(l => w.Cities[l]).Where(x => x.Owner == faction && x.Def.IsCity && InScope(x.Id))
                               .OrderByDescending(x => w.OfficersAt(x.Id).Count(o => o.Faction == faction)).FirstOrDefault();
                if (home == null) continue;
                var mover = w.OfficersAt(post.Id).Concat(w.OfficersAt(home.Id))
                              .FirstOrDefault(o => o.Faction == faction && !o.Acted);
                if (mover == null) continue;
                string original = mover.Location;
                try
                {
                    // 거점에 무장이 없으면 도시 무장이 거점으로 건너가 병력을 이끌어 온다
                    mover.Location = post.Id;
                    Domestic.Transport(w, mover.Id, post.Id, home.Id, 0, 0, surplus);
                    mover.Location = home.Id;
                }
                catch (InvalidOperationException)
                {
                    mover.Location = original;
                }
            }
        }

        /// <summary>
        /// 원정 준비: 출발 거점에 지휘관이 모자라면 이어진 거점에서 무장을 불러 모으고 (각 거점에 한 명은 남긴다),
        /// 원정 군량이 모자라면 금으로 사 둔다.
        /// </summary>
        private void PrepareCampaign(Game g, string faction, Campaign camp)
        {
            var w = g.World;
            var src = w.Cities[camp.Source];
            var rules = w.Data.Rules;
            int Cap(OfficerState o) => UnitStats.MaxTroops(o.Def, o.Rank != null ? w.Data.Ranks[o.Rank] : null, rules.Unit);
            var here = w.OfficersAt(src.Id).Where(o => o.Faction == faction && o.InjuredTurns == 0).ToList();
            int capacity = here.Select(Cap).OrderByDescending(x => x).Skip(1).Take(8).Sum();
            if (capacity < camp.Required)
            {
                foreach (var id in Connected(w, src.Id).Where(id => id != src.Id).OrderBy(id => id))
                {
                    var there = w.OfficersAt(id).Where(o => o.Faction == faction).ToList();
                    if (there.Count < 2) continue;
                    var mover = there.Where(o => !o.Acted && o.InjuredTurns == 0 && o.Id != w.Cities[id].Governor)
                                     .OrderByDescending(Cap).FirstOrDefault();
                    if (mover == null) continue;
                    try
                    {
                        Domestic.MoveOfficer(w, mover.Id, id, src.Id);
                        capacity += Cap(mover);
                    }
                    catch (InvalidOperationException) { }
                    if (capacity >= camp.Required) break;
                }
            }
            int dist = src.Placement.Center.DistanceTo(w.Cities[camp.Target].Placement.Center);
            double perTurn = rules.Economy.FoodPerSoldierMonth / rules.Time.TurnsPerMonth;
            int needFood = (int)(camp.Required * perTurn * (dist / 4 * 2 + 15) * 1.2) + Domestic.MonthlyFoodUse(w, src) * 2;
            if (src.Food < needFood && src.Gold > 300)
            {
                var pool = w.OfficersAt(src.Id).Where(o => o.Faction == faction && !o.Acted).ToList();
                int gold = Math.Min((int)(src.Gold * 0.7), (int)Math.Ceiling((needFood - src.Food) / Domestic.FoodPerGold(w)));
                if (gold > 50) Try(pool, o => Domestic.BuyFood(w, o.Id, src.Id, gold));
            }
        }

        /// <summary>
        /// 징병해도 먹여 살릴 수 있는지: 징병 뒤에도 군량이 8개월분 이상이고,
        /// 1년 수확이 늘어난 병력 1년 소비의 절반은 감당해야 한다.
        /// </summary>
        private static bool CanFeedMore(Game g, CityState c)
        {
            var w = g.World;
            double perSoldier = w.Data.Rules.Economy.FoodPerSoldierMonth;
            int extra = 2500;
            double monthly = (c.Troops + c.FieldGarrison + extra) * perSoldier;
            return c.Food >= monthly * 8 && Domestic.Harvest(w, c) >= monthly * 12 * 0.5;
        }

        private static void Try(List<OfficerState> pool, Func<OfficerState, string> command, bool byCharisma = false, bool byLeadership = false)
        {
            if (pool.Count == 0) return;
            var o = byCharisma ? pool.OrderByDescending(x => x.Def.Stats.Cha + x.Def.Stats.Ldr).First()
                  : byLeadership ? pool.OrderByDescending(x => x.Def.Stats.Ldr).First()
                  : pool.OrderByDescending(x => x.Def.Stats.Pol).First();
            try
            {
                command(o);
                pool.Remove(o);
            }
            catch (InvalidOperationException)
            {
                // 조건이 맞지 않으면 다른 일을 시킨다
            }
        }

        /// <summary>군량: 4개월분이 안 되면 금으로 사고, 수확 뒤 넘치면 판다. 명령 없이 처리할 수 없어 무장 하나를 쓴다.</summary>
        private void ManageFood(Game g, CityState c, List<OfficerState> pool)
        {
            var w = g.World;
            int use = Math.Max(1, Domestic.MonthlyFoodUse(w, c));
            int months = c.Food / use;
            bool rich = c.Gold > 5000;
            if ((months < 4 && c.Gold > 300) || (rich && months < 8))
            {
                int wantFood = use * (rich ? 10 : 7) - c.Food;
                int gold = Math.Min((int)(c.Gold * 0.6), (int)Math.Ceiling(wantFood / Domestic.FoodPerGold(w)));
                if (gold > 50) Try(pool, o => Domestic.BuyFood(w, o.Id, c.Id, gold));
            }
            else if (w.Date.Month >= 9 && c.Food > Domestic.FoodCap(w, c) * 0.9 && months > 14)
                Try(pool, o => Domestic.SellFood(w, o.Id, c.Id, c.Food / 5));
        }

        /// <summary>
        /// 건설: 수확이 소비보다 적으면 농장, 아니면 시장. 시장 옆 조폐, 농장 옆 곡창처럼 인접 보너스를 노린다.
        /// 빈 슬롯이 없으면 기존 시설을 올린다.
        /// </summary>
        private void BuildSomething(Game g, CityState c, List<OfficerState> pool)
        {
            var w = g.World;
            if (c.Placement.Slots.Count == 0 || pool.Count == 0) return;
            if (c.Facilities.Values.Any(f => f.BuildTurnsLeft > 0)) return; // 한 번에 한 곳씩
            int use = Domestic.MonthlyFoodUse(w, c) * 12;
            int harvest = Domestic.Harvest(w, c);
            int count(string id) => c.Facilities.Values.Count(f => f.FacilityId == id);

            // 세력 전체에 군사 시설이 없으면 하나씩: 공성창(성문 깨기), 마구간(추행), 공방(안행)
            var factionFacilities = w.Cities.Values.Where(x => x.Owner == c.Owner).SelectMany(x => x.Facilities.Values).Select(f => f.FacilityId).ToList();
            string? military = new[] { "siege_works", "stable", "workshop" }.FirstOrDefault(id => !factionFacilities.Contains(id));
            bool frontline = Campaigns.Any(x => x.Source == c.Id) || c.Def.Links.Any(l => w.Cities[l].Owner != c.Owner);

            string want;
            if (military != null && frontline && c.Gold > 1500) want = military;
            else if (harvest < use * 1.1) want = "farm";
            else if (count("market") <= count("farm")) want = "market";
            else if (count("barracks") == 0) want = "barracks";
            else if (count("mint") < count("market")) want = "mint";
            else if (count("granary") < Math.Max(1, count("farm") / 2)) want = "granary";
            else want = harvest < use * 1.5 ? "farm" : "market";

            var def = w.Data.Facilities[want];
            var empty = c.Placement.Slots.Where(s => !c.Facilities.ContainsKey(s.Index) && (!def.RequiresWaterside || s.Waterside)).ToList();
            if (empty.Count > 0)
            {
                if (c.Gold < def.Cost.Gold[0] + 300) return;
                string partner = want switch { "mint" => "market", "granary" => "farm", "market" => "mint", "farm" => "granary", _ => "" };
                var slot = empty.OrderByDescending(s => c.Facilities.Any(kv => kv.Value.FacilityId == partner
                                                    && c.Placement.Slots[kv.Key].Hex.DistanceTo(s.Hex) == 1))
                                .ThenBy(s => s.Index).First();
                Try(pool, o => Domestic.Build(w, o.Id, c.Id, slot.Index, want));
                return;
            }
            // 슬롯이 다 찼으면 가장 싼 업그레이드
            var up = c.Facilities.Where(kv => kv.Value.Active && kv.Value.Level < w.Data.Facilities[kv.Value.FacilityId].MaxLevel)
                      .OrderBy(kv => w.Data.Facilities[kv.Value.FacilityId].Cost.Gold[kv.Value.Level]).FirstOrDefault();
            if (up.Value != null && c.Gold > w.Data.Facilities[up.Value.FacilityId].Cost.Gold[up.Value.Level] + 500)
                Try(pool, o => Domestic.Build(w, o.Id, c.Id, up.Key, up.Value.FacilityId));
        }

        /// <summary>무장 배치: 남는 무장을 원정 거점이나 무장이 없는 거점으로 보낸다.</summary>
        private void RedistributeOfficers(Game g, CityState c, List<OfficerState> pool, string faction)
        {
            var w = g.World;
            int here = w.OfficersAt(c.Id).Count(o => o.Faction == faction);
            if (here <= 2 || pool.Count == 0) return;
            var connected = Connected(w, c.Id);
            var source = Campaigns.FirstOrDefault(x => x.State == CampaignState.Gathering && x.Source != c.Id && connected.Contains(x.Source));
            string? dest = source?.Source;
            if (dest == null || w.OfficersAt(dest).Count(o => o.Faction == faction) >= 5)
                dest = connected.Where(id => id != c.Id && w.Cities[id].Def.IsCity && !w.OfficersAt(id).Any(o => o.Faction == faction))
                                .OrderBy(id => id).FirstOrDefault();
            if (dest == null) return;
            var mover = pool.OrderByDescending(o => o.Def.Stats.Ldr + o.Def.Stats.War).First();
            try
            {
                Domestic.MoveOfficer(w, mover.Id, c.Id, dest);
                pool.Remove(mover);
            }
            catch (InvalidOperationException) { }
        }
    }
}
