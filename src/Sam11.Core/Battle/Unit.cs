using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Common;
using Sam11.Core.Data;

namespace Sam11.Core.Battle
{
    public enum UnitStatus { Ready, Confused, Destroyed, Routed, Withdrawn }

    /// <summary>일정 턴 동안만 걸리는 효과 (견수의 방어 증가, 고수의 연쇄 무효 등).</summary>
    public sealed class TimedEffect
    {
        public string Key = "";
        public double Value;
        public int TurnsLeft;
    }

    /// <summary>
    /// 전장의 부대. 주장 1 + 부장 최대 2 (04 군사·전투 설계서 6장).
    /// </summary>
    public sealed class Unit
    {
        public int Id { get; }
        public string Faction { get; }
        public OfficerDef Leader { get; }
        public IReadOnlyList<OfficerDef> Subs { get; }
        public FormationDef Formation { get; private set; }
        public HexCoord Position { get; set; }
        public int Troops { get; set; }
        public int MaxTroops { get; }

        /// <summary>실제로 칼을 맞대는 병력: 지휘 한도(MaxTroops)를 넘는 병력은 공격에 보태지 못한다.</summary>
        public int FightingTroops => Math.Min(Troops, MaxTroops);
        public int Energy { get; set; }
        public int Morale { get; set; }

        /// <summary>휴대 군량. 0 미만이면 보급을 따지지 않는다 (단독 전투 시험용).</summary>
        public int Food { get; set; } = -1;
        public Dictionary<string, int> Equipment { get; } = new Dictionary<string, int>();
        /// <summary>출진한 거점 (귀환할 곳).</summary>
        public string? Home { get; set; }
        /// <summary>거점 수비대 (Home 거점의 병력이 잠시 전장에 나와 있는 것).</summary>
        public bool IsGarrison { get; set; }
        public UnitStatus Status { get; set; } = UnitStatus.Ready;
        public int TurnsSinceSortie { get; set; }
        public bool HasActed { get; set; }
        public List<TimedEffect> TimedEffects { get; } = new List<TimedEffect>();

        public int EffLdr { get; }
        public int EffWar { get; }
        public int EffInt { get; }
        public AptitudeRank Aptitude { get; private set; }

        /// <summary>부대원 전원의 특기 (중복 제거).</summary>
        public IReadOnlyList<TraitDef> Traits { get; }

        public IEnumerable<OfficerDef> Members
        {
            get
            {
                yield return Leader;
                foreach (var s in Subs)
                    yield return s;
            }
        }

        public bool IsAlive => Status == UnitStatus.Ready || Status == UnitStatus.Confused;

        internal Unit(int id, string faction, OfficerDef leader, IReadOnlyList<OfficerDef> subs, FormationDef formation,
                      int troops, int maxTroops, int energy, int morale, GameData data)
        {
            Id = id;
            Faction = faction;
            Leader = leader;
            Subs = subs;
            Formation = formation;
            Troops = troops;
            MaxTroops = maxTroops;
            Energy = energy;
            Morale = morale;
            var rules = data.Rules.Unit;
            EffLdr = UnitStats.Combine(leader.Stats.Ldr, subs.Select(s => s.Stats.Ldr), rules);
            EffWar = UnitStats.Combine(leader.Stats.War, subs.Select(s => s.Stats.War), rules);
            EffInt = UnitStats.Combine(leader.Stats.Int, subs.Select(s => s.Stats.Int), rules);
            Aptitude = UnitStats.BestAptitude(Members, formation.Id);
            Traits = Members.SelectMany(o => o.Traits).Distinct()
                            .Where(data.Traits.ContainsKey).Select(t => data.Traits[t]).ToList();
        }

        /// <summary>진형 변경 (성내 진입 시 장비 진형 → 기본 진형).</summary>
        internal void SetFormation(FormationDef f)
        {
            Formation = f;
            Aptitude = UnitStats.BestAptitude(Members, f.Id);
        }

        public bool HasTimed(string key) => TimedEffects.Any(e => e.Key == key && e.TurnsLeft > 0);

        public double TimedValue(string key) => TimedEffects.Where(e => e.Key == key && e.TurnsLeft > 0).Sum(e => e.Value);

        public string Label => Subs.Count == 0 ? Leader.Name.Ko : $"{Leader.Name.Ko} 부대";

        public override string ToString() => $"{Label}[{Formation.Name.Ko} {Troops}명]";
    }

    /// <summary>부대 능력치·적성 계산 규칙.</summary>
    public static class UnitStats
    {
        /// <summary>
        /// 부대 능력치 = 주장 + Σ max(0, 부장 − 주장) × 0.2, 상한 100.
        /// 부장은 주장보다 높은 능력만 일부 메운다.
        /// </summary>
        public static int Combine(int leader, IEnumerable<int> subs, UnitRules rules)
        {
            double v = leader + subs.Sum(s => Math.Max(0, s - leader) * rules.SubOfficerBonusRatio);
            return Math.Min(rules.StatCap, (int)Math.Round(v, MidpointRounding.AwayFromZero));
        }

        /// <summary>진형 적성은 부대원 중 최고 등급.</summary>
        public static AptitudeRank BestAptitude(IEnumerable<OfficerDef> members, string formationId) =>
            members.Max(o => o.AptitudeFor(formationId));

        /// <summary>최대 병력 = 주장 통솔 × 100 + 관직 보너스.</summary>
        public static int MaxTroops(OfficerDef leader, RankDef? rank, UnitRules rules) =>
            leader.Stats.Ldr * rules.TroopsPerLeadership + (rank?.TroopBonus ?? 0);

        /// <summary>적성 등급별 사용 가능한 전법 수: S=3, A=2, B=1, C=0.</summary>
        public static int TacticSlots(AptitudeRank rank) => (int)rank;
    }
}
