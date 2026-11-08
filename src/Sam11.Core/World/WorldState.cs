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

        public string Id => Def.Id;
        public override string ToString() => Def.Name.Ko;
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
