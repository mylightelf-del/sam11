using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Common;
using Sam11.Core.Data;

namespace Sam11.Core.World
{
    public enum DiploType { Alliance, Truce, Gift, CaptiveExchange, Ransom, Aid, Vassal, Absorb }

    public sealed class Proposal
    {
        public DiploType Type;
        public string From = "";
        public string To = "";
        public string Envoy = "";
        public int Gold;
        public string? Captive;
        public override string ToString() => $"{Type} {From}→{To}";
    }

    /// <summary>외교 제안을 받는 쪽의 판단 (AI 또는 플레이어 입력).</summary>
    public interface IDiplomacyResponder
    {
        bool Wants(Game game, Proposal p);
    }

    /// <summary>
    /// 외교 (05 외교·계략 설계서 2장). 사자를 보내 제안하고, 성공률은 사자의 정치·매력, 우호도, 신용,
    /// 상대가 원하는지로 정한다. 상대가 망설이면 설전으로 한 번 더.
    /// </summary>
    public static class Diplomacy
    {
        private static DiplomacyRules R(WorldState w) => w.Data.Rules.Diplomacy;

        /// <summary>국력: 병력 + 도시당 1만.</summary>
        public static int Power(Game g, string faction) =>
            g.World.Cities.Values.Where(c => c.Owner == faction).Sum(c => c.Troops + c.FieldGarrison + (c.Def.IsCity ? 10000 : 0))
            + g.Field.Alive.Where(u => u.Faction == faction && !u.IsGarrison).Sum(u => u.Troops);

        public static int AllianceCount(WorldState w, string f) =>
            w.Alliances.Count(a => a.Kind == PactKind.Alliance && a.Active(w.Date) && a.Members.Contains(f));

        public static bool Borders(WorldState w, string a, string b) =>
            w.Cities.Values.Any(c => c.Owner == a && c.Def.Links.Any(l => w.Cities[l].Owner == b));

        public static double Chance(Game g, Proposal p, bool targetWants)
        {
            var w = g.World;
            var r = R(w);
            var envoy = w.Officers[p.Envoy].Def.Stats;
            double c = r.BaseChance + ((envoy.Pol + envoy.Cha) / 2.0 - r.EnvoyStatCenter) / r.EnvoyStatDivisor
                       + (w.FriendshipOf(p.From, p.To) - 50) / r.FriendshipDivisor
                       + (w.TrustOf(p.From) - 50) / r.TrustDivisor;
            if (p.Type == DiploType.Gift) c = 1;
            if (!targetWants) c *= r.UnwillingFactor;
            return Math.Max(0.02, Math.Min(0.98, c));
        }

        /// <summary>제안. 성공하면 true. 사자는 이번 턴 행동을 마친다.</summary>
        public static bool Propose(Game g, Proposal p)
        {
            var w = g.World;
            var envoy = w.Officers[p.Envoy];
            if (envoy.Status != OfficerStatus.Active || envoy.Faction != p.From || envoy.Acted)
                throw new InvalidOperationException($"{Korean.EunNeun(envoy.ToString())} 사자로 갈 수 없음");
            if (!w.Factions.TryGetValue(p.To, out var to) || !to.Alive || p.From == p.To)
                throw new InvalidOperationException("제안할 수 없는 세력");
            Validate(g, p);
            envoy.Acted = true;

            bool wants = g.Controllers.TryGetValue(p.To, out var ctl) && ctl is IDiplomacyResponder resp ? resp.Wants(g, p) : true;
            double chance = Chance(g, p, wants);
            bool ok = w.Rng.Chance(chance);
            string what = Describe(p);
            if (!ok && chance >= R(w).DebateMinChance && p.Type != DiploType.Gift)
            {
                var rival = w.Officers.Values.Where(o => o.Faction == p.To && o.Status == OfficerStatus.Active)
                             .OrderByDescending(o => o.Def.Stats.Int).ThenBy(o => o.Id).FirstOrDefault();
                if (rival != null)
                {
                    var debate = new Duel.CardDuel(w.Data, w.Rng, Duel.DuelKind.Debate, envoy.Def, rival.Def);
                    ok = debate.Run();
                    w.Log.Add($"[{w.Date}] 외교 설전: {envoy} 대 {rival} → {(ok ? "설득" : "결렬")}");
                }
            }
            w.Log.Add($"[{w.Date}] 외교: {w.NameOf(p.From)} → {w.NameOf(p.To)} {what} {(ok ? "성사" : "거절")} (사자 {envoy}, {chance:P0})");
            if (ok) Apply(g, p);
            else w.ChangeFriendship(p.From, p.To, -2);
            return ok;
        }

        private static string Describe(Proposal p) => p.Type switch
        {
            DiploType.Alliance => "동맹",
            DiploType.Truce => "정전",
            DiploType.Gift => $"선물(금 {p.Gold})",
            DiploType.CaptiveExchange => "포로 교환",
            DiploType.Ransom => $"몸값(금 {p.Gold})",
            DiploType.Aid => "원조 요청",
            DiploType.Vassal => "종속",
            DiploType.Absorb => "병합",
            _ => p.Type.ToString(),
        };

