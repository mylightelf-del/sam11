using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sam11.Core.Battle;
using Sam11.Core.Common;
using Sam11.Core.Data;

namespace Sam11.Core.World
{
    public enum TileKind { Center, Street, Wall, Gate }

    public sealed class FacilitySlot
    {
        public int Index;
        public HexCoord Hex;
        public bool Waterside;
    }

    /// <summary>거점 하나가 맵 위에 차지한 칸들 (02 맵·도시 설계서 3.2).</summary>
    public sealed class StrongholdPlacement
    {
        public string Id = "";
        public HexCoord Center;
        public Dictionary<HexCoord, TileKind> Tiles = new Dictionary<HexCoord, TileKind>();
        public string? TerritoryOf;
        public List<FacilitySlot> Slots = new List<FacilitySlot>();
    }

    /// <summary>data/map/world.json — 전국 헥스 맵.</summary>
    public sealed class WorldMap
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public Dictionary<HexCoord, string> Terrain { get; } = new Dictionary<HexCoord, string>();
        public Dictionary<HexCoord, string> Territory { get; } = new Dictionary<HexCoord, string>();
        public Dictionary<string, StrongholdPlacement> Strongholds { get; } = new Dictionary<string, StrongholdPlacement>();

        /// <summary>어느 칸이 어느 거점의 구조물인지.</summary>
        public Dictionary<HexCoord, string> StructureOwner { get; } = new Dictionary<HexCoord, string>();

        public static WorldMap Load(string dataDir)
        {
            var root = GameData.ReadObject(Path.Combine(dataDir, "map", "world.json"));
            var m = new WorldMap { Width = root.Value<int>("width"), Height = root.Value<int>("height") };
            var legend = ((JObject)root["terrain_legend"]!).Properties().ToDictionary(p => p.Name[0], p => p.Value.Value<string>()!);
            var tLegend = ((JObject)root["territory_legend"]!).Properties().ToDictionary(p => p.Name[0], p => p.Value.Value<string>()!);
            var terrainRows = root["terrain"]!.Values<string>().ToList();
            var territoryRows = root["territory"]!.Values<string>().ToList();
            for (int row = 0; row < m.Height; row++)
            {
                string tr = terrainRows[row]!, cr = territoryRows[row]!;
                for (int col = 0; col < m.Width; col++)
                {
                    var h = new HexCoord(col - (row >> 1), row);
                    m.Terrain[h] = legend[tr[col]];
                    if (tLegend.TryGetValue(cr[col], out var city))
                        m.Territory[h] = city;
                }
            }
            foreach (JObject s in root["strongholds"]!)
            {
                var p = new StrongholdPlacement
                {
                    Id = s.Value<string>("id")!,
                    Center = Hex(s["center"]!),
                    TerritoryOf = s.Value<string>("territory_of"),
                };
                foreach (var t in s["tiles"]!)
                {
                    var kind = t.Value<string>("kind") switch
                    {
                        "center" => TileKind.Center,
                        "street" => TileKind.Street,
                        "gate" => TileKind.Gate,
                        _ => TileKind.Wall,
                    };
                    var h = Hex(t);
                    p.Tiles[h] = kind;
                    m.StructureOwner[h] = p.Id;
                }
                int i = 0;
                foreach (var sl in s["facility_slots"]!)
                    p.Slots.Add(new FacilitySlot { Index = i++, Hex = Hex(sl), Waterside = sl.Value<bool>("waterside") });
                m.Strongholds[p.Id] = p;
            }
            return m;
        }

        private static HexCoord Hex(JToken t) => new HexCoord(t.Value<int>("q"), t.Value<int>("r"));

        public string TerrainAt(HexCoord h) => Terrain.TryGetValue(h, out var t) ? t : "";

        /// <summary>전투 엔진이 쓰는 지형 맵으로 변환한다 (11식 단일 맵: 전투도 같은 맵에서 벌어진다).</summary>
        public BattleMap ToBattleMap()
        {
            var b = new BattleMap();
            foreach (var kv in Terrain)
                b.Set(kv.Key, kv.Value);
            return b;
        }
    }
}
