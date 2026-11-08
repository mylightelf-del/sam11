using System.Collections.Generic;

namespace Sam11.Core.Data
{
    public sealed class HexRef
    {
        public int Q { get; set; }
        public int R { get; set; }
        public Common.HexCoord ToHex() => new Common.HexCoord(Q, R);
    }

    /// <summary>data/base/strongholds.json</summary>
    public sealed class StrongholdDef
    {
        public string Id { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        public string Type { get; set; } = "";
        public string Province { get; set; } = "";
        public int FacilitySlots { get; set; }
        public List<string> Links { get; set; } = new List<string>();
        public HexRef? Hex { get; set; }

        public bool IsCity => Type == "major_city" || Type == "city";
        public override string ToString() => Name.Ko;
    }

    public sealed class AdjacencyDef
    {
        public string Target { get; set; } = "";
        public string Stat { get; set; } = "";
        public double Value { get; set; }
    }

    public sealed class FacilityCost
    {
        public List<int> Gold { get; set; } = new List<int>();
        public List<int> BuildTurns { get; set; } = new List<int>();
    }

    /// <summary>data/base/facilities.json</summary>
    public sealed class FacilityDef
    {
        public string Id { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        public string Category { get; set; } = "";
        public int MaxLevel { get; set; }
        public string Description { get; set; } = "";
        public Dictionary<string, List<double>> PerLevel { get; set; } = new Dictionary<string, List<double>>();
        public List<AdjacencyDef> Adjacency { get; set; } = new List<AdjacencyDef>();
        public FacilityCost Cost { get; set; } = new FacilityCost();
        public bool RequiresWaterside { get; set; }

        /// <summary>level(1부터)에서의 stat 값. 정의가 없으면 0.</summary>
        public double ValueAt(string stat, int level)
        {
            if (level <= 0 || !PerLevel.TryGetValue(stat, out var list) || list.Count == 0)
                return 0;
            return list[System.Math.Min(level, list.Count) - 1];
        }

        public override string ToString() => Name.Ko;
    }

    public sealed class TaxLevel
    {
        public double Gold { get; set; } = 1.0;
        public int MoraleMonthly { get; set; }
    }

    public sealed class MoraleIncomeBand
    {
        public int Min { get; set; }
        public double Mult { get; set; } = 1.0;
        public bool Rebellion { get; set; }
    }

    public sealed class DomesticRules
    {
        public Dictionary<string, TaxLevel> Tax { get; set; } = new Dictionary<string, TaxLevel>();
        public List<MoraleIncomeBand> MoraleIncome { get; set; } = new List<MoraleIncomeBand>();
    }

    public sealed class EconomyRules
    {
        public double TaxPerCapitaGold { get; set; }
        public double HarvestPerCapita { get; set; }
        public double FoodPerSoldierMonth { get; set; }
        public double PopGrowthBase { get; set; }
        public double PopGrowthPerMorale { get; set; }
        public Dictionary<string, int> PopCap { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> FoodCap { get; set; } = new Dictionary<string, int>();
        public int MoraleDriftTarget { get; set; } = 50;
        public int MoraleDriftRate { get; set; } = 1;
        public StarvationRules Starvation { get; set; } = new StarvationRules();
        public RecruitRules Recruit { get; set; } = new RecruitRules();
        public TrainRules Train { get; set; } = new TrainRules();
        public PatrolRules Patrol { get; set; } = new PatrolRules();
        public ProduceRules Produce { get; set; } = new ProduceRules();
        public CharityRules Charity { get; set; } = new CharityRules();
        public TradeRules Trade { get; set; } = new TradeRules();
        public int MeritPerCommand { get; set; }
        public RearSupplyRules RearSupply { get; set; } = new RearSupplyRules();
    }

    public sealed class RearSupplyRules
    {
        public int TargetMonths { get; set; } = 3;
        public int CityReserveMonths { get; set; } = 3;
        /// <summary>중계 거점(관문 등)이 남기는 몫. 목표보다 작아야 앞쪽으로 전달된다.</summary>
        public int PostReserveMonths { get; set; } = 2;
    }
    public sealed class StarvationRules { public double Desertion { get; set; } public int MoraleLoss { get; set; } }
    public sealed class RecruitRules
    {
        public int Base { get; set; }
        public int StatFactor { get; set; }
        public double PopRatioCap { get; set; }
        public double GoldPerSoldier { get; set; }
        public int MoraleLoss { get; set; }
        /// <summary>도시가 보유할 수 있는 병력 상한 = 인구 × 이 비율.</summary>
        public double MaxTroopRatio { get; set; } = 0.15;
    }
    public sealed class TrainRules { public int Base { get; set; } public int LdrDivisor { get; set; } = 10; }
    public sealed class PatrolRules { public int Base { get; set; } public int StatDivisor { get; set; } = 40; }
    public sealed class ProduceRules
    {
        public int PolDivisor { get; set; } = 200;
        public Dictionary<string, double> GoldPerUnit { get; set; } = new Dictionary<string, double>();
    }
    public sealed class CharityRules { public double FoodRatio { get; set; } public int Base { get; set; } public int ChaDivisor { get; set; } = 20; }
    public sealed class TradeRules
    {
        public double FoodPerGold { get; set; }
        public List<double> MonthlyFactor { get; set; } = new List<double>();
        /// <summary>한 번에 사고팔 수 있는 군량 = 도시 인구 × 이 비율 (지역 시장의 한계).</summary>
        public double MaxFoodRatio { get; set; } = 0.05;
    }
}
