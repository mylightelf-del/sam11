using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Common;
using Sam11.Core.Data;

namespace Sam11.Core.World
{
    /// <summary>
    /// 내정 (03 내정 설계서). 무장 1명은 한 턴에 명령 1개를 수행한다.
    /// 잘못된 명령은 InvalidOperationException, 성공하면 결과 문장을 돌려준다.
    /// </summary>
    public static class Domestic
    {
        public static readonly Dictionary<string, (string Facility, string Stat)> Production = new Dictionary<string, (string, string)>
        {
            ["horse"] = ("stable", "produce_horse"),
            ["crossbow"] = ("workshop", "produce_crossbow"),
            ["siege_engine"] = ("siege_works", "produce_siege_engine"),
            ["ship"] = ("shipyard", "produce_ship"),
        };

        private static EconomyRules Eco(WorldState w) => w.Data.Rules.Economy;

        // ------------------------------------------------------------------ 계산

        /// <summary>도시 안 시설들의 stat 합. 인접 보너스 포함 (예: 시장 옆 조폐 → 시장 +50%).</summary>
        public static double FacilityTotal(WorldState w, CityState c, string facilityId, string stat)
        {
            double total = 0;
            foreach (var (slot, f) in c.ActiveFacilities(facilityId))
            {
                double v = w.Data.Facilities[f.FacilityId].ValueAt(stat, f.Level);
                total += v * (1 + AdjacencyBonus(w, c, slot, f.FacilityId, stat));
            }
            return total;
        }

        public static double AdjacencyBonus(WorldState w, CityState c, FacilitySlot slot, string facilityId, string stat)
        {
            double bonus = 0;
            var neighbors = new HashSet<HexCoord>(slot.Hex.Neighbors());
            foreach (var (other, of) in c.ActiveFacilities())
            {
                if (!neighbors.Contains(other.Hex)) continue;
                foreach (var a in w.Data.Facilities[of.FacilityId].Adjacency)
                    if (a.Target == facilityId && a.Stat == stat)
                        bonus += a.Value;
            }
            return bonus;
        }

        /// <summary>태수 특기 중 "태수일 때" 효과의 합.</summary>
        public static double GovernorBonus(WorldState w, CityState c, string stat, string op = "mul")
        {
            if (c.Governor == null || !w.Officers.TryGetValue(c.Governor, out var g)) return 0;
            double sum = 0;
            foreach (var t in g.Def.Traits)
            {
                if (!w.Data.Traits.TryGetValue(t, out var def)) continue;
                foreach (var e in def.Effects)
                {
                    if (e.Type != "modifier" || e.Stat != stat || e.Op != op) continue;
                    var role = e.When?["role"];
                    if (e.When == null || (role != null && role.ToString().Contains("governor")))
                        sum += e.Value;
                }
            }
            return sum;
        }

        private static double TraitSum(OfficerDef o, GameData data, string stat, string op)
        {
            double sum = 0;
            foreach (var t in o.Traits)
                if (data.Traits.TryGetValue(t, out var def))
                    sum += def.Effects.Where(e => e.Type == "modifier" && e.Stat == stat && e.Op == op && e.When == null).Sum(e => e.Value);
            return sum;
        }

        public static double MoraleIncomeMultiplier(WorldState w, int morale)
        {
            foreach (var b in w.Data.Rules.Domestic.MoraleIncome.OrderByDescending(b => b.Min))
                if (morale >= b.Min) return b.Mult;
            return 1.0;
        }

        private static TaxLevel Tax(WorldState w, CityState c) =>
            w.Data.Rules.Domestic.Tax[c.Owner != null ? w.Factions[c.Owner].Tax : "mid"];

