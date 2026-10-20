using System;
using System.Collections.Generic;
using Forgebound.Core.Content;
using Forgebound.Core.Run;
using UnityEngine;
using UnityEngine.UI;

namespace Forgebound.Presentation
{
    /// <summary>Routes to the screen matching the run phase and autosaves at that safe boundary.</summary>
    public static class Flow
    {
        public static void Continue(Services s)
        {
            var run = s.Run;
            if (run == null) { s.Ui.Show(() => new TitleScreen()); return; }
            s.SaveRun();
            switch (run.Run.Phase)
            {
                case RunPhase.Finished: s.Ui.Show(() => new RunEndScreen()); break;
                case RunPhase.ChoosingNode: s.Ui.Show(() => new MapScreen()); break;
                case RunPhase.InEvent: s.Ui.Show(() => new EventScreen()); break;
                case RunPhase.InReward: s.Ui.Show(() => new RewardScreen()); break;
                default: s.Ui.Show(() => new ForgeScreen()); break;   // PreBattle, Shop, Forge, Rest
            }
        }

        /// <summary>Show an action error as a readable toast instead of failing silently.</summary>
        public static bool Report(Services s, ActionResult r)
        {
            if (r.Ok) return true;
            s.Ui.Toast(s.Loc.Format("error." + r.Error, r.Detail ?? ""));
            s.Audio.Ui("ui_error");
            return false;
        }
    }

    public sealed class TitleScreen : UiScreen
    {
        protected override void Build()
        {
            var bg = UiKit.Panel(Root, "Bg", Theme.Bg);
            UiKit.Stretch(bg.rectTransform);
            var title = UiKit.Label(Root, "FORGEBOUND", 120, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, 0.1f, 0.68f, 0.9f, 0.92f);
            var sub = UiKit.Label(Root, L("ui.title.tagline"), 32, Theme.Muted, TextAnchor.MiddleCenter);
            UiKit.Anchor(sub.rectTransform, 0.1f, 0.6f, 0.9f, 0.68f);
            var v = UiKit.VBox(Root, "Menu", 14, 0);
            UiKit.Anchor((RectTransform)v.transform, 0.36f, 0.08f, 0.64f, 0.58f);
            if (S.Run != null || S.HasSavedRun())
                UiKit.Button(v.transform, L("ui.title.continue"), () => { if (S.Run != null || S.ResumeRun()) Flow.Continue(S); else S.Ui.Toast(L("ui.title.resume_failed")); }, Theme.Ember);
            UiKit.Button(v.transform, L("ui.title.new_run"), () => S.Ui.Show(() => new ForgemasterSelectScreen(), true));
            UiKit.Button(v.transform, L("ui.title.workshop"), () => S.Ui.Show(() => new WorkshopScreen(), true));
            UiKit.Button(v.transform, L("ui.title.codex"), () => S.Ui.Show(() => new CodexScreen(), true));
            UiKit.Button(v.transform, L("ui.title.settings"), () => S.Ui.Show(() => new SettingsScreen(), true));
            if (Application.platform != RuntimePlatform.IPhonePlayer)
                UiKit.Button(v.transform, L("ui.title.quit"), () => { S.SaveProfile(); Application.Quit(); });
            var ver = UiKit.Label(Root, "v" + Application.version + "  •  " + LF("ui.title.stats", S.Profile.Runs, S.Profile.Wins), 20, Theme.Muted, TextAnchor.LowerRight);
            UiKit.Anchor(ver.rectTransform, 0.5f, 0.01f, 0.99f, 0.06f);
            S.Audio.Music("mus_workshop");
            if (S.StartupWarnings.Count > 0) S.Ui.Toast(LF("ui.title.warnings", S.StartupWarnings.Count), 4f);
        }
    }

    public sealed class ForgemasterSelectScreen : UiScreen
    {
        private string _selected;
        private int _difficulty = 1;
        private Text _detail;
        private InputField _seed;
        private readonly Dictionary<string, Image> _cards = new Dictionary<string, Image>();
        private Button _diffButton;

