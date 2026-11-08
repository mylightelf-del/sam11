using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Battle;
using Sam11.Core.Common;
using Sam11.Core.Data;
using Sam11.Core.World;

namespace Sam11.Core.AI
{
    /// <summary>작전 계층(원정군 편성·출진·진군)과 전술 계층(부대별 행동).</summary>
    public sealed partial class AiController
    {
        private void RunField(Game g, string faction)
        {
            var w = g.World;

            // 수비대: 붙은 적만 친다
            foreach (var gar in g.Garrisons.Values.Where(u => u.Faction == faction && u.Status == UnitStatus.Ready && !u.HasActed).ToList())
                Safe(() => g.GarrisonStrike(gar));

            var units = g.Field.Alive.Where(u => u.Faction == faction && !u.IsGarrison).ToList();
            // 연쇄를 노릴 때는 일반 공격 부대가 먼저 붙고, 전법을 쓸 부대가 나중에 친다
            units = Config.ChainOrdering
                ? units.OrderBy(u => g.Field.AvailableTactics(u).Any(t => t.Power > 0 && t.EnergyCost <= u.Energy)).ThenBy(u => u.Id).ToList()
                : units.OrderBy(u => u.Id).ToList();
            foreach (var u in units)
            {
                if (u.Status != UnitStatus.Ready || u.HasActed) continue;
                Stats.UnitTurns++;
                _unitCampaign.TryGetValue(u, out var camp);
                Safe(() => ActUnit(g, faction, u, camp));
                g.Resolve();
            }

            foreach (var camp in Campaigns.Where(c => c.State == CampaignState.Marching))
            {
                foreach (var gone in camp.Units.Where(u => !u.IsAlive && u.Status != UnitStatus.Withdrawn))
                    camp.Exits.Add($"{gone.Label}: {gone.Status}");
                camp.Units.RemoveAll(u => !u.IsAlive);
                if (camp.Units.Count == 0)
                {
                    camp.State = CampaignState.Done;
                    camp.Won = w.Cities[camp.Target].Owner == faction;
                    camp.LogEnd = g.Field.Log.Lines.Count;
                    History.Add(camp);
                    if (camp.Won) Stats.CampaignsWon++;
                    else Stats.CampaignsFailed++;
                }
            }
        }

        private static void Safe(Action a)
        {
            try { a(); }
            catch (InvalidOperationException) { }
        }

        // ------------------------------------------------------------------ 작전: 출진

