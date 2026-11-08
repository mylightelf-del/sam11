using System.Collections.Generic;
using Sam11.Core.Common;

namespace Sam11.Core.Battle
{
    /// <summary>전투가 벌어지는 헥스 지형. 지정하지 않은 칸은 맵 밖이다.</summary>
    public sealed class BattleMap
    {
        private readonly Dictionary<HexCoord, string> _terrain = new Dictionary<HexCoord, string>();
        private readonly Dictionary<HexCoord, int> _fireTurns = new Dictionary<HexCoord, int>();

        public IEnumerable<HexCoord> Cells => _terrain.Keys;

        public bool Contains(HexCoord h) => _terrain.ContainsKey(h);

        public string TerrainAt(HexCoord h) => _terrain.TryGetValue(h, out var t) ? t : "";

        public void Set(HexCoord h, string terrainId) => _terrain[h] = terrainId;

        /// <summary>width × height 직사각형 평지 맵 (테스트·시뮬레이션용).</summary>
        public static BattleMap Rectangle(int width, int height, string terrain = "plain")
        {
            var m = new BattleMap();
            for (int r = 0; r < height; r++)
            {
                int offset = r / 2;
                for (int q = -offset; q < width - offset; q++)
                    m.Set(new HexCoord(q, r), terrain);
            }
            return m;
        }

        public bool IsBurning(HexCoord h) => _fireTurns.ContainsKey(h);

        public void Ignite(HexCoord h, int turns)
        {
            if (Contains(h))
                _fireTurns[h] = turns;
        }

        public void Extinguish(HexCoord h) => _fireTurns.Remove(h);

        public IEnumerable<HexCoord> BurningCells => new List<HexCoord>(_fireTurns.Keys);

        /// <summary>매 라운드 화재 지속 시간을 줄인다.</summary>
        public void TickFire()
        {
            foreach (var h in BurningCells)
            {
                int left = _fireTurns[h] - 1;
                if (left <= 0)
                    _fireTurns.Remove(h);
                else
                    _fireTurns[h] = left;
            }
        }
    }
}
