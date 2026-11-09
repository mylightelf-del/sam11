using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Sam11.Core.Data
{
    /// <summary>불러온 데이터(기본 + 모드)의 참조·범위 검증. 문제 목록을 돌려준다 (비면 정상).</summary>
    public static class DataValidator
    {
        public static List<string> Validate(GameData d, World.WorldMap? map = null)
        {
            var errors = new List<string>();
            foreach (var o in d.Officers.Values.OrderBy(o => o.Id, StringComparer.Ordinal))
                errors.AddRange(OfficerEditor.Check(d, o, existing: true).Select(e => $"무장 {o.Id}: {e}"));
            foreach (var t in d.Tactics.Values)
                if (!d.Formations.ContainsKey(t.Formation)) errors.Add($"전법 {t.Id}: 없는 진형 {t.Formation}");
            foreach (var t in d.Treasures.Values)
                foreach (var tr in t.GrantsTraits.Where(x => !d.Traits.ContainsKey(x)))
                    errors.Add($"보물 {t.Id}: 없는 특기 {tr}");
            foreach (var s in d.Strongholds.Values)
            {
                foreach (var l in s.Links.Where(l => !d.Strongholds.ContainsKey(l))) errors.Add($"거점 {s.Id}: 없는 이웃 {l}");
                if (map != null && !map.Strongholds.ContainsKey(s.Id)) errors.Add($"거점 {s.Id}: 맵에 자리가 없음 (모드로 새 거점을 더할 수 없음)");
            }
            var scenarios = d.ScenarioIds().ToHashSet();
            foreach (var p in d.Placements)
            {
                string id = p.Value<string>("officer") ?? "";
                string sc = p.Value<string>("scenario") ?? "*";
                if (!d.Officers.ContainsKey(id)) errors.Add($"배치: 없는 무장 {id}");
                if (!d.Strongholds.ContainsKey(p.Value<string>("location") ?? "")) errors.Add($"배치 {id}: 없는 거점 {p.Value<string>("location")}");
                if (sc != "*" && !scenarios.Contains(sc)) errors.Add($"배치 {id}: 없는 시나리오 {sc}");
            }
            return errors;
        }
    }

    /// <summary>
    /// 신무장 편집기의 데이터 쪽 (07 설계서 5장). 화면(Unity)은 이 검사와 내보내기를 그대로 쓴다.
    /// </summary>
    public static class OfficerEditor
    {
        /// <summary>편집기로 만든 무장의 능력치 총합 상한 (옵션, 0이면 없음).</summary>
        public const int DefaultStatCap = 400;
        private static readonly Regex IdPattern = new Regex("^[a-z][a-z0-9_]*$");

        /// <summary>
        /// 무장 하나를 검사한다. existing=false면 편집기 규칙(새 id, 특기 1~2개, 총합 상한)까지 본다.
        /// </summary>
        public static List<string> Check(GameData d, OfficerDef o, bool existing = false, int statCap = DefaultStatCap)
        {
            var e = new List<string>();
            if (!IdPattern.IsMatch(o.Id)) e.Add("id는 영소문자·숫자·밑줄 (영문자로 시작)");
            if (!existing && d.Officers.ContainsKey(o.Id)) e.Add($"이미 있는 id: {o.Id}");
            if (string.IsNullOrWhiteSpace(o.Name.Ko)) e.Add("이름이 비었음");
            if (o.Gender != "male" && o.Gender != "female") e.Add("성별은 male 또는 female");
            if (o.Death != null && o.Death < o.Birth + 15) e.Add("사망 연도가 출생+15년보다 이름");
            var s = o.Stats;
            foreach (var (name, v) in new[] { ("통솔", s.Ldr), ("무력", s.War), ("지력", s.Int), ("정치", s.Pol), ("매력", s.Cha) })
                if (v < 1 || v > 100) e.Add($"{name} {v}: 1~100");
            int total = s.Ldr + s.War + s.Int + s.Pol + s.Cha;
            if (!existing && statCap > 0 && total > statCap) e.Add($"능력치 합 {total}이 상한 {statCap}을 넘음");
            if (o.Hidden.Loyalty < 1 || o.Hidden.Loyalty > 15) e.Add("의리 1~15");
            if (o.Hidden.Ambition < 1 || o.Hidden.Ambition > 15) e.Add("야망 1~15");
            if (o.Hidden.Compat < 0 || o.Hidden.Compat > 149) e.Add("상성 0~149");
            foreach (var f in d.Formations.Keys.Where(f => !o.Aptitude.ContainsKey(f))) e.Add($"진형 적성 빠짐: {f}");
            foreach (var f in o.Aptitude.Keys.Where(f => !d.Formations.ContainsKey(f))) e.Add($"없는 진형: {f}");
            foreach (var t in o.Traits.Where(t => !d.Traits.ContainsKey(t))) e.Add($"없는 특기: {t}");
            if (o.Traits.Distinct().Count() != o.Traits.Count) e.Add("특기가 겹침");
            if (!existing && (o.Traits.Count < 1 || o.Traits.Count > 2)) e.Add("새 무장의 특기는 1~2개");
            if (o.Traits.Count > d.Rules.Traits.Max) e.Add($"특기는 최대 {d.Rules.Traits.Max}개");
            if (o.Kin != null)
                foreach (var k in new[] { o.Kin.Father, o.Kin.Mother }.Concat(o.Kin.Spouse).Concat(o.Kin.Siblings).Where(k => k != null))
                    if (!d.Officers.ContainsKey(k!) && k != o.Id) e.Add($"없는 혈연 무장: {k}");
            if (o.RulerPersonality is RulerPersonality p
                && new[] { p.Aggression, p.Faith, p.Domestic, p.Diplomacy }.Any(v => v < 1 || v > 10)) e.Add("군주 성향 1~10");
            return e;
        }

        /// <summary>
        /// 무장들을 모드 폴더로 내보낸다 (mod.json, base/officers.json, placements.json).
        /// 같은 id가 이미 그 모드에 있으면 바꾼다. 다른 사람과 나누려면 폴더째 주면 된다.
        /// </summary>
        public static void Export(string modDir, ModInfo info, IEnumerable<OfficerDef> officers, IEnumerable<JObject>? placements = null)
        {
            Directory.CreateDirectory(Path.Combine(modDir, "base"));
            File.WriteAllText(Path.Combine(modDir, "mod.json"), new JObject
            {
                ["id"] = info.Id, ["name"] = info.Name, ["version"] = info.Version,
                ["description"] = info.Description, ["load_after"] = new JArray(info.LoadAfter),
            }.ToString());

            string offFile = Path.Combine(modDir, "base", "officers.json");
            var list = File.Exists(offFile) ? (JArray)GameData.ReadObject(offFile)["officers"]! : new JArray();
            foreach (var o in officers)
            {
                var j = JObject.FromObject(o, GameData.Serializer);
                foreach (var nul in j.Properties().Where(p => p.Value.Type == JTokenType.Null).ToList()) nul.Remove();
                list.OfType<JObject>().FirstOrDefault(x => x.Value<string>("id") == o.Id)?.Remove();
                list.Add(j);
            }
            File.WriteAllText(offFile, new JObject { ["officers"] = list }.ToString(Formatting.Indented));

            if (placements == null) return;
            string plFile = Path.Combine(modDir, "placements.json");
            var pl = File.Exists(plFile) ? (JArray)GameData.ReadObject(plFile)["placements"]! : new JArray();
            foreach (var p in placements)
            {
                pl.OfType<JObject>().FirstOrDefault(x => x.Value<string>("officer") == p.Value<string>("officer")
                                                      && x.Value<string>("scenario") == p.Value<string>("scenario"))?.Remove();
                pl.Add(p.DeepClone());
            }
            File.WriteAllText(plFile, new JObject { ["placements"] = pl }.ToString(Formatting.Indented));
        }
    }
}