        private void W(string reason) => Stats.LaunchBlocked[reason] = Stats.LaunchBlocked.TryGetValue(reason, out var v) ? v + 1 : 1;
        private void TryLaunch(Game g, string faction, Campaign camp)
        {
            var w = g.World;
            var src = w.Cities[camp.Source];
            var target = w.Cities[camp.Target];
            int sendable = Surplus(g, src, faction);
            if (sendable < camp.Required) { W("병력 부족"); return; }

            var officers = w.OfficersAt(src.Id).Where(o => o.Faction == faction && !o.Acted && o.InjuredTurns == 0)
                            .OrderByDescending(o => o.Def.Stats.Ldr + o.Def.Stats.War).ToList();
            if (officers.Count >= 2) officers.RemoveAt(officers.Count - 1); // 한 명은 남겨 성을 지킨다
            if (officers.Count == 0) { W("무장 없음"); return; }
            // 무장들이 이끌 수 있는 병력이 필요 병력에 못 미치면 나가지 않는다 (축차 투입 방지)
            int capacity = officers.Take(8).Sum(o => UnitStats.MaxTroops(o.Def, o.Rank != null ? w.Data.Ranks[o.Rank] : null, w.Data.Rules.Unit));
            if (capacity < camp.Required) { W("지휘 한도"); return; }

            // 작은 목표에도 최소 3,000명은 보낸다 (그 사이 수비가 늘었을 수 있다)
            int toSend = Math.Min(sendable, Math.Max((int)(camp.Required * 1.2), 3000));
            int dist = src.Placement.Center.DistanceTo(target.Placement.Center);
            // 왕복 진군 + 공성 15턴 (성문을 깨고 수비대를 깎는 데 걸리는 시간)
            int turns = dist / 4 * 2 + 15;
            // 왕복할 군량이 없으면 출진하지 않는다 (내정 AI가 군량을 사 모은다)
            double perSoldierTurn = w.Data.Rules.Economy.FoodPerSoldierMonth / w.Data.Rules.Time.TurnsPerMonth;
            int spareFood = src.Food - Domestic.MonthlyFoodUse(w, src) * 2;
            if (spareFood < toSend * perSoldierTurn * turns) { W("군량 부족"); return; }
            bool walls = target.Structures.Values.Any(s => s.Kind == TileKind.Gate && !s.Broken) && target.Def.Type != "port" && target.Def.Type != "ferry";
            var launched = new List<Unit>();
            var used = new HashSet<string>();
            bool haveSiege = false;
            for (int i = 0; i < 8 && toSend >= 300; i++)
            {
                var leader = officers.FirstOrDefault(o => !used.Contains(o.Id));
                if (leader == null) break;
                used.Add(leader.Id);
                var members = new List<string> { leader.Id };
                // 부장: 주장보다 무력이 높은 무장 하나 (약점 보완)
                var sub = officers.Where(o => !used.Contains(o.Id) && o.Def.Stats.War > leader.Def.Stats.War + 10).FirstOrDefault();
                if (sub != null && officers.Count - used.Count > 2) { members.Add(sub.Id); used.Add(sub.Id); }

                var memberDefs = members.Select(id => w.Officers[id].Def).ToList();
                int max = UnitStats.MaxTroops(memberDefs[0], leader.Rank != null ? w.Data.Ranks[leader.Rank] : null, w.Data.Rules.Unit);
                int troops = Math.Min(max, toSend);
                string? formation = ChooseFormation(g, memberDefs, troops, src, walls && !haveSiege);
                if (formation == null) { W("펼칠 진형 없음"); continue; }
                if (formation == "siege") haveSiege = true;
                int perTurn = (int)Math.Ceiling(troops * w.Data.Rules.Economy.FoodPerSoldierMonth / w.Data.Rules.Time.TurnsPerMonth);
                int food = Math.Min(perTurn * turns * 12 / 10, Math.Max(0, src.Food - Domestic.MonthlyFoodUse(w, src)));
                try
                {
                    var u = SiegeSetup.Sortie(w, g.Field, src.Id, members, formation, troops, food);
                    launched.Add(u);
                    _unitCampaign[u] = camp;
                    toSend -= troops;
                }
                catch (InvalidOperationException ex) { W("출진 오류: " + ex.Message.Split(':')[0]); break; }
            }
            if (launched.Count == 0) { W("부대 0개"); return; }
            camp.Units.AddRange(launched);
            camp.LogStart = g.Field.Log.Lines.Count;
            camp.Labels.AddRange(launched.Select(u => u.Label));
            camp.State = CampaignState.Marching;
            camp.LaunchRatio = launched.Sum(u => u.Troops) / Math.Max(1.0, camp.EstimatedDefense);
            Stats.CampaignsLaunched++;
            if (camp.LaunchRatio < 1.5 * 0.95) Stats.LaunchedUnderRatio++;
            w.Log.Add($"[{w.Date}] (AI) {w.NameOf(faction)} 세력, {w.Cities[camp.Target]} 공략군 출진: {launched.Count}개 부대 {launched.Sum(u => u.Troops)}명 (추정 수비 {camp.EstimatedDefense:F0})");
        }

