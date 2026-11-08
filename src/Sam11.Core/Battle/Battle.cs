using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Common;
using Sam11.Core.Data;

namespace Sam11.Core.Battle
{
    public sealed class BattleOptions
    {
        /// <summary>전사 옵션 (04 군사·전투 설계서 10장). 끄면 전사는 중상으로 바뀐다.</summary>
        public bool DeathEnabled = true;
        public string Weather = "clear";

        /// <summary>전법 성공률을 고정한다 (테스트·디버깅용). null이면 정상 계산.</summary>
        public double? TacticSuccessOverride;
    }

    /// <summary>
    /// 전투 상태와 행동 API. 표현 계층(Unity)은 이 클래스에 명령을 내리고 BattleLog로 결과를 받는다.
    /// </summary>
    public sealed class Battle
    {
        public GameData Data { get; }
        public BattleMap Map { get; }
        public Rng Rng { get; }
        public BattleOptions Options { get; }
        public BattleLog Log { get; } = new BattleLog();
        public List<Unit> Units { get; } = new List<Unit>();
        public List<OfficerFate> Fates { get; } = new List<OfficerFate>();
        public int Round { get; private set; } = 1;
        public string WeatherId { get; set; }

        private int _nextId = 1;

        public Battle(GameData data, BattleMap map, ulong seed, BattleOptions? options = null)
        {
            Data = data;
            Map = map;
            Rng = new Rng(seed);
            Options = options ?? new BattleOptions();
            WeatherId = Options.Weather;
        }

        public WeatherDef? CurrentWeather => Data.Weather.TryGetValue(WeatherId, out var w) ? w : null;

        public IEnumerable<Unit> Alive => Units.Where(u => u.IsAlive);

        public Unit? UnitAt(HexCoord h) => Alive.FirstOrDefault(u => u.Position == h);

        public IEnumerable<Unit> AdjacentUnits(HexCoord h) => h.Neighbors().Select(UnitAt).Where(u => u != null)!;

        // ------------------------------------------------------------------ 편성

        /// <summary>진형을 펼칠 수 있는지. 적성 C는 불가, 장비 진형은 병장기 필요.</summary>
        public static string? CanDeploy(GameData data, IReadOnlyList<OfficerDef> members, string formationId,
                                        int troops, IReadOnlyDictionary<string, int>? equipment)
        {
            if (!data.Formations.TryGetValue(formationId, out var f))
                return $"없는 진형: {formationId}";
            if (members.Count == 0 || members.Count > data.Rules.Unit.MaxOfficers)
                return $"부대원은 1~{data.Rules.Unit.MaxOfficers}명이어야 함";
            if (UnitStats.BestAptitude(members, formationId) == AptitudeRank.C)
                return $"{f.Name.Ko} 적성이 C라 펼칠 수 없음";
            if (f.RequiredEquipment != null)
            {
                int have = equipment != null && equipment.TryGetValue(f.RequiredEquipment, out var n) ? n : 0;
                if (have < troops)
                    return $"{f.Name.Ko}에 필요한 병장기({f.RequiredEquipment})가 부족함: {have}/{troops}";
            }
            return null;
        }

        /// <summary>부대를 전장에 배치한다. 편성 규칙을 어기면 예외.</summary>
        public Unit Deploy(string faction, IReadOnlyList<string> officerIds, string formationId, int troops, HexCoord at,
                           IReadOnlyDictionary<string, int>? equipment = null, string? leaderRank = null,
                           int energy = 50, int morale = 70)
        {
            var members = officerIds.Select(id => Data.Officers[id]).ToList();
            string? why = CanDeploy(Data, members, formationId, troops, equipment);
            if (why != null)
                throw new InvalidOperationException(why);
            if (!Map.Contains(at) || UnitAt(at) != null)
                throw new InvalidOperationException($"{at}에 배치할 수 없음");
            var rank = leaderRank != null ? Data.Ranks[leaderRank] : null;
            int max = UnitStats.MaxTroops(members[0], rank, Data.Rules.Unit);
            if (troops > max)
                throw new InvalidOperationException($"병력 {troops}이 최대 병력 {max}을 넘음");
            var u = new Unit(_nextId++, faction, members[0], members.Skip(1).ToList(), Data.Formations[formationId],
                             troops, max, energy, morale, Data) { Position = at };
            Units.Add(u);
            return u;
        }