        public static int MonthlyGold(WorldState w, CityState c)
        {
            double v = FacilityTotal(w, c, "market", "gold_income")
                       + c.Population * Eco(w).TaxPerCapitaGold * Tax(w, c).Gold;
            v *= MoraleIncomeMultiplier(w, c.Morale) * (1 + GovernorBonus(w, c, "gold_income"));
            if (c.Owner != null) v *= 1 + w.Factions[c.Owner].IncomeBonus;
            return (int)Math.Round(v);
        }

        public static int Harvest(WorldState w, CityState c)
        {
            double v = FacilityTotal(w, c, "farm", "harvest") + c.Population * Eco(w).HarvestPerCapita;
            v *= MoraleIncomeMultiplier(w, c.Morale) * (1 + GovernorBonus(w, c, "harvest"));
            return (int)Math.Round(v);
        }

        public static int MonthlyFoodUse(WorldState w, CityState c) => (int)Math.Ceiling((c.Troops + c.FieldGarrison) * Eco(w).FoodPerSoldierMonth);

        public static int FoodCap(WorldState w, CityState c) =>
            (Eco(w).FoodCap.TryGetValue(c.Def.Type, out var cap) ? cap : 0) + (int)FacilityTotal(w, c, "granary", "food_cap");

        public static int PopulationCap(WorldState w, CityState c) =>
            (Eco(w).PopCap.TryGetValue(c.Def.Type, out var cap) ? cap : 0) + (int)FacilityTotal(w, c, "housing", "population_cap");

        // ------------------------------------------------------------------ 명령 공통

        private static (OfficerState o, CityState c) Prepare(WorldState w, string officerId, string cityId)
        {
            if (!w.Officers.TryGetValue(officerId, out var o)) throw new InvalidOperationException($"없는 무장: {officerId}");
            if (!w.Cities.TryGetValue(cityId, out var c)) throw new InvalidOperationException($"없는 거점: {cityId}");
            if (o.Status != OfficerStatus.Active || o.Faction == null)
                throw new InvalidOperationException($"{Korean.EunNeun(o.Def.Name.Ko)} 세력 소속이 아님");
            if (c.Owner != o.Faction) throw new InvalidOperationException($"{Korean.EunNeun(c.ToString())} {o}의 세력 거점이 아님");
            if (o.Location != cityId) throw new InvalidOperationException($"{Korean.EunNeun(o.Def.Name.Ko)} {c}에 없음");
            if (o.Acted) throw new InvalidOperationException($"{Korean.EunNeun(o.Def.Name.Ko)} 이번 턴에 이미 행동함");
            return (o, c);
        }

        private static void Spend(CityState c, int gold, int food = 0)
        {
            if (c.Gold < gold) throw new InvalidOperationException($"금 부족: {c.Gold}/{gold}");
            if (c.Food < food) throw new InvalidOperationException($"군량 부족: {c.Food}/{food}");
            c.Gold -= gold;
            c.Food -= food;
        }

        private static string Done(WorldState w, OfficerState o, string message)
        {
            o.Acted = true;
            o.Merit += Eco(w).MeritPerCommand;
            w.Log.Add($"[{w.Date}] {message}");
            return message;
        }

        // ------------------------------------------------------------------ 명령

