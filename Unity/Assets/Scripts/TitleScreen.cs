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
            GUILayout.Label("<size=28><b>삼국지</b></size>  <size=14>(개발판)</size>", _box);
            if (Root.Data == null)
            {
                GUILayout.Label(Root.LastMessage);
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            // 시나리오
            GUILayout.BeginVertical(GUILayout.Width(260));
            GUILayout.Label("<b>시나리오</b>", _box);
            _s1 = GUILayout.BeginScrollView(_s1, GUILayout.Height(300));
            foreach (var id in Root.Data.ScenarioIds())
                if (GUILayout.Toggle(_scenario == id, $"{id}  {ScenarioName(id)}") && _scenario != id) PickScenario(id);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // 세력
            GUILayout.BeginVertical(GUILayout.Width(260));
            GUILayout.Label("<b>세력</b>", _box);
            _s2 = GUILayout.BeginScrollView(_s2, GUILayout.Height(300));
            foreach (var f in _factions)
                if (GUILayout.Toggle(_faction == f.Id, $"{f.Name} (군주 {f.Ruler})")) _faction = f.Id;
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // 옵션
            GUILayout.BeginVertical();
            GUILayout.Label("<b>옵션</b>", _box);
            _options.DeathInBattle = GUILayout.Toggle(_options.DeathInBattle, "전투 중 전사");
            _options.Coalition = GUILayout.Toggle(_options.Coalition, "반최강세력 연합");
            _options.GoalVictory = GUILayout.Toggle(_options.GoalVictory, "목표 승리");
            GUILayout.Label("수명");
            GUILayout.BeginHorizontal();
            foreach (var (m, label) in new[] { (LifespanMode.Historical, "사실"), (LifespanMode.Fictional, "가상"), (LifespanMode.Off, "없음") })
                if (GUILayout.Toggle(_options.Lifespan == m, label)) _options.Lifespan = m;
            GUILayout.EndHorizontal();
            GUILayout.Label("난이도");
            GUILayout.BeginHorizontal();
            foreach (var (d, label) in new[] { (Difficulty.Novice, "입문"), (Difficulty.Normal, "보통"), (Difficulty.Hard, "어려움"), (Difficulty.Hell, "지옥") })
                if (GUILayout.Toggle(_options.Difficulty == d, label)) _options.Difficulty = d;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("시드", GUILayout.Width(40));
            _seed = GUILayout.TextField(_seed, GUILayout.Width(80));
            GUILayout.EndHorizontal();
            if (Root.AvailableMods.Count > 0)
            {
                GUILayout.Label("<b>모드</b>", _box);
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
            if (GUILayout.Button("<b>시작</b>", GUILayout.Height(40)))
            {
                _options.Players.Clear();
                _options.Players.Add(_faction);
                Root.StartNew(_scenario, _options, ulong.TryParse(_seed, out var seed) ? seed : 1);
            }
            GUI.enabled = Root.SaveFiles().Any();
            if (GUILayout.Button("이어 하기 (마지막 저장)", GUILayout.Height(40)))
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
                return j["name"]?["ko"]?.ToString() ?? "";
            }
            catch (IOException) { return ""; }
        }

        private void PickScenario(string id)
        {
            _scenario = id;
            _faction = null;
            var w = ScenarioLoader.Load(Root.Data, Root.Map, id, 1);
            _factions = w.Factions.Values.OrderByDescending(f => w.Cities.Values.Count(c => c.Owner == f.Id))
                         .Select(f => (f.Id, w.NameOf(f.Id), w.Officers[f.Ruler].Def.Name.Ko)).ToList();
        }
    }
}
