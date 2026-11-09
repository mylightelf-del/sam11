using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sam11.Core.Battle;
using Sam11.Core.Common;
using Sam11.Core.Data;
using Sam11.Core.World;

namespace Sam11.Core.AI
{
    /// <summary>이벤트 선택, 보물 하사, 포로 처리, 방어(증원·요격), 공동 작전 판단.</summary>
    public sealed partial class AiController
    {
        // ------------------------------------------------------------------ 이벤트 선택 (성향 반영)

        /// <summary>
        /// 이벤트 선택: 데이터의 ai_weight에 군주 성향을 곱한다. 신용을 깎는 선택지는 신의가 낮을수록,
        /// 우호도를 올리는 선택지는 외교를 좋아할수록, 병력·자원을 옮기는(물러나는) 선택지는 덜 호전적일수록 더 고른다.
        /// </summary>
        public EventChoice Choose(Game g, EventDef ev, string faction)
        {
            var p = Personality(g.World, faction);
            var weighted = ev.Choices.Select(c => new EventChoice
            {
                Id = c.Id, Text = c.Text, Effects = c.Effects,
                AiWeight = c.AiWeight * PersonalityFactor(c, p),
            }).ToList();
            var pick = EventSystem.WeightedPick(g.World.Rng, weighted);
            return ev.Choices.First(c => c.Id == pick.Id);
        }

        private static double PersonalityFactor(EventChoice c, RulerPersonality p)
        {
            // 같은 종류의 효과가 여럿이어도 성향 보정은 한 번만 (효과 수가 많은 선택지가 지나치게 불리해지지 않게)
            var factors = new Dictionary<string, double>();
            foreach (var fx in c.Effects)
            {
                string type = fx.Value<string>("type") ?? "";
                int delta = fx.Value<int?>("delta") ?? 0;
                if (type == "trust")
                    factors["trust"] = delta < 0 ? 1 + (5 - p.Faith) * 0.15 : 1 + (p.Faith - 5) * 0.15;
                else if (type == "friendship")
                    factors["friendship"] = delta > 0 ? 1 + (p.Diplomacy - 5) * 0.1 : 1 + (5 - p.Diplomacy) * 0.1;
                else if (type == "transfer_resources" || type == "move_capital")
                    factors["retreat"] = 1 + (5 - p.Aggression) * 0.1;
            }
            double f = factors.Values.Aggregate(1.0, (a, b) => a * b);
            return Math.Max(0.05, f);
        }

        // ------------------------------------------------------------------ 보물 하사

        private static readonly Dictionary<string, Func<OfficerDef, int>> SlotFit = new Dictionary<string, Func<OfficerDef, int>>
        {
            ["weapon"] = o => o.Stats.War,
            ["horse"] = o => (o.Stats.Ldr + o.Stats.War) / 2,
            ["book"] = o => (o.Stats.Ldr + o.Stats.Int) / 2,
            ["artifact"] = o => (o.Stats.Pol + o.Stats.Cha) / 2,
        };

        /// <summary>
        /// 보관고의 보물을 내린다: 충성이 낮은 유능한 무장 먼저 (이간·매수에 대비), 그다음 종류마다 적임자.
        /// </summary>
        private void PlanTreasures(Game g, string me)
        {
            var w = g.World;
            foreach (var tid in w.TreasureVault.Where(kv => kv.Value == me).Select(kv => kv.Key).OrderBy(x => x).ToList())
            {
                var t = w.Data.Treasures[tid];
                var fit = SlotFit.TryGetValue(t.Slot, out var f) ? f : o => o.Stats.Cha;
                var candidates = w.Officers.Values.Where(o => o.Faction == me && o.Status == OfficerStatus.Active
                                                              && !Treasures.Held(w, o.Id).Any(h => h.Slot == t.Slot)).ToList();
                if (candidates.Count == 0) continue;
                var shaky = candidates.Where(o => o.Loyalty < 70 && Value(o) >= 330).OrderBy(o => o.Loyalty).FirstOrDefault();
                var pick = shaky ?? candidates.OrderByDescending(o => fit(o.Def)).ThenBy(o => o.Id).First();
                Treasures.Bestow(w, tid, pick.Id);
                Stats.Bestowals++;
            }
        }

