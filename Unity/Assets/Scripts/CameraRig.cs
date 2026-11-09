using Sam11.Core.Common;
using UnityEngine;

namespace Sam11.Unity
{
    /// <summary>전략 시점 카메라: WASD·방향키·화면 가장자리로 이동, 휠로 확대, Q/E로 회전, 가운데 버튼 끌기로 이동.</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public static CameraRig Instance { get; private set; }
        private Camera _cam;
        private Vector3 _target;
        private float _distance = 40f;
        private float _yaw;
        private const float Pitch = 55f;

        public static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("CameraRig");
            Instance = go.AddComponent<CameraRig>();
            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.transform.SetParent(go.transform, false);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = InkPalette.Paper;
            cam.farClipPlane = 1000f;
            Instance._cam = cam;
            // 먹이 옅어지듯 먼 곳은 종이색으로
            RenderSettings.fog = true;
            RenderSettings.fogColor = InkPalette.Paper;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 60f;
            RenderSettings.fogEndDistance = 220f;
            if (Object.FindObjectOfType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                sun.color = new Color(1f, 0.97f, 0.9f);
            }
        }

        public void Focus(HexCoord h) => _target = HexMetrics.ToWorld(h);

        private void Update()
        {
            float speed = _distance * 1.2f * Time.unscaledDeltaTime;
            var fwd = Quaternion.Euler(0, _yaw, 0) * Vector3.forward;
            var right = Quaternion.Euler(0, _yaw, 0) * Vector3.right;
            var move = Vector3.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move += fwd;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move -= fwd;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += right;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move -= right;
            var m = Input.mousePosition;
            if (Application.isFocused && !Hud.PointerOverUi)
            {
                if (m.x <= 2) move -= right;
                if (m.x >= Screen.width - 2) move += right;
                if (m.y <= 2) move -= fwd;
                if (m.y >= Screen.height - 2) move += fwd;
            }
            _target += move * speed;
            if (Input.GetMouseButton(2))
                _target -= (right * Input.GetAxis("Mouse X") + fwd * Input.GetAxis("Mouse Y")) * _distance * 0.05f;
            if (!Hud.PointerOverUi) _distance = Mathf.Clamp(_distance * (1f - Input.mouseScrollDelta.y * 0.1f), 6f, 260f);
            if (Input.GetKey(KeyCode.Q)) _yaw -= 90f * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.E)) _yaw += 90f * Time.unscaledDeltaTime;

            var rot = Quaternion.Euler(Pitch, _yaw, 0f);
            _cam.transform.position = _target - rot * Vector3.forward * _distance;
            _cam.transform.rotation = rot;
        }

        /// <summary>마우스가 가리키는 칸 (맵 콜라이더에 맞으면 그 점, 아니면 바닥 평면).</summary>
        public bool PointerHex(out HexCoord hex)
        {
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 2000f))
            {
                hex = HexMetrics.FromWorld(hit.point);
                return true;
            }
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float d))
            {
                hex = HexMetrics.FromWorld(ray.GetPoint(d));
                return true;
            }
            hex = default;
            return false;
        }

        public Vector3 WorldToScreen(Vector3 p) => _cam.WorldToScreenPoint(p);
        public float Distance => _distance;
    }
}
