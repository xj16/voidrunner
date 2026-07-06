using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VoidRunner.EditorTools
{
    /// <summary>
    /// Command-line build entry points invoked by CI:
    ///   Unity -batchmode -quit -projectPath . -executeMethod VoidRunner.EditorTools.BuildScript.BuildLinux
    ///
    /// These are only compiled in the editor. The GitHub Actions "unity-build" job (see ci.yml) uses
    /// GameCI to run them on a licensed runner. They are defensive: they log clearly and set a
    /// non-zero exit code on failure so CI goes red.
    /// </summary>
    public static class BuildScript
    {
        private const string OutputRoot = "Build";

        private static string[] Scenes =>
            EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        [MenuItem("VoidRunner/Build/Standalone (current platform)")]
        public static void BuildCurrent()
        {
            Run(EditorUserBuildSettings.activeBuildTarget, "VoidRunner");
        }

        public static void BuildLinux() => Run(BuildTarget.StandaloneLinux64, "VoidRunner");
        public static void BuildWindows() => Run(BuildTarget.StandaloneWindows64, "VoidRunner.exe");

        private static void Run(BuildTarget target, string exeName)
        {
            var scenes = Scenes;
            if (scenes.Length == 0)
            {
                // Fall back to the known main scene so a fresh checkout still builds.
                scenes = new[] { "Assets/Scenes/Main.unity" };
            }

            string outDir = System.IO.Path.Combine(OutputRoot, target.ToString());
            System.IO.Directory.CreateDirectory(outDir);
            string outPath = System.IO.Path.Combine(outDir, exeName);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outPath,
                target = target,
                options = BuildOptions.None
            };

            Debug.Log($"[VoidRunner] Building {target} → {outPath} with {scenes.Length} scene(s).");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[VoidRunner] Build succeeded: {summary.totalSize} bytes.");
            }
            else
            {
                Debug.LogError($"[VoidRunner] Build failed: {summary.result} ({summary.totalErrors} errors).");
                EditorApplication.Exit(1);
            }
        }
    }
}
