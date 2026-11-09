using UnityEngine;

namespace Sam11.Unity
{
    /// <summary>한글 글꼴: 운영체제 글꼴에서 찾는다 (정식 글꼴 에셋은 M0에서 정한다).</summary>
    public static class Fonts
    {
        private static Font _korean;

        public static Font Korean => _korean ??= Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "AppleGothic", "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Arial" }, 16);

        public static void Apply(TextMesh t)
        {
            t.font = Korean;
            t.GetComponent<MeshRenderer>().sharedMaterial = Korean.material;
        }
    }
}