        /// <summary>건설: 빈 슬롯에 새로 짓거나, 같은 시설을 한 단계 올린다. 공사 중에는 효과가 없다.</summary>
        public static string Build(WorldState w, string officerId, string cityId, int slotIndex, string facilityId)
        {
            var (o, c) = Prepare(w, officerId, cityId);
            if (slotIndex < 0 || slotIndex >= c.Placement.Slots.Count) throw new InvalidOperationException($"{c}에 {slotIndex}번 슬롯이 없음");
            if (!w.Data.Facilities.TryGetValue(facilityId, out var def)) throw new InvalidOperationException($"없는 시설: {facilityId}");
            var slot = c.Placement.Slots[slotIndex];
            if (def.RequiresWaterside && !slot.Waterside) throw new InvalidOperationException($"{Korean.EunNeun(def.Name.Ko)} 물가 슬롯에만 지을 수 있음");

            c.Facilities.TryGetValue(slotIndex, out var existing);
            int level;
            if (existing == null) level = 1;
            else if (existing.FacilityId != facilityId)
                throw new InvalidOperationException($"{slotIndex}번 슬롯에는 이미 {Korean.IGa(w.Data.Facilities[existing.FacilityId].Name.Ko)} 있음");
            else if (existing.BuildTurnsLeft > 0) throw new InvalidOperationException("공사 중인 시설");
            else if (existing.Level >= def.MaxLevel) throw new InvalidOperationException($"{Korean.EunNeun(def.Name.Ko)} 이미 최고 단계");
            else level = existing.Level + 1;

            int gold = def.Cost.Gold[level - 1];
            int turns = def.Cost.BuildTurns[level - 1] + (int)TraitSum(o.Def, w.Data, "build_time", "add");
            Spend(c, gold);
            c.Facilities[slotIndex] = new FacilityState { FacilityId = facilityId, Level = level, BuildTurnsLeft = Math.Max(1, turns) };
            return Done(w, o, $"{o}, {c}에 {def.Name.Ko} {level}단계 착공 ({Math.Max(1, turns)}턴, 금 {gold})");
        }

        /// <summary>징병: 인구 → 병력. 민심이 떨어진다.</summary>
        public static string Recruit(WorldState w, string officerId, string cityId)
        {
            var (o, c) = Prepare(w, officerId, cityId);
            var r = Eco(w).Recruit;
            double n = (r.Base + (o.Def.Stats.Cha + o.Def.Stats.Ldr) * r.StatFactor) * (c.Morale / 100.0);
            n *= 1 + FacilityTotal(w, c, "barracks", "recruit") + TraitSum(o.Def, w.Data, "recruit", "mul");
            n = Math.Min(n, c.Population * r.PopRatioCap);
            n = Math.Min(n, c.Population * r.MaxTroopRatio - c.Troops - c.FieldGarrison); // 인구가 감당할 병력 상한
            n = Math.Min(n, c.Gold / r.GoldPerSoldier);
            int troops = (int)n;
            if (troops <= 0) throw new InvalidOperationException("징병할 수 있는 인구·금이 없음");
            int gold = (int)Math.Ceiling(troops * r.GoldPerSoldier);
            Spend(c, gold);
            c.Training = (c.Training * c.Troops + 20 * troops) / Math.Max(1, c.Troops + troops);
            c.Population -= troops;
            c.Troops += troops;
            double loss = r.MoraleLoss * (1 + TraitSum(o.Def, w.Data, "recruit_morale_loss", "mul"));
            c.Morale = Math.Max(0, c.Morale - (int)Math.Round(loss));
            return Done(w, o, $"{o}, {c}에서 {troops}명 징병 (금 {gold}, 민심 -{Math.Round(loss)})");
        }

        public static string Train(WorldState w, string officerId, string cityId)
        {
            var (o, c) = Prepare(w, officerId, cityId);
            var r = Eco(w).Train;
            double gain = (r.Base + o.Def.Stats.Ldr / (double)r.LdrDivisor)
                          * (1 + FacilityTotal(w, c, "barracks", "train") + TraitSum(o.Def, w.Data, "train", "mul"));
            int before = c.Training;
            c.Training = Math.Min(100, c.Training + (int)Math.Round(gain));
            return Done(w, o, $"{o}, {c} 훈련 ({before} → {c.Training})");
        }

        public static string Patrol(WorldState w, string officerId, string cityId)
        {
            var (o, c) = Prepare(w, officerId, cityId);
            var r = Eco(w).Patrol;
            int gain = r.Base + (o.Def.Stats.War + o.Def.Stats.Cha) / r.StatDivisor;
            int before = c.Morale;
            c.Morale = Math.Min(100, c.Morale + gain);
            return Done(w, o, $"{o}, {c} 순찰 (민심 {before} → {c.Morale})");
        }

