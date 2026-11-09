using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Sam11.Editor
{
    /// <summary>
    /// 출시 빌드. 메뉴(Sam11 > 빌드) 또는 명령줄:
    ///   Unity -batchmode -quit -projectPath Unity -executeMethod Sam11.Editor.BuildScript.BuildWindows
    /// 빌드 전에 tools/unity/sync.sh로 코어·데이터를 넣어 두어야 한다.
    /// </summary>
    public static class BuildScript
    {
        private const string Product = "Sam11";

        [MenuItem("Sam11/빌드/Windows (64비트)")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, $"Builds/Windows/{Product}.exe");

        [MenuItem("Sam11/빌드/macOS")]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, $"Builds/macOS/{Product}.app");

        [MenuItem("Sam11/빌드/Linux")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, $"Builds/Linux/{Product}.x86_64");

        private static void Build(BuildTarget target, string output)
        {
            string data = Path.Combine(Application.dataPath, "StreamingAssets", "data", "base", "rules.json");
            if (!File.Exists(data))
                throw new BuildFailedException("StreamingAssets에 데이터가 없음: tools/unity/sync.sh를 먼저 실행");
            if (!File.Exists(Path.Combine(Application.dataPath, "Plugins", "Sam11", "Sam11.Core.dll")))
                throw new BuildFailedException("Sam11.Core.dll이 없음: tools/unity/sync.sh를 먼저 실행");

            PlayerSettings.productName = "삼국지";
            PlayerSettings.companyName = "Sam11";
            PlayerSettings.bundleVersion = ReadVersion();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                throw new BuildFailedException("빌드 설정에 장면이 없음: 빈 장면 하나를 저장해 File > Build Settings에 넣는다 (Boot가 나머지를 만든다)");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"빌드 실패: {report.summary.result}, 오류 {report.summary.totalErrors}건");
            Debug.Log($"빌드 완료: {output} ({report.summary.totalSize / (1024 * 1024)}MB, {report.summary.totalTime})");
        }

        /// <summary>코어의 GameInfo.Version을 빌드 버전으로 (DLL을 읽어 온다).</summary>
        private static string ReadVersion()
        {
            var type = System.Type.GetType("Sam11.Core.GameInfo, Sam11.Core");
            return type?.GetField("Version")?.GetValue(null) as string ?? "0.0.0";
        }
    }
}
