using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SmallTown.EditorTools
{
    /// <summary>
    /// Command-line and menu builds. Headless:
    ///   Unity -batchmode -quit -buildTarget Android -projectPath . -executeMethod SmallTown.EditorTools.BuildScript.BuildAndroidDevelopment
    /// </summary>
    public static class BuildScript
    {
        public const string AndroidOutput = "Builds/Android/SmallTownBigTrouble-dev.apk";

        [MenuItem("Small Town/Build/Android Development APK (Quest)")]
        public static void BuildAndroidDevelopment()
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Fail("No scenes in Build Settings. Run Small Town > Setup > Run Everything first.");
                return;
            }

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AndroidOutput));
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = AndroidOutput,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log("[SmallTown] Android build " + summary.result + ": " + summary.outputPath + " (" +
                      (summary.totalSize / (1024f * 1024f)).ToString("0.0") + " MB, " + summary.totalErrors + " errors, " +
                      summary.totalTime.TotalSeconds.ToString("0") + " s)");
            if (summary.result != BuildResult.Succeeded) Fail("Android build failed.");
        }

        static void Fail(string message)
        {
            Debug.LogError("[SmallTown] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
