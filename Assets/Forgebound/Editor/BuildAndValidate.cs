using System;
using System.IO;
using System.Linq;
using Forgebound.Core.Content;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Forgebound.EditorTools
{
    /// <summary>
    /// Batch-mode entry points (see docs/BUILD.md):
    ///   Unity -batchmode -quit -projectPath . -executeMethod Forgebound.EditorTools.BuildScript.BuildWindows
    ///   Unity -batchmode -quit -projectPath . -executeMethod Forgebound.EditorTools.BuildScript.BuildAndroid
    ///   Unity -batchmode -quit -projectPath . -executeMethod Forgebound.EditorTools.BuildScript.ExportIOS
    ///   Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults reports/unity_editmode.xml
    /// The game boots from an empty scene (GameRoot auto-boot), so the build only needs the bootstrap scene created here.
    /// NOTE: written against the Unity 2022.3 editor API but not executed in this repository's CI environment (no Unity).
    /// </summary>
    public static class BuildScript
    {
        public const string BootScene = "Assets/Forgebound/Scenes/Boot.unity";

        [MenuItem("Forgebound/Create Boot Scene")]
        public static void EnsureBootScene()
        {
            if (File.Exists(BootScene)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(BootScene));
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, BootScene);
        }

        private static string[] Scenes()
        {
            EnsureBootScene();
            return new[] { BootScene };
        }

        private static void Validate()
        {
            var report = ValidateMenu.RunValidation(false);
            if (report.ErrorCount > 0) throw new Exception("Content validation failed with " + report.ErrorCount + " errors; see Console / reports/content.");
        }

        private static void Build(BuildTarget target, BuildTargetGroup group, string path, BuildOptions options = BuildOptions.None)
        {
            Validate();
            PlayerSettings.productName = "Forgebound";
            PlayerSettings.companyName = "Forgebound";
            PlayerSettings.SetApplicationIdentifier(group, "com.forgebound.game");
            var opts = new BuildPlayerOptions { scenes = Scenes(), locationPathName = path, target = target, targetGroup = group, options = options };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[Forgebound] build " + target + ": " + report.summary.result + " size=" + report.summary.totalSize + " errors=" + report.summary.totalErrors);
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        [MenuItem("Forgebound/Build/Windows")]
        public static void BuildWindows() { Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, "Builds/Windows/Forgebound.exe"); }

        /// <summary>Unsigned/debug-keystore AAB unless signing env vars are provided (docs/BUILD.md lists them).</summary>
        [MenuItem("Forgebound/Build/Android (AAB)")]
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.buildAppBundle = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            var ks = Environment.GetEnvironmentVariable("FORGEBOUND_KEYSTORE");
            if (!string.IsNullOrEmpty(ks))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = ks;
                PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("FORGEBOUND_KEYSTORE_PASS");
                PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("FORGEBOUND_KEY_ALIAS");
                PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("FORGEBOUND_KEY_PASS");
            }
            Build(BuildTarget.Android, BuildTargetGroup.Android, "Builds/Android/Forgebound.aab");
        }

        /// <summary>Exports an Xcode project; signing/archiving happens on macOS (docs/BUILD.md).</summary>
        [MenuItem("Forgebound/Build/iOS (Xcode export)")]
        public static void ExportIOS() { Build(BuildTarget.iOS, BuildTargetGroup.iOS, "Builds/iOS"); }
    }

    public static class ValidateMenu
    {
        [MenuItem("Forgebound/Validate Content")]
        public static void Menu() { RunValidation(true); }

        public static ValidationReport RunValidation(bool log)
        {
            var db = ContentDatabase.Load(new Forgebound.Presentation.ResourcesContentSource());
            var report = ContentValidator.Validate(db);
            if (log)
                foreach (var i in report.Issues)
                {
                    if (i.Severity == "error") Debug.LogError("[Forgebound] " + i); else Debug.LogWarning("[Forgebound] " + i);
                }
            Directory.CreateDirectory("reports/content");
            File.WriteAllText("reports/content/content_validation_unity.json", JsonWriter.Write(report.ToJson(), true));
            Debug.Log("[Forgebound] content validation: " + report.ErrorCount + " errors, " + report.WarningCount + " warnings");
            return report;
        }

        [MenuItem("Forgebound/Simulate Seed (Bot Run)")]
        public static void SimulateSeed()
        {
            var db = ContentDatabase.Load(new Forgebound.Presentation.ResourcesContentSource());
            var c = Core.Run.RunController.StartNew(db, new Core.Run.StartOptions { ForgemasterId = db.Forgemasters[0].Id, Seed = 1 });
            var bot = new Core.Balance.BotAgent(c, Core.Balance.BotPolicy.Balanced, 1) { Verbose = true };
            bot.PlayToEnd();
            Debug.Log("[Forgebound] bot run: " + c.Run.Outcome + " chapter " + (c.Run.Chapter + 1) + "\n" + string.Join("\n", bot.Log.Take(200)));
        }
    }
}
