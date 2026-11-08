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

        private Dictionary<HexCoord, int> PathTo(Game g, Unit u, HexCoord goal) =>
            PathTo(g, u, goal.ToString(), new[] { goal });

        /// <summary>
        /// 목표 칸들까지의 경로 지도. 이 부대 위치와 이번 턴 이동 범위를 덮는 만큼만 계산하고,
        /// 이미 계산해 둔 지도가 그 범위를 덮으면 그대로 쓴다. key는 목표 칸 묶음을 구별하는 이름.
        /// </summary>
        private Dictionary<HexCoord, int> PathTo(Game g, Unit u, string key, IReadOnlyCollection<HexCoord> goals)
        {
            var cacheKey = (key, u.Formation.Id, u.Faction);
            int margin = g.Field.MovePoints(u) * 2 + 12;
            if (_pathCache.TryGetValue(cacheKey, out var cached)
                && cached.Field.TryGetValue(u.Position, out var here) && here + margin <= cached.Covered)
                return cached.Field;
            var field = g.Field.PathField(u, goals, u.Position, margin, 1600, out int covered);
            _pathCache[cacheKey] = (field, covered);
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
            // 원정 도중 목표 세력과 평화 관계가 되면 원정을 그만둔다
            if (camp != null && target != null && target.Owner != null && w.AtPeace(target.Owner, faction))
            {
                camp.State = CampaignState.Done;
                camp = null;
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

            // 성벽·성문이 모두 서 있으면 성 둘레 어디든 가까운 곳으로 가서 두드린다 (한 성문에 몰리지 않게).
            // 한 곳이라도 뚫렸으면 정청을 향해 들어간다.
            var walls = target!.Placement.Tiles.Keys.Where(h => b.Structures.TryGetValue(h, out var s) && s.Blocking).ToList();
            bool breached = target.Placement.Tiles.Keys.Any(h => b.Structures.TryGetValue(h, out var s)
                                && (s.Kind == StructureKind.Wall || s.Kind == StructureKind.Gate) && s.MaxDurability > 0 && !s.Blocking);
            bool encircle = walls.Count > 0 && !breached;
            // 뚫린 뒤에는 성 안으로 들어가 수비대를 치고 정청을 차지하는 게 먼저다.
            // 틈 앞에 줄이 막혀 나아갈 수 없는 부대만 옆 성벽을 쳐서 두 번째 틈을 낸다 (ChooseAction의 breach 점수).
            List<HexCoord> cluster = walls;
            if (encircle)
            {
                // 집중 공격 지점: 남은 내구가 가장 낮은 곳(성문 우선). 반경 2칸 안의 성벽도 함께 공격 대상으로 삼는다.
                if (camp.Focus == null || !walls.Contains(camp.Focus.Value))
                {
                    var centroid = camp.Units.Where(x => x.IsAlive).Select(x => x.Position).DefaultIfEmpty(u.Position).First();
                    camp.Focus = walls.OrderBy(h => b.Structures[h].Durability * (b.Structures[h].Kind == StructureKind.Gate ? 1.0 : 1.5)
                                                    + h.DistanceTo(centroid) * 60).First();
                }
                var focus = camp.Focus.Value;
                cluster = walls.Where(h => h.DistanceTo(focus) <= 2).ToList();
            }
            // 이동은 집중 지점 하나를 향해 모이고, 공격은 그 둘레 성벽까지 (자리가 없는 부대도 옆 성벽을 친다)
            var field = encircle ? PathTo(g, u, camp.Focus!.Value) : PathTo(g, u, target.Placement.Center);
            HexCoord goal = encircle ? camp.Focus!.Value : target.Placement.Center;

            if (TryDuel(g, u)) return;

            var best = ChooseAction(g, u, goal, cluster, field, target.Placement.Center, breached: !encircle && walls.Count > 0);
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
                    if (before == u.Position)
                    {
                        Stats.StalledUnitTurns++;
                        string why = StallReason(b, u, field);
                        Stats.StallReasons[why] = Stats.StallReasons.TryGetValue(why, out var n) ? n + 1 : 1;
                    }
                    break;
            }
        }

        /// <summary>제자리에 선 이유: 목표 쪽 이웃 칸이 왜 막혔는지.</summary>
        private static string StallReason(Battle.Battle b, Unit u, Dictionary<HexCoord, int> field)
        {
            if (!field.TryGetValue(u.Position, out var here)) return "경로 없음";
            var better = u.Position.Neighbors().Where(n => field.TryGetValue(n, out var v) && v < here).ToList();
            if (better.Count == 0) return "도착 후 할 일 없음";
            if (better.All(n => b.UnitAt(n) is Unit x && x.Faction == u.Faction)) return "아군에 막힘";
            if (better.All(n => b.UnitAt(n) is Unit x)) return "부대에 막힘(적 포함)";
            return "이동 불가(지배 영역 등)";
        }

        /// <summary>
        /// 일기토: 우리 대표 무력이 85 이상이고 붙어 있는 적 대표보다 15 이상 높으면 절반 확률로 신청한다
        /// (사기를 크게 흔들 기회. 매번 쓰면 기력이 바닥나므로 아낀다).
        /// </summary>
        private bool TryDuel(Game g, Unit u)
        {
            var b = g.Field;
            if (u.Energy < g.World.Data.Rules.Duel.ChallengeEnergy) return false;
            var mine = Battle.Battle.Champion(u);
            if (mine.Stats.War < 85) return false;
            var foe = b.AdjacentUnits(u.Position).Where(e => b.Hostile(e.Faction, u.Faction))
                       .OrderByDescending(e => e.Troops).FirstOrDefault(e => Battle.Battle.Champion(e).Stats.War <= mine.Stats.War - 15);
            if (foe == null || !g.World.Rng.Chance(0.5)) return false;
            Stats.Duels++;
            b.ChallengeDuel(u, foe);
            return true;
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
        private Choice ChooseAction(Game g, Unit u, HexCoord goal, IReadOnlyList<HexCoord> structures, Dictionary<HexCoord, int> field,
                                    HexCoord? capturePoint = null, bool breached = false)
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
            int maxStructureRange = Math.Max(u.Formation.Range, siegeTactics.Select(t => b.TacticRange(u, t)).DefaultIfEmpty(1).Max());
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
                // 정청에 들어서면 (수비 부대가 없을 때) 함락이다
                double capture = capturePoint != null && pos == capturePoint.Value ? 5000 : 0;
                Consider(best, progress + terrain - danger + capture, pos, ActionKind.Move);

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
                // 성벽·성문 공격: 성문은 내구가 낮아 더 높이 친다. 공성진은 언제나 구조물 우선
                foreach (var st in structures)
                {
                    int d = pos.DistanceTo(st);
                    if (d > maxStructureRange) continue;
                    bool isGate = b.Structures[st].Kind == StructureKind.Gate;
                    double weight = (u.Formation.Id == "siege" ? 2.0 : (enemies.Count == 0 ? 0.8 : 0.3)) * (isGate ? 1.0 : 0.7)
                                    * (st == goal ? 1.3 : 1.0); // 집중 지점을 먼저
                    // 이미 뚫렸으면(공성진 제외): 성 안으로 한 칸이라도 나아갈 수 있으면 그게 낫고(1칸 = 2점),
                    // 막혔을 때만 성벽을 친다 (진척 + 아주 작은 가산점)
                    bool sideJob = breached && u.Formation.Id != "siege";
                    double Score(double dmg) => sideJob ? progress + terrain + 1 + dmg * 0.002 : dmg * weight + terrain;
                    if (d <= u.Formation.Range)
                        Consider(best, Score(b.StructureDamage(u, 1.0, false)), pos, ActionKind.Structure, null, null, st);
                    foreach (var t in siegeTactics)
                        if (d <= b.TacticRange(u, t))
                            Consider(best, Score(b.StructureDamage(u, t.Power, false)), pos, ActionKind.Structure, null, t, st);
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
