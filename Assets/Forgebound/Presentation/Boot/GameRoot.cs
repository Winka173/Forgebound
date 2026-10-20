using UnityEngine;

namespace Forgebound.Presentation
{
    /// <summary>
    /// Entry point. Created automatically after the first scene loads (works with an empty scene, so there are no
    /// hand-authored scene/prefab references that can silently break). Owns services and the UI root.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public static Services Services { get; private set; }
        public static GameRoot Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (Instance != null) return;
            var go = new GameObject("Forgebound");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<GameRoot>();
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Input.multiTouchEnabled = false;
            Services = Services.Create(gameObject);
            ApplySettings();
            BattleWorld.Create();
            Services.Ui = UiRoot.Create(Services);
            Services.Ui.Show(() => new TitleScreen());
            foreach (var w in Services.StartupWarnings) Debug.LogWarning("[Forgebound] " + w);
        }

        public static void ApplySettings()
        {
            var s = Services.Profile.Settings;
            Services.Loc.Language = s.Language;
            UiKit.TextScale = s.TextScalePercent / 100f;
            Application.targetFrameRate = s.TargetFps30 ? 30 : 60;
            QualitySettings.vSyncCount = 0;
            Services.Audio.ApplyVolumes(s);
        }

        private void OnApplicationPause(bool paused)
        {
            // Mobile suspend: persist at the current safe boundary.
            if (paused && Services != null) { Services.SaveRun(); Services.SaveProfile(); }
        }

        private void OnApplicationQuit()
        {
            if (Services != null) { Services.SaveRun(); Services.SaveProfile(); }
        }
    }
}
