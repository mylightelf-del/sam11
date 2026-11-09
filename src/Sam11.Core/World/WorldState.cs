using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Common;
using Sam11.Core.Data;

namespace Sam11.Core.World
{
    /// <summary>게임 날짜. 1턴 = 10일, 한 달 3턴.</summary>
    public sealed class GameDate
    {
        public int Year;
        public int Month;
        public int Turn;

        public bool IsMonthStart => Turn == 1;

        public void Advance(int turnsPerMonth)
        {
            Turn++;
            if (Turn <= turnsPerMonth) return;
            Turn = 1;
            Month++;
            if (Month <= 12) return;
            Month = 1;
            Year++;
        }

        public override string ToString() => $"{Year}년 {Month}월 {(Turn == 1 ? "상" : Turn == 2 ? "중" : "하")}순";
    }

    public sealed class FactionState
    {
        public string Id = "";
        public string Ruler = "";
        public string Color = "";
        public int Title;
        public string Capital = "";
        public string Tax = "mid";
        public bool Alive = true;
        /// <summary>금 수입 보너스 (지옥 난이도 AI 등).</summary>
        public double IncomeBonus;
        public List<FactionGoal> Goals = new List<FactionGoal>();
    }

    public sealed class FacilityState
    {
        public string FacilityId = "";
        public int Level;
        /// <summary>남은 건설 턴. 0이면 완공 (효과 발휘).</summary>
        public int BuildTurnsLeft;
        public bool Active => BuildTurnsLeft == 0 && Level > 0;
    }

    public sealed class StructureState
    {
        public TileKind Kind;
        public int Durability;
        public int MaxDurability;
        public bool Broken => Durability <= 0;
    }

    /// <summary>거점 하나의 상태. 도시가 아닌 관문·항구 등도 같은 구조를 쓴다.</summary>
    public sealed class CityState
    {
        public StrongholdDef Def = null!;
        public StrongholdPlacement Placement = null!;
        public string? Owner;
        public string? Governor;
        public int Population;
        public int Morale;
        public int Gold;
        public int Food;
        public int Troops;
        public int Training;
        public Dictionary<string, int> Equipment = new Dictionary<string, int>
        {
            ["horse"] = 0, ["crossbow"] = 0, ["siege_engine"] = 0, ["ship"] = 0,
        };
        /// <summary>슬롯 번호 → 시설.</summary>
        public Dictionary<int, FacilityState> Facilities = new Dictionary<int, FacilityState>();
        public Dictionary<HexCoord, StructureState> Structures = new Dictionary<HexCoord, StructureState>();
        /// <summary>전장에 나가 있는 수비대 병력 (월 군량 소비에 포함).</summary>
        public int FieldGarrison;

        public string Id => Def.Id;
        public override string ToString() => Def.Name.Ko;

        public IEnumerable<(FacilitySlot Slot, FacilityState Facility)> ActiveFacilities(string? facilityId = null) =>
            Facilities.Where(kv => kv.Value.Active && (facilityId == null || kv.Value.FacilityId == facilityId))
                      .Select(kv => (Placement.Slots[kv.Key], kv.Value));
    }

    public enum OfficerStatus { Active, Free, Unborn, Captive, Dead }

    public sealed class OfficerState
    {
        public OfficerDef Def = null!;
        public string? Faction;
        public string Location = "";
        public OfficerStatus Status;
        public int Loyalty;
        public string? Rank;
        public int Merit;
        public bool Acted;
        /// <summary>남은 부상 턴. 0보다 크면 출진할 수 없다.</summary>
        public int InjuredTurns;
        /// <summary>포로가 된 뒤 지난 달 수.</summary>
        public int CaptiveMonths;
        /// <summary>포로가 되기 전 세력 (포로 교환에 쓴다).</summary>
        public string? PreviousFaction;

        public string Id => Def.Id;
        public override string ToString() => Def.Name.Ko;
    }

    /// <summary>동맹 (05 외교 설계서). 기한이 지나면 풀린다.</summary>
    public enum PactKind { Alliance, Truce, Coalition }

    public sealed class Alliance
    {
        public PactKind Kind = PactKind.Alliance;
        /// <summary>연합의 대상 세력 (반최강세력 연합일 때).</summary>
        public string? Target;
        public string Name = "";
        public HashSet<string> Members = new HashSet<string>();
        public int UntilYear;
        public int UntilMonth;

        public bool Active(GameDate d) => d.Year < UntilYear || (d.Year == UntilYear && d.Month < UntilMonth);
    }