        /// <summary>생산: 해당 시설이 있어야 병장기를 만든다.</summary>
        public static string Produce(WorldState w, string officerId, string cityId, string equipment)
        {
            var (o, c) = Prepare(w, officerId, cityId);
            if (!Production.TryGetValue(equipment, out var p)) throw new InvalidOperationException($"없는 병장기: {equipment}");
            double amount = FacilityTotal(w, c, p.Facility, p.Stat);
            if (amount <= 0) throw new InvalidOperationException($"{c}에 {Korean.IGa(w.Data.Facilities[p.Facility].Name.Ko)} 없음");
            var r = Eco(w).Produce;
            amount *= 1 + o.Def.Stats.Pol / (double)r.PolDivisor + TraitSum(o.Def, w.Data, "produce", "mul");
            int n = (int)amount;
            int gold = (int)Math.Ceiling(n * r.GoldPerUnit[equipment]);
            Spend(c, gold);
            c.Equipment[equipment] += n;
            return Done(w, o, $"{o}, {c}에서 {equipment} {n} 생산 (금 {gold})");
        }

        /// <summary>시혜: 군량을 풀어 민심을 올린다.</summary>
        public static string Charity(WorldState w, string officerId, string cityId)
        {
            var (o, c) = Prepare(w, officerId, cityId);
            var r = Eco(w).Charity;
            int food = (int)Math.Ceiling(c.Population * r.FoodRatio);
            Spend(c, 0, food);
            int before = c.Morale;
            c.Morale = Math.Min(100, c.Morale + r.Base + o.Def.Stats.Cha / r.ChaDivisor);
            return Done(w, o, $"{o}, {c}에 군량 {food} 시혜 (민심 {before} → {c.Morale})");
        }

        /// <summary>금 1당 군량 시세. 수확 직후(9~10월)에 싸고, 수확 직전(7~8월)에 비싸다.</summary>
        public static double FoodPerGold(WorldState w) =>
            Eco(w).Trade.FoodPerGold * Eco(w).Trade.MonthlyFactor[w.Date.Month - 1];

        /// <summary>한 번에 거래할 수 있는 군량: 도시 인구에 비례 (지역 시장의 한계).</summary>
        public static int MaxTradeFood(WorldState w, CityState c) => (int)(c.Population * Eco(w).Trade.MaxFoodRatio);

        public static string BuyFood(WorldState w, string officerId, string cityId, int gold)
        {
            var (o, c) = Prepare(w, officerId, cityId);
            if (gold <= 0) throw new InvalidOperationException("금액이 0 이하");
            int limit = MaxTradeFood(w, c);
            if (limit <= 0) throw new InvalidOperationException($"{c}에는 군량을 사고팔 시장이 없음");
            gold = Math.Min(gold, (int)Math.Ceiling(limit / FoodPerGold(w)));
            Spend(c, gold);
            int food = (int)(gold * FoodPerGold(w));
            c.Food = Math.Min(FoodCap(w, c), c.Food + food);
            return Done(w, o, $"{o}, {c}에서 금 {gold}으로 군량 {food} 구입");
        }

        public static string SellFood(WorldState w, string officerId, string cityId, int food)
        {
            var (o, c) = Prepare(w, officerId, cityId);
            if (food <= 0) throw new InvalidOperationException("수량이 0 이하");
            food = Math.Min(food, MaxTradeFood(w, c));
            if (food <= 0) throw new InvalidOperationException($"{c}에는 군량을 사고팔 시장이 없음");
            Spend(c, 0, food);
            int gold = (int)(food / FoodPerGold(w));
            c.Gold += gold;
            return Done(w, o, $"{o}, {c}에서 군량 {food}을 금 {gold}에 판매");
        }

