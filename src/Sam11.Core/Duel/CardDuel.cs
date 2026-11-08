using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Common;
using Sam11.Core.Data;

namespace Sam11.Core.Duel
{
    public enum DuelKind
    {
        /// <summary>설전: 지력 카드, 정치·매력 게이지. 논리 → 감정 → 위세 → 논리.</summary>
        Debate,
        /// <summary>일기토: 무력 카드, 통솔 게이지. 공격 → 견제 → 방어 → 공격.</summary>
        Combat,
    }

    /// <summary>카드 종류. 0 → 1 → 2 → 0 순서로 이긴다. Special은 상성을 무시하고 위력으로 겨룬다.</summary>
    public enum CardType { First = 0, Second = 1, Third = 2, Special = 3 }

    public sealed class Card
    {
        public CardType Type;
        public int Value;
        public override string ToString() => $"{Type}:{Value}";
    }

    public sealed class Duelist
    {
        public OfficerDef Officer = null!;
        public int Gauge;
        public int MaxGauge;
        public List<Card> Hand = new List<Card>();
        public bool SpecialAvailable;
    }

    /// <summary>카드를 고르는 쪽 (AI 또는 플레이어 입력).</summary>
    public interface ICardPicker
    {
        Card Pick(CardDuel duel, Duelist me, Duelist opponent);
    }

    /// <summary>
    /// AI 카드 선택: 상대 카드를 모르므로 세 종류의 기대값은 같다. 그래서 위력이 가장 큰 카드를 내되,
    /// 가끔(20%) 둘째로 센 카드를 내 읽히지 않게 한다. 특수 카드는 상대 기세가 한 방 거리이거나 마지막 합에 쓴다.
    /// </summary>
    public sealed class AiCardPicker : ICardPicker
    {
        public Card Pick(CardDuel duel, Duelist me, Duelist opponent)
        {
            var special = me.Hand.FirstOrDefault(c => c.Type == CardType.Special);
            if (special != null && (opponent.Gauge <= special.Value * duel.Rules.SpecialMultiplier || duel.Round == duel.Rules.Rounds))
                return special;
            var normal = me.Hand.Where(c => c.Type != CardType.Special).OrderByDescending(c => c.Value).ToList();
            if (normal.Count == 0) return me.Hand[0];
            return normal.Count > 1 && duel.Rng.Chance(0.2) ? normal[1] : normal[0];
        }
    }

    public sealed class DuelRules
    {
        public int Rounds { get; set; } = 5;
        public int HandBase { get; set; } = 3;
        public int HandStatStep { get; set; } = 30;
        public int GaugeBase { get; set; } = 20;
        /// <summary>기세 게이지 = 능력치 × 이 비율 + GaugeBase.</summary>
        public double GaugeStatRatio { get; set; } = 0.5;
        public int CardOffset { get; set; } = 40;
        public int CardDivisor { get; set; } = 4;
        public int CardRandom { get; set; } = 4;
        public double SpecialMultiplier { get; set; } = 1.5;
        public double SameKindRatio { get; set; } = 0.5;
        /// <summary>진 쪽 카드도 위력의 이 비율만큼 피해를 막는다 (실력 차가 운보다 크게 드러나게).</summary>
        public double LoserBlockRatio { get; set; } = 0.3;
        public int ChallengeEnergy { get; set; } = 20;
        public int WinMorale { get; set; } = 15;
        public int LoseMorale { get; set; } = 15;
        public Dictionary<string, double> LoserFate { get; set; } = new Dictionary<string, double>();
        public int AcceptMargin { get; set; } = 10;
        public double EmployDebateMinChance { get; set; } = 0.25;
    }

    /// <summary>
    /// 설전·일기토 공통 카드 대결 (05 외교·계략 설계서 4장).
    /// 매 합 양쪽이 동시에 카드 한 장을 낸다. 상성에서 이긴 카드의 위력(진 카드 위력의 30%만큼 막힘)만큼 상대 기세가 깎이고,
    /// 같은 종류끼리면 서로 위력의 절반씩 깎는다. 기세가 0이 되거나 합이 끝나면 남은 기세 비율이 높은 쪽이 이긴다.
    /// </summary>
    public sealed class CardDuel
    {
        public static readonly string[] DebateNames = { "논리", "감정", "위세", "언변" };
        public static readonly string[] CombatNames = { "공격", "견제", "방어", "필살" };

        public DuelKind Kind { get; }
        public DuelRules Rules { get; }
        public Rng Rng { get; }
        public Duelist A { get; }
        public Duelist B { get; }
        public int Round { get; private set; }
        public List<string> Log { get; } = new List<string>();

