using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Common;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sam11.Unity
{
    /// <summary>
    /// 전국 헥스 맵을 덩어리(32×32칸) 메시로 그린다. 칸 윗면 + 이웃보다 높을 때 옆면.
    /// 덩어리마다 MeshCollider를 붙여 마우스가 가리키는 칸을 찾는다. 강조(이동 범위 등)는 따로 얇은 메시.
    /// </summary>
    public sealed class MapView : MonoBehaviour
    {
        private const int Chunk = 32;
        private Material _terrainMat;
        private Material _overlayMat;
        private GameObject _overlay;
        private Mesh _overlayMesh;

        private void Start()
        {
            GameRoot.Instance.SessionStarted += Build;
            if (GameRoot.Instance.Session != null) Build();
        }

        private void OnDestroy()
        {
            if (GameRoot.Instance != null) GameRoot.Instance.SessionStarted -= Build;
        }

        public float HeightAt(HexCoord h)
        {
            var t = GameRoot.Instance.Map.TerrainAt(h);
            return t == "" ? 0f : HexMetrics.Height(t);
        }

        private void Build()
        {
            foreach (Transform child in transform) Destroy(child.gameObject);
            _terrainMat ??= new Material(Shader.Find("Sam11/InkWash") ?? Shader.Find("Sprites/Default"));
            _overlayMat ??= new Material(Shader.Find("Sprites/Default"));

            var map = GameRoot.Instance.Map;
            var groups = map.Terrain.Keys.GroupBy(h => (Mathf.FloorToInt(h.Q / (float)Chunk), Mathf.FloorToInt(h.R / (float)Chunk)));
            foreach (var g in groups)
            {
                var mesh = BuildChunk(g.ToList());
                var go = new GameObject($"chunk {g.Key.Item1},{g.Key.Item2}");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = _terrainMat;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            _overlay = new GameObject("overlay");
            _overlay.transform.SetParent(transform, false);
            _overlayMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            _overlay.AddComponent<MeshFilter>().sharedMesh = _overlayMesh;
            _overlay.AddComponent<MeshRenderer>().sharedMaterial = _overlayMat;
        }

        private Mesh BuildChunk(List<HexCoord> cells)
        {
            var map = GameRoot.Instance.Map;
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            foreach (var h in cells)
            {
                string t = map.TerrainAt(h);
                float y = HexMetrics.Height(t);
                var color = InkPalette.Jitter(InkPalette.Terrain(t), h);
                var c = HexMetrics.ToWorld(h, y);
                int start = verts.Count;
                verts.Add(c);
                colors.Add(color);
                for (int i = 0; i < 6; i++)
                {
                    verts.Add(c + HexMetrics.Corner(i) * 0.98f);
                    colors.Add(color * 0.97f);
                }
                for (int i = 0; i < 6; i++)
                {
                    tris.Add(start);
                    tris.Add(start + 1 + (i + 1) % 6);
                    tris.Add(start + 1 + i);
                }
                // 옆면: 이웃이 더 낮으면 절벽을 내린다 (먹이 짙어지듯 어둡게)
                for (int i = 0; i < 6; i++)
                {
                    var n = h + HexCoord.Directions[DirectionForEdge(i)];
                    float ny = map.Terrain.ContainsKey(n) ? HexMetrics.Height(map.TerrainAt(n)) : -0.6f;
                    if (ny >= y - 0.01f) continue;
                    var a = c + HexMetrics.Corner(i) * 0.98f;
                    var b = c + HexMetrics.Corner((i + 1) % 6) * 0.98f;
                    int s = verts.Count;
                    var dark = Color.Lerp(color, InkPalette.Ink, 0.45f);
                    verts.Add(a); verts.Add(b);
                    verts.Add(new Vector3(a.x, ny, a.z)); verts.Add(new Vector3(b.x, ny, b.z));
                    colors.Add(dark); colors.Add(dark); colors.Add(dark * 0.8f); colors.Add(dark * 0.8f);
                    tris.AddRange(new[] { s, s + 1, s + 2, s + 1, s + 3, s + 2 });
                }
            }
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int[] _edgeDir;

        /// <summary>꼭짓점 i와 i+1 사이 변이 마주하는 이웃 방향 (HexCoord.Directions 순서).</summary>
        private static int DirectionForEdge(int i)
        {
            if (_edgeDir == null)
            {
                _edgeDir = new int[6];
                for (int e = 0; e < 6; e++) _edgeDir[e] = ComputeEdge(e);
            }
            return _edgeDir[i];
        }

        private static int ComputeEdge(int i)
        {
            // 꼭짓점 각도 -30+60i, 변의 바깥 방향 각도 60i (z가 남쪽으로 뒤집혀 있으므로 r 방향을 반대로 센다)
            var mid = (HexMetrics.Corner(i) + HexMetrics.Corner((i + 1) % 6)) * 0.5f;
            int best = 0;
            float bestDot = float.MinValue;
            for (int d = 0; d < 6; d++)
            {
                var dir = HexMetrics.ToWorld(HexCoord.Directions[d]);
                float dot = Vector3.Dot(dir.normalized, mid.normalized);
                if (dot > bestDot) { bestDot = dot; best = d; }
            }
            return best;
        }

        /// <summary>강조할 칸 (이동 범위, 공격 대상 등). 같은 색 칸끼리 묶어 준다.</summary>
        public void Highlight(IEnumerable<(HexCoord Hex, Color Color)> cells)
        {
            if (_overlayMesh == null) return;
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            foreach (var (h, col) in cells)
            {
                var c = HexMetrics.ToWorld(h, HeightAt(h) + 0.03f);
                int s = verts.Count;
                verts.Add(c);
                colors.Add(col);
                for (int i = 0; i < 6; i++) { verts.Add(c + HexMetrics.Corner(i) * 0.8f); colors.Add(col); }
                for (int i = 0; i < 6; i++) { tris.Add(s); tris.Add(s + 1 + (i + 1) % 6); tris.Add(s + 1 + i); }
            }
            _overlayMesh.Clear();
            _overlayMesh.SetVertices(verts);
            _overlayMesh.SetColors(colors);
            _overlayMesh.SetTriangles(tris, 0);
        }

        public void ClearHighlight() => Highlight(Enumerable.Empty<(HexCoord, Color)>());
    }
}