        /// <summary>
        /// 증원: 원정군이 목표에 붙어 싸우는 중인데 병력이 남은 수비의 1.2배에 못 미치면,
        /// 출발 거점의 여유 병력으로 부대를 더 보낸다. 주력이 이미 붙어 있으므로 축차 투입으로 세지 않는다.
        /// </summary>
        private void TryReinforce(Game g, string faction, Campaign camp)
        {
            var w = g.World;
            if ((g.TurnCount - camp.StartTurn) % 3 != 0) return;
            var src = w.Cities[camp.Source];
            var target = w.Cities[camp.Target];
            if (src.Owner != faction || target.Owner == faction) return;
            var alive = camp.Units.Where(u => u.IsAlive).ToList();
            if (alive.Count == 0) return;
            bool engaged = alive.Any(u => u.Position.DistanceTo(target.Placement.Center) <= 6);
            if (!engaged) return;
            double defense = Knowledge!.EstimateTroops(g, target);
            if (alive.Sum(u => u.Troops) >= defense * 1.2) return;
            int sendable = Surplus(g, src, faction);
            if (sendable < 3000) return;
            var officers = w.OfficersAt(src.Id).Where(o => o.Faction == faction && !o.Acted && o.InjuredTurns == 0)
                            .OrderByDescending(o => o.Def.Stats.Ldr + o.Def.Stats.War).ToList();
            if (officers.Count >= 2) officers.RemoveAt(officers.Count - 1);
            int dist = src.Placement.Center.DistanceTo(target.Placement.Center);
            double perTurn = w.Data.Rules.Economy.FoodPerSoldierMonth / w.Data.Rules.Time.TurnsPerMonth;
            foreach (var leader in officers.Take(2))
            {
                if (sendable < 3000) break;
                var defs = new List<OfficerDef> { leader.Def };
                int troops = Math.Min(sendable, UnitStats.MaxTroops(leader.Def, leader.Rank != null ? w.Data.Ranks[leader.Rank] : null, w.Data.Rules.Unit));
                string? formation = ChooseFormation(g, defs, troops, src, false);
                if (formation == null) continue;
                int food = Math.Min((int)(troops * perTurn * (dist / 4 * 2 + 12)), Math.Max(0, src.Food - Domestic.MonthlyFoodUse(w, src) * 2));
                if (food <= 0) break;
                try
                {
                    var u = SiegeSetup.Sortie(w, g.Field, src.Id, new[] { leader.Id }, formation, troops, food);
                    camp.Units.Add(u);
                    camp.Labels.Add(u.Label);
                    _unitCampaign[u] = camp;
                    sendable -= troops;
                    Stats.Reinforcements++;
                }
                catch (InvalidOperationException) { break; }
            }
        }

        /// <summary>
        /// 진형 선택: 적성 S/A/B 가운데 공격·방어가 좋은 진형. 성벽이 있는 목표면 공성진 하나를 포함한다.
        /// 장비 진형은 거점에 병장기가 충분해야 한다. 입문 난이도는 가끔 아무 진형이나 고른다.
        /// </summary>
        private string? ChooseFormation(Game g, IReadOnlyList<OfficerDef> members, int troops, CityState src, bool wantSiege)
        {
            var data = g.World.Data;
            var options = data.Formations.Values
                .Where(f => !f.WaterOnly && Battle.Battle.CanDeploy(data, members, f.Id, troops, src.Equipment) == null)
                .ToList();
            if (options.Count == 0) return null;
            if (Config.FormationBlunder > 0 && g.World.Rng.Chance(Config.FormationBlunder))
                return options[g.World.Rng.NextInt(options.Count)].Id;
            double Score(FormationDef f)
            {
                int apt = (int)UnitStats.BestAptitude(members, f.Id);
                double s = apt * 12 + f.Attack / 10.0 + f.Defense / 15.0 + f.Move;
                if (f.Id == "siege") s = wantSiege ? 200 : -100;
                return s;
            }
            return options.OrderByDescending(Score).ThenBy(f => f.Id).First().Id;
        }

        // ------------------------------------------------------------------ 전술: 부대 행동

        /// <summary>
        /// goal까지의 경로 지도. 이 부대 위치와 이번 턴 이동 범위를 덮는 만큼만 계산하고,
        /// 이미 계산해 둔 지도가 그 범위를 덮으면 그대로 쓴다.
        /// </summary>
        private Dictionary<HexCoord, int> PathTo(Game g, Unit u, HexCoord goal)
        {
            var key = (goal, u.Formation.Id, u.Faction);
            int margin = g.Field.MovePoints(u) * 2 + 12;
            if (_pathCache.TryGetValue(key, out var cached)
                && cached.Field.TryGetValue(u.Position, out var here) && here + margin <= cached.Covered)
                return cached.Field;
            var field = g.Field.PathField(u, goal, u.Position, margin, 1600, out int covered);
            _pathCache[key] = (field, covered);
            return field;
        }