        private static void Validate(Game g, Proposal p)
        {
            var w = g.World;
            var capital = w.Cities[w.Factions[p.From].Capital];
            switch (p.Type)
            {
                case DiploType.Gift:
                case DiploType.Ransom:
                    if (p.Gold <= 0 || capital.Gold < p.Gold) throw new InvalidOperationException("금이 모자람");
                    break;
                case DiploType.Alliance:
                case DiploType.Truce:
                    if (w.AtPeace(p.From, p.To)) throw new InvalidOperationException("이미 싸우지 않는 사이");
                    if (p.Type == DiploType.Alliance && (AllianceCount(w, p.From) >= R(w).MaxAlliances || AllianceCount(w, p.To) >= R(w).MaxAlliances))
                        throw new InvalidOperationException("동맹이 이미 너무 많음");
                    break;
                case DiploType.Absorb:
                    if (!w.Vassals.TryGetValue(p.To, out var s) || s != p.From) throw new InvalidOperationException("종속국만 병합할 수 있음");
                    break;
            }
            if (p.Type == DiploType.Ransom && (p.Captive == null || w.Officers[p.Captive].Status != OfficerStatus.Captive || w.Officers[p.Captive].Faction != p.To))
                throw new InvalidOperationException("상대가 잡고 있는 포로가 아님");
        }

        private static void Apply(Game g, Proposal p)
        {
            var w = g.World;
            var r = R(w);
            var capital = w.Cities[w.Factions[p.From].Capital];
            var theirCapital = w.Cities[w.Factions[p.To].Capital];
            switch (p.Type)
            {
                case DiploType.Alliance:
                    w.AddPact(PactKind.Alliance, p.From, p.To, r.AllianceMonths, "동맹");
                    w.ChangeFriendship(p.From, p.To, 10);
                    break;
                case DiploType.Truce:
                    w.AddPact(PactKind.Truce, p.From, p.To, r.TruceMonths, "정전");
                    break;
                case DiploType.Gift:
                    capital.Gold -= p.Gold;
                    theirCapital.Gold += p.Gold;
                    w.ChangeFriendship(p.From, p.To, Math.Min(r.GiftMaxFriendship, p.Gold / r.GiftGoldPerFriendship));
                    break;
                case DiploType.Ransom:
                    capital.Gold -= p.Gold;
                    theirCapital.Gold += p.Gold;
                    Release(w, p.Captive!, p.From);
                    break;
                case DiploType.CaptiveExchange:
                    foreach (var o in w.Officers.Values.Where(o => o.Status == OfficerStatus.Captive && (o.Faction == p.From || o.Faction == p.To)).ToList())
                    {
                        // 원래 세력을 기억하지 않으므로, 상대 세력에 잡힌 포로는 상대에게 이전 소속 기록이 있으면 돌려준다
                        string? home = o.PreviousFaction;
                        if (home == p.From || home == p.To) Release(w, o.Id, home);
                    }
                    w.ChangeFriendship(p.From, p.To, 5);
                    break;
                case DiploType.Aid:
                {
                    int food = (int)(theirCapital.Food * r.AidRatio);
                    int gold = (int)(theirCapital.Gold * r.AidRatio);
                    theirCapital.Food -= food; capital.Food += food;
                    theirCapital.Gold -= gold; capital.Gold += gold;
                    w.Log.Add($"[{w.Date}]   원조: 금 {gold}, 군량 {food}");
                    break;
                }
                case DiploType.Vassal:
                    // From이 To의 종속국이 된다
                    w.Vassals[p.From] = p.To;
                    w.ChangeFriendship(p.From, p.To, 10);
                    break;
                case DiploType.Absorb:
                    Absorb(g, p.To, p.From);
                    break;
            }
        }

        private static void Release(WorldState w, string officerId, string toFaction)
        {
            var o = w.Officers[officerId];
            o.Status = OfficerStatus.Active;
            o.Faction = toFaction;
            o.CaptiveMonths = 0;
            o.Location = w.Factions[toFaction].Capital;
            w.Log.Add($"[{w.Date}]   {Korean.IGa(o.ToString())} 풀려나 {w.NameOf(toFaction)} 세력으로 돌아갔다");
        }

        /// <summary>평화 병합: 종속국의 거점·무장이 종주국으로 넘어간다.</summary>
        private static void Absorb(Game g, string vassal, string suzerain)
        {
            var w = g.World;
            foreach (var c in w.Cities.Values.Where(c => c.Owner == vassal))
            {
                c.Owner = suzerain;
                g.Field.Strongholds[c.Id].Owner = suzerain;
            }
            foreach (var u in g.Field.Alive.Where(u => u.Faction == vassal).ToList())
                u.Status = Battle.UnitStatus.Withdrawn;
            foreach (var o in w.Officers.Values.Where(o => o.Faction == vassal && o.Status == OfficerStatus.Active))
            {
                o.Faction = suzerain;
                o.Loyalty = Math.Min(o.Loyalty, 70);
            }
            w.Vassals.Remove(vassal);
            w.Factions[vassal].Alive = false;
            w.Log.Add($"[{w.Date}] ★ {w.NameOf(vassal)} 세력이 {w.NameOf(suzerain)} 세력에 평화롭게 병합되었다");
        }

