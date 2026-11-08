using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Battle;
using Sam11.Core.Common;
using Sam11.Core.World;

namespace Sam11.Core.AI
{
    /// <summary>
    /// 한 세력의 시야와 기억 (06 AI 설계서 4장: AI도 전장의 안개를 받는다).
    /// 보이는 곳의 적 병력은 정확히 알고, 보이지 않는 거점은 마지막으로 본 값이나 평판치로 추정한다.
    /// </summary>
    public sealed class Knowledge
    {
        public const int UnitVision = 5;
        public const int StrongholdVision = 8;
        public const int BeaconBonus = 4;

        private readonly string _faction;
        private readonly Dictionary<string, (int Troops, int Turn)> _seen = new Dictionary<string, (int, int)>();
        private List<(HexCoord Pos, int Radius)> _eyes = new List<(HexCoord, int)>();

        public Knowledge(string faction) => _faction = faction;

        public void Update(Game g)
        {
            var w = g.World;
            _eyes = g.Field.Alive.Where(u => u.Faction == _faction).Select(u => (u.Position, UnitVision)).ToList();
            foreach (var c in w.Cities.Values.Where(c => c.Owner == _faction))
            {
                int r = StrongholdVision + (c.ActiveFacilities("beacon").Any() ? BeaconBonus : 0);
                _eyes.Add((c.Placement.Center, r));
            }
            foreach (var c in w.Cities.Values.Where(c => c.Owner != _faction && Sees(c.Placement.Center)))
                _seen[c.Id] = (c.Troops + c.FieldGarrison, g.TurnCount);
        }

        public bool Sees(HexCoord h) => _eyes.Any(e => e.Pos.DistanceTo(h) <= e.Radius);

        public IEnumerable<Unit> VisibleEnemies(Game g) =>
            g.Field.Alive.Where(u => g.Field.Hostile(u.Faction, _faction) && !u.Hidden && Sees(u.Position));

        /// <summary>
        /// 거점 수비 병력 추정: 보이면 정확히, 우리 거점과 길로 이어진 이웃이면 국경 정찰(±20% 오차),
        /// 아니면 마지막 기억, 그것도 없으면 종류별 평판치.
        /// </summary>
        public int EstimateTroops(Game g, CityState c)
        {
            if (Sees(c.Placement.Center) || g.World.HasSpyReport(_faction, c.Id)) return c.Troops + c.FieldGarrison;
            if (c.Def.Links.Any(l => g.World.Cities[l].Owner == _faction))
            {
                // 같은 달에는 같은 오차가 나오도록 거점·세력·달로 정한다
                int month = g.World.Date.Year * 12 + g.World.Date.Month;
                uint h = Rng.StableHash(c.Id + "|" + _faction + "|" + month);
                double noise = 0.8 + (h % 401) / 1000.0;
                return (int)((c.Troops + c.FieldGarrison) * noise);
            }
            if (_seen.TryGetValue(c.Id, out var s)) return s.Troops;
            int guess = c.Def.Type switch
            {
                "major_city" => 25000,
                "city" => 10000,
                "gate" => 15000,
                "fortress" => 8000,
                "port" => 3000,
                _ => 1000,
            };
            return c.Owner == null ? guess / 3 : guess;
        }
    }
}