        // ------------------------------------------------------------------ 이동

        internal bool TerrainIgnoresPenalty(Unit u, string terrain) =>
            u.Traits.SelectMany(t => t.Effects).Any(e => e.Type == "ignore_terrain_penalty"
                && e.Extra.TryGetValue("terrain", out var list) && list.Values<string>().Contains(terrain));

        /// <summary>진형별 이동 비용 (×2 정수, 도로 0.5 표현용). null이면 진입 불가.</summary>
        public int? MoveCost2(Unit u, HexCoord h)
        {
            if (!Map.Contains(h))
                return null;
            string t = Map.TerrainAt(h);
            if (!Data.Terrain.TryGetValue(t, out var def))
                return null;
            bool water = u.Formation.WaterOnly;
            if (water ? !def.PassableWater : !def.PassableLand)
                return null;
            if (def.MoveCost == null)
                return null;
            string f = u.Formation.Id;
            bool heavy = f == "wedge" || f == "siege";
            if (t == "mountain" && heavy) return null;
            if (t == "road" && heavy) return 1;
            if (t == "forest" && f == "wedge") return 6;
            int cost = def.MoveCost.Value * 2;
            if (WeatherId == "rain" && t != "plain" && t != "road") cost += 2;
            return cost;
        }

        public int MovePoints(Unit u)
        {
            var ctx = new Modifiers.Context { Unit = u, Terrain = Map.TerrainAt(u.Position), Weather = WeatherId };
            double add = Modifiers.SumAdd(Modifiers.Collect(u, CurrentWeather), "move", ctx);
            return Math.Max(1, (int)Math.Round(u.Formation.Move + add));
        }

        /// <summary>
        /// 이동 가능한 칸. 적 부대 칸은 지나갈 수 없고, 적에 인접한 칸에 들어가면 그 자리에서 멈춘다 (지배 영역).
        /// </summary>
        public Dictionary<HexCoord, int> Reachable(Unit u)
        {
            int budget = MovePoints(u) * 2;
            var best = new Dictionary<HexCoord, int> { [u.Position] = 0 };
            var frontier = new List<HexCoord> { u.Position };
            while (frontier.Count > 0)
            {
                frontier.Sort((a, b) => best[a].CompareTo(best[b]));
                var cur = frontier[0];
                frontier.RemoveAt(0);
                if (cur != u.Position && InEnemyZone(u, cur))
                    continue;
                foreach (var n in cur.Neighbors())
                {
                    var occupant = UnitAt(n);
                    if (occupant != null && occupant != u)
                        continue;
                    int? step = MoveCost2(u, n);
                    if (step == null)
                        continue;
                    int cost = best[cur] + step.Value;
                    if (cost > budget || (best.TryGetValue(n, out var old) && old <= cost))
                        continue;
                    best[n] = cost;
                    frontier.Add(n);
                }
            }
            return best;
        }

        private bool InEnemyZone(Unit u, HexCoord h) => AdjacentUnits(h).Any(o => o.Faction != u.Faction);

        public void Move(Unit u, HexCoord to)
        {
            EnsureCanAct(u);
            if (!Reachable(u).ContainsKey(to))
                throw new InvalidOperationException($"{Korean.EunNeun(u.Label)} {to}로 이동할 수 없음");
            u.Position = to;
        }

        // ------------------------------------------------------------------ 공격

        private void EnsureCanAct(Unit u)
        {
            if (!u.IsAlive) throw new InvalidOperationException($"{Korean.EunNeun(u.Label)} 전장에 없음");
            if (u.Status == UnitStatus.Confused) throw new InvalidOperationException($"{Korean.EunNeun(u.Label)} 혼란 상태");
            if (u.HasActed) throw new InvalidOperationException($"{Korean.EunNeun(u.Label)} 이미 행동함");
        }