        /// <summary>아군 거점끼리 길로 이어져 있으면 그 거점들의 목록 (from 포함).</summary>
        public static HashSet<string> ConnectedOwn(WorldState w, string from)
        {
            string? owner = w.Cities[from].Owner;
            var seen = new HashSet<string> { from };
            var stack = new Stack<string>();
            stack.Push(from);
            while (stack.Count > 0)
                foreach (var l in w.Cities[stack.Pop()].Def.Links)
                    if (w.Cities[l].Owner == owner && seen.Add(l))
                        stack.Push(l);
            return seen;
        }

        /// <summary>무장 이동: 길로 이어진 아군 거점으로 옮긴다 (이번 턴 행동 소모).</summary>
        public static string MoveOfficer(WorldState w, string officerId, string from, string to)
        {
            var (o, c) = Prepare(w, officerId, from);
            if (!ConnectedOwn(w, from).Contains(to) || from == to)
                throw new InvalidOperationException($"{c}에서 {w.Cities[to]}까지 아군 거점으로 이어지지 않음");
            o.Location = to;
            return Done(w, o, $"{o}, {c}에서 {Korean.EuRo(w.Cities[to].ToString())} 이동");
        }

        /// <summary>수송: 금·군량·병력·병장기를 길로 이어진 아군 거점으로 보낸다.</summary>
        public static string Transport(WorldState w, string officerId, string from, string to,
                                       int gold, int food, int troops, IReadOnlyDictionary<string, int>? equipment = null)
        {
            var (o, c) = Prepare(w, officerId, from);
            if (!ConnectedOwn(w, from).Contains(to) || from == to)
                throw new InvalidOperationException($"{c}에서 {w.Cities[to]}까지 아군 거점으로 이어지지 않음");
            if (gold < 0 || food < 0 || troops < 0) throw new InvalidOperationException("음수는 보낼 수 없음");
            if (troops > c.Troops) throw new InvalidOperationException($"병력 부족: {c.Troops}/{troops}");
            if (equipment != null)
                foreach (var kv in equipment)
                    if (kv.Value < 0 || c.Equipment[kv.Key] < kv.Value) throw new InvalidOperationException($"{kv.Key} 부족");
            Spend(c, gold, food);
            var dst = w.Cities[to];
            dst.Gold += gold;
            dst.Food = Math.Min(FoodCap(w, dst), dst.Food + food);
            c.Troops -= troops;
            dst.Troops += troops;
            if (equipment != null)
                foreach (var kv in equipment)
                {
                    c.Equipment[kv.Key] -= kv.Value;
                    dst.Equipment[kv.Key] += kv.Value;
                }
            return Done(w, o, $"{o}, {c} → {dst} 수송 (금 {gold}, 군량 {food}, 병력 {troops})");
        }

        /// <summary>등용 성공률: 매력, 상성(원형 거리), 대상의 의리(포로일 때), 명성 특기.</summary>
        public static double EmployChance(WorldState w, OfficerState recruiter, OfficerState target)
        {
            var ruler = w.Officers[w.Factions[recruiter.Faction!].Ruler];
            int diff = Math.Abs(ruler.Def.Hidden.Compat - target.Def.Hidden.Compat);
            diff = Math.Min(diff, 150 - diff);
            double p = 0.25 + (recruiter.Def.Stats.Cha - 50) / 100.0 + (1 - diff / 75.0) * 0.3;
            if (target.Status == OfficerStatus.Captive) p -= target.Def.Hidden.Loyalty * 0.03;
            p += TraitSum(recruiter.Def, w.Data, "recruit_officer", "mul");
            return Math.Max(0.02, Math.Min(0.95, p));
        }

