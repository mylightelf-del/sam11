using System;
using System.Linq;
using Sam11.Core.Data;

namespace Sam11.Core.Battle
{
    /// <summary>
    /// 피해 계산 (04 군사·전투 설계서 2장).
    /// 최종 피해 = 기본 피해 × 진형 고유 보정 × 상성 × 지형 × 날씨·특기 × 사기 × 전법·연쇄 × 난수
    /// </summary>
    public static class Combat
    {
        public sealed class Breakdown
        {
            public double Base;
            public double Formation;
            public double Affinity;
            public double Terrain;
            public double Traits;
            public double Morale;
            public double Power;
            public double Chain;
            public double Random;
            public double Total => Base * Formation * Affinity * Terrain * Traits * Morale * Power * Chain * Random;
        }

        public static Breakdown Compute(Battle battle, Unit attacker, Unit defender, double power, int chainLinks,
                                        double chainBonusAdd, bool randomize, bool ignoreWallBonus = false)
        {
            var data = battle.Data;
            var c = data.Rules.Combat;
            var b = new Breakdown();

            // 기본 피해: 싸우는 병력 × 무력 대 통솔 비율.
            // 싸우는 병력은 지휘관이 이끌 수 있는 만큼까지다 (수비대처럼 병력이 지휘 한도를 넘으면 나머지는 버티는 몫)
            double ratio = (attacker.EffWar + c.StatSmoothing) / (defender.EffLdr + c.StatSmoothing);
            b.Base = attacker.FightingTroops * c.BaseRatio * ratio;

            // 진형 고유 보정: 공격측 공격치 / 방어측 방어치
            b.Formation = (attacker.Formation.Attack / 100.0) / (defender.Formation.Defense / 100.0);

            b.Affinity = data.AffinityMultiplier(attacker.Formation.Id, defender.Formation.Id);

            // 지형 보정: 공격측 진형의 자기 칸 보정 / 방어측 진형의 자기 칸 보정
            string atkTerrain = battle.Map.TerrainAt(attacker.Position);
            string defTerrain = battle.Map.TerrainAt(defender.Position);
            double atkT = TerrainMod(attacker.Formation, atkTerrain);
            double defT = TerrainMod(defender.Formation, defTerrain);
            if (battle.TerrainIgnoresPenalty(attacker, atkTerrain)) atkT = Math.Max(1.0, atkT);
            if (battle.TerrainIgnoresPenalty(defender, defTerrain)) defT = Math.Max(1.0, defT);
            if (ignoreWallBonus && (defTerrain == "city_wall" || defTerrain == "gate")) defT = Math.Min(1.0, defT);
            b.Terrain = atkT / defT;
            if (data.Terrain.TryGetValue(atkTerrain, out var at) && at.HighGround
                && !(data.Terrain.TryGetValue(defTerrain, out var dt) && dt.HighGround))
                b.Terrain *= 1 + data.Rules.HighGroundBonus;
            if (data.Terrain.TryGetValue(defTerrain, out var dtt))
                b.Terrain /= dtt.DefenseMod;

            // 특기·날씨: (1 + 공격 보정) / (1 + 방어 보정), 시한 효과(견수 등) 포함
            var weather = battle.CurrentWeather;
            var atkCtx = new Modifiers.Context { Unit = attacker, Terrain = atkTerrain, Weather = battle.WeatherId };
            var defCtx = new Modifiers.Context { Unit = defender, Terrain = defTerrain, Weather = battle.WeatherId };
            double atkMod = Modifiers.SumMul(Modifiers.Collect(attacker, weather), "attack", atkCtx);
            double defMod = Modifiers.SumMul(Modifiers.Collect(defender, weather), "defense", defCtx)
                            + defender.TimedValue("defense");
            // 성내 시가에서 싸우는 수비측 방어 보정
            if (battle.Structures.TryGetValue(defender.Position, out var st) && st.Kind == StructureKind.Street
                && battle.OwnerOf(st) == defender.Faction)
                defMod += battle.Data.Rules.City.StreetDefenseBonus;
            // 학익 고유 특성 "포위": 대상에 인접한 아군 하나당 +5% (익전 특기는 2배)
            if (attacker.Formation.Trait.Id == "encircle")
            {
                int allies = battle.AdjacentUnits(defender.Position).Count(u => u.Faction == attacker.Faction && u != attacker);
                double encircle = 0.05 * (1 + Modifiers.SumMul(Modifiers.Collect(attacker, null), "encircle_bonus", atkCtx));
                atkMod += allies * encircle;
            }
            b.Traits = Math.Max(0.1, 1 + atkMod) / Math.Max(0.1, 1 + defMod);

            b.Morale = data.Rules.Morale.MultiplierFor(attacker.Morale);
            b.Power = power;
            double chain = chainLinks * data.Rules.Chain.BonusPerLink + (chainLinks > 0 ? chainBonusAdd : 0);
            b.Chain = 1 + Math.Min(chain, data.Rules.Chain.MaxBonus + chainBonusAdd);
            b.Random = randomize ? battle.Rng.Range(c.RandomMin, c.RandomMax) : 1.0;
            return b;
        }

        /// <summary>진형의 지형 보정. 성문 위는 성벽 위와 같이 취급한다.</summary>
        public static double TerrainMod(FormationDef formation, string terrain)
        {
            if (terrain == "gate") terrain = "city_wall";
            return formation.TerrainModifiers.TryGetValue(terrain, out var v) ? v / 100.0 : 1.0;
        }

        /// <summary>
        /// 전법 성공률: 기본 + (공격측 무력 − 방어측 통솔) + 상성 + 특기.
        /// 전법은 군사 기술이라 무력·통솔로 겨루고, 계략(05 설계서)은 지력으로 겨룬다.
        /// </summary>
        public static double TacticSuccessChance(Battle battle, Unit attacker, Unit defender)
        {
            var c = battle.Data.Rules.Combat;
            double p = c.TacticBaseSuccess + (attacker.EffWar - defender.EffLdr) / c.TacticStatDivisor;
            if (battle.Data.IsStrongAgainst(attacker.Formation.Id, defender.Formation.Id)) p += c.TacticAffinityShift;
            else if (battle.Data.IsStrongAgainst(defender.Formation.Id, attacker.Formation.Id)) p -= c.TacticAffinityShift;
            var ctx = new Modifiers.Context { Unit = attacker, Terrain = battle.Map.TerrainAt(attacker.Position), Weather = battle.WeatherId };
            p += Modifiers.SumMul(Modifiers.Collect(attacker, null), "tactic_success", ctx);
            return Math.Max(c.TacticSuccessMin, Math.Min(c.TacticSuccessMax, p));
        }
    }
}