        public bool InRange(Unit attacker, Unit target, int? rangeOverride = null) =>
            attacker.Position.DistanceTo(target.Position) <= (rangeOverride ?? attacker.Formation.Range);

        /// <summary>일반 공격. 근접 공격이면 반격을 받는다.</summary>
        public AttackResult Attack(Unit attacker, Unit target)
        {
            EnsureCanAct(attacker);
            if (target.Faction == attacker.Faction || !target.IsAlive || !InRange(attacker, target))
                throw new InvalidOperationException("공격할 수 없는 대상");
            var r = new AttackResult();
            r.Damage = DealDamage(attacker, target, 1.0, 0, 0, true, $"{attacker.Label}의 공격 → {target.Label} {{0}} 피해");
            if (attacker.Formation.Trait.Id == "charge" && target.IsAlive)
                Push(attacker, target, 1);
            if (target.IsAlive && !attacker.Formation.IsRanged)
                r.CounterDamage = Counter(target, attacker);
            r.TargetDestroyed = !target.IsAlive;
            Finish(attacker);
            return r;
        }

        /// <summary>쓸 수 있는 전법. 적성 S=3개, A=2개, B=1개.</summary>
        public IEnumerable<TacticDef> AvailableTactics(Unit u) =>
            u.Formation.Tactics.Take(UnitStats.TacticSlots(u.Aptitude)).Select(id => Data.Tactics[id]);

        public int TacticRange(Unit u, TacticDef t)
        {
            var r = t.Effects.FirstOrDefault(e => e.Type == "range");
            return r != null ? (int)r.Value : u.Formation.Range;
        }

        public bool TacticTargetsStructureOnly(TacticDef t) =>
            t.Effects.Any(e => e.Type == "structure_only" || e.Type == "target_on_wall");

        /// <summary>전법. 성공하면 대상에 인접한 아군이 연쇄 공격한다.</summary>
        public AttackResult UseTactic(Unit attacker, string tacticId, Unit target)
        {
            EnsureCanAct(attacker);
            var t = AvailableTactics(attacker).FirstOrDefault(x => x.Id == tacticId)
                    ?? throw new InvalidOperationException($"{Korean.EunNeun(attacker.Label)} {tacticId} 전법을 쓸 수 없음 (적성 {attacker.Aptitude})");
            bool selfOnly = t.Power <= 0;
            if (TacticTargetsStructureOnly(t))
                throw new InvalidOperationException($"{t.Name.Ko}는 거점 공격 전용 (공성 단계에서 구현)");
            if (attacker.Energy < t.EnergyCost)
                throw new InvalidOperationException($"기력 부족: {attacker.Energy}/{t.EnergyCost}");
            if (!selfOnly && (target.Faction == attacker.Faction || !target.IsAlive || !InRange(attacker, target, TacticRange(attacker, t))))
                throw new InvalidOperationException("전법 대상이 아님");

            attacker.Energy -= t.EnergyCost;
            var r = new AttackResult();

            if (selfOnly)
            {
                ApplySelfEffects(attacker, t);
                Log.Add($"{attacker.Label}의 {t.Name.Ko}!");
                Finish(attacker);
                return r;
            }

            double chance = Options.TacticSuccessOverride ?? Combat.TacticSuccessChance(this, attacker, target);
            if (!Rng.Chance(chance))
            {
                r.Success = false;
                Log.Add($"{attacker.Label}의 {t.Name.Ko} 실패 (성공률 {chance:P0})");
                Finish(attacker);
                return r;
            }

            HexCoord targetOrigin = target.Position;
            double power = t.Power;
            foreach (var e in t.Effects)
            {
                if (e.Type == "power_per_adjacent_ally")
                    power += e.Value * AdjacentUnits(target.Position).Count(u => u.Faction == attacker.Faction && u != attacker);
                if (e.Type == "bonus_vs_status" && target.Status == UnitStatus.Confused)
                    power += e.Value;
            }
            int repeat = (int)Math.Max(1, t.Effects.FirstOrDefault(e => e.Type == "repeat")?.ExtraInt("times", 1) ?? 1);
            for (int i = 0; i < repeat && target.IsAlive; i++)
                r.Damage += DealDamage(attacker, target, power, 0, 0, true,
                    (i == 0 ? $"{attacker.Label}의 {t.Name.Ko}! → " : "  추가 공격 → ") + $"{target.Label} {{0}} 피해");

            ApplyAreaEffects(attacker, t, target, power, r);
            if (target.IsAlive)
                ApplyTargetEffects(attacker, t, target, targetOrigin);

            // 전법 연쇄: 대상에 인접한 아군이 차례로 추가 공격
            if (target.IsAlive && !target.HasTimed("chain_immune"))
            {
                double chainAdd = Modifiers.SumAdd(Modifiers.Collect(attacker, null), "chain_bonus",
                    new Modifiers.Context { Unit = attacker, Terrain = Map.TerrainAt(attacker.Position), Weather = WeatherId });
                var partners = AdjacentUnits(target.Position)
                    .Where(u => u.Faction == attacker.Faction && u != attacker && u.Status == UnitStatus.Ready)
                    .OrderBy(u => u.Id).ToList();
                foreach (var p in partners)
                {
                    if (!target.IsAlive) break;
                    r.ChainLinks++;
                    DealDamage(p, target, 1.0, r.ChainLinks, chainAdd, true, $"  {r.ChainLinks}연쇄! {p.Label} → {target.Label} {{0}} 피해");
                }
            }
            r.TargetDestroyed = !target.IsAlive;
            attacker.Morale = Math.Min(Data.Rules.Morale.Max, attacker.Morale + Data.Rules.Combat.MoraleGainAttack);
            Finish(attacker);
            return r;
        }