        // ------------------------------------------------------------------ 포로 처리

        /// <summary>
        /// 사로잡은 지 석 달이 지난 포로: 신의 높은 군주는 풀어 주고(우호도 +), 호전적이고 신의가 낮은 군주는
        /// 전사 옵션이 켜져 있으면 처단한다(신용·우호도 −). 그 밖에는 등용을 계속 시도하다 여섯 달 뒤 풀려난다.
        /// </summary>
        private void PlanCaptives(Game g, string me)
        {
            var w = g.World;
            var r = w.Data.Rules.Captives;
            var p = Personality(w, me);
            foreach (var c in w.Officers.Values.Where(o => o.Status == OfficerStatus.Captive && o.Faction == me
                                                           && o.CaptiveMonths >= r.DecideAfterMonths).ToList())
            {
                string? home = c.PreviousFaction;
                if (p.Faith >= 7)
                {
                    c.Status = OfficerStatus.Free;
                    c.Faction = null;
                    if (home != null && w.Factions.ContainsKey(home)) w.ChangeFriendship(me, home, r.ReleaseFriendship);
                    w.Log.Add($"[{w.Date}] {w.NameOf(me)} 세력이 포로 {Korean.EulReul(c.ToString())} 풀어 주었다");
                    Stats.Releases++;
                }
                else if (p.Aggression >= 8 && p.Faith <= 3 && g.Field.Options.DeathEnabled && Value(c) >= 300)
                {
                    c.Status = OfficerStatus.Dead;
                    c.Location = "";
                    if (home != null && w.Factions.ContainsKey(home)) w.ChangeFriendship(me, home, r.ExecuteFriendship);
                    w.ChangeTrust(me, r.ExecuteTrust);
                    w.Log.Add($"[{w.Date}] {w.NameOf(me)} 세력이 포로 {Korean.EulReul(c.ToString())} 처단했다");
                    Stats.Executions++;
                }
            }
        }

        // ------------------------------------------------------------------ 공동 작전