        /// <summary>등용: 같은 거점의 재야 무장이나 우리 세력의 포로를 영입한다.</summary>
        public static string Employ(WorldState w, string officerId, string cityId, string targetId)
        {
            var (o, c) = Prepare(w, officerId, cityId);
            if (!w.Officers.TryGetValue(targetId, out var t)) throw new InvalidOperationException($"없는 무장: {targetId}");
            bool free = t.Status == OfficerStatus.Free && t.Location == cityId;
            bool captive = t.Status == OfficerStatus.Captive && t.Faction == o.Faction;
            if (!free && !captive) throw new InvalidOperationException($"{Korean.EunNeun(t.ToString())} 등용할 수 없는 상태");
            double p = EmployChance(w, o, t);
            if (!w.Rng.Chance(p))
            {
                // 망설이는 상대에게는 설전으로 한 번 더 (05 설계서 4.2)
                if (p < w.Data.Rules.Duel.EmployDebateMinChance)
                    return Done(w, o, $"{o}, {t} 등용 실패 ({p:P0})");
                var debate = new Duel.CardDuel(w.Data, w.Rng, Duel.DuelKind.Debate, o.Def, t.Def);
                if (!debate.Run())
                    return Done(w, o, $"{o}, {t} 등용 실패 — 설전에서 밀렸다 ({p:P0})");
                w.Log.Add($"[{w.Date}] {o}, 설전으로 {Korean.EulReul(t.ToString())} 설득했다");
            }
            t.Status = OfficerStatus.Active;
            t.Faction = o.Faction;
            t.Location = cityId;
            t.Loyalty = 70;
            t.CaptiveMonths = 0;
            return Done(w, o, $"{o}, {Korean.EulReul(t.ToString())} 등용! ({p:P0})");
        }

        /// <summary>1월마다 15세가 된 무장이 등장한다. 아버지가 세력에 있으면 그 세력으로, 아니면 재야로.</summary>
        public static void Debut(WorldState w)
        {
            foreach (var o in w.Officers.Values.Where(o => o.Status == OfficerStatus.Unborn && w.Date.Year - o.Def.Birth >= 15))
            {
                string? fatherId = o.Def.Kin?.Father;
                if (fatherId != null && w.Officers.TryGetValue(fatherId, out var father)
                    && father.Status == OfficerStatus.Active && father.Faction != null)
                {
                    o.Status = OfficerStatus.Active;
                    o.Faction = father.Faction;
                    o.Location = father.Location != "" ? father.Location : w.Factions[father.Faction].Capital;
                    o.Loyalty = 90;
                    w.Log.Add($"[{w.Date}] {Korean.IGa(o.ToString())} 성인이 되어 {w.NameOf(o.Faction)} 세력에 합류");
                }
                else
                {
                    o.Status = OfficerStatus.Free;
                    w.Log.Add($"[{w.Date}] {Korean.IGa(o.ToString())} 성인이 되어 {w.Cities[o.Location]}에 재야로 등장");
                }
            }
        }

        // ------------------------------------------------------------------ 턴 진행

        /// <summary>턴을 마친다: 공사 진행 → 날짜 진행 → 월초면 월간 처리 (9월이면 수확).</summary>
        public static void AdvanceTurn(WorldState w)
        {
            foreach (var c in w.Cities.Values)
                foreach (var kv in c.Facilities)
                    if (kv.Value.BuildTurnsLeft > 0 && --kv.Value.BuildTurnsLeft == 0)
                        w.Log.Add($"[{w.Date}] {c}의 {w.Data.Facilities[kv.Value.FacilityId].Name.Ko} {kv.Value.Level}단계 완공");
            foreach (var o in w.Officers.Values)
            {
                o.Acted = false;
                if (o.InjuredTurns > 0) o.InjuredTurns--;
            }

            w.Date.Advance(w.Data.Rules.Time.TurnsPerMonth);
            if (w.Date.IsMonthStart)
                MonthStart(w);
        }