        private void ApplySelfEffects(Unit u, TacticDef t)
        {
            foreach (var e in t.Effects)
            {
                int turns = e.ExtraInt("turns", 1);
                if (e.Type == "self_buff")
                    u.TimedEffects.Add(new TimedEffect { Key = e.Stat ?? "defense", Value = e.Value, TurnsLeft = turns });
                else if (e.Type == "reflect")
                    u.TimedEffects.Add(new TimedEffect { Key = "reflect", Value = e.Value, TurnsLeft = turns });
                else if (e.Type == "immunity" && e.ExtraString("status") == "chain")
                    u.TimedEffects.Add(new TimedEffect { Key = "chain_immune", Value = 1, TurnsLeft = turns });
            }
        }

        private void ApplyAreaEffects(Unit attacker, TacticDef t, Unit target, double power, AttackResult r)
        {
            foreach (var e in t.Effects.Where(e => e.Type == "area"))
            {
                var extra = new List<Unit>();
                string shape = e.ExtraString("shape") ?? "";
                if (shape == "adjacent_enemies" || (shape == "radius" && e.ExtraInt("radius") >= 1))
                    extra.AddRange(AdjacentUnits(target.Position).Where(u => u.Faction != attacker.Faction));
                else if (shape == "line")
                {
                    var behind = target.Position.StepAwayFrom(attacker.Position);
                    var u = UnitAt(behind);
                    if (u != null && u.Faction != attacker.Faction) extra.Add(u);
                }
                foreach (var u in extra.Distinct().Where(x => x != target && x.IsAlive))
                {
                    int d = DealDamage(attacker, u, power * 0.7, 0, 0, true, $"  범위 피해 → {u.Label} {{0}}");
                    r.ExtraHits.Add((u, d));
                }
            }
        }

