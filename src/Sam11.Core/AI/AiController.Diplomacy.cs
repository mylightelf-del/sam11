using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.World;

namespace Sam11.Core.AI
{
    /// <summary>외교 AI: 제안에 답하고, 매월 상황에 맞는 외교를 한 건 시도한다.</summary>
    public sealed partial class AiController : IDiplomacyResponder
    {
        public bool Wants(Game g, Proposal p)
        {
            var w = g.World;
            string me = p.To;
            int mine = Diplomacy.Power(g, me), theirs = Diplomacy.Power(g, p.From);
            int friend = w.FriendshipOf(p.From, me);
            switch (p.Type)
            {
                case DiploType.Alliance:
                    // 우호도 조건은 규칙이 이미 보장한다. 신용이 바닥이거나, 나를 삼킬 만큼 강한 상대와는 맺지 않는다
                    return w.TrustOf(p.From) >= 30 && theirs <= mine * 2.5;
                case DiploType.Truce:
                    // 빠른 판: 상대가 충분히 강하고, 나도 다른 곳에서 싸우는 중일 때만
                    return theirs >= mine * 0.8 && Campaigns.Any(c => w.Cities[c.Target].Owner != p.From);
                case DiploType.Gift:
                case DiploType.CaptiveExchange:
                    return true;
                case DiploType.Ransom:
                    return true;
                case DiploType.Aid:
                {
                    var cap = w.Cities[w.Factions[me].Capital];
                    return friend >= 60 && w.AreAllied(me, p.From) && cap.Food > Domestic.MonthlyFoodUse(w, cap) * 8;
                }
                case DiploType.Vassal:
                    return theirs < mine * 0.5;
                case DiploType.Absorb:
                    return friend >= w.Data.Rules.Diplomacy.AbsorbFriendship;
                default:
                    return false;
            }
        }

        private OfficerState? Envoy(Game g, string faction) =>
            g.World.Officers.Values.Where(o => o.Faction == faction && o.Status == OfficerStatus.Active && !o.Acted && o.Location != "")
             .OrderByDescending(o => o.Def.Stats.Pol + o.Def.Stats.Cha).ThenBy(o => o.Id).FirstOrDefault();

