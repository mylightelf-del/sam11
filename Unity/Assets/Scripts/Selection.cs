using System.Linq;
using Sam11.Core.Battle;
using Sam11.Core.Common;
using UnityEngine;

namespace Sam11.Unity
{
    /// <summary>
    /// 마우스: 왼쪽 클릭으로 부대·거점 선택, 오른쪽 클릭으로 선택한 우리 부대에 명령 (빈 칸이면 이동, 적이면 공격).
    /// 전법·계략을 고른 상태에서는 왼쪽 클릭한 적에게 쓴다. Esc로 취소.
    /// </summary>
    public sealed class Selection : MonoBehaviour
    {
        public enum Mode { None, Tactic, Stratagem, Duel }

        public int? UnitId { get; private set; }
        public string CityId { get; private set; }
        public Mode Targeting { get; private set; }
        public string TargetingId { get; private set; }
        public HexCoord? Hover { get; private set; }

        private GameRoot Root => GameRoot.Instance;

        public Unit SelectedUnit =>
            UnitId is int id ? Root.Session.Game.Field.Alive.FirstOrDefault(u => u.Id == id) : null;

        private void OnEnable() { if (Root != null) Root.Changed += RefreshHighlight; }
        private void OnDisable() { if (Root != null) Root.Changed -= RefreshHighlight; }

        public void SelectUnit(Unit u)
        {
            UnitId = u?.Id;
            CityId = null;
            Targeting = Mode.None;
            RefreshHighlight();
        }

        public void SelectCity(string id)
        {
            CityId = id;
            UnitId = null;
            Targeting = Mode.None;
            RefreshHighlight();
        }

        public void BeginTargeting(Mode mode, string id)
        {
            Targeting = mode;
            TargetingId = id;
            RefreshHighlight();
        }

        private void Update()
        {
            if (Root.Session == null || Hud.PointerOverUi) return;
            Hover = CameraRig.Instance.PointerHex(out var h) ? h : (HexCoord?)null;
            if (Input.GetKeyDown(KeyCode.Escape)) { Targeting = Mode.None; RefreshHighlight(); }
            if (Hover == null) return;
            var hex = Hover.Value;
            var layer = FindObjectOfType<UnitLayer>();
            var unit = layer != null ? layer.UnitAt(hex) : null;
            string me = Root.Session.Faction;

            if (Input.GetMouseButtonDown(0))
            {
                if (Targeting != Mode.None && SelectedUnit is Unit actor && unit != null)
                {
                    switch (Targeting)
                    {
                        case Mode.Tactic: Root.Execute("tactic", new { unit = actor.Id, tactic = TargetingId, target = unit.Id }); break;
                        case Mode.Stratagem: Root.Execute("stratagem", new { unit = actor.Id, kind = TargetingId, target = unit.Id }); break;
                        case Mode.Duel: Root.Execute("duel", new { unit = actor.Id, target = unit.Id }); break;
                    }
                    Targeting = Mode.None;
                    RefreshHighlight();
                    return;
                }
                if (unit != null) { SelectUnit(unit); return; }
                var w = Root.Session.World;
                var city = w.Cities.Values.FirstOrDefault(c => c.Placement.Tiles.ContainsKey(hex));
                if (city != null) SelectCity(city.Id);
                else { UnitId = null; CityId = null; RefreshHighlight(); }
            }
            else if (Input.GetMouseButtonDown(1) && SelectedUnit is Unit mine && mine.Faction == me)
            {
                var b = Root.Session.Game.Field;
                if (unit != null && b.Hostile(mine.Faction, unit.Faction))
                    Root.Execute("attack", new { unit = mine.Id, target = unit.Id });
                else if (b.Structures.TryGetValue(hex, out var st) && st.Blocking && b.OwnerOf(st) != me)
                    Root.Execute("strike", new { unit = mine.Id, q = hex.Q, r = hex.R });
                else if (unit == null)
                {
                    // 우리 거점 옆이면 귀환할지 묻지 않고 이동만 한다 (귀환은 부대 창의 버튼)
                    Root.Execute("move", new { unit = mine.Id, q = hex.Q, r = hex.R });
                }
            }
        }

        /// <summary>선택한 우리 부대의 이동 범위(옅은 먹)와 공격할 수 있는 적(붉은 칸).</summary>
        public void RefreshHighlight()
        {
            if (Root?.MapView == null) return;
            var u = SelectedUnit;
            if (u == null || u.Faction != Root.Session.Faction || u.HasActed)
            {
                Root.MapView.ClearHighlight();
                return;
            }
            var b = Root.Session.Game.Field;
            var reach = b.Reachable(u).Keys.Select(h => (h, new Color(0.2f, 0.2f, 0.2f, 0.35f)));
            int range = u.Formation.Range;
            if (Targeting == Mode.Tactic && Root.Session.Data.Tactics.TryGetValue(TargetingId, out var t)) range = b.TacticRange(u, t);
            if (Targeting == Mode.Stratagem || Targeting == Mode.Duel) range = Targeting == Mode.Duel ? 1 : 4;
            var foes = b.Alive.Where(e => b.Hostile(u.Faction, e.Faction) && Root.Session.IsVisible(e) && e.Position.DistanceTo(u.Position) <= range)
                        .Select(e => (e.Position, new Color(0.7f, 0.15f, 0.1f, 0.6f)));
            Root.MapView.Highlight(Targeting == Mode.None ? reach.Concat(foes) : foes);
        }
    }
}