        private void ApplyTargetEffects(Unit attacker, TacticDef t, Unit target, HexCoord targetOrigin)
        {
            foreach (var e in t.Effects)
            {
                switch (e.Type)
                {
                    case "push":
                        Push(attacker, target, e.ExtraInt("distance", 1));
                        break;
                    case "pull":
                    {
                        var to = target.Position.StepToward(attacker.Position);
                        if (to != attacker.Position && CanStand(target, to)) target.Position = to;
                        break;
                    }
                    case "advance":
                        if (target.Position != targetOrigin && CanStand(attacker, targetOrigin))
                            attacker.Position = targetOrigin;
                        break;
                    case "retreat_after":
                    {
                        var to = attacker.Position.StepAwayFrom(target.Position);
                        if (CanStand(attacker, to)) attacker.Position = to;
                        break;
                    }
                    case "inflict":
                        if (e.ExtraString("status") == "confused" && Rng.Chance(e.ExtraDouble("chance", 1)))
                            Confuse(target);
                        break;
                    case "energy_damage":
                        target.Energy = Math.Max(0, target.Energy - (int)e.ExtraDouble("value", e.Value));
                        break;
                    case "fire":
                        if (WeatherId != "rain")
                        {
                            Map.Ignite(target.Position, Data.Rules.Combat.FireTurns);
                            int radius = e.ExtraInt("radius") + (WeatherId == "gale" ? 1 : 0);
                            if (radius >= 1)
                                foreach (var n in target.Position.Neighbors())
                                    Map.Ignite(n, Data.Rules.Combat.FireTurns);
                            Log.Add($"  {target.Label} 주변에 불이 붙었다");
                        }
                        break;
                }
            }
        }

        private bool CanStand(Unit u, HexCoord h) => MoveCost2(u, h) != null && UnitAt(h) == null;

        private void Push(Unit attacker, Unit target, int distance)
        {
            for (int i = 0; i < distance; i++)
            {
                var to = target.Position.StepAwayFrom(attacker.Position);
                if (!CanStand(target, to)) break;
                target.Position = to;
            }
        }

        private int Counter(Unit defender, Unit attacker)
        {
            if (defender.Status != UnitStatus.Ready)
                return 0;
            var ctx = new Modifiers.Context { Unit = defender, Terrain = Map.TerrainAt(defender.Position), Weather = WeatherId };
            double bonus = Modifiers.SumMul(Modifiers.Collect(defender, null), "counter_damage", ctx);
            if (defender.Formation.Trait.Id == "counter") bonus += 0.3;
            double power = Data.Rules.Combat.CounterRatio * (1 + bonus);
            return DealDamage(defender, attacker, power, 0, 0, true, $"  {defender.Label}의 반격 → {attacker.Label} {{0}} 피해");
        }

        /// <summary>피해를 주고 사기·궤멸을 처리한다.</summary>
        /// <param name="line">피해를 기록할 문장. {0} 자리에 피해량이 들어간다. 결과(궤멸·혼란)보다 먼저 기록된다.</param>
        private int DealDamage(Unit attacker, Unit target, double power, int chainLinks, double chainAdd, bool randomize, string line)
        {
            var b = Combat.Compute(this, attacker, target, power, chainLinks, chainAdd, randomize);
            int dmg = Math.Max(1, (int)Math.Round(b.Total));
            dmg = Math.Min(dmg, target.Troops);
            int before = target.Troops;
            target.Troops -= dmg;
            Log.Add(string.Format(line, dmg));

            double reflect = target.TimedValue("reflect");
            if (reflect > 0 && attacker.IsAlive && attacker.Position.DistanceTo(target.Position) == 1)
            {
                int back = Math.Min(attacker.Troops, (int)Math.Round(dmg * reflect));
                attacker.Troops -= back;
                Log.Add($"  역습! {attacker.Label} {back} 피해");
                if (attacker.Troops <= 0) Destroy(attacker, target);
            }

            if (target.Troops <= 0)
            {
                Destroy(target, attacker);
                attacker.Morale = Math.Min(Data.Rules.Morale.Max, attacker.Morale + Data.Rules.Combat.MoraleGainDestroy);
            }
            else
            {
                LoseMorale(target, Data.Rules.Combat.MoraleLossScale * dmg / Math.Max(1, before));
            }
            return dmg;
        }

