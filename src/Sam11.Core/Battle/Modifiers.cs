using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sam11.Core.Data;

namespace Sam11.Core.Battle
{
    /// <summary>특기·날씨의 modifier 효과를 상황에 맞게 합산한다 (09 데이터 형식 3.3).</summary>
    public static class Modifiers
    {
        public sealed class Context
        {
            public Unit Unit = null!;
            public string Terrain = "";
            public string Weather = "clear";
            public string? Stratagem;
        }

        /// <summary>stat에 대한 곱셈 보정 합 (예: 0.15 = +15%).</summary>
        public static double SumMul(IEnumerable<EffectDef> effects, string stat, Context ctx) =>
            effects.Where(e => e.Type == "modifier" && e.Stat == stat && e.Op == "mul" && Matches(e.When, ctx))
                   .Sum(e => e.Value);

        /// <summary>stat에 대한 덧셈 보정 합.</summary>
        public static double SumAdd(IEnumerable<EffectDef> effects, string stat, Context ctx) =>
            effects.Where(e => e.Type == "modifier" && e.Stat == stat && e.Op == "add" && Matches(e.When, ctx))
                   .Sum(e => e.Value);

        /// <summary>부대 특기 + 현재 날씨 효과.</summary>
        public static IEnumerable<EffectDef> Collect(Unit unit, WeatherDef? weather)
        {
            foreach (var t in unit.Traits)
                foreach (var e in t.Effects)
                    yield return e;
            if (weather != null)
                foreach (var e in weather.Effects)
                    yield return e;
        }

        public static bool Matches(JObject? when, Context ctx)
        {
            if (when == null)
                return true;
            foreach (var p in when.Properties())
            {
                switch (p.Name)
                {
                    case "formation":
                        if (!Contains(p.Value, ctx.Unit.Formation.Id)) return false;
                        break;
                    case "terrain":
                        if (!Contains(p.Value, ctx.Terrain)) return false;
                        break;
                    case "weather":
                        if (!Contains(p.Value, ctx.Weather)) return false;
                        break;
                    case "stratagem":
                        if (ctx.Stratagem == null || !Contains(p.Value, ctx.Stratagem)) return false;
                        break;
                    case "troop_ratio_lte":
                        if ((double)ctx.Unit.Troops / ctx.Unit.MaxTroops > p.Value.Value<double>()) return false;
                        break;
                    case "turns_since_sortie_lte":
                        if (ctx.Unit.TurnsSinceSortie > p.Value.Value<int>()) return false;
                        break;
                    case "role":
                        // 태수·군주 조건은 전투 밖(내정)에서만 성립한다.
                        return false;
                    default:
                        // 알 수 없는 조건은 안전하게 불성립으로 본다.
                        return false;
                }
            }
            return true;
        }

        private static bool Contains(JToken list, string value) =>
            list is JArray arr ? arr.Any(t => t.Value<string>() == value) : list.Value<string>() == value;
    }
}