        /// <summary>동맹·정전 파기: 신용과 우호도가 크게 떨어진다.</summary>
        public static void Break(Game g, string faction, string other)
        {
            var w = g.World;
            var r = R(w);
            int removed = w.Alliances.RemoveAll(a => a.Kind != PactKind.Coalition && a.Active(w.Date) && a.Members.Contains(faction) && a.Members.Contains(other));
            if (w.Vassals.TryGetValue(other, out var s) && s == faction) { w.Vassals.Remove(other); removed++; }
            if (w.Vassals.TryGetValue(faction, out var s2) && s2 == other) { w.Vassals.Remove(faction); removed++; }
            if (removed == 0) return;
            w.ChangeTrust(faction, r.BreakTrust);
            w.ChangeFriendship(faction, other, r.BreakFriendship);
            w.Log.Add($"[{w.Date}] ✖ {w.NameOf(faction)} 세력이 {w.NameOf(other)} 세력과의 약속을 깼다 (신용 {w.TrustOf(faction)})");
        }

        // ------------------------------------------------------------------ 월간

        public static void Monthly(Game g)
        {
            var w = g.World;
            var r = R(w);
            // 우호도는 서서히 50으로 (종속 관계는 공물이 오가며 오히려 오른다)
            foreach (var key in w.Friendship.Keys.ToList())
            {
                int v = w.Friendship[key];
                w.Friendship[key] = v > 50 ? v - r.MonthlyDrift : v < 50 ? v + r.MonthlyDrift : v;
            }
            foreach (var kv in w.Vassals)
                w.ChangeFriendship(kv.Key, kv.Value, r.VassalMonthlyFriendship + r.MonthlyDrift);
            w.Alliances.RemoveAll(a => !a.Active(w.Date));
            // 종속국 공물
            foreach (var kv in w.Vassals.ToList())
            {
                if (!w.Factions[kv.Key].Alive || !w.Factions[kv.Value].Alive) { w.Vassals.Remove(kv.Key); continue; }
                var from = w.Cities[w.Factions[kv.Key].Capital];
                var to = w.Cities[w.Factions[kv.Value].Capital];
                int gold = (int)(from.Gold * r.VassalTribute), food = (int)(from.Food * r.VassalTribute);
                from.Gold -= gold; to.Gold += gold;
                from.Food -= food; to.Food = Math.Min(Domestic.FoodCap(w, to), to.Food + food);
            }
            if (r.Coalition.Enabled) UpdateCoalition(g);
        }

        /// <summary>
        /// 반(反)최강세력 연합 (06 AI 설계서 7장): 어떤 세력이 도시의 30% 이상을 차지하거나
        /// 국력이 2위의 2배 이상이면, 그 세력과 맞닿은 세력들이 연합한다. 조건이 풀리면 해체.
        /// </summary>
        public static void UpdateCoalition(Game g)
        {
            var w = g.World;
            var r = R(w).Coalition;
            var alive = w.Factions.Values.Where(f => f.Alive).Select(f => f.Id).ToList();
            if (alive.Count < 3) return;
            int totalCities = w.Cities.Values.Count(c => c.Def.IsCity);
            var power = alive.ToDictionary(f => f, f => Power(g, f));
            var ranked = power.OrderByDescending(kv => kv.Value).ToList();
            string top = ranked[0].Key;
            bool dominant = w.Cities.Values.Count(c => c.Owner == top && c.Def.IsCity) >= totalCities * r.CityShare
                            || ranked[0].Value >= ranked[1].Value * r.PowerRatioVsSecond;

            var existing = w.Alliances.FirstOrDefault(a => a.Kind == PactKind.Coalition && a.Active(w.Date));
            if (existing != null && (!dominant || existing.Target != top || !w.Factions[existing.Target!].Alive))
            {
                w.Alliances.Remove(existing);
                w.Log.Add($"[{w.Date}] {existing.Name} 해체");
                existing = null;
            }
            if (!dominant || existing != null) return;
            var members = alive.Where(f => f != top && !w.AtPeace(f, top) && Borders(w, f, top)).ToList();
            if (members.Count < 2) return;
            int total = w.Date.Year * 12 + (w.Date.Month - 1) + r.Months;
            var pact = new Alliance
            {
                Kind = PactKind.Coalition, Target = top, Name = $"반{w.NameOf(top)} 연합",
                UntilYear = total / 12, UntilMonth = total % 12 + 1,
            };
            foreach (var m in members) pact.Members.Add(m);
            w.Alliances.Add(pact);
            foreach (var m in members) w.ChangeFriendship(m, top, -20);
            w.Log.Add($"[{w.Date}] ★ {pact.Name} 결성: {string.Join(", ", members.Select(w.NameOf))}");
        }
    }
}
