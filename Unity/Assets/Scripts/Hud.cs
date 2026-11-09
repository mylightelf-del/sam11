using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sam11.Core.Battle;
using Sam11.Core.Common;
using Sam11.Core.World;
using UnityEngine;

namespace Sam11.Unity
{
    /// <summary>
    /// 임시 화면 (IMGUI): 윗줄(날짜·자원·차례), 거점 창(내정·출진), 부대 창(전법·계략·귀환),
    /// 결정 창(이벤트·외교 제안), 소식. 정식 UI(수묵 디자인)는 M0 뒤 UI Toolkit으로 바꾼다.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        /// <summary>마우스가 UI 위에 있으면 맵 입력을 받지 않는다.</summary>
        public static bool PointerOverUi { get; private set; }

        private readonly List<Rect> _rects = new List<Rect>();
        private readonly HashSet<string> _sortieOfficers = new HashSet<string>();
        private string _sortieCity;
        private string _formation;
        private float _troops = 5000, _food = 5000;
        private bool _showLog, _showLoad;
        private Vector2 _scrollCity, _scrollLog, _scrollLoad;
        private string _officer;   // 내정 명령을 맡길 무장
        private GUIStyle _box, _small;

        private GameRoot Root => GameRoot.Instance;
        private Selection Sel => Root.Selection;

        private void Update()
        {
            var m = Input.mousePosition;
            var p = new Vector2(m.x, Screen.height - m.y);
            PointerOverUi = _rects.Any(r => r.Contains(p));
            if (Root.Session != null && !Root.Session.IsOver && Input.GetKeyDown(KeyCode.Return)) Root.Execute("end_turn");
        }

