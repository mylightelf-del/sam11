using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Sam11.Core.Data
{
    /// <summary>
    /// 화면 문자열 표 (08 설계서 지역화). data/locale/&lt;언어&gt;.json과 모드의 locale/&lt;언어&gt;.json을 합친다.
    /// 찾는 순서: 고른 언어 → 한국어 → 키 그대로. {0} {1}… 자리에 인자를 넣는다.
    /// 데이터의 이름(무장·거점 등)은 LocalizedText.Get(언어)로 고른다.
    /// </summary>
    public sealed class Localization
    {
        public const string Default = "ko";

        public string Language { get; private set; } = Default;
        private readonly Dictionary<string, string> _strings = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _fallback = new Dictionary<string, string>();

        /// <summary>쓸 수 있는 언어: 코드 → 그 언어로 쓴 이름.</summary>
        public static Dictionary<string, string> Available(string dataDir)
        {
            var result = new Dictionary<string, string>();
            string dir = Path.Combine(dataDir, "locale");
            if (!Directory.Exists(dir)) return result;
            foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.Ordinal))
            {
                var j = GameData.ReadObject(f);
                result[Path.GetFileNameWithoutExtension(f)] = j.Value<string>("name") ?? Path.GetFileNameWithoutExtension(f);
            }
            return result;
        }

        public static Localization Load(string dataDir, string language, IEnumerable<ModInfo>? mods = null)
        {
            var l = new Localization { Language = language };
            var modList = mods?.ToList() ?? new List<ModInfo>();
            Fill(l._fallback, dataDir, Default, modList);
            if (language != Default) Fill(l._strings, dataDir, language, modList);
            return l;
        }

        private static void Fill(Dictionary<string, string> target, string dataDir, string lang, List<ModInfo> mods)
        {
            var files = new[] { Path.Combine(dataDir, "locale", lang + ".json") }
                        .Concat(mods.Select(m => Path.Combine(m.Dir, "locale", lang + ".json")));
            foreach (var f in files.Where(File.Exists))
                foreach (var p in ((JObject?)GameData.ReadObject(f)["strings"])?.Properties() ?? Enumerable.Empty<JProperty>())
                    target[p.Name] = p.Value.ToString();
        }

        public bool Has(string key) => _strings.ContainsKey(key) || _fallback.ContainsKey(key);

        public string T(string key, params object[] args)
        {
            string s = _strings.TryGetValue(key, out var v) ? v : _fallback.TryGetValue(key, out var f) ? f : key;
            return args.Length == 0 ? s : string.Format(s, args);
        }

        /// <summary>데이터 이름을 이 언어로.</summary>
        public string Name(LocalizedText text) => text.Get(Language);

        /// <summary>한국어 표에는 있는데 이 언어 표에는 없는 키 (번역 누락 점검용).</summary>
        public IEnumerable<string> Missing() => _fallback.Keys.Where(k => !_strings.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal);
    }
}
