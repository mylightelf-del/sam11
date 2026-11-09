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
        /// <summary>동시에 벌이는 원정 수 배율 (입문은 적게).</summary>
        public double CampaignFactor = 1;
        /// <summary>내정에서 할 일을 놓치는 무장 비율 (입문).</summary>
        public double IdleOfficers;
        /// <summary>전략 계략(유언비어·이간·매수·내응·첩보)을 쓰는지.</summary>
        public bool StrategicMissions = true;
        /// <summary>플레이어 세력 거점을 노리는 정도 (원정 목표 점수 배율).</summary>
        public double PlayerFocus = 1;

        /// <summary>
        /// 난이도 (06 설계서 5장: 판단의 질이 주, 자원 보정은 양 끝에서 조금).
        /// 입문: 원정이 적고, 무장이 놀고, 계략을 안 쓰고, 진형을 잘못 고르고, 물러나지 않고, 수입 −20%.
        /// 어려움: 연쇄를 노리고 플레이어를 더 노리며, 수입 +5%. 지옥: 플레이어를 크게 노리고 수입 +15%.
        /// 측정 (06 설계서 9.4.7): 플레이어 대역 세력의 10년 뒤 평균 도시가 난이도를 따라 줄어든다.
        /// </summary>
        public static AiConfig For(Difficulty d) => d switch
        {
            Difficulty.Novice => new AiConfig { Level = d, ConcentrationRatio = 1.5, ChainOrdering = false, FormationBlunder = 0.3, Retreats = false,
                                                CampaignFactor = 0.5, IdleOfficers = 0.35, StrategicMissions = false, IncomeBonus = -0.2 },
            Difficulty.Normal => new AiConfig { Level = d, ConcentrationRatio = 1.5, ChainOrdering = false },
            Difficulty.Hard => new AiConfig { Level = d, ConcentrationRatio = 1.5, ChainOrdering = true, PlayerFocus = 1.5, IncomeBonus = 0.05 },
            _ => new AiConfig { Level = d, ConcentrationRatio = 1.5, ChainOrdering = true, IncomeBonus = 0.15, PlayerFocus = 1.8 },
        };
    }
}
