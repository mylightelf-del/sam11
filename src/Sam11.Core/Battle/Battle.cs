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

        /// <summary>성벽·성문·성내 칸. 비어 있으면 야전.</summary>
        public Dictionary<HexCoord, SiegeStructure> Structures { get; } = new Dictionary<HexCoord, SiegeStructure>();
        public Dictionary<string, StrongholdOnField> Strongholds { get; } = new Dictionary<string, StrongholdOnField>();
        public List<CaptureEvent> Captures { get; } = new List<CaptureEvent>();

        /// <summary>두 세력이 적대 관계인지. 동맹끼리는 길을 막지 않는다 (기본: 세력이 다르면 적).</summary>
        public Func<string, string, bool> Hostile { get; set; } = (a, b) => a != b;

        /// <summary>칸이 어느 도시 영역인지 (전국 맵에서만). null이면 보급을 따지지 않는다.</summary>
        public Func<HexCoord, string?>? TerritoryOf { get; set; }
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

        /// <summary>
        /// 거점 수비대를 정청(중심)에 배치한다. 수비대는 성의 병력 전부를 담지만,
        /// 공격에 나서는 병력은 주장이 지휘할 수 있는 만큼(통솔 × 100)이다. 나머지는 버티는 몫이다.
        /// 주장이 없으면 이름 없는 수비대장이 지휘한다.
        /// </summary>
        public Unit DeployGarrison(string strongholdId, IReadOnlyList<string> officerIds, int troops,
                                   IReadOnlyDictionary<string, int>? equipment = null, int morale = 70)
        {
            var sh = Strongholds[strongholdId];
            if (sh.Owner == null) throw new InvalidOperationException("주인 없는 거점에는 수비대가 없음");
            var members = officerIds.Select(id => Data.Officers[id]).Take(Data.Rules.Unit.MaxOfficers).ToList();
            if (members.Count == 0) members.Add(Garrison.Anonymous(Data));
            string formation = Data.Rules.Siege.GarrisonFormationPreference
                .FirstOrDefault(f => CanDeploy(Data, members, f, troops, equipment) == null) ?? "square";
            if (UnitAt(sh.Center) != null) throw new InvalidOperationException($"{sh.Name} 정청이 이미 점거됨");
            int command = UnitStats.MaxTroops(members[0], null, Data.Rules.Unit);
            var u = new Unit(_nextId++, sh.Owner, members[0], members.Skip(1).ToList(), Data.Formations[formation],
                             troops, command, 50, morale, Data) { Position = sh.Center };
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
            if (Structures.TryGetValue(h, out var st))
            {
                if (u.Formation.WaterOnly) return null;
                if (st.Blocking)
                    return OwnerOf(st) == u.Faction ? 2 : (int?)null; // 아군은 성벽 위에 서고 성문을 지난다
                return 2; // 성내 칸, 무너진 성벽·성문
            }
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

        /// <summary>
        /// goal까지의 이동 비용 지도 (부대 위치는 무시하고 지형·구조물만 본다). goal 칸은 막혀 있어도 된다.
        /// AI가 강·산을 돌아가는 길을 찾을 때 쓴다. maxCost를 넘는 칸은 넣지 않는다.
        /// </summary>
        public Dictionary<HexCoord, int> PathField(Unit u, HexCoord goal, int maxCost = int.MaxValue) =>
            PathField(u, goal, null, 0, maxCost, out _);

        /// <summary>
        /// from 칸에 닿은 뒤 margin만큼만 더 넓히고 멈추는 경로 지도 (맵 전체를 다 돌지 않는다).
        /// covered: 이 비용까지는 지도가 완전하다. from에 닿지 못하면 maxCost까지 돈다.
        /// </summary>
        public Dictionary<HexCoord, int> PathField(Unit u, HexCoord goal, HexCoord? from, int margin, int maxCost, out int covered) =>
            PathField(u, new[] { goal }, from, margin, maxCost, out covered);

        /// <summary>여러 목표 칸 중 가장 가까운 곳까지의 비용 지도 (성벽·성문 전체를 목표로 삼을 때).</summary>
        public Dictionary<HexCoord, int> PathField(Unit u, IEnumerable<HexCoord> goals, HexCoord? from, int margin, int maxCost, out int covered)
        {
            var dist = new Dictionary<HexCoord, int>();
            var heap = new MinHeap<HexCoord>();
            foreach (var goal in goals)
            {
                dist[goal] = 0;
                heap.Push(goal, 0);
            }
            int limit = maxCost;
            covered = maxCost;
            while (heap.Count > 0)
            {
                var (cur, d) = heap.Pop();
                if (d > dist[cur]) continue;
                if (d > limit)
                {
                    covered = limit;
                    break;
                }
                if (from != null && cur == from.Value && limit == maxCost)
                    limit = Math.Min(maxCost, d + margin);
                foreach (var n in cur.Neighbors())
                {
                    int? step = MoveCost2(u, n);
                    if (step == null) continue;
                    int nd = d + step.Value;
                    if (nd > maxCost) continue;
                    if (!dist.TryGetValue(n, out var old) || nd < old)
                    {
                        dist[n] = nd;
                        heap.Push(n, nd);
                    }
                }
            }
            return dist;
        }

        private bool InEnemyZone(Unit u, HexCoord h) => AdjacentUnits(h).Any(o => Hostile(o.Faction, u.Faction));

        public void Move(Unit u, HexCoord to)
        {
            EnsureCanAct(u);
            if (!Reachable(u).ContainsKey(to))
                throw new InvalidOperationException($"{Korean.EunNeun(u.Label)} {to}로 이동할 수 없음");
            u.Position = to;
            EnterCityCheck(u);
            CheckCaptures();
        }

        public string? OwnerOf(SiegeStructure st) => Strongholds.TryGetValue(st.Stronghold, out var sh) ? sh.Owner : null;

        public bool IsInsideCity(HexCoord h) => Structures.TryGetValue(h, out var st) && (st.IsInterior || !st.Blocking);

        /// <summary>추행·공성진은 성내에서 쓸 수 없다: 성 안에 들어서면 기본 진형으로 바뀐다.</summary>
        private void EnterCityCheck(Unit u)
        {
            if (u.Formation.UsableInCity || !IsInsideCity(u.Position)) return;
            var best = Data.Formations.Values.Where(f => f.Category == "base")
                .OrderByDescending(f => UnitStats.BestAptitude(u.Members, f.Id)).ThenBy(f => f.Id).First();
            Log.Add($"{Korean.IGa(u.Label)} 성 안에 들어서며 {Korean.EulReul(best.Name.Ko)} 펼쳤다");
            u.SetFormation(best);
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
            if (!Hostile(attacker.Faction, target.Faction) || !target.IsAlive || !InRange(attacker, target))
                throw new InvalidOperationException("공격할 수 없는 대상 (같은 편이거나 동맹·정전 중이면 먼저 약속을 깨야 한다)");
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

        /// <summary>구조물 피해: 병력 × 무력 대 구조물 방어 × 진형 공성치 × 공성 특기 × 전법 위력.</summary>
        public int StructureDamage(Unit attacker, double power, bool randomize)
        {
            var c = Data.Rules.Combat;
            double ratio = (attacker.EffWar + c.StatSmoothing) / (Data.Rules.Siege.StructureDefenseStat + c.StatSmoothing);
            double formation = (attacker.Formation.StrongholdAttack ?? attacker.Formation.Attack) / 100.0;
            var ctx = new Modifiers.Context { Unit = attacker, Terrain = Map.TerrainAt(attacker.Position), Weather = WeatherId };
            double trait = 1 + Modifiers.SumMul(Modifiers.Collect(attacker, null), "structure_damage", ctx);
            double rnd = randomize ? Rng.Range(c.RandomMin, c.RandomMax) : 1.0;
            double dmg = attacker.FightingTroops * c.BaseRatio * ratio * formation * trait * power * rnd
                         * Data.Rules.Siege.StructureDamageRatio * Data.Rules.Morale.MultiplierFor(attacker.Morale);
            return Math.Max(1, (int)Math.Round(dmg));
        }

        /// <summary>성벽·성문 공격. 반격은 없다.</summary>
        public int AttackStructure(Unit attacker, HexCoord target) => HitStructure(attacker, target, null);

        /// <summary>구조물에 전법 사용 (충차·투석 등 공성진 전법).</summary>
        public int UseTacticOnStructure(Unit attacker, string tacticId, HexCoord target)
        {
            var t = AvailableTactics(attacker).FirstOrDefault(x => x.Id == tacticId)
                    ?? throw new InvalidOperationException($"{Korean.EunNeun(attacker.Label)} {tacticId} 전법을 쓸 수 없음");
            if (t.Formation != "siege")
                throw new InvalidOperationException($"{Korean.EunNeun(t.Name.Ko)} 구조물에 쓸 수 없음 (공성진 전법만 가능)");
            if (t.Effects.Any(e => e.Type == "target_on_wall"))
                throw new InvalidOperationException($"{Korean.EunNeun(t.Name.Ko)} 성벽 위 부대를 노리는 전법");
            if (attacker.Energy < t.EnergyCost)
                throw new InvalidOperationException($"기력 부족: {attacker.Energy}/{t.EnergyCost}");
            return HitStructure(attacker, target, t);
        }

        private int HitStructure(Unit attacker, HexCoord target, TacticDef? tactic)
        {
            EnsureCanAct(attacker);
            if (!Structures.TryGetValue(target, out var st) || !st.Blocking)
                throw new InvalidOperationException("공격할 성벽·성문이 없음");
            if (OwnerOf(st) is string owner && !Hostile(attacker.Faction, owner))
                throw new InvalidOperationException("아군·동맹 구조물");
            int range = tactic != null ? TacticRange(attacker, tactic) : attacker.Formation.Range;
            if (attacker.Position.DistanceTo(target) > range)
                throw new InvalidOperationException("사거리 밖");
            if (tactic != null) attacker.Energy -= tactic.EnergyCost;
            int dmg = Math.Min(st.Durability, StructureDamage(attacker, tactic?.Power ?? 1.0, true));
            st.Durability -= dmg;
            string what = st.Kind == StructureKind.Gate ? "성문" : "성벽";
            string name = Strongholds.TryGetValue(st.Stronghold, out var sh) ? sh.Name : st.Stronghold;
            Log.Add($"{attacker.Label}의 {(tactic != null ? tactic.Name.Ko + "! " : "공격 ")}→ {name} {what} {dmg} 피해 (내구 {st.Durability}/{st.MaxDurability})");
            if (st.Durability <= 0)
            {
                Map.Set(target, "street");
                Log.Add($"  {name} {Korean.IGa(what)} 무너졌다!");
            }
            Finish(attacker);
            return dmg;
        }

        // ------------------------------------------------------------------ 일기토 (05 설계서 4.3)

        /// <summary>부대에서 무력이 가장 높은 무장 (일기토 대표).</summary>
        public static OfficerDef Champion(Unit u) => u.Members.OrderByDescending(o => o.Stats.War).ThenBy(o => o.Id).First();

        /// <summary>상대가 일기토를 받는지: 대표 무력이 도전자보다 크게 밀리지 않으면 받는다.</summary>
        public bool WouldAccept(Unit challenger, Unit target) =>
            Champion(target).Stats.War >= Champion(challenger).Stats.War - Data.Rules.Duel.AcceptMargin;

        /// <summary>
        /// 일기토 신청 (기력 소모). 거절하면 상대 부대 사기 하락. 수락하면 카드 대결로 승부를 내고,
        /// 이긴 부대 사기 상승·진 부대 사기 하락, 진 무장은 부상·포로·전사 판정.
        /// 진 무장이 부장이면 부대에서 빠지고, 주장이 사로잡히거나 쓰러지면 부대가 무너진다.
        /// accept/pickers로 수락 여부와 카드 선택을 정할 수 있다 (플레이어 입력용). 이기면 true.
        /// </summary>
        public bool? ChallengeDuel(Unit challenger, Unit target, bool? accept = null,
                                   Duel.ICardPicker? pickChallenger = null, Duel.ICardPicker? pickTarget = null)
        {
            EnsureCanAct(challenger);
            var r = Data.Rules.Duel;
            if (!target.IsAlive || !Hostile(challenger.Faction, target.Faction) || challenger.Position.DistanceTo(target.Position) != 1)
                throw new InvalidOperationException("일기토를 신청할 수 없는 대상");
            if (challenger.Energy < r.ChallengeEnergy)
                throw new InvalidOperationException($"기력 부족: {challenger.Energy}/{r.ChallengeEnergy}");
            challenger.Energy -= r.ChallengeEnergy;
            var a = Champion(challenger);
            var d = Champion(target);
            Log.Add($"{Korean.IGa(a.Name.Ko)} {d.Name.Ko}에게 일기토를 청했다!");
            if (!(accept ?? WouldAccept(challenger, target)))
            {
                Log.Add($"  {Korean.EunNeun(d.Name.Ko)} 응하지 않았다. {target.Label} 사기 -{Data.Rules.Morale.RefuseDuelPenalty}");
                LoseMorale(target, Data.Rules.Morale.RefuseDuelPenalty);
                Finish(challenger);
                return null;
            }
            var duel = new Duel.CardDuel(Data, Rng, Duel.DuelKind.Combat, a, d);
            bool won = duel.Run(pickChallenger, pickTarget);
            foreach (var line in duel.Log) Log.Add("  " + line);
            var (winU, loseU) = won ? (challenger, target) : (target, challenger);
            var loser = won ? d : a;
            Log.Add($"  {Korean.IGa((won ? a : d).Name.Ko)} 이겼다!");
            winU.Morale = Math.Min(Data.Rules.Morale.Max, winU.Morale + r.WinMorale);
            LoseMorale(loseU, r.LoseMorale);
            DuelLoserFate(loser, loseU, winU);
            Finish(challenger);
            CheckCaptures();
            return won;
        }

        private void DuelLoserFate(OfficerDef loser, Unit loseU, Unit winU)
        {
            if (loser.Id.StartsWith("_")) return;
            var p = Data.Rules.Duel.LoserFate;
            double roll = Rng.NextDouble();
            DefeatOutcome outcome;
            if (roll < p.GetValueOrDefault("death")) outcome = Options.DeathEnabled ? DefeatOutcome.Death : DefeatOutcome.SeriousInjury;
            else if (roll < p.GetValueOrDefault("death") + p.GetValueOrDefault("capture")) outcome = DefeatOutcome.Capture;
            else if (roll < p.GetValueOrDefault("death") + p.GetValueOrDefault("capture") + p.GetValueOrDefault("injury")) outcome = DefeatOutcome.Injury;
            else outcome = DefeatOutcome.Return;
            if (outcome == DefeatOutcome.Return)
            {
                Log.Add($"  {Korean.EunNeun(loser.Name.Ko)} 간신히 물러났다");
                return;
            }
            Fates.Add(new OfficerFate
            {
                OfficerId = loser.Id, Outcome = outcome, Faction = loseU.Faction, By = winU.Faction,
                Home = loseU.Home, Garrison = loseU.IsGarrison, Position = loseU.Position,
                ByOfficers = new List<string> { Champion(winU).Id },
            });
            Log.Add($"  {loser.Name.Ko}: {FateText(outcome)}");
            if (outcome == DefeatOutcome.Injury) return;
            if (loseU.Leader == loser && loseU.IsGarrison)
            {
                // 수비대는 성의 병력 전부라 주장 하나에 무너지지 않는다: 사기만 크게 꺾인다
                LoseMorale(loseU, 20);
                Log.Add($"  주장을 잃은 {Korean.EunNeun(loseU.Label)} 크게 동요했다");
            }
            else if (loseU.Leader == loser)
            {
                // 주장이 쓰러지거나 사로잡히면 부대가 무너진다. 남은 부장은 돌아간다.
                loseU.Status = UnitStatus.Routed;
                Log.Add($"  주장을 잃은 {Korean.IGa(loseU.Label)} 무너졌다");
                foreach (var o in loseU.Subs.Where(m => !m.Id.StartsWith("_")))
                    Fates.Add(new OfficerFate
                    {
                        OfficerId = o.Id, Outcome = DefeatOutcome.Return, Faction = loseU.Faction,
                        Home = loseU.Home, Garrison = loseU.IsGarrison, Position = loseU.Position,
                    });
                foreach (var ally in Alive.Where(x => x.Faction == loseU.Faction))
                    LoseMorale(ally, Data.Rules.Combat.MoraleLossAllyDestroyed);
            }
            else loseU.RemoveSub(loser, Data);
        }

        // ------------------------------------------------------------------ 함락

        /// <summary>
        /// 성내(정청·시가) 칸에 수비 부대가 하나도 없고, 공격측 부대가 정청을 점거하면 함락된다.
        /// </summary>
        public void CheckCaptures()
        {
            foreach (var sh in Strongholds.Values)
            {
                if (sh.Owner == null) continue;
                var occupant = UnitAt(sh.Center);
                if (occupant == null || !Hostile(occupant.Faction, sh.Owner)) continue;
                var tiles = Structures.Where(kv => kv.Value.Stronghold == sh.Id).ToList();
                // 성내 칸이 있으면 성내만, 없으면(관문) 성벽·성문 위 수비 부대까지 본다
                var held = tiles.Any(kv => kv.Value.IsInterior) ? tiles.Where(kv => kv.Value.IsInterior) : tiles;
                bool defended = held.Any(kv => UnitAt(kv.Key)?.Faction == sh.Owner);
                if (defended) continue;
                Captures.Add(new CaptureEvent
                {
                    Stronghold = sh.Id, From = sh.Owner, To = occupant.Faction,
                    Officers = occupant.Members.Select(m => m.Id).ToList(),
                });
                Log.Add($"{sh.Name} 함락! {Korean.IGa(occupant.Label)} 정청을 점거했다");
                sh.Owner = occupant.Faction;
            }
        }

        /// <summary>쓸 수 있는 전법. 적성 S=3개, A=2개, B=1개.</summary>
        public IEnumerable<TacticDef> AvailableTactics(Unit u) =>
            u.Formation.Tactics.Take(UnitStats.TacticSlots(u.Aptitude)).Select(id => Data.Tactics[id]);

        public int TacticRange(Unit u, TacticDef t)
        {
            var r = t.Effects.FirstOrDefault(e => e.Type == "range");
            return r != null ? (int)r.Value : u.Formation.Range;
        }

        public bool TacticTargetsStructureOnly(TacticDef t) => t.Effects.Any(e => e.Type == "structure_only");

        /// <summary>전법. 성공하면 대상에 인접한 아군이 연쇄 공격한다.</summary>
        public AttackResult UseTactic(Unit attacker, string tacticId, Unit target)
        {
            EnsureCanAct(attacker);
            var t = AvailableTactics(attacker).FirstOrDefault(x => x.Id == tacticId)
                    ?? throw new InvalidOperationException($"{Korean.EunNeun(attacker.Label)} {tacticId} 전법을 쓸 수 없음 (적성 {attacker.Aptitude})");
            bool selfOnly = t.Power <= 0;
            bool onWall = t.Effects.Any(e => e.Type == "target_on_wall");
            if (t.Effects.Any(e => e.Type == "structure_only"))
                throw new InvalidOperationException($"{Korean.EunNeun(t.Name.Ko)} 성벽·성문 전용 (UseTacticOnStructure)");
            if (onWall && !(Structures.TryGetValue(target.Position, out var ws) && (ws.Kind == StructureKind.Wall || ws.Kind == StructureKind.Gate) && ws.Blocking))
                throw new InvalidOperationException($"{Korean.EunNeun(t.Name.Ko)} 성벽 위 부대에만 쓸 수 있음");
            if (attacker.Energy < t.EnergyCost)
                throw new InvalidOperationException($"기력 부족: {attacker.Energy}/{t.EnergyCost}");
            if (!selfOnly && (!Hostile(attacker.Faction, target.Faction) || !target.IsAlive || !InRange(attacker, target, TacticRange(attacker, t))))
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
                    (i == 0 ? $"{attacker.Label}의 {t.Name.Ko}! → " : "  추가 공격 → ") + $"{target.Label} {{0}} 피해", onWall);

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
                        if (Anchored(target)) break;
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

        /// <summary>성벽 위나 성 안에 자리 잡은 부대는 밀리거나 끌려 나오지 않는다.</summary>
        private bool Anchored(Unit u) => Structures.ContainsKey(u.Position);

        private void Push(Unit attacker, Unit target, int distance)
        {
            if (Anchored(target)) return;
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
        private int DealDamage(Unit attacker, Unit target, double power, int chainLinks, double chainAdd, bool randomize, string line,
                               bool ignoreWallBonus = false)
        {
            var b = Combat.Compute(this, attacker, target, power, chainLinks, chainAdd, randomize, ignoreWallBonus);
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
                if (o.Id.StartsWith("_")) continue; // 이름 없는 수비대장
                var fate = RollFate(o, u, surrounded, onFire);
                if (fate == DefeatOutcome.Capture && by == null) fate = DefeatOutcome.Return; // 잡을 적이 없음
                Fates.Add(new OfficerFate
                {
                    OfficerId = o.Id, Outcome = fate, Faction = u.Faction, By = by?.Faction,
                    Home = u.Home, Garrison = u.IsGarrison, Position = u.Position,
                    ByOfficers = by?.Members.Select(m => m.Id).ToList() ?? new List<string>(),
                });
                Log.Add($"  {o.Name.Ko}: {FateText(fate)}");
            }
            foreach (var ally in Alive.Where(a => a.Faction == u.Faction))
                LoseMorale(ally, Data.Rules.Combat.MoraleLossAllyDestroyed);
            CheckCaptures();
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

        // ------------------------------------------------------------------ 보급

        /// <summary>도시 바깥 둘레 칸 중 적 부대가 선 칸의 비율이 기준 이상이면 포위 (02 맵·도시 설계서 4.2).</summary>
        public bool IsBlockaded(string strongholdId)
        {
            if (!Strongholds.TryGetValue(strongholdId, out var sh) || sh.Owner == null) return false;
            var footprint = new HashSet<HexCoord>(Structures.Where(kv => kv.Value.Stronghold == strongholdId).Select(kv => kv.Key));
            if (footprint.Count == 0) footprint.Add(sh.Center);
            var ring = footprint.SelectMany(h => h.Neighbors()).Where(n => !footprint.Contains(n) && Map.Contains(n))
                                .Where(n => Data.Terrain.TryGetValue(Map.TerrainAt(n), out var t) && t.PassableLand)
                                .Distinct().ToList();
            if (ring.Count == 0) return false;
            int enemies = ring.Count(n => UnitAt(n) is Unit u && Hostile(u.Faction, sh.Owner));
            return enemies >= ring.Count * Data.Rules.Siege.BlockadeRatio;
        }

        /// <summary>아군 영역 안이고 그 영역의 도시가 포위되지 않았으면 보급 상태.</summary>
        public bool IsSupplied(Unit u)
        {
            if (TerritoryOf == null) return true;
            string? city = TerritoryOf(u.Position);
            if (city == null || !Strongholds.TryGetValue(city, out var sh)) return false;
            return sh.Owner == u.Faction && !IsBlockaded(city);
        }

        /// <summary>원정 중인 부대는 매 턴 휴대 군량을 쓴다. 떨어지면 사기 하락과 탈영.</summary>
        private void ConsumeFood(Unit u)
        {
            if (u.Food < 0 || IsSupplied(u)) return;
            var eco = Data.Rules.Economy;
            int use = (int)Math.Ceiling(u.Troops * eco.FoodPerSoldierMonth / Data.Rules.Time.TurnsPerMonth);
            if (u.Food >= use)
            {
                u.Food -= use;
                return;
            }
            u.Food = 0;
            int deserters = Math.Max(1, (int)(u.Troops * eco.Starvation.Desertion));
            u.Troops -= deserters;
            Log.Add($"{u.Label} 군량 부족! 병사 {deserters}명 탈영");
            LoseMorale(u, eco.Starvation.MoraleLoss * 2);
            if (u.Troops <= 0) Destroy(u, null);
        }

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
                        foreach (var o in u.Members.Where(m => !m.Id.StartsWith("_")))
                            Fates.Add(new OfficerFate
                            {
                                OfficerId = o.Id, Outcome = DefeatOutcome.Return, Faction = u.Faction,
                                Home = u.Home, Garrison = u.IsGarrison, Position = u.Position,
                            });
                        continue;
                    }
                }

                u.Energy = Math.Min(Data.Rules.Energy.Max, u.Energy + Data.Rules.Energy.RegenPerTurn);
                ConsumeFood(u);
                if (!u.IsAlive) continue;
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
            // 전장에서 빠진 부대는 목록에서 정리한다 (긴 게임에서 탐색이 느려지지 않게)
            Units.RemoveAll(u => !u.IsAlive);
            Map.TickFire();
            if (WeatherId == "rain")
                foreach (var h in Map.BurningCells) Map.Extinguish(h);
            Round++;
        }

        public IEnumerable<string> FactionsAlive => Alive.Select(u => u.Faction).Distinct();
    }
}
