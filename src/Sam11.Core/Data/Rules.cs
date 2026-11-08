using System.Collections.Generic;

namespace Sam11.Core.Data
{
    /// <summary>data/base/rules.json — 밸런스 상수.</summary>
    public sealed class Rules
    {
        public TimeRules Time { get; set; } = new TimeRules();
        public AffinityRules Affinity { get; set; } = new AffinityRules();
        public UnitRules Unit { get; set; } = new UnitRules();
        public ChainRules Chain { get; set; } = new ChainRules();
        public EnergyRules Energy { get; set; } = new EnergyRules();
        public MoraleRules Morale { get; set; } = new MoraleRules();
        public Dictionary<string, double> Defeat { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, Dictionary<string, double>> DefeatModifiers { get; set; } = new Dictionary<string, Dictionary<string, double>>();
        public double HighGroundBonus { get; set; }
        public CombatRules Combat { get; set; } = new CombatRules();
        public EconomyRules Economy { get; set; } = new EconomyRules();
        public SiegeRules Siege { get; set; } = new SiegeRules();
        public CityRules City { get; set; } = new CityRules();
        public DomesticRules Domestic { get; set; } = new DomesticRules();
    }

    public sealed class TimeRules
    {
        public int DaysPerTurn { get; set; } = 10;
        public int TurnsPerMonth { get; set; } = 3;
        public int HarvestMonth { get; set; } = 9;
    }

    public sealed class CityRules
    {
        public double StreetDefenseBonus { get; set; } = 0.1;
        public double IsolatedOutpostTroopLoss { get; set; }
    }

    public sealed class SiegeRules
    {
        /// <summary>구조물 피해 배율. 성벽은 부대보다 훨씬 단단하다.</summary>
        public double StructureDamageRatio { get; set; } = 0.3;
        /// <summary>구조물의 방어 능력치 (부대의 통솔 자리에 들어간다).</summary>
        public int StructureDefenseStat { get; set; } = 70;
        public int CaptureMoraleLoss { get; set; } = 20;
        /// <summary>도시 바깥 둘레 칸 중 이 비율 이상을 적이 점거하면 포위 (보급 차단).</summary>
        public double BlockadeRatio { get; set; } = 0.5;
        public List<string> GarrisonFormationPreference { get; set; } = new List<string>();
    }

    public sealed class AffinityRules
    {
        public double Advantage { get; set; } = 1.2;
        public double Disadvantage { get; set; } = 0.8;
    }

    public sealed class UnitRules
    {
        public int MaxOfficers { get; set; } = 3;
        public double SubOfficerBonusRatio { get; set; } = 0.2;
        public int StatCap { get; set; } = 100;
        public int TroopsPerLeadership { get; set; } = 100;
    }

    public sealed class ChainRules
    {
        public double BonusPerLink { get; set; } = 0.1;
        public double MaxBonus { get; set; } = 0.5;
    }

    public sealed class EnergyRules
    {
        public int Max { get; set; } = 100;
        public int RegenPerTurn { get; set; } = 5;
        public int RegenNearOwnStronghold { get; set; } = 10;
    }

    public sealed class MoraleBand
    {
        public int Min { get; set; }
        public double Mult { get; set; } = 1.0;
        public string? Status { get; set; }
    }

    public sealed class MoraleRules
    {
        public int Max { get; set; } = 100;
        public List<MoraleBand> Bands { get; set; } = new List<MoraleBand>();
        public int SurroundedThreshold { get; set; } = 3;
        public int RefuseDuelPenalty { get; set; } = 10;
        public int RoutThreshold { get; set; } = 30;

        public double MultiplierFor(int morale)
        {
            foreach (var b in Bands)
                if (morale >= b.Min)
                    return b.Mult;
            return 1.0;
        }
    }

    public sealed class CombatRules
    {
        public double BaseRatio { get; set; }
        public double StatSmoothing { get; set; }
        public double RandomMin { get; set; }
        public double RandomMax { get; set; }
        public double CounterRatio { get; set; }
        public double TacticBaseSuccess { get; set; }
        public double TacticStatDivisor { get; set; }
        public double TacticAffinityShift { get; set; }
        public double TacticSuccessMin { get; set; }
        public double TacticSuccessMax { get; set; }
        public double MoraleLossScale { get; set; }
        public int MoraleGainAttack { get; set; }
        public int MoraleGainDestroy { get; set; }
        public int MoraleLossSurrounded { get; set; }
        public int MoraleLossAllyDestroyed { get; set; }
        public double FireDamageRatio { get; set; }
        public int FireTurns { get; set; }
        public double ConfusionRecoverLdrRatio { get; set; }
    }
}
