using System.Globalization;
using Sam11.Core.Common;
using UnityEngine;

namespace Sam11.Unity
{
    /// <summary>수묵화풍 색: 종이색 바탕에 먹의 농담, 물은 옅은 쪽빛, 세력색은 채도를 낮춘다.</summary>
    public static class InkPalette
    {
        public static readonly Color Paper = new Color(0.93f, 0.90f, 0.82f);
        public static readonly Color Ink = new Color(0.12f, 0.11f, 0.10f);

        public static Color Terrain(string t) => t switch
        {
            "mountain" => new Color(0.36f, 0.36f, 0.34f),
            "high_ground" => new Color(0.60f, 0.58f, 0.52f),
            "forest" => new Color(0.46f, 0.52f, 0.44f),
            "river" => new Color(0.62f, 0.70f, 0.72f),
            "ford" => new Color(0.70f, 0.75f, 0.74f),
            "sea" => new Color(0.55f, 0.63f, 0.68f),
            "marsh" => new Color(0.66f, 0.68f, 0.60f),
            "road" => new Color(0.80f, 0.75f, 0.64f),
            "street" => new Color(0.74f, 0.70f, 0.62f),
            _ => new Color(0.86f, 0.83f, 0.74f),
        };

        /// <summary>칸마다 먹이 고르지 않게 번진 느낌 (결정론적).</summary>
        public static Color Jitter(Color c, HexCoord h)
        {
            float n = (Rng.StableHash($"{h.Q},{h.R}") % 1000) / 1000f;
            return Color.Lerp(c, c * 0.86f, n * 0.6f);
        }

        public static Color Faction(string hex)
        {
            if (hex == null || hex.Length < 7 || hex[0] != '#') return Ink;
            float P(int i) => int.Parse(hex.Substring(i, 2), NumberStyles.HexNumber) / 255f;
            var c = new Color(P(1), P(3), P(5));
            // 먹과 섞어 채도를 낮춘다
            return Color.Lerp(c, new Color(0.3f, 0.28f, 0.26f), 0.25f);
        }
    }
}