    /// <summary>전략 단계의 전체 게임 상태.</summary>
    public sealed class WorldState
    {
        public GameData Data { get; }
        public WorldMap Map { get; }
        public Rng Rng { get; }
        public GameDate Date { get; } = new GameDate();
        public Dictionary<string, FactionState> Factions { get; } = new Dictionary<string, FactionState>();
        public Dictionary<string, CityState> Cities { get; } = new Dictionary<string, CityState>();
        public Dictionary<string, OfficerState> Officers { get; } = new Dictionary<string, OfficerState>();
        public List<string> Log { get; } = new List<string>();
        public List<Alliance> Alliances { get; } = new List<Alliance>();
        public GameOptions Options { get; set; } = new GameOptions();

        public List<EventDef> Events { get; } = new List<EventDef>();
        public HashSet<string> EventsDone { get; } = new HashSet<string>();

        /// <summary>보물 → 소유 무장 (null이면 아무도 장착하지 않음).</summary>
        public Dictionary<string, string?> TreasureOwner { get; } = new Dictionary<string, string?>();
        /// <summary>장착되지 않은 보물 → 보관 중인 세력.</summary>
        public Dictionary<string, string> TreasureVault { get; } = new Dictionary<string, string>();

        /// <summary>세력별 신용 (0~100, 기본 50). 약속을 깨면 떨어진다 (05 외교 설계서 2.1).</summary>
        public Dictionary<string, int> Trust { get; } = new Dictionary<string, int>();
        /// <summary>세력 쌍의 우호도 (0~100, 기본 50).</summary>
        public Dictionary<string, int> Friendship { get; } = new Dictionary<string, int>();

        public int TrustOf(string faction) => Trust.TryGetValue(faction, out var t) ? t : 50;

        public void ChangeTrust(string faction, int delta) => Trust[faction] = Math.Max(0, Math.Min(100, TrustOf(faction) + delta));

        private static string PairKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;

        public int FriendshipOf(string a, string b) => Friendship.TryGetValue(PairKey(a, b), out var v) ? v : 50;

        public void ChangeFriendship(string a, string b, int delta) =>
            Friendship[PairKey(a, b)] = Math.Max(0, Math.Min(100, FriendshipOf(a, b) + delta));

        public bool AreAllied(string a, string b) =>
            a == b || Alliances.Any(x => x.Kind != PactKind.Truce && x.Active(Date) && x.Members.Contains(a) && x.Members.Contains(b));

        public List<Mission> Missions { get; } = new List<Mission>();
        /// <summary>내응 약속: 도시 → 약속받은 세력.</summary>
        public Dictionary<string, string> Insiders { get; } = new Dictionary<string, string>();
        /// <summary>첩보: (세력, 도시) → 유효한 마지막 달 (연 × 12 + 월).</summary>
        public Dictionary<(string, string), int> SpyReports { get; } = new Dictionary<(string, string), int>();

        public bool HasSpyReport(string faction, string city) =>
            SpyReports.TryGetValue((faction, city), out var until) && Date.Year * 12 + Date.Month <= until;

        /// <summary>종속국 → 종주국.</summary>
        public Dictionary<string, string> Vassals { get; } = new Dictionary<string, string>();

        /// <summary>서로 싸우지 않는 사이: 같은 세력, 동맹·연합, 정전, 종속 관계.</summary>
        public bool AtPeace(string a, string b) =>
            a == b
            || Alliances.Any(x => x.Active(Date) && x.Members.Contains(a) && x.Members.Contains(b))
            || (Vassals.TryGetValue(a, out var sa) && sa == b) || (Vassals.TryGetValue(b, out var sb) && sb == a);

        public Alliance AddPact(PactKind kind, string a, string b, int months, string name = "")
        {
            int total = Date.Year * 12 + (Date.Month - 1) + months;
            var p = new Alliance { Kind = kind, Name = name, UntilYear = total / 12, UntilMonth = total % 12 + 1 };
            p.Members.Add(a);
            p.Members.Add(b);
            Alliances.Add(p);
            return p;
        }

        public string NameOf(string factionId) =>
            Factions.TryGetValue(factionId, out var f) && Officers.TryGetValue(f.Ruler, out var r) ? r.Def.Name.Ko : factionId;

        public WorldState(GameData data, WorldMap map, ulong seed)
        {
            Data = data;
            Map = map;
            Rng = new Rng(seed);
        }

        public IEnumerable<OfficerState> OfficersAt(string cityId) =>
            Officers.Values.Where(o => o.Location == cityId && o.Status == OfficerStatus.Active);

        public IEnumerable<CityState> CitiesOf(string faction) => Cities.Values.Where(c => c.Owner == faction);
    }
}