        public void LoseMorale(Unit u, double amount)
        {
            var ctx = new Modifiers.Context { Unit = u, Terrain = Map.TerrainAt(u.Position), Weather = WeatherId };
            double mod = Modifiers.SumMul(Modifiers.Collect(u, null), "morale_loss", ctx);
            int loss = (int)Math.Ceiling(Math.Max(0, amount * (1 + mod)));
            u.Morale = Math.Max(0, u.Morale - loss);
            if (u.Morale == 0 && u.Status == UnitStatus.Ready)
                Confuse(u);
        }

        private void Confuse(Unit u)
        {
            u.Status = UnitStatus.Confused;
            // 시한 효과는 자기 턴 시작마다 1씩 줄어든다. 2로 두어야 다음 자기 턴을 쉬고 그다음 턴에 풀린다.
            u.TimedEffects.RemoveAll(e => e.Key == "confused");
            u.TimedEffects.Add(new TimedEffect { Key = "confused", TurnsLeft = 2 });
            Log.Add($"  {Korean.IGa(u.Label)} 혼란에 빠졌다");
        }

        private void Finish(Unit u)
        {
            if (u.IsAlive) u.HasActed = true;
        }

        // ------------------------------------------------------------------ 궤멸

        public bool IsSurrounded(Unit u) =>
            AdjacentUnits(u.Position).Count(o => o.Faction != u.Faction) >= Data.Rules.Morale.SurroundedThreshold
            || u.Position.Neighbors().All(n => !CanStand(u, n));

        private void Destroy(Unit u, Unit? by)
        {
            u.Troops = 0;
            bool surrounded = IsSurrounded(u);
            bool onFire = Map.IsBurning(u.Position);
            u.Status = UnitStatus.Destroyed;
            Log.Add($"{u.Label} 궤멸!");
            foreach (var o in u.Members)
            {
                var fate = RollFate(o, u, surrounded, onFire);
                Fates.Add(new OfficerFate { OfficerId = o.Id, Outcome = fate });
                Log.Add($"  {o.Name.Ko}: {FateText(fate)}");
            }
            foreach (var ally in Alive.Where(a => a.Faction == u.Faction))
                LoseMorale(ally, Data.Rules.Combat.MoraleLossAllyDestroyed);
        }

        /// <summary>무장별 궤멸 판정: 귀환·부상·포로·전사 (04 군사·전투 설계서 10장).</summary>
        public DefeatOutcome RollFate(OfficerDef o, Unit u, bool surrounded, bool onFire)
        {
            var p = FateProbabilities(o, u, surrounded, onFire);
            double roll = Rng.NextDouble(), acc = 0;
            foreach (var kv in p)
            {
                acc += kv.Value;
                if (roll < acc) return kv.Key;
            }
            return DefeatOutcome.Return;
        }

        public List<KeyValuePair<DefeatOutcome, double>> FateProbabilities(OfficerDef o, Unit u, bool surrounded, bool onFire)
        {
            var d = Data.Rules.Defeat;
            var p = new Dictionary<string, double>
            {
                ["return"] = d["return"], ["injury"] = d["injury"], ["capture"] = d["capture"], ["death"] = d["death"],
            };
            void Apply(string key)
            {
                if (!Data.Rules.DefeatModifiers.TryGetValue(key, out var m)) return;
                foreach (var kv in m)
                    if (p.ContainsKey(kv.Key)) p[kv.Key] += kv.Value;
            }
            if (surrounded) Apply("surrounded");
            if (onFire) Apply("on_fire");
            if (Data.Rules.DefeatModifiers.TryGetValue("high_war", out var hw) && o.Stats.War >= hw["threshold"]) Apply("high_war");
            if (u.Formation.Id == "wedge") Apply("wedge");

            var keys = new List<string>(p.Keys);
            foreach (var k in keys) p[k] = Math.Max(0, p[k]);
            double sum = p.Values.Sum();
            var list = new List<KeyValuePair<DefeatOutcome, double>>
            {
                new KeyValuePair<DefeatOutcome, double>(DefeatOutcome.Return, p["return"] / sum),
                new KeyValuePair<DefeatOutcome, double>(DefeatOutcome.Injury, p["injury"] / sum),
                new KeyValuePair<DefeatOutcome, double>(DefeatOutcome.Capture, p["capture"] / sum),
                new KeyValuePair<DefeatOutcome, double>(Options.DeathEnabled ? DefeatOutcome.Death : DefeatOutcome.SeriousInjury, p["death"] / sum),
            };
            return list;
        }