        private void OnGUI()
        {
            if (Root.Session == null) return;
            GUI.skin.font = Fonts.Korean;
            _box ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, wordWrap = true, richText = true };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, richText = true };
            if (Event.current.type == EventType.Layout) _rects.Clear();

            TopBar();
            if (Sel.CityId != null) CityPanel(Root.Session.World.Cities[Sel.CityId]);
            else if (Sel.SelectedUnit is Unit u) UnitPanel(u);
            Decisions();
            NewsPanel();
            if (_showLoad) LoadPanel();
            if (Root.Session.IsOver) GameOverPanel();
        }

        private Rect Track(Rect r)
        {
            if (Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint) _rects.Add(r);
            return r;
        }

        // ------------------------------------------------------------------ 윗줄

        private void TopBar()
        {
            var s = Root.Session;
            var w = s.World;
            string me = s.Faction ?? s.Options.Players.First();
            var mine = w.Cities.Values.Where(c => c.Owner == me).ToList();
            var r = Track(new Rect(0, 0, Screen.width, 34));
            GUI.Box(r, "");
            GUILayout.BeginArea(new Rect(8, 5, Screen.width - 16, 26));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{w.Date}</b>  {w.NameOf(me)}  도시 {mine.Count(c => c.Def.IsCity)}  금 {mine.Sum(c => c.Gold)}  군량 {mine.Sum(c => c.Food)}  병력 {mine.Sum(c => c.Troops + c.FieldGarrison)}  신용 {w.TrustOf(me)}", _small, GUILayout.Width(560));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("차례 끝 (Enter)", GUILayout.Width(120))) Root.Execute("end_turn");
            if (GUILayout.Button("위임", GUILayout.Width(60))) Root.Execute("delegate");
            if (GUILayout.Button("저장", GUILayout.Width(60))) Root.SaveGame();
            if (GUILayout.Button("불러오기", GUILayout.Width(80))) _showLoad = !_showLoad;
            if (GUILayout.Button("소식", GUILayout.Width(60))) _showLog = !_showLog;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ 거점

        private void CityPanel(CityState c)
        {
            var s = Root.Session;
            var w = s.World;
            string me = s.Faction;
            var r = Track(new Rect(8, 42, 380, Screen.height - 220));
            GUILayout.BeginArea(r, _box);
            _scrollCity = GUILayout.BeginScrollView(_scrollCity);
            GUILayout.Label($"<b>{c.Def.Name.Ko}</b> ({w.NameOf(c.Owner ?? "")})", _box);
            if (c.Owner != me)
            {
                GUILayout.Label(c.Owner == null ? "무주지" : $"{w.NameOf(c.Owner)} 세력의 거점. 병력은 첩보로 알 수 있다.", _small);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }
            GUILayout.Label($"병력 {c.Troops} (훈련 {c.Training})  금 {c.Gold}  군량 {c.Food}\n인구 {c.Population}  민심 {c.Morale}\n병장기: {string.Join(" ", c.Equipment.Select(kv => $"{EquipName(kv.Key)} {kv.Value}"))}", _small);

            var officers = w.OfficersAt(c.Id).Where(o => o.Faction == me && o.Status == OfficerStatus.Active).OrderByDescending(o => o.Def.Stats.Ldr).ToList();
            GUILayout.Label("<b>무장</b> (이름을 눌러 고르고 아래에서 명령)", _small);
            foreach (var o in officers)
            {
                var st = o.Def.Stats;
                GUILayout.BeginHorizontal();
                bool chosen = _officer == o.Id;
                if (GUILayout.Toggle(chosen, $"{o.Def.Name.Ko}{(o.Acted ? " ·" : "")}", GUILayout.Width(110)) != chosen) _officer = chosen ? null : o.Id;
                GUILayout.Label($"통{st.Ldr} 무{st.War} 지{st.Int} 정{st.Pol} 매{st.Cha} 충{o.Loyalty}", _small);
                bool inSortie = _sortieOfficers.Contains(o.Id) && _sortieCity == c.Id;
                if (GUILayout.Toggle(inSortie, "출진", GUILayout.Width(50)) != inSortie)
                {
                    if (_sortieCity != c.Id) { _sortieOfficers.Clear(); _sortieCity = c.Id; }
                    if (inSortie) _sortieOfficers.Remove(o.Id); else if (_sortieOfficers.Count < 3) _sortieOfficers.Add(o.Id);
                }
                GUILayout.EndHorizontal();
            }

            if (_officer != null && officers.Any(o => o.Id == _officer))
            {
                GUILayout.Label($"<b>{w.Officers[_officer]}에게 맡길 일</b>", _small);
                GUILayout.BeginHorizontal();
                foreach (var (label, type) in new[] { ("징병", "recruit"), ("훈련", "train"), ("순찰", "patrol"), ("시혜", "charity") })
                    if (GUILayout.Button(label)) Root.Execute(type, new { officer = _officer, city = c.Id });
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                foreach (var eq in new[] { "horse", "crossbow", "siege_engine", "ship" })
                    if (GUILayout.Button($"{EquipName(eq)} 생산")) Root.Execute("produce", new { officer = _officer, city = c.Id, equipment = eq });
                GUILayout.EndHorizontal();
                GUILayout.Label("시설 (빈터를 누르면 지을 시설을 고른다)", _small);
                for (int i = 0; i < c.Placement.Slots.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    if (c.Facilities.TryGetValue(i, out var f))
                    {
                        var def = s.Data.Facilities[f.FacilityId];
                        GUILayout.Label($"{i}. {def.Name.Ko} {f.Level}단계{(f.BuildTurnsLeft > 0 ? $" (공사 {f.BuildTurnsLeft})" : "")}", _small, GUILayout.Width(200));
                        if (f.BuildTurnsLeft == 0 && f.Level < def.MaxLevel && GUILayout.Button("증축"))
                            Root.Execute("build", new { officer = _officer, city = c.Id, slot = i, facility = f.FacilityId });
                    }
                    else
                    {
                        GUILayout.Label($"{i}. 빈터{(c.Placement.Slots[i].Waterside ? " (물가)" : "")}", _small, GUILayout.Width(110));
                        foreach (var def in s.Data.Facilities.Values.Where(d => !d.RequiresWaterside || c.Placement.Slots[i].Waterside).Take(6))
                            if (GUILayout.Button(def.Name.Ko, GUILayout.Width(44)))
                                Root.Execute("build", new { officer = _officer, city = c.Id, slot = i, facility = def.Id });
                    }
                    GUILayout.EndHorizontal();
                }
            }

            if (_sortieCity == c.Id && _sortieOfficers.Count > 0) SortieBox(c);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void SortieBox(CityState c)
        {
            var s = Root.Session;
            var w = s.World;
            var ids = _sortieOfficers.OrderByDescending(id => w.Officers[id].Def.Stats.Ldr).ToList();
            var members = ids.Select(id => Treasures.Effective(w, id)).ToList();
            var leader = w.Officers[ids[0]];
            int max = UnitStats.MaxTroops(members[0], leader.Rank != null ? s.Data.Ranks[leader.Rank] : null, s.Data.Rules.Unit);
            GUILayout.Label($"<b>출진</b> 대장 {leader} (최대 {max}명)", _small);
            GUILayout.BeginHorizontal();
            foreach (var f in s.Data.Formations.Values)
            {
                bool ok = Battle.CanDeploy(s.Data, members, f.Id, Mathf.Max(1, (int)_troops), c.Equipment) == null;
                GUI.enabled = ok;
                if (GUILayout.Toggle(_formation == f.Id, f.Name.Ko, GUILayout.Width(42))) _formation = f.Id;
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
            _troops = GUILayout.HorizontalSlider(_troops, 0, Mathf.Min(c.Troops, max));
            GUILayout.Label($"병력 {(int)_troops}", _small);
            _food = GUILayout.HorizontalSlider(_food, 0, c.Food);
            GUILayout.Label($"군량 {(int)_food} (병력×{s.Data.Rules.Economy.FoodPerSoldierMonth}/월)", _small);
            GUI.enabled = _formation != null && _troops >= 1;
            if (GUILayout.Button("출진"))
            {
                var r = Root.Execute("sortie", new { city = c.Id, officers = ids, formation = _formation, troops = (int)_troops, food = (int)_food });
                if (r.Ok)
                {
                    _sortieOfficers.Clear();
                    var u = s.Game.Field.Alive.Where(x => x.Faction == s.Faction && x.Leader.Id == ids[0]).OrderByDescending(x => x.Id).FirstOrDefault();
                    if (u != null) Sel.SelectUnit(u);
                }
            }
            GUI.enabled = true;
        }

        private static string EquipName(string id) => id switch
        {
            "horse" => "군마", "crossbow" => "노", "siege_engine" => "병기", "ship" => "배", _ => id,
        };

        // ------------------------------------------------------------------ 부대

        private void UnitPanel(Unit u)
        {
            var s = Root.Session;
            var w = s.World;
            var b = s.Game.Field;
            bool mine = u.Faction == s.Faction;
            var r = Track(new Rect(8, 42, 340, 360));
            GUILayout.BeginArea(r, _box);
            GUILayout.Label($"<b>{u.Label}</b> ({w.NameOf(u.Faction)}) {u.Formation.Name.Ko}", _box);
            GUILayout.Label($"병력 {u.Troops}/{u.StartTroops}  사기 {u.Morale}  기력 {u.Energy}  군량 {u.Food}\n" +
                            $"부대원: {string.Join(", ", u.Members.Select(m => m.Name.Ko))}{(u.Status == UnitStatus.Confused ? "\n혼란" : "")}", _small);
            if (mine && !u.HasActed && u.Status == UnitStatus.Ready)
            {
                GUILayout.Label("오른쪽 클릭: 이동 / 공격 / 성벽 공격", _small);
                GUILayout.Label("<b>전법</b> (고른 뒤 적을 왼쪽 클릭)", _small);
                foreach (var t in b.AvailableTactics(u).Where(t => !b.TacticTargetsStructureOnly(t)))
                {
                    GUI.enabled = u.Energy >= t.EnergyCost;
                    if (GUILayout.Button($"{t.Name.Ko} (기력 {t.EnergyCost}, 사거리 {b.TacticRange(u, t)})"))
                    {
                        if (t.Power <= 0) Root.Execute("tactic", new { unit = u.Id, tactic = t.Id, target = u.Id });
                        else Sel.BeginTargeting(Selection.Mode.Tactic, t.Id);
                    }
                    GUI.enabled = true;
                }
                GUILayout.BeginHorizontal();
                foreach (var (kind, label) in new[] { ("fire", "화계"), ("confuse", "혼란"), ("false_report", "허보") })
                    if (GUILayout.Button(label)) Sel.BeginTargeting(Selection.Mode.Stratagem, kind);
                if (GUILayout.Button("매복")) Root.Execute("stratagem", new { unit = u.Id, kind = "ambush" });
                if (GUILayout.Button("진정")) Root.Execute("stratagem", new { unit = u.Id, kind = "calm" });
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("일기토")) Sel.BeginTargeting(Selection.Mode.Duel, "");
                var home = w.Cities.Values.FirstOrDefault(c => c.Owner == u.Faction
                                                               && (c.Placement.Tiles.ContainsKey(u.Position) || c.Placement.Tiles.Keys.Any(t => t.DistanceTo(u.Position) == 1)));
                GUI.enabled = home != null;
                if (GUILayout.Button(home != null ? $"{home.Def.Name.Ko}로 귀환" : "귀환 (거점 옆에서)"))
                    Root.Execute("withdraw", new { unit = u.Id, city = home.Id });
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                if (Sel.Targeting != Selection.Mode.None) GUILayout.Label("대상을 고르세요 (Esc 취소)", _small);
            }
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ 결정 (이벤트·제안)

        private void Decisions()
        {
            var s = Root.Session;
            var w = s.World;
            var pe = w.PendingEvents.FirstOrDefault(p => p.Faction == s.Faction);
            if (pe != null)
            {
                var ev = w.Events.First(e => e.Id == pe.Event);
                var r = Track(new Rect(Screen.width / 2 - 260, Screen.height / 2 - 170, 520, 340));
                GUILayout.BeginArea(r, _box);
                GUILayout.Label($"<size=18><b>{ev.Name}</b></size>", _box);
                GUILayout.Label(ev.Text, _small);
                GUILayout.FlexibleSpace();
                foreach (var c in ev.Choices)
                    if (GUILayout.Button(c.Text)) Root.Execute("event_choice", new { @event = ev.Id, choice = c.Id });
                GUILayout.EndArea();
                return;
            }
            var props = w.PendingProposals.Where(p => p.To == s.Faction).ToList();
            if (props.Count == 0) return;
            var p0 = props[0];
            var pr = Track(new Rect(Screen.width / 2 - 220, Screen.height / 2 - 90, 440, 180));
            GUILayout.BeginArea(pr, _box);
            GUILayout.Label($"<b>{w.NameOf(p0.From)}의 사자 {w.Officers[p0.Envoy]}</b>", _box);
            GUILayout.Label($"{Diplomacy.Describe(p0)}을(를) 청한다. (우호도 {w.FriendshipOf(p0.From, s.Faction)})", _small);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("받아들인다")) Root.Execute("respond", new { index = 0, accept = true });
            if (GUILayout.Button("거절한다")) Root.Execute("respond", new { index = 0, accept = false });
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ 소식·불러오기·끝

        private void NewsPanel()
        {
            int lines = _showLog ? 18 : 5;
            var r = Track(new Rect(Screen.width - 520, Screen.height - 24 - lines * 18, 512, 16 + lines * 18));
            GUILayout.BeginArea(r, _box);
            _scrollLog = GUILayout.BeginScrollView(_scrollLog);
            GUILayout.Label(Root.LastMessage, _small);
            foreach (var l in Root.News.Skip(Mathf.Max(0, Root.News.Count - (lines - 1))))
                GUILayout.Label(l, _small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void LoadPanel()
        {
            var r = Track(new Rect(Screen.width / 2 - 200, 60, 400, 300));
            GUILayout.BeginArea(r, _box);
            GUILayout.Label("<b>불러오기</b>", _box);
            _scrollLoad = GUILayout.BeginScrollView(_scrollLoad);
            foreach (var f in Root.SaveFiles().Take(30))
                if (GUILayout.Button(Path.GetFileNameWithoutExtension(f)))
                {
                    _showLoad = false;
                    try { Root.LoadGame(f); }
                    catch (System.Exception e) { Debug.LogError(e); }
                }
            GUILayout.EndScrollView();
            if (GUILayout.Button("닫기")) _showLoad = false;
            GUILayout.EndArea();
        }

        private void GameOverPanel()
        {
            var g = Root.Session.Game;
            var w = Root.Session.World;
            string text = g.Outcome switch
            {
                GameOutcome.Unification => $"{w.NameOf(g.Winner)} 세력이 천하를 통일했다",
                GameOutcome.Goal => $"목표 달성: {g.AchievedGoal.Description}",
                GameOutcome.Defeat => "우리 세력이 멸망했다",
                _ => "",
            };
            var r = Track(new Rect(Screen.width / 2 - 220, Screen.height / 2 - 60, 440, 120));
            GUILayout.BeginArea(r, _box);
            GUILayout.Label($"<size=20><b>{text}</b></size>\n{w.Date}", _box);
            if (GUILayout.Button("처음 화면으로"))
            {
                var title = Root.GetComponent<TitleScreen>();
                title.enabled = true;
            }
            GUILayout.EndArea();
        }
    }
}
