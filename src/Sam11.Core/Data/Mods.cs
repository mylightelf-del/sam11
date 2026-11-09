using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Sam11.Core.Data
{
    /// <summary>Mods/&lt;id&gt;/mod.json.</summary>
    public sealed class ModInfo
    {
        public string Id = "";
        public string Name = "";
        public string Version = "";
        public string Description = "";
        /// <summary>이 모드보다 먼저 불러와야 하는 모드 (있을 때만).</summary>
        public List<string> LoadAfter = new List<string>();
        public string Dir = "";
        public override string ToString() => $"{Name} ({Id} {Version})";
    }

    /// <summary>두 모드가 같은 항목을 바꿨을 때 (나중 모드가 이긴다).</summary>
    public sealed class ModConflict
    {
        public string File = "";
        public string Entry = "";
        public List<string> Mods = new List<string>();
        public override string ToString() => $"{File} {Entry}: {string.Join(" → ", Mods)} (마지막이 적용)";
    }

    /// <summary>
    /// 모드 (08 설계서 6.2). 모드 폴더 구조:
    ///   mod.json                      {id, name, version, description, load_after}
    ///   base/&lt;파일&gt;.json            기본 데이터와 같은 형식. 항목은 id로 합친다: 새 id는 추가, 같은 id는 적은 필드만 덮어쓰기,
    ///                                 "remove": true는 삭제. rules.json은 통째로 깊게 합친다.
    ///   scenarios/&lt;id&gt;/...          새 시나리오, 또는 기존 시나리오에 events.json만 두면 이벤트 추가·덮어쓰기
    ///   placements.json               {placements: [{officer, scenario ("*"=모든 시나리오), location, faction, loyalty}]}
    ///   faces/&lt;무장 id&gt;.png          무장 얼굴
    /// </summary>
    public static class ModLoader
    {
        /// <summary>data 폴더 옆의 Mods 폴더 (없으면 null).</summary>
        public static string? DefaultModsDir(string dataDir)
        {
            string dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataDir).TrimEnd(Path.DirectorySeparatorChar))!, "Mods");
            return Directory.Exists(dir) ? dir : null;
        }

        public static List<ModInfo> Discover(string? modsDir)
        {
            var list = new List<ModInfo>();
            if (modsDir == null || !Directory.Exists(modsDir)) return list;
            foreach (var dir in Directory.GetDirectories(modsDir).OrderBy(d => d, StringComparer.Ordinal))
            {
                string file = Path.Combine(dir, "mod.json");
                if (!File.Exists(file)) continue;
                var j = GameData.ReadObject(file);
                list.Add(new ModInfo
                {
                    Id = j.Value<string>("id") ?? Path.GetFileName(dir),
                    Name = j.Value<string>("name") ?? Path.GetFileName(dir),
                    Version = j.Value<string>("version") ?? "",
                    Description = j.Value<string>("description") ?? "",
                    LoadAfter = (j["load_after"] as JArray)?.Values<string>().Select(x => x!).ToList() ?? new List<string>(),
                    Dir = dir,
                });
            }
            return list;
        }

        /// <summary>
        /// 켜진 모드를 불러올 순서로. 주어진 순서를 따르되 load_after가 먼저 오게 옮긴다.
        /// 없는 모드나 순환은 예외.
        /// </summary>
        public static List<ModInfo> Order(IReadOnlyList<ModInfo> available, IEnumerable<string> enabled)
        {
            var byId = available.ToDictionary(m => m.Id);
            var want = enabled.ToList();
            foreach (var id in want)
                if (!byId.ContainsKey(id)) throw new InvalidDataException($"모드가 없음: {id}");
            var result = new List<ModInfo>();
            var visiting = new HashSet<string>();
            void Visit(string id)
            {
                if (result.Any(m => m.Id == id)) return;
                if (!visiting.Add(id)) throw new InvalidDataException($"모드 순서가 돌고 있음: {id}");
                foreach (var dep in byId[id].LoadAfter.Where(want.Contains)) Visit(dep);
                result.Add(byId[id]);
            }
            foreach (var id in want) Visit(id);
            return result;
        }

        /// <summary>기본 파일과 모드들의 같은 파일을 합친다.</summary>
        internal static JObject MergeFile(string baseDir, string file, IReadOnlyList<ModInfo> mods, List<ModConflict> conflicts)
        {
            var root = GameData.ReadObject(Path.Combine(baseDir, file));
            var touched = new Dictionary<string, List<string>>();
            foreach (var mod in mods)
            {
                string path = Path.Combine(mod.Dir, "base", file);
                if (!File.Exists(path)) continue;
                var patch = GameData.ReadObject(path);
                if (file == "rules.json")
                {
                    root.Merge(patch, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                    foreach (var p in patch.Properties()) Touch(touched, "rules." + p.Name, mod.Id);
                    continue;
                }
                foreach (var prop in patch.Properties())
                {
                    if (!(prop.Value is JArray items) || !(root[prop.Name] is JArray target))
                    {
                        if (root[prop.Name] is JObject o && prop.Value is JObject po) o.Merge(po);
                        else root[prop.Name] = prop.Value.DeepClone();
                        continue;
                    }
                    string key = target.FirstOrDefault() is JObject first && first["id"] == null && first["level"] != null ? "level" : "id";
                    foreach (var item in items.OfType<JObject>())
                    {
                        string? id = item[key]?.ToString();
                        if (id == null) throw new InvalidDataException($"모드 {mod.Id}의 {file}: '{key}'가 없는 항목");
                        Touch(touched, $"{prop.Name}.{id}", mod.Id);
                        var existing = target.OfType<JObject>().FirstOrDefault(x => x[key]?.ToString() == id);
                        if (item.Value<bool?>("remove") == true)
                        {
                            existing?.Remove();
                            continue;
                        }
                        if (existing == null) target.Add(item.DeepClone());
                        else existing.Merge(item, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                    }
                }
            }
            foreach (var kv in touched.Where(kv => kv.Value.Count > 1).OrderBy(kv => kv.Key, StringComparer.Ordinal))
                conflicts.Add(new ModConflict { File = file, Entry = kv.Key, Mods = kv.Value });
            return root;
        }

        private static void Touch(Dictionary<string, List<string>> touched, string key, string mod)
        {
            if (!touched.TryGetValue(key, out var l)) touched[key] = l = new List<string>();
            if (!l.Contains(mod)) l.Add(mod);
        }
    }
}
