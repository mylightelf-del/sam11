using UnityEngine;

namespace Sam11.Unity
{
    /// <summary>
    /// 장면 파일 없이 시작한다: 빈 장면에서 Play를 누르면 GameRoot를 만든다.
    /// (장면에 GameRoot를 직접 놓아 두었으면 그것을 쓴다.)
    /// </summary>
    public static class Boot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (Object.FindObjectOfType<GameRoot>() != null) return;
            new GameObject("Sam11").AddComponent<GameRoot>();
        }
    }
}
