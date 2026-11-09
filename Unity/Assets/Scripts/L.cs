using Sam11.Core.Battle;
using Sam11.Core.Data;
using Sam11.Core.World;

namespace Sam11.Unity
{
    /// <summary>화면 문자열 줄임말: L.T("키", 인자...), 데이터 이름은 L.N(이름).</summary>
    public static class L
    {
        private static Localization Loc => GameRoot.Instance.Loc;

        public static string T(string key, params object[] args) => Loc == null ? key : Loc.T(key, args);
        public static string N(LocalizedText text) => text == null ? "" : Loc == null ? text.Ko : Loc.Name(text);

        /// <summary>세력 이름 = 군주 이름.</summary>
        public static string Faction(string id)
        {
            var w = GameRoot.Instance.Session?.World;
            if (id == null || w == null) return "";
            return w.Factions.TryGetValue(id, out var f) && w.Officers.TryGetValue(f.Ruler, out var r) ? N(r.Def.Name) : id;
        }

        public static string Date(GameDate d) => T("date", d.Year, d.Month, T("xun." + (d.Turn - 1)));

        public static string UnitLabel(Unit u) => u.Subs.Count == 0 ? N(u.Leader.Name) : T("unit.label", N(u.Leader.Name));
    }
}