        private static string FateText(DefeatOutcome f) => f switch
        {
            DefeatOutcome.Return => "귀환",
            DefeatOutcome.Injury => "부상",
            DefeatOutcome.Capture => "포로",
            DefeatOutcome.Death => "전사",
            DefeatOutcome.SeriousInjury => "중상",
            _ => f.ToString(),
        };

        // ------------------------------------------------------------------ 턴

        /// <summary>
        /// 한 세력의 턴을 시작한다: 혼란 해제·도주 판정, 기력 회복, 포위 사기 감소, 화재 피해.
        /// </summary>
        public void BeginTurn(string faction)
        {
            foreach (var u in Alive.Where(x => x.Faction == faction).ToList())
            {
                u.HasActed = false;
                u.TurnsSinceSortie++;
                foreach (var e in u.TimedEffects) e.TurnsLeft--;
                u.TimedEffects.RemoveAll(e => e.TurnsLeft <= 0);

                if (u.Status == UnitStatus.Confused && u.HasTimed("confused"))
                {
                    Log.Add($"{Korean.EunNeun(u.Label)} 혼란에 빠져 움직이지 못한다");
                    continue;
                }
                if (u.Status == UnitStatus.Confused)
                {
                    // 혼란이 풀리면 주장 통솔에 비례해 사기를 수습하고, 그래도 낮으면 확률로 도주한다.
                    u.Status = UnitStatus.Ready;
                    u.Morale += (int)(u.EffLdr * Data.Rules.Combat.ConfusionRecoverLdrRatio);
                    int rout = Data.Rules.Morale.RoutThreshold;
                    if (u.Morale < rout && Rng.Chance((double)(rout - u.Morale) / rout))
                    {
                        u.Status = UnitStatus.Routed;
                        Log.Add($"{Korean.IGa(u.Label)} 전장에서 도주했다");
                        foreach (var o in u.Members)
                            Fates.Add(new OfficerFate { OfficerId = o.Id, Outcome = DefeatOutcome.Return });
                        continue;
                    }
                }

                u.Energy = Math.Min(Data.Rules.Energy.Max, u.Energy + Data.Rules.Energy.RegenPerTurn);
                if (AdjacentUnits(u.Position).Count(o => o.Faction != faction) >= Data.Rules.Morale.SurroundedThreshold)
                    LoseMorale(u, Data.Rules.Combat.MoraleLossSurrounded);

                foreach (var aura in AdjacentUnits(u.Position).Where(o => o.Faction != faction && o.Status == UnitStatus.Ready))
                    foreach (var e in aura.Traits.SelectMany(t => t.Effects).Where(e => e.Type == "aura" && e.Stat == "morale"))
                        LoseMorale(u, -e.Value);

                if (Map.IsBurning(u.Position) && u.IsAlive)
                {
                    int burn = Math.Max(1, (int)(u.Troops * Data.Rules.Combat.FireDamageRatio));
                    u.Troops -= burn;
                    Log.Add($"{u.Label} 화재 피해 {burn}");
                    if (u.Troops <= 0) Destroy(u, null);
                }
            }
        }

        /// <summary>모든 세력이 한 번씩 행동하면 라운드를 넘긴다 (화재 감소).</summary>
        public void EndRound()
        {
            Map.TickFire();
            if (WeatherId == "rain")
                foreach (var h in Map.BurningCells) Map.Extinguish(h);
            Round++;
        }

        public IEnumerable<string> FactionsAlive => Alive.Select(u => u.Faction).Distinct();
    }
}
