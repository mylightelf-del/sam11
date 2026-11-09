using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sam11.Core.AI;
using Sam11.Core.Data;
using Sam11.Core.World;
using UnityEngine;

namespace Sam11.Unity
{
    /// <summary>처음 화면 (IMGUI): 시나리오·세력·옵션·모드를 고르고 시작하거나 이어 한다.</summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        private string _scenario;
        private string _faction;
        private readonly GameOptions _options = new GameOptions();
        private readonly HashSet<string> _mods = new HashSet<string>();
        private string _seed = "1";
        private List<(string Id, string Name, string Ruler)> _factions = new List<(string, string, string)>();
        private Vector2 _s1, _s2;
        private GUIStyle _box;

        private GameRoot Root => GameRoot.Instance;

        private void OnEnable()
        {
            if (Camera.main == null)
            {
                var cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = InkPalette.Paper;
            }
        }

        private void OnGUI()
        {
            GUI.skin.font = Fonts.Korean;
            _box ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, wordWrap = true, richText = true };
            var area = new Rect(Screen.width / 2 - 420, 40, 840, Screen.height - 80);
            GUILayout.BeginArea(area, _box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("title.name"), _box);
            GUILayout.FlexibleSpace();
            GUILayout.Label(L.T("lang.label"), GUILayout.Width(60));
            foreach (var lang in Root.Languages)
                if (GUILayout.Toggle(Settings.Language == lang.Key, lang.Value, GUILayout.Width(80)) && Settings.Language != lang.Key)
                {
                    Root.SetLanguage(lang.Key);
                    if (_scenario != null) PickScenario(_scenario);
                }
            GUILayout.EndHorizontal();
            if (Root.Data == null)
            {
                GUILayout.Label(Root.LastMessage);
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            // 시나리오
            GUILayout.BeginVertical(GUILayout.Width(260));
            GUILayout.Label(L.T("title.scenario"), _box);
            _s1 = GUILayout.BeginScrollView(_s1, GUILayout.Height(300));
            foreach (var id in Root.Data.ScenarioIds())
                if (GUILayout.Toggle(_scenario == id, $"{id}  {ScenarioName(id)}") && _scenario != id) PickScenario(id);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // 세력
            GUILayout.BeginVertical(GUILayout.Width(260));
            GUILayout.Label(L.T("title.faction"), _box);
            _s2 = GUILayout.BeginScrollView(_s2, GUILayout.Height(300));
            foreach (var f in _factions)
                if (GUILayout.Toggle(_faction == f.Id, L.T("title.faction.item", f.Name, f.Ruler))) _faction = f.Id;
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // 옵션
            GUILayout.BeginVertical();
            GUILayout.Label(L.T("title.options"), _box);
            _options.DeathInBattle = GUILayout.Toggle(_options.DeathInBattle, L.T("opt.death"));
            _options.Coalition = GUILayout.Toggle(_options.Coalition, L.T("opt.coalition"));
            _options.GoalVictory = GUILayout.Toggle(_options.GoalVictory, L.T("opt.goal"));
            GUILayout.Label(L.T("opt.lifespan"));
            GUILayout.BeginHorizontal();
            foreach (var m in new[] { LifespanMode.Historical, LifespanMode.Fictional, LifespanMode.Off })
                if (GUILayout.Toggle(_options.Lifespan == m, L.T("opt.lifespan." + m.ToString().ToLowerInvariant()))) _options.Lifespan = m;
            GUILayout.EndHorizontal();
            GUILayout.Label(L.T("opt.difficulty"));
            GUILayout.BeginHorizontal();
            foreach (var d in new[] { Difficulty.Novice, Difficulty.Normal, Difficulty.Hard, Difficulty.Hell })
                if (GUILayout.Toggle(_options.Difficulty == d, L.T("difficulty." + d.ToString().ToLowerInvariant()))) _options.Difficulty = d;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("opt.seed"), GUILayout.Width(40));
            _seed = GUILayout.TextField(_seed, GUILayout.Width(80));
            GUILayout.EndHorizontal();
            if (Root.AvailableMods.Count > 0)
            {
                GUILayout.Label(L.T("title.mods"), _box);
                foreach (var m in Root.AvailableMods)
                {
                    bool on = _mods.Contains(m.Id);
                    if (GUILayout.Toggle(on, m.Name) != on)
                    {
                        if (on) _mods.Remove(m.Id); else _mods.Add(m.Id);
                        Root.LoadData(Root.AvailableMods.Where(x => _mods.Contains(x.Id)).Select(x => x.Id));
                        if (_scenario != null) PickScenario(_scenario);
                    }
                }
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            GUI.enabled = _scenario != null && _faction != null;
            if (GUILayout.Button(L.T("title.start"), GUILayout.Height(40)))
            {
                _options.Players.Clear();
                _options.Players.Add(_faction);
                Root.StartNew(_scenario, _options, ulong.TryParse(_seed, out var seed) ? seed : 1);
            }
            GUI.enabled = Root.SaveFiles().Any();
            if (GUILayout.Button(L.T("title.continue"), GUILayout.Height(40)))
                Root.LoadGame(Root.SaveFiles().First());
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(Root.LastMessage);
            GUILayout.EndArea();
        }

        private string ScenarioName(string id)
        {
            try
            {
                var j = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(Root.Data.ScenarioDir(id), "scenario.json")));
                var name = j["name"];
                return name?[Settings.Language]?.ToString() ?? name?["ko"]?.ToString() ?? "";
            }
            catch (IOException) { return ""; }
        }

        private void PickScenario(string id)
        {
            _scenario = id;
            _faction = null;
            var w = ScenarioLoader.Load(Root.Data, Root.Map, id, 1);
            _factions = w.Factions.Values.OrderByDescending(f => w.Cities.Values.Count(c => c.Owner == f.Id))
                         .Select(f => (f.Id, L.N(w.Officers[f.Ruler].Def.Name), L.N(w.Officers[f.Ruler].Def.Name))).ToList();
        }
    }
}
