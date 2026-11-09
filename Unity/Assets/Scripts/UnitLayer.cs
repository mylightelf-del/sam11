using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Battle;
using UnityEngine;

namespace Sam11.Unity
{
    /// <summary>
    /// 부대: 세력색 깃발 + 병력 막대 + 이름표. 플레이어에게 보이는 부대만 (PlayerSession.IsVisible).
    /// 위치가 바뀌면 부드럽게 옮긴다. (병사 군집 표시는 M0 기술 검증에서 이 자리를 바꾼다.)
    /// </summary>
    public sealed class UnitLayer : MonoBehaviour
    {
        private sealed class View
        {
            public GameObject Root;
            public Renderer Flag;
            public Transform Bar;
            public TextMesh Label;
            public Vector3 Target;
        }

        private readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        private Material _mat;

        private void Start()
        {
            GameRoot.Instance.Changed += Refresh;
            GameRoot.Instance.SessionStarted += Clear;
            if (GameRoot.Instance.Session != null) Refresh();
        }

        private void OnDestroy()
        {
            if (GameRoot.Instance == null) return;
            GameRoot.Instance.Changed -= Refresh;
            GameRoot.Instance.SessionStarted -= Clear;
        }

        private void Clear()
        {
            foreach (var v in _views.Values) Destroy(v.Root);
            _views.Clear();
        }

        public Unit UnitAt(Sam11.Core.Common.HexCoord h)
        {
            var s = GameRoot.Instance.Session;
            var u = s.Game.Field.UnitAt(h);
            return u != null && s.IsVisible(u) ? u : null;
        }

        private void Refresh()
        {
            _mat ??= new Material(Shader.Find("Sam11/InkWash") ?? Shader.Find("Standard"));
            var root = GameRoot.Instance;
            var s = root.Session;
            var w = s.World;
            var alive = new HashSet<int>();
            foreach (var u in s.Game.Field.Alive)
            {
                if (u.IsGarrison || !s.IsVisible(u)) continue;
                alive.Add(u.Id);
                if (!_views.TryGetValue(u.Id, out var v)) _views[u.Id] = v = Create(u);
                v.Target = HexMetrics.ToWorld(u.Position, root.MapView.HeightAt(u.Position));
                v.Flag.material.color = InkPalette.Faction(w.Factions[u.Faction].Color);
                float ratio = Mathf.Clamp01(u.Troops / Mathf.Max(1f, u.StartTroops));
                v.Bar.localScale = new Vector3(0.9f * ratio, 0.08f, 0.08f);
                string state = u.Status == UnitStatus.Confused ? " (혼란)" : u.HasActed ? " ·" : "";
                v.Label.text = $"{u.Label}{state}\n<size=28>{u.Troops}</size>";
            }
            foreach (var id in _views.Keys.Where(id => !alive.Contains(id)).ToList())
            {
                Destroy(_views[id].Root);
                _views.Remove(id);
            }
        }

        private View Create(Unit u)
        {
            var go = new GameObject($"unit {u.Id}");
            go.transform.SetParent(transform, false);
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(pole.GetComponent<Collider>());
            pole.transform.SetParent(go.transform, false);
            pole.transform.localPosition = new Vector3(0, 0.6f, 0);
            pole.transform.localScale = new Vector3(0.06f, 0.6f, 0.06f);
            pole.GetComponent<Renderer>().sharedMaterial = _mat;
            var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(flag.GetComponent<Collider>());
            flag.transform.SetParent(go.transform, false);
            flag.transform.localPosition = new Vector3(0.25f, 1.0f, 0);
            flag.transform.localScale = new Vector3(0.5f, 0.35f, 0.03f);
            var fr = flag.GetComponent<Renderer>();
            fr.sharedMaterial = _mat;
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(go.transform, false);
            bar.transform.localPosition = new Vector3(0, 0.15f, 0.5f);
            bar.GetComponent<Renderer>().sharedMaterial = _mat;
            bar.GetComponent<Renderer>().material.color = new Color(0.55f, 0.15f, 0.12f);
            var label = new GameObject("label").AddComponent<TextMesh>();
            label.transform.SetParent(go.transform, false);
            label.transform.localPosition = new Vector3(0, 1.6f, 0);
            label.anchor = TextAnchor.MiddleCenter;
            label.characterSize = 0.22f;
            label.fontSize = 48;
            label.color = InkPalette.Ink;
            label.gameObject.AddComponent<Billboard>();
            Fonts.Apply(label);
            go.transform.position = HexMetrics.ToWorld(u.Position, GameRoot.Instance.MapView.HeightAt(u.Position));
            return new View { Root = go, Flag = fr, Bar = bar.transform, Label = label, Target = go.transform.position };
        }

        private void Update()
        {
            foreach (var v in _views.Values)
                v.Root.transform.position = Vector3.Lerp(v.Root.transform.position, v.Target, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
        }
    }
}
