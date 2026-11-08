namespace Sam11.Core.AI
{
    public enum Difficulty { Novice, Normal, Hard, Hell }

    /// <summary>난이도별 AI 판단 수준 (06 AI 설계서 5장).</summary>
    public sealed class AiConfig
    {
        public Difficulty Level = Difficulty.Normal;
        /// <summary>공격 전에 모아야 하는 전력비 (목표 추정 수비력 대비).</summary>
        public double ConcentrationRatio = 1.5;
        /// <summary>연쇄를 노려 공격 순서를 정하는지.</summary>
        public bool ChainOrdering = true;
        /// <summary>진형을 고를 때 상성·지형을 따지는지 (입문은 가끔 무시).</summary>
        public double FormationBlunder;
        public bool Retreats = true;
        public double IncomeBonus;

        public static AiConfig For(Difficulty d) => d switch
        {
            Difficulty.Novice => new AiConfig { Level = d, ConcentrationRatio = 0.8, ChainOrdering = false, FormationBlunder = 0.3, Retreats = false },
            Difficulty.Normal => new AiConfig { Level = d, ConcentrationRatio = 1.5, ChainOrdering = false },
            Difficulty.Hard => new AiConfig { Level = d, ConcentrationRatio = 1.5, ChainOrdering = true },
            _ => new AiConfig { Level = d, ConcentrationRatio = 1.5, ChainOrdering = true, IncomeBonus = 0.15 },
        };
    }
}
