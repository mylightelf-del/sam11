using Sam11.Core.Common;
using UnityEngine;

namespace Sam11.Unity
{
    /// <summary>헥스 좌표 ↔ 월드 좌표 (뾰족한 위쪽, 축 좌표). r이 커지면 남쪽(−z).</summary>
    public static class HexMetrics
    {
        public const float Size = 1f;
        private static readonly float Sqrt3 = Mathf.Sqrt(3f);

        public static Vector3 ToWorld(HexCoord h, float y = 0f) =>
            new Vector3(Size * Sqrt3 * (h.Q + h.R / 2f), y, -Size * 1.5f * h.R);

        public static HexCoord FromWorld(Vector3 p)
        {
            double r = -p.z / (1.5 * Size);
            double q = p.x / (Sqrt3 * Size) - r / 2.0;
            return HexCoord.Round(q, r);
        }

        /// <summary>꼭짓점 i (0~5)의 중심 기준 위치.</summary>
        public static Vector3 Corner(int i)
        {
            float a = Mathf.Deg2Rad * (60f * i - 30f);
            return new Vector3(Size * Mathf.Cos(a), 0f, Size * Mathf.Sin(a));
        }

        /// <summary>지형 높이 (수묵 산수화처럼 산은 높게, 물은 낮게).</summary>
        public static float Height(string terrain) => terrain switch
        {
            "mountain" => 1.6f,
            "high_ground" => 0.7f,
            "forest" => 0.35f,
            "river" => -0.25f,
            "ford" => -0.15f,
            "sea" => -0.45f,
            "marsh" => -0.05f,
            _ => 0f,
        };
    }
}