        private bool TryPropose(Game g, Proposal p)
        {
            int month = g.World.Date.Year * 12 + g.World.Date.Month;
            if (_refused.TryGetValue((p.To, p.Type), out var when) && month - when < 3) return false;
            var envoy = Envoy(g, p.From);
            if (envoy == null) return false;
            p.Envoy = envoy.Id;
            try
            {
                Stats.DiplomacyAttempts++;
                if (Diplomacy.Propose(g, p)) Stats.DiplomacySuccesses++;
                else _refused[(p.To, p.Type)] = month;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>매월 외교 한 건 (06 AI 설계서 6장 성향: 외교 선호·신의 반영).</summary>
        private void PlanDiplomacy(Game g, string me)
        {
            var w = g.World;
            var p = Personality(w, me);
            int mine = Diplomacy.Power(g, me);
            var others = w.Factions.Values.Where(f => f.Alive && f.Id != me).Select(f => f.Id).ToList();
            var neighbors = others.Where(f => Diplomacy.Borders(w, me, f)).ToList();
            var power = others.ToDictionary(f => f, f => Diplomacy.Power(g, f));
            var capital = w.Cities[w.Factions[me].Capital];
            int myCities = w.Cities.Values.Count(c => c.Owner == me && c.Def.IsCity);
            var threat = neighbors.Where(f => !w.AtPeace(me, f)).OrderByDescending(f => power[f]).FirstOrDefault();

            // 배신: 호전적이고 신의가 낮은 군주는 약한 동맹을 친다
            if (p.Aggression >= 8 && p.Faith <= 3)
            {
                var prey = neighbors.Where(f => w.AreAllied(me, f) && power[f] < mine * 0.4).OrderBy(f => power[f]).FirstOrDefault();
                if (prey != null && w.Rng.Chance(0.3)) { Diplomacy.Break(g, me, prey); return; }
            }

            // 포로 교환·몸값
            foreach (var other in others)
            {
                bool theyHoldOurs = w.Officers.Values.Any(o => o.Status == OfficerStatus.Captive && o.Faction == other && o.PreviousFaction == me);
                bool weHoldTheirs = w.Officers.Values.Any(o => o.Status == OfficerStatus.Captive && o.Faction == me && o.PreviousFaction == other);
                if (theyHoldOurs && weHoldTheirs && TryPropose(g, new Proposal { Type = DiploType.CaptiveExchange, From = me, To = other })) return;
            }
            var hostage = w.Officers.Values.Where(o => o.Status == OfficerStatus.Captive && o.PreviousFaction == me && o.Faction != null && w.Factions[o.Faction].Alive)
                           .OrderByDescending(o => Value(o)).FirstOrDefault();
            if (hostage != null && Value(hostage) >= 380)
            {
                int ransom = Value(hostage) * w.Data.Rules.Diplomacy.RansomGoldPerStat;
                if (capital.Gold > ransom * 2 && TryPropose(g, new Proposal { Type = DiploType.Ransom, From = me, To = hostage.Faction!, Gold = ransom, Captive = hostage.Id })) return;
            }

            // 병합: 오래 따른 약한 종속국
            // 망한 종속국은 월초 정리 전까지 목록에 남아 있다
            foreach (var v in w.Vassals.Where(kv => kv.Value == me && power.ContainsKey(kv.Key)).Select(kv => kv.Key).ToList())
                if (w.FriendshipOf(me, v) >= w.Data.Rules.Diplomacy.AbsorbFriendship && mine >= power[v] * w.Data.Rules.Diplomacy.AbsorbPowerRatio
                    && TryPropose(g, new Proposal { Type = DiploType.Absorb, From = me, To = v })) return;

            // 종속: 너무 약하면 강한 이웃 밑으로
            if (threat != null && myCities <= 2 && mine < power[threat] * 0.35 && p.Aggression <= 6 && !w.Vassals.ContainsKey(me))
                if (TryPropose(g, new Proposal { Type = DiploType.Vassal, From = me, To = threat })) return;

            // 원조: 군량이 바닥이면 동맹에게
            if (capital.Food < Domestic.MonthlyFoodUse(w, capital) * 3)
            {
                var donor = others.Where(f => w.AreAllied(me, f)).OrderByDescending(f => w.Cities[w.Factions[f].Capital].Food).FirstOrDefault();
                if (donor != null && TryPropose(g, new Proposal { Type = DiploType.Aid, From = me, To = donor })) return;
            }

            // 동맹: 강한 이웃에게 위협받으면 그 이웃과 맞닿은 다른 세력과 손잡는다.
            // 동맹은 우호도 70 이상이 필요하므로, 모자라면 선물로 먼저 우호도를 쌓는다.
            if (threat != null && power[threat] > mine * 1.2 && w.Rng.Chance(0.3 + p.Diplomacy * 0.05))
            {
                int need = w.Data.Rules.Diplomacy.AllianceMinFriendship;
                var ally = others.Where(f => f != threat && !w.AtPeace(me, f) && Diplomacy.Borders(w, f, threat) && power[f] >= mine * 0.3
                                             && power[f] <= mine * 2.5)
                                 .OrderByDescending(f => w.FriendshipOf(me, f)).ThenByDescending(f => power[f]).FirstOrDefault();
                if (ally != null)
                {
                    if (w.FriendshipOf(me, ally) >= need)
                    {
                        if (TryPropose(g, new Proposal { Type = DiploType.Alliance, From = me, To = ally })) return;
                    }
                    else
                    {
                        int gold = Math.Min(2000, (need - w.FriendshipOf(me, ally)) * w.Data.Rules.Diplomacy.GiftGoldPerFriendship);
                        if (capital.Gold > gold * 3 && TryPropose(g, new Proposal { Type = DiploType.Gift, From = me, To = ally, Gold = gold })) return;
                    }
                }
            }

            // 정전: 원정 중이면 다른 쪽의 강한 이웃과 싸우지 않는다 (양면 전쟁 피하기)
            if (Campaigns.Count > 0 && w.Rng.Chance(0.2 + p.Diplomacy * 0.03))
            {
                var targets = Campaigns.Select(c => w.Cities[c.Target].Owner).Where(o => o != null).ToHashSet();
                var other = neighbors.Where(f => !w.AtPeace(me, f) && !targets.Contains(f) && power[f] >= mine * 0.8)
                                     .OrderByDescending(f => power[f]).FirstOrDefault();
                if (other != null) TryPropose(g, new Proposal { Type = DiploType.Truce, From = me, To = other });
            }
        }

        private static int Value(OfficerState o) =>
            o.Def.Stats.Ldr + o.Def.Stats.War + o.Def.Stats.Int + o.Def.Stats.Pol + o.Def.Stats.Cha;
    }
}