        public CardDuel(GameData data, Rng rng, DuelKind kind, OfficerDef a, OfficerDef b)
        {
            Kind = kind;
            Rules = data.Rules.Duel;
            Rng = rng;
            A = Make(data, a);
            B = Make(data, b);
        }

        private Duelist Make(GameData data, OfficerDef o)
        {
            int stat = Kind == DuelKind.Combat ? o.Stats.Ldr : (o.Stats.Pol + o.Stats.Cha) / 2;
            int gauge = (int)(stat * Rules.GaugeStatRatio) + Rules.GaugeBase;
            string trait = Kind == DuelKind.Combat ? "peerless" : "eloquence";
            gauge = (int)(gauge * (1 + TraitGauge(data, o)));
            return new Duelist { Officer = o, Gauge = gauge, MaxGauge = gauge, SpecialAvailable = o.Traits.Contains(trait) };
        }

        private double TraitGauge(GameData data, OfficerDef o)
        {
            string stat = Kind == DuelKind.Combat ? "duel_gauge" : "debate_gauge";
            return o.Traits.Where(data.Traits.ContainsKey).SelectMany(t => data.Traits[t].Effects)
                    .Where(e => e.Type == "modifier" && e.Stat == stat).Sum(e => e.Value);
        }

        private int Stat(OfficerDef o) => Kind == DuelKind.Combat ? o.Stats.War : o.Stats.Int;

        public string Name(CardType t) => (Kind == DuelKind.Combat ? CombatNames : DebateNames)[(int)t];

        /// <summary>손패: 기본 3장 + 능력치 30마다 1장. 위력 = (능력치 − 40) ÷ 4 + 0~3 (최소 1).</summary>
        private void Deal(Duelist d)
        {
            int stat = Stat(d.Officer);
            int size = Rules.HandBase + stat / Rules.HandStatStep;
            d.Hand.Clear();
            for (int i = 0; i < size; i++)
                d.Hand.Add(new Card { Type = (CardType)Rng.NextInt(3), Value = BaseValue(stat) + Rng.NextInt(Rules.CardRandom) });
            if (d.SpecialAvailable)
                d.Hand.Add(new Card { Type = CardType.Special, Value = BaseValue(stat) + Rules.CardRandom - 1 });
        }

        private int BaseValue(int stat) => Math.Max(1, (stat - Rules.CardOffset) / Rules.CardDivisor);

        /// <summary>
        /// a가 b를 이기면 1, 지면 -1, 비기면 0. 보통 카드는 상성(0 → 1 → 2 → 0)으로,
        /// 특수 카드가 끼면 상성을 무시하고 위력으로 겨룬다 (같으면 비김).
        /// </summary>
        public static int Beats(CardType a, CardType b, int va = 0, int vb = 0)
        {
            if (a == CardType.Special || b == CardType.Special)
                return a == b ? 0 : Math.Sign(va - vb);
            if (a == b) return 0;
            return ((int)b - (int)a + 3) % 3 == 1 ? 1 : -1;
        }

        /// <summary>대결을 끝까지 진행한다. true면 A가 이겼다.</summary>
        public bool Run(ICardPicker? pickA = null, ICardPicker? pickB = null)
        {
            pickA ??= new AiCardPicker();
            pickB ??= new AiCardPicker();
            for (Round = 1; Round <= Rules.Rounds && A.Gauge > 0 && B.Gauge > 0; Round++)
            {
                Deal(A);
                Deal(B);
                var ca = pickA.Pick(this, A, B);
                var cb = pickB.Pick(this, B, A);
                if (ca.Type == CardType.Special) A.SpecialAvailable = false;
                if (cb.Type == CardType.Special) B.SpecialAvailable = false;
                int va = Power(ca), vb = Power(cb);
                int r = Beats(ca.Type, cb.Type, va, vb);
                if (r > 0) B.Gauge -= Math.Max(1, va - (int)(vb * Rules.LoserBlockRatio));
                else if (r < 0) A.Gauge -= Math.Max(1, vb - (int)(va * Rules.LoserBlockRatio));
                else
                {
                    B.Gauge -= (int)(va * Rules.SameKindRatio);
                    A.Gauge -= (int)(vb * Rules.SameKindRatio);
                }
                Log.Add($"{Round}합: {A.Officer.Name.Ko} {Name(ca.Type)}({va}) 대 {B.Officer.Name.Ko} {Name(cb.Type)}({vb}) → 기세 {Math.Max(0, A.Gauge)} : {Math.Max(0, B.Gauge)}");
            }
            double ra = (double)A.Gauge / A.MaxGauge, rb = (double)B.Gauge / B.MaxGauge;
            return ra > rb || (ra == rb && Rng.Chance(0.5));
        }

        private int Power(Card c) => c.Type == CardType.Special ? (int)(c.Value * Rules.SpecialMultiplier) : c.Value;
    }
}