        protected override void Build()
        {
            UiKit.Stretch(UiKit.Panel(Root, "Bg", Theme.Bg).rectTransform);
            var header = UiKit.Label(Root, L("ui.select.title"), 48, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(header.rectTransform, 0.03f, 0.88f, 0.7f, 0.97f);
            var grid = UiKit.Rect(Root, "Grid");
            UiKit.Anchor(grid, 0.03f, 0.2f, 0.62f, 0.86f);
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(250, 300);
            gl.spacing = new Vector2(14, 14);
            foreach (var fm in S.Db.Forgemasters)
            {
                var id = fm.Id;
                bool unlocked = S.Profile.IsUnlocked(id);
                var card = UiKit.Panel(grid, id, unlocked ? Theme.Panel : Theme.Disabled);
                _cards[id] = card;
                var btn = card.gameObject.AddComponent<Button>();
                btn.onClick.AddListener(() => Select(id));
                var clip = S.Sprites.CharacterClip(id, "idle");
                var icon = UiKit.Icon(card.transform, clip[0], unlocked ? Color.white : new Color(0.2f, 0.2f, 0.2f), 170);
                UiKit.Anchor(icon.rectTransform, 0.1f, 0.3f, 0.9f, 0.95f);
                var name = UiKit.Label(card.transform, S.Loc.Name("forgemaster", id), 26, unlocked ? Theme.Text : Theme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Anchor(name.rectTransform, 0.02f, 0.15f, 0.98f, 0.3f);
                var hint = UiKit.Label(card.transform, unlocked ? "" : L("ui.unlock." + fm.Unlock), 18, Theme.Muted, TextAnchor.MiddleCenter);
                UiKit.Anchor(hint.rectTransform, 0.02f, 0.0f, 0.98f, 0.15f);
            }
            var side = UiKit.Panel(Root, "Detail", Theme.Panel);
            UiKit.Anchor(side.rectTransform, 0.64f, 0.2f, 0.97f, 0.86f);
            _detail = UiKit.Label(side.transform, "", 24, Theme.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(_detail.rectTransform, 18, 18, 18, 18);

            var bottom = UiKit.HBox(Root, "Bottom", 14);
            UiKit.Anchor((RectTransform)bottom.transform, 0.03f, 0.04f, 0.97f, 0.15f);
            UiKit.Button(bottom.transform, L("ui.back"), () => S.Ui.Back());
            _diffButton = UiKit.Button(bottom.transform, "", () => { _difficulty = (_difficulty + 1) % 4; RefreshDifficulty(); });
            var seedBox = UiKit.Panel(bottom.transform, "Seed", Theme.PanelLight);
            _seed = seedBox.gameObject.AddComponent<InputField>();
            var seedText = UiKit.Label(seedBox.transform, "", 26, Theme.Text, TextAnchor.MiddleCenter);
            UiKit.Stretch(seedText.rectTransform, 10, 4, 10, 4);
            var ph = UiKit.Label(seedBox.transform, L("ui.select.seed_placeholder"), 22, Theme.Muted, TextAnchor.MiddleCenter);
            UiKit.Stretch(ph.rectTransform, 10, 4, 10, 4);
            _seed.textComponent = seedText;
            _seed.placeholder = ph;
            _seed.contentType = InputField.ContentType.IntegerNumber;
            UiKit.Button(bottom.transform, L("ui.select.start"), Start, Theme.Ember);
            RefreshDifficulty();
            foreach (var fm in S.Db.Forgemasters) if (S.Profile.IsUnlocked(fm.Id)) { Select(fm.Id); break; }
        }

        private void RefreshDifficulty()
        {
            UiKit.SetLabel(_diffButton, LF("ui.select.difficulty", L("difficulty." + Difficulty.Names[_difficulty])));
        }

        private void Select(string id)
        {
            _selected = id;
            foreach (var kv in _cards) kv.Value.color = kv.Key == id ? Theme.PanelLight : (S.Profile.IsUnlocked(kv.Key) ? Theme.Panel : Theme.Disabled);
            var fm = S.Db.Forgemaster(id);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>" + S.Loc.Name("forgemaster", id) + "</b>");
            sb.AppendLine(L("forgemaster." + id + ".desc"));
            sb.AppendLine();
            foreach (var line in S.Tips.Forgemaster(fm)) sb.AppendLine("• " + line);
            sb.AppendLine();
            sb.Append(L("ui.select.starts_with")).Append(' ').Append(S.Loc.Name("frame", fm.StartFrame));
            if (fm.StartCore != null) sb.Append(" + ").Append(S.Loc.Name("core", fm.StartCore));
            _detail.supportRichText = true;
            _detail.text = sb.ToString();
        }

        private void Start()
        {
            if (_selected == null || !S.Profile.IsUnlocked(_selected)) { S.Ui.Toast(L("ui.select.locked")); return; }
            ulong seed;
            if (string.IsNullOrEmpty(_seed.text) || !ulong.TryParse(_seed.text, out seed)) seed = (ulong)DateTime.UtcNow.Ticks & 0xFFFFFFFFUL;
            S.Run = RunController.StartNew(S.Db, new StartOptions { ForgemasterId = _selected, Seed = seed, Difficulty = _difficulty, FavoriteRecipe = S.Profile.FavoriteRecipe });
            S.Audio.Stinger("sting_run_start");
            Flow.Continue(S);
        }
    }

    public sealed class SettingsScreen : UiScreen
    {
        private RectTransform _list;

        protected override void Build()
        {
            UiKit.Stretch(UiKit.Panel(Root, "Bg", Theme.Bg).rectTransform);
            var header = UiKit.Label(Root, L("ui.settings.title"), 48, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(header.rectTransform, 0.05f, 0.88f, 0.8f, 0.97f);
            ScrollRect sr;
            _list = UiKit.ScrollList(Root, "List", out sr);
            UiKit.Anchor((RectTransform)_list.parent.parent, 0.2f, 0.14f, 0.8f, 0.86f);
            Rebuild();
            var back = UiKit.Button(Root, L("ui.back"), Leave);
            UiKit.Anchor((RectTransform)back.transform, 0.4f, 0.03f, 0.6f, 0.11f);
        }

        private void Rebuild()
        {
            UiKit.Clear(_list);
            var st = S.Profile.Settings;
            Row(L("ui.settings.language"), st.Language == "vi" ? "Tiếng Việt" : "English", () => { st.Language = st.Language == "vi" ? "en" : "vi"; Apply(); });
            Row(L("ui.settings.text_scale"), st.TextScalePercent + "%", () => { st.TextScalePercent = st.TextScalePercent >= 150 ? 80 : st.TextScalePercent + 10; Apply(); });
            Row(L("ui.settings.reduced_flashes"), OnOff(st.ReducedFlashes), () => { st.ReducedFlashes = !st.ReducedFlashes; Apply(); });
            Row(L("ui.settings.shake"), st.ScreenShakePercent + "%", () => { st.ScreenShakePercent = st.ScreenShakePercent >= 100 ? 0 : st.ScreenShakePercent + 20; Apply(); });
            Row(L("ui.settings.colorblind"), L("ui.settings.cb." + st.ColorblindMode), () =>
            {
                var modes = new[] { "none", "protan", "deutan", "tritan" };
                st.ColorblindMode = modes[(Array.IndexOf(modes, st.ColorblindMode) + 1) % modes.Length];
                Apply();
            });
            Row(L("ui.settings.speed"), st.DefaultBattleSpeed + "x", () => { st.DefaultBattleSpeed = st.DefaultBattleSpeed == 1 ? 2 : 1; Apply(); });
            Row(L("ui.settings.particles"), st.ParticleBudgetPercent + "%", () => { st.ParticleBudgetPercent = st.ParticleBudgetPercent >= 100 ? 25 : st.ParticleBudgetPercent + 25; Apply(); });
            Row(L("ui.settings.fps30"), OnOff(st.TargetFps30), () => { st.TargetFps30 = !st.TargetFps30; Apply(); });
            Row(L("ui.settings.hints"), OnOff(st.HintsEnabled), () => { st.HintsEnabled = !st.HintsEnabled; Apply(); });
            Row(L("ui.settings.skip_forge"), OnOff(st.SkipForgeAnimations), () => { st.SkipForgeAnimations = !st.SkipForgeAnimations; Apply(); });
            Row(L("ui.settings.master"), st.MasterVolume + "%", () => { st.MasterVolume = (st.MasterVolume + 10) % 110; Apply(); });
            Row(L("ui.settings.music"), st.MusicVolume + "%", () => { st.MusicVolume = (st.MusicVolume + 10) % 110; Apply(); });
            Row(L("ui.settings.sfx"), st.SfxVolume + "%", () => { st.SfxVolume = (st.SfxVolume + 10) % 110; Apply(); });
        }

        private string OnOff(bool b) { return L(b ? "ui.on" : "ui.off"); }

        private void Apply()
        {
            GameRoot.ApplySettings();
            S.SaveProfile();
            S.Ui.Show(() => new SettingsScreen(), false);   // rebuild with new language/text scale
        }

        private void Row(string label, string value, Action onClick)
        {
            var h = UiKit.HBox(_list, "Row", 12);
            UiKit.Size(h, -1, UiKit.MinTouch + 8);
            var l = UiKit.Label(h.transform, label, 28, Theme.Text);
            UiKit.Size(l, -1, -1, -1, -1, 2);
            var b = UiKit.Button(h.transform, value, onClick);
            UiKit.Size(b, 260, -1, -1, -1, 1);
        }

        private void Leave()
        {
            S.SaveProfile();
            // Settings rebuilds itself on every change, so return explicitly instead of relying on history.
            if (S.Run != null && S.Run.Run.Phase != Core.Run.RunPhase.Finished) S.Ui.Show(() => new PauseMenuScreen());
            else S.Ui.Show(() => new TitleScreen());
        }

        public override bool OnBack() { Leave(); return true; }
    }
}