        /// <summary>동맹 중 누군가가 지금 그 세력의 거점을 치고 있는지.</summary>
        private static bool AllyIsFighting(Game g, string me, string enemy)
        {
            var w = g.World;
            foreach (var kv in g.Controllers)
            {
                if (kv.Key == me || !w.AreAllied(me, kv.Key) || !(kv.Value is AiController ally)) continue;
                if (ally.Campaigns.Any(c => !c.Defense && w.Cities[c.Target].Owner == enemy)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ 방어

        /// <summary>
        /// 위협받는 도시를 지킨다: (1) 이어진 안전한 거점에서 병력을 보내 메우고,
        /// (2) 우리 영역 안으로 들어온 적 야전군을 1.3배 이상으로 요격할 수 있으면 근처 거점에서 내보낸다.
        /// </summary>
        private void PlanDefense(Game g, string me)
        {
            var w = g.World;
            var enemies = Knowledge!.VisibleEnemies(g).Where(u => !u.IsGarrison).ToList();
            if (enemies.Count == 0) return;
            foreach (var c in w.Cities.Values.Where(c => c.Owner == me && InScope(c.Id) && c.Def.IsCity).OrderBy(c => c.Id).ToList())
            {
                var near = enemies.Where(u => u.Position.DistanceTo(c.Placement.Center) <= 8
                                              && w.Map.Territory.TryGetValue(u.Position, out var owner) && w.Cities[owner].Owner == me).ToList();
                if (near.Count == 0) continue;
                int threat = near.Sum(u => u.Troops);

                // (1) 증원
                int gap = threat - (c.Troops + c.FieldGarrison);
                if (gap > 0)
                    foreach (var id in Connected(w, c.Id).Where(id => id != c.Id).OrderBy(id => id))
                    {
                        if (gap <= 0) break;
                        var src = w.Cities[id];
                        int spare = Surplus(g, src, me);
                        var courier = w.OfficersAt(id).FirstOrDefault(o => o.Faction == me && !o.Acted && !w.Missions.Any(m => m.Agent == o.Id));
                        if (spare < 2000 || courier == null) continue;
                        int send = Math.Min(spare, gap);
                        try
                        {
                            Domestic.Transport(w, courier.Id, id, c.Id, 0, 0, send);
                            gap -= send;
                            Stats.Reinforcements2++;
                        }
                        catch (InvalidOperationException) { }
                    }

                // (2) 요격
                if (Campaigns.Any(x => x.Defense && x.Target == c.Id)) continue;
                var bases = new[] { c.Id }.Concat(c.Def.Links.Where(l => w.Cities[l].Owner == me && InScope(l))).ToList();
                var source = bases.Select(id => w.Cities[id]).Where(x => Surplus(g, x, me) >= threat * 1.3)
                                  .OrderByDescending(x => Surplus(g, x, me)).FirstOrDefault();
                if (source == null) continue;
                var officers = w.OfficersAt(source.Id).Where(o => o.Faction == me && !o.Acted && o.InjuredTurns == 0
                                                                  && !w.Missions.Any(m => m.Agent == o.Id))
                                .OrderByDescending(o => o.Def.Stats.Ldr + o.Def.Stats.War).ToList();
                if (officers.Count >= 2) officers.RemoveAt(officers.Count - 1);
                int toSend = (int)(threat * 1.3);
                var camp = new Campaign { Target = c.Id, Source = source.Id, Defense = true, State = CampaignState.Marching, StartTurn = g.TurnCount, Required = toSend };
                foreach (var o in officers.Take(4))
                {
                    if (toSend <= 300) break;
                    var defs = new List<OfficerDef> { o.Def };
                    int troops = Math.Min(toSend, UnitStats.MaxTroops(o.Def, o.Rank != null ? w.Data.Ranks[o.Rank] : null, w.Data.Rules.Unit));
                    string? formation = ChooseFormation(g, defs, troops, source, false);
                    if (formation == null) continue;
                    try
                    {
                        var u = SiegeSetup.Sortie(w, g.Field, source.Id, new[] { o.Id }, formation, troops, 0);
                        camp.Units.Add(u);
                        camp.Labels.Add(u.Label);
                        _unitCampaign[u] = camp;
                        toSend -= troops;
                    }
                    catch (InvalidOperationException) { break; }
                }
                if (camp.Units.Count == 0) continue;
                Campaigns.Add(camp);
                Stats.Intercepts++;
                w.Log.Add($"[{w.Date}] (AI) {w.NameOf(me)} 세력, {c} 방어를 위해 {source}에서 요격군 출진 ({camp.Units.Sum(u => u.Troops)}명, 적 {threat}명)");
            }
        }

        /// <summary>요격 부대의 행동: 지킬 도시 근처(10칸)의 적을 치고, 적이 사라지면 돌아간다.</summary>
        private void ActDefender(Game g, string faction, Unit u, Campaign camp)
        {
            var w = g.World;
            var c = w.Cities[camp.Target];
            var foe = Knowledge!.VisibleEnemies(g).Where(e => !e.IsGarrison && e.Position.DistanceTo(c.Placement.Center) <= 10)
                               .OrderBy(e => e.Position.DistanceTo(u.Position)).FirstOrDefault();
            if (foe == null || c.Owner != faction)
            {
                camp.State = CampaignState.Done;
                string home = c.Owner == faction ? c.Id : (u.Home ?? g.NearestCityOf(faction, u.Position) ?? c.Id);
                GoHome(g, u, home);
                return;
            }
            var field = PathTo(g, u, foe.Position);
            var best = ChooseAction(g, u, foe.Position, new List<Common.HexCoord>(), field);
            if (best.Pos != u.Position) g.Field.Move(u, best.Pos, _reach);
            if (!u.IsAlive || u.HasActed) return;
            ExecuteChoice(g, u, best);
        }
    }
}