        private void ActUnit(Game g, string faction, Unit u, Campaign? camp)
        {
            var w = g.World;
            var b = g.Field;

            // 목표가 없거나(원정 종료) 퇴각해야 하면 가장 가까운 아군 거점으로
            bool retreat = Config.Retreats && ((u.Morale < 30 && u.Troops < u.MaxTroops * 0.35) || (u.Food == 0 && !b.IsSupplied(u)));
            var target = camp != null ? w.Cities[camp.Target] : null;
            if (target != null && target.Owner == faction)
            {
                GoHome(g, u, target.Id);
                return;
            }
            if (camp == null || retreat || camp.State == CampaignState.Done)
            {
                if (camp != null)
                    camp.Exits.Add($"{u.Label}: {(retreat ? "퇴각" : "원정 종료")} 사기 {u.Morale} 병력 {u.Troops}/{u.MaxTroops} 군량 {u.Food} 보급 {b.IsSupplied(u)} 상태 {camp.State}");
                if (retreat && camp != null)
                {
                    Stats.Retreats++;
                    if (u.Food == 0) Stats.RetreatsFood++; else Stats.RetreatsMorale++;
                    _unitCampaign.Remove(u);
                }
                string? home = u.Home != null && w.Cities[u.Home].Owner == faction ? u.Home : g.NearestCityOf(faction, u.Position);
                if (home != null) GoHome(g, u, home);
                return;
            }

            var gates = target!.Placement.Tiles.Keys.Where(h => b.Structures.TryGetValue(h, out var s) && s.Blocking && s.Kind == StructureKind.Gate).ToList();
            HexCoord goal = gates.Count > 0 ? gates.OrderBy(h => h.DistanceTo(u.Position)).First() : target.Placement.Center;
            var field = PathTo(g, u, goal);

            var best = ChooseAction(g, u, goal, gates.Count > 0 ? goal : (HexCoord?)null, field);
            var before = u.Position;
            if (best.Pos != u.Position) b.Move(u, best.Pos);
            if (!u.IsAlive || u.HasActed) return;
            switch (best.Kind)
            {
                case ActionKind.Attack: b.Attack(u, best.Target!); break;
                case ActionKind.Tactic: b.UseTactic(u, best.Tactic!.Id, best.Target!); break;
                case ActionKind.Structure:
                    if (best.Tactic != null) b.UseTacticOnStructure(u, best.Tactic.Id, best.Structure!.Value);
                    else b.AttackStructure(u, best.Structure!.Value);
                    break;
                default:
                    if (before == u.Position) Stats.StalledUnitTurns++;
                    break;
            }
        }

        private void GoHome(Game g, Unit u, string cityId)
        {
            var c = g.World.Cities[cityId];
            bool near() => c.Placement.Tiles.ContainsKey(u.Position) || c.Placement.Tiles.Keys.Any(t => t.DistanceTo(u.Position) == 1);
            if (!near())
            {
                var field = PathTo(g, u, c.Placement.Center);
                var reach = g.Field.Reachable(u);
                var dest = reach.Keys.Where(field.ContainsKey).OrderBy(h => field[h]).ThenBy(h => reach[h]).DefaultIfEmpty(u.Position).First();
                if (dest != u.Position) g.Field.Move(u, dest);
            }
            if (u.IsAlive && near())
            {
                SiegeSetup.Withdraw(g.World, g.Field, u, cityId);
                _unitCampaign.Remove(u);
            }
        }

        private enum ActionKind { Move, Attack, Tactic, Structure }

        private sealed class Choice
        {
            public double Score = double.MinValue;
            public HexCoord Pos;
            public ActionKind Kind;
            public Unit? Target;
            public TacticDef? Tactic;
            public HexCoord? Structure;
        }

