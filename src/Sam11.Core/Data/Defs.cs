using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Sam11.Core.Data
{
    // JSON 파일(data/base)과 1:1로 대응하는 정의 클래스.
    // 필드 이름은 snake_case JSON 키와 자동으로 매칭된다 (GameData 참고).

    public enum AptitudeRank { C = 0, B = 1, A = 2, S = 3 }

    public sealed class LocalizedText
    {
        public string Ko { get; set; } = "";
        public string? Zh { get; set; }
        public override string ToString() => Ko;
    }

    public sealed class StatBlock
    {
        public int Ldr { get; set; }
        public int War { get; set; }
        public int Int { get; set; }
        public int Pol { get; set; }
        public int Cha { get; set; }
    }

    public sealed class HiddenStats
    {
        public int Loyalty { get; set; }
        public int Ambition { get; set; }
        public int Compat { get; set; }
    }

    public sealed class OfficerDef
    {
        public string Id { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        public string Gender { get; set; } = "male";
        public int Birth { get; set; }
        public int? Death { get; set; }
        public StatBlock Stats { get; set; } = new StatBlock();
        public HiddenStats Hidden { get; set; } = new HiddenStats();
        public Dictionary<string, AptitudeRank> Aptitude { get; set; } = new Dictionary<string, AptitudeRank>();
        public List<string> Traits { get; set; } = new List<string>();
        public KinDef? Kin { get; set; }
        public RulerPersonality? RulerPersonality { get; set; }

        public AptitudeRank AptitudeFor(string formationId) =>
            Aptitude.TryGetValue(formationId, out var r) ? r : AptitudeRank.C;

        public override string ToString() => Name.Ko;
    }

    public sealed class KinDef
    {
        public string? Father { get; set; }
        public string? Mother { get; set; }
        public List<string> Spouse { get; set; } = new List<string>();
        public List<string> Siblings { get; set; } = new List<string>();
    }

    /// <summary>AI 군주 성향값 (06 AI 설계서 6.2). 각 1~10.</summary>
    public sealed class RulerPersonality
    {
        public int Aggression { get; set; } = 5;
        public int Faith { get; set; } = 5;
        public int Domestic { get; set; } = 5;
        public int Diplomacy { get; set; } = 5;
    }

    public sealed class EffectDef
    {
        public string Type { get; set; } = "";
        public string? Stat { get; set; }
        public string Op { get; set; } = "mul";
        public double Value { get; set; }
        public JObject? When { get; set; }

        /// <summary>효과의 나머지 필드 (전법 효과처럼 형식이 다양한 것).</summary>
        [Newtonsoft.Json.JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();

        public int ExtraInt(string key, int fallback = 0) =>
            Extra.TryGetValue(key, out var t) ? t.Value<int>() : fallback;

        public double ExtraDouble(string key, double fallback = 0) =>
            Extra.TryGetValue(key, out var t) ? t.Value<double>() : fallback;

        public string? ExtraString(string key) =>
            Extra.TryGetValue(key, out var t) ? t.Value<string>() : null;
    }

    public sealed class TraitDef
    {
        public string Id { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public List<EffectDef> Effects { get; set; } = new List<EffectDef>();
    }

    public sealed class FormationTraitDef
    {
        public string Id { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public sealed class FormationDef
    {
        public string Id { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        public string Category { get; set; } = "base";
        public string? RequiredEquipment { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int Move { get; set; }
        public FormationTraitDef Trait { get; set; } = new FormationTraitDef();
        public List<string> StrongAgainst { get; set; } = new List<string>();
        public List<string> Tactics { get; set; } = new List<string>();
        public Dictionary<string, int> TerrainModifiers { get; set; } = new Dictionary<string, int>();
        public bool UsableInCity { get; set; }
        public bool WaterOnly { get; set; }
        public int Range { get; set; } = 1;
        public int? StrongholdAttack { get; set; }

        public bool IsRanged => Range > 1;
        public override string ToString() => Name.Ko;
    }

    public sealed class TacticDef
    {
        public string Id { get; set; } = "";
        public string Formation { get; set; } = "";
        public AptitudeRank RequiredRank { get; set; }
        public LocalizedText Name { get; set; } = new LocalizedText();
        public int EnergyCost { get; set; }
        public double Power { get; set; }
        public string Description { get; set; } = "";
        public List<EffectDef> Effects { get; set; } = new List<EffectDef>();
        public override string ToString() => Name.Ko;
    }

    public sealed class TerrainDef
    {
        public string Id { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        public int? MoveCost { get; set; }
        public bool PassableLand { get; set; }
        public bool PassableWater { get; set; }
        public bool HighGround { get; set; }
        public bool Ambush { get; set; }
        public double DefenseMod { get; set; } = 1.0;
    }

    public sealed class WeatherDef
    {
        public string Id { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        public List<EffectDef> Effects { get; set; } = new List<EffectDef>();
    }

    public sealed class RankDef
    {
        public string Id { get; set; } = "";
        public int Tier { get; set; }
        public LocalizedText Name { get; set; } = new LocalizedText();
        public int TroopBonus { get; set; }
    }
}
