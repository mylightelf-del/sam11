using System.Collections.Generic;
using Sam11.Core.World;
using UnityEngine;

namespace Sam11.Unity
{
    /// <summary>거점: 성벽·성문 칸은 먹색 담, 정청은 세력색 기단과 이름표. 소유가 바뀌면 색을 바꾼다.</summary>
    public sealed class CityLayer : MonoBehaviour
    {
        private readonly Dictionary<string, (Renderer Base, TextMesh Label)> _views = new Dictionary<string, (Renderer, TextMesh)>();
        private Material _mat;

        private void Start()
        {
            GameRoot.Instance.SessionStarted += Build;
            GameRoot.Instance.Changed += Refresh;
            if (GameRoot.Instance.Session != null) { Build(); Refresh(); }
        }

        private void OnDestroy()
        {
            if (GameRoot.Instance == null) return;
            GameRoot.Instance.SessionStarted -= Build;
            GameRoot.Instance.Changed -= Refresh;
        }

        private void Build()
        {
            foreach (Transform child in transform) Destroy(child.gameObject);
            _views.Clear();
            _mat ??= new Material(Shader.Find("Sam11/InkWash") ?? Shader.Find("Standard"));
            var root = GameRoot.Instance;
            foreach (var c in root.Session.World.Cities.Values)
            {
                var go = new GameObject(c.Id);
                go.transform.SetParent(transform, false);
                float y = root.MapView.HeightAt(c.Placement.Center);
                foreach (var t in c.Placement.Tiles)
                {
                    if (t.Value != TileKind.Wall && t.Value != TileKind.Gate) continue;
                    var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(wall.GetComponent<Collider>());
                    wall.transform.SetParent(go.transform, false);
                    wall.transform.position = HexMetrics.ToWorld(t.Key, root.MapView.HeightAt(t.Key) + 0.25f);
                    wall.transform.localScale = t.Value == TileKind.Gate ? new Vector3(0.9f, 0.7f, 0.9f) : new Vector3(1.2f, 0.5f, 1.2f);
                    var r = wall.GetComponent<Renderer>();
                    r.sharedMaterial = _mat;
                    r.material.color = t.Value == TileKind.Gate ? new Color(0.35f, 0.25f, 0.2f) : new Color(0.22f, 0.21f, 0.2f);
                }
                var keep = GameObject.CreatePrimitive(c.Def.IsCity ? PrimitiveType.Cylinder : PrimitiveType.Cube);
                Destroy(keep.GetComponent<Collider>());
                keep.transform.SetParent(go.transform, false);
                keep.transform.position = HexMetrics.ToWorld(c.Placement.Center, y + 0.4f);
                keep.transform.localScale = c.Def.IsCity ? new Vector3(1.4f, 0.4f, 1.4f) : new Vector3(0.9f, 0.6f, 0.9f);
                var kr = keep.GetComponent<Renderer>();
                kr.sharedMaterial = _mat;

                var label = new GameObject("label").AddComponent<TextMesh>();
                label.transform.SetParent(go.transform, false);
                label.transform.position = HexMetrics.ToWorld(c.Placement.Center, y + 1.6f);
                label.text = L.N(c.Def.Name);
                label.anchor = TextAnchor.MiddleCenter;
                label.characterSize = c.Def.IsCity ? 0.35f : 0.25f;
                label.fontSize = 48;
                label.color = InkPalette.Ink;
                label.gameObject.AddComponent<Billboard>();
                Fonts.Apply(label);
                _views[c.Id] = (kr, label);
            }
        }

        private void Refresh()
        {
            var w = GameRoot.Instance.Session.World;
            foreach (var kv in _views)
            {
                var c = w.Cities[kv.Key];
                var color = c.Owner != null ? InkPalette.Faction(w.Factions[c.Owner].Color) : new Color(0.7f, 0.68f, 0.62f);
                kv.Value.Base.material.color = color;
                kv.Value.Label.text = c.Owner != null ? $"{L.N(c.Def.Name)}\n<size=30>{L.Faction(c.Owner)}</size>" : L.N(c.Def.Name);
            }
        }
    }

    /// <summary>이름표가 늘 카메라를 보게.</summary>
    public sealed class Billboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }
    }
}