        /// <summary>
        /// 이동 가능한 모든 칸에서 공격·전법·성문 공격·이동을 평가해 가장 좋은 행동을 고른다.
        /// 점수 = 기대 피해 − 기대 반격 + 궤멸 보너스 + 연쇄 기대치, 이동은 목표까지 남은 경로로 평가.
        /// </summary>
        private Choice ChooseAction(Game g, Unit u, HexCoord goal, HexCoord? gate, Dictionary<HexCoord, int> field)
        {
            var b = g.Field;
            var reach = b.Reachable(u);
            var origin = u.Position;
            int reachRadius = b.MovePoints(u) + 4;
            var enemies = Knowledge!.VisibleEnemies(g).Where(e => e.Position.DistanceTo(origin) <= reachRadius).ToList();
            var tactics = b.AvailableTactics(u).Where(t => t.Power > 0 && t.EnergyCost <= u.Energy && !b.TacticTargetsStructureOnly(t)).ToList();
            var siegeTactics = u.Formation.Id == "siege"
                ? b.AvailableTactics(u).Where(t => t.EnergyCost <= u.Energy && t.Formation == "siege" && !t.Effects.Any(e => e.Type == "target_on_wall")).ToList()
                : new List<TacticDef>();
            var best = new Choice { Pos = origin, Kind = ActionKind.Move };
            try
            {
                Evaluate();
            }
            finally
            {
                u.Position = origin; // 평가하느라 옮겨 둔 위치를 반드시 되돌린다
            }
            return best;

            void Evaluate()
            {
            foreach (var pos in reach.Keys)
            {
                double progress = field.TryGetValue(pos, out var fd) ? -fd : -2000;
                double terrain = Combat.TerrainMod(u.Formation, b.Map.TerrainAt(pos)) * 8;
                int adjacentFoes = pos.Neighbors().Count(n => b.UnitAt(n) is Unit x && b.Hostile(x.Faction, u.Faction));
                double danger = adjacentFoes * (u.Morale < 40 ? 120 : 40);
                Consider(best, progress + terrain - danger, pos, ActionKind.Move);

                u.Position = pos;
                foreach (var e in enemies)
                {
                    int d = pos.DistanceTo(e.Position);
                    if (d <= u.Formation.Range)
                    {
                        double dmg = Expected(g, u, e, 1.0);
                        double counter = !u.Formation.IsRanged && e.Status == UnitStatus.Ready
                            ? Expected(g, e, u, g.World.Data.Rules.Combat.CounterRatio) : 0;
                        Consider(best, dmg - counter * 0.8 + Kill(dmg, e) + terrain - danger * 0.3, pos, ActionKind.Attack, e);
                    }
                    foreach (var t in tactics)
                    {
                        if (d > b.TacticRange(u, t)) continue;
                        if (t.Effects.Any(x => x.Type == "target_on_wall")
                            && !(b.Structures.TryGetValue(e.Position, out var ws) && ws.Blocking)) continue;
                        double p = g.Field.Options.TacticSuccessOverride ?? Combat.TacticSuccessChance(b, u, e);
                        double dmg = Expected(g, u, e, t.Power);
                        double chain = 0;
                        if (Config.ChainOrdering)
                        {
                            int partners = e.Position.Neighbors().Count(n => b.UnitAt(n) is Unit a && a != u && a.Faction == u.Faction && a.Status == UnitStatus.Ready);
                            chain = partners * dmg * 0.6;
                        }
                        Consider(best, p * (dmg + chain + Kill(dmg, e)) + terrain - danger * 0.3, pos, ActionKind.Tactic, e, t);
                    }
                }
                if (gate != null)
                {
                    int d = pos.DistanceTo(gate.Value);
                    double weight = u.Formation.Id == "siege" ? 2.0 : (enemies.Count == 0 ? 0.8 : 0.3);
                    if (d <= u.Formation.Range)
                        Consider(best, b.StructureDamage(u, 1.0, false) * weight + terrain, pos, ActionKind.Structure, null, null, gate);
                    foreach (var t in siegeTactics)
                        if (d <= b.TacticRange(u, t))
                            Consider(best, b.StructureDamage(u, t.Power, false) * weight + terrain, pos, ActionKind.Structure, null, t, gate);
                }
            }
            }
        }

        private static void Consider(Choice best, double score, HexCoord pos, ActionKind kind, Unit? target = null,
                                     TacticDef? tactic = null, HexCoord? structure = null)
        {
            if (score <= best.Score) return;
            best.Score = score;
            best.Pos = pos;
            best.Kind = kind;
            best.Target = target;
            best.Tactic = tactic;
            best.Structure = structure;
        }

        private static double Expected(Game g, Unit attacker, Unit target, double power) =>
            Math.Min(target.Troops, Combat.Compute(g.Field, attacker, target, power, 0, 0, false).Total);

        private static double Kill(double dmg, Unit target) => dmg >= target.Troops ? target.Troops * 0.5 + 300 : 0;
    }
}