        /// <summary>
        /// 후방 보급: 관문·요새·항구·나루는 자기가 속한 영역의 도시, 또는 길로 이어진 같은 세력 거점에서
        /// 남는 군량을 자동으로 받는다. 보내는 쪽은 자기 몫 몇 달 치를 남긴다.
        /// 달마다 한 단계씩 전달되므로 낙양 → 호로관 → 사수관처럼 줄줄이 이어진다.
        /// 이어진 아군 거점이 모두 끊기면 고립되어 굶는다.
        /// </summary>
        public static void RearSupply(WorldState w)
        {
            var r = Eco(w).RearSupply;
            foreach (var post in w.Cities.Values.Where(c => !c.Def.IsCity && c.Owner != null).OrderBy(c => c.Id))
            {
                int want = MonthlyFoodUse(w, post) * r.TargetMonths - post.Food;
                if (want <= 0) continue;
                var sources = post.Def.Links.Append(post.Placement.TerritoryOf ?? "")
                    .Distinct()
                    .Where(id => w.Cities.TryGetValue(id, out var s) && s.Owner == post.Owner)
                    .Select(id => w.Cities[id])
                    .OrderByDescending(s => s.Def.IsCity).ThenBy(s => s.Id);
                foreach (var src in sources)
                {
                    int spare = src.Food - MonthlyFoodUse(w, src) * (src.Def.IsCity ? r.CityReserveMonths : r.PostReserveMonths);
                    int moved = Math.Min(want, spare);
                    if (moved <= 0) continue;
                    src.Food -= moved;
                    post.Food += moved;
                    want -= moved;
                    if (want <= 0) break;
                }
            }
        }

        public static void MonthStart(WorldState w)
        {
            var eco = Eco(w);
            bool harvest = w.Date.Month == w.Data.Rules.Time.HarvestMonth;
            if (w.Date.Month == 1) Debut(w);
            var harvested = new Dictionary<string, int>();
            RearSupply(w);
            foreach (var c in w.Cities.Values)
            {
                if (c.Owner != null)
                {
                    c.Gold += MonthlyGold(w, c);

                    int use = MonthlyFoodUse(w, c);
                    if (c.Food >= use) c.Food -= use;
                    else
                    {
                        c.Food = 0;
                        int deserters = (int)(c.Troops * eco.Starvation.Desertion);
                        c.Troops -= deserters;
                        c.Morale = Math.Max(0, c.Morale - eco.Starvation.MoraleLoss);
                        w.Log.Add($"[{w.Date}] {c} 군량 부족! 병사 {deserters}명 탈영");
                    }

                    if (harvest)
                    {
                        int h = Harvest(w, c);
                        c.Food = Math.Min(FoodCap(w, c), c.Food + h);
                        harvested[c.Owner] = harvested.TryGetValue(c.Owner, out var s) ? s + h : h;
                    }
                }

                // 인구: 민심이 높을수록 빨리 는다. 상한을 넘으면 늘지 않는다.
                if (c.Population > 0 && c.Population < PopulationCap(w, c))
                {
                    double rate = eco.PopGrowthBase + (c.Morale - 50) * eco.PopGrowthPerMorale;
                    c.Population = Math.Min(PopulationCap(w, c), c.Population + (int)(c.Population * rate));
                }

                // 민심: 50으로 서서히 돌아가고, 세율·태수·민가 효과를 받는다.
                int m = c.Morale;
                if (m > eco.MoraleDriftTarget) m -= eco.MoraleDriftRate;
                else if (m < eco.MoraleDriftTarget) m += eco.MoraleDriftRate;
                if (c.Owner != null)
                    m += Tax(w, c).MoraleMonthly + (int)GovernorBonus(w, c, "city_morale_monthly", "add")
                         + (int)FacilityTotal(w, c, "housing", "city_morale_monthly");
                c.Morale = Math.Max(0, Math.Min(100, m));
            }
            if (harvest)
                foreach (var kv in harvested.OrderByDescending(kv => kv.Value))
                    w.Log.Add($"[{w.Date}] 가을 수확: {w.Officers[w.Factions[kv.Key].Ruler].Def.Name.Ko} 세력 군량 +{kv.Value}");
        }
    }
}
