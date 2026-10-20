using System.Collections.Generic;
using System.Text;
using Forgebound.Core.Content;
using Forgebound.Core.Progression;
using Forgebound.Core.Run;
using UnityEngine;
using UnityEngine.UI;

namespace Forgebound.Presentation
{
    public sealed class EventScreen : UiScreen
    {
        protected override void Build()
        {
            var ev = S.Db.Event(S.Run.Run.PendingEventId);
            UiKit.Stretch(UiKit.Panel(Root, "Bg", Theme.Bg).rectTransform);
            RunHud.Build(S, Root);
            if (ev == null) { Flow.Report(S, S.Run.ChooseEventOption(0)); Flow.Continue(S); return; }
            var art = S.Sprites.Find(ev.Art);
            if (art != null)
            {
                var img = UiKit.Icon(Root, art, Color.white, 400);
                UiKit.Anchor(img.rectTransform, 0.05f, 0.35f, 0.4f, 0.85f);
            }
            var title = UiKit.Label(Root, L("event." + ev.Id + ".title"), 48, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, 0.42f, 0.74f, 0.97f, 0.86f);
            var body = UiKit.Label(Root, L("event." + ev.Id + ".body"), 28, Theme.Text, TextAnchor.UpperLeft);
            UiKit.Anchor(body.rectTransform, 0.42f, 0.5f, 0.97f, 0.73f);
            var v = UiKit.VBox(Root, "Options", 12, 0);
            UiKit.Anchor((RectTransform)v.transform, 0.42f, 0.08f, 0.97f, 0.48f);
            for (int i = 0; i < ev.Options.Count; i++)
            {
                int idx = i;
                var o = ev.Options[i];
                bool ok = S.Run.OptionAvailable(o);
                var label = L(o.TextKey);
                if (o.SuccessPermille < 1000) label += "  (" + (o.SuccessPermille / 10) + "%)";
                var b = UiKit.Button(v.transform, label, () =>
                {
                    if (!Flow.Report(S, S.Run.ChooseEventOption(idx))) return;
                    S.Audio.Ui("ui_confirm");
                    Flow.Continue(S);
                }, ok ? Theme.PanelLight : Theme.Disabled, 24);
                b.interactable = ok;
                UiKit.Size(b, -1, 70);
            }
        }
    }

    public sealed class RewardScreen : UiScreen
    {
        protected override void Build()
        {
            UiKit.Stretch(UiKit.Panel(Root, "Bg", Theme.Bg).rectTransform);
            RunHud.Build(S, Root);
            var rewards = S.Run.Run.PendingRewards;
            bool blessing = rewards.Count > 0 && rewards[0].IsBlessing;
            var title = UiKit.Label(Root, L(blessing ? "ui.reward.blessing_title" : "ui.reward.title"), 48, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, 0.1f, 0.76f, 0.9f, 0.88f);
            var h = UiKit.HBox(Root, "Choices", 24, 0);
            UiKit.Anchor((RectTransform)h.transform, 0.08f, 0.22f, 0.92f, 0.72f);
            for (int i = 0; i < rewards.Count; i++)
            {
                int idx = i;
                var r = rewards[i];
                var card = UiKit.Panel(h.transform, "Card", Theme.Panel);
                var v = UiKit.VBox(card.transform, "V", 8, 16);
                UiKit.Stretch((RectTransform)v.transform);
                string name, desc;
                Color accent;
                Sprite icon;
                if (r.IsBlessing)
                {
                    var b = S.Db.Blessing(r.Id);
                    name = S.Loc.Name("blessing", r.Id);
                    desc = string.Join("\n", S.Tips.Blessing(b));
                    accent = b.IsCurse ? Theme.Danger : Theme.Rarity(b.Rarity);
                    icon = S.Sprites.Icon("blessing", r.Id);
                }
                else
                {
                    var cat = r.Category.ToString().ToLowerInvariant();
                    name = S.Loc.Name(cat, r.Id);
                    var tip = new ForgeTooltipHelper(S).Item(r.Category, r.Id);
                    desc = tip;
                    accent = Theme.Rarity(S.Db.ItemRarity(r.Category, r.Id));
                    icon = r.Category == ItemCategory.Piece ? S.Sprites.Piece(r.Id) : S.Sprites.Icon(cat, r.Id);
                }
                var ic = UiKit.Icon(v.transform, icon, Color.white, 140);
                UiKit.Label(v.transform, name, 30, accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                var d = UiKit.Label(v.transform, desc, 20, Theme.Text, TextAnchor.UpperLeft);
                UiKit.Size(d, -1, 200, -1, -1, -1, 1);
                UiKit.Button(v.transform, L("ui.reward.take"), () =>
                {
                    if (Flow.Report(S, S.Run.TakeReward(idx))) { S.Audio.Play("sfx_reward"); Flow.Continue(S); }
                }, Theme.Ember);
            }
            if (!blessing)
            {
                var skip = UiKit.Button(Root, LF("ui.reward.skip", Core.Economy.EconomyRules.SkipRewardGold), () => { if (Flow.Report(S, S.Run.SkipReward())) Flow.Continue(S); });
                UiKit.Anchor((RectTransform)skip.transform, 0.4f, 0.06f, 0.6f, 0.14f);
            }
        }
    }

    /// <summary>Tooltip text for items, shared by screens that are not the ForgeScreen.</summary>
    public sealed class ForgeTooltipHelper
    {
        private readonly Services _s;
        public ForgeTooltipHelper(Services s) { _s = s; }

        public string Item(ItemCategory cat, string id)
        {
            List<string> lines = null;
            switch (cat)
            {
                case ItemCategory.Frame: lines = _s.Tips.Frame(_s.Db.Frame(id)); break;
                case ItemCategory.Core: lines = _s.Tips.Core(_s.Db.Core(id)); break;
                case ItemCategory.Modifier: lines = _s.Tips.Modifier(_s.Db.Modifier(id)); break;
                case ItemCategory.Rune: lines = new List<string> { _s.Tips.Rune(_s.Db.Rune(id)) }; break;
                case ItemCategory.Piece: lines = _s.Tips.Piece(_s.Db.Piece(id)); break;
            }
            return lines == null ? "" : "• " + string.Join("\n• ", lines);
        }
    }

    /// <summary>Post-run recap: outcome, seed, weapons, discoveries, unlocks; quick retry with the same seed.</summary>
    public sealed class RunEndScreen : UiScreen
    {
        protected override void Build()
        {
            var run = S.Run.Run;
            var unlocked = new List<string>();
            if (!run.RecordedInProfile)
            {
                unlocked = S.Profile.RecordRun(S.Db, run, System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"));
                run.RecordedInProfile = true;
                S.SaveProfile();
            }
            S.SaveRun();   // writes the "empty" marker: a finished run can't be continued
            UiKit.Stretch(UiKit.Panel(Root, "Bg", Theme.Bg).rectTransform);
            bool won = run.Outcome == RunOutcome.Victory;
            var title = UiKit.Label(Root, L("ui.end." + run.Outcome), 64, won ? Theme.Gold : Theme.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, 0.1f, 0.84f, 0.9f, 0.97f);
            var sb = new StringBuilder();
            sb.AppendLine(LF("ui.end.summary", S.Loc.Name("forgemaster", run.ForgemasterId), run.Chapter + 1, run.Step, run.BattlesFought, run.TotalDamage, run.Seed, L("difficulty." + Difficulty.Names[run.Difficulty])));
            if (run.DeathReason != null) sb.AppendLine(LF("ui.end.death", DeathText(run.DeathReason)));
            sb.AppendLine();
            sb.AppendLine("<b>" + L("ui.end.weapons") + "</b>");
            foreach (var w in run.Weapons) sb.AppendLine("• " + RunBoardHost.WeaponName(S, w));
            sb.AppendLine();
            sb.AppendLine("<b>" + L("ui.end.timeline") + "</b>");
            foreach (var h in run.History)
                sb.AppendLine((h.Won ? "✓ " : "✗ ") + L("ui.node." + h.Type) + " — " + (h.EncounterId != null ? h.EncounterId : "") + "  HP " + h.HpBefore + "→" + h.HpAfter + "  " + LF("ui.end.dmg", h.DamageDealt));
            if (unlocked.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("<b>" + L("ui.end.unlocked") + "</b>");
                foreach (var u in unlocked) sb.AppendLine("★ " + UnlockText(u));
            }
            var text = UiKit.Label(Root, sb.ToString(), 22, Theme.Text, TextAnchor.UpperLeft);
            text.supportRichText = true;
            UiKit.Anchor(text.rectTransform, 0.08f, 0.15f, 0.92f, 0.83f);
            var h2 = UiKit.HBox(Root, "Buttons", 16);
            UiKit.Anchor((RectTransform)h2.transform, 0.2f, 0.03f, 0.8f, 0.12f);
            ulong seed = run.Seed;
            string fm = run.ForgemasterId;
            int diff = run.Difficulty;
            UiKit.Button(h2.transform, L("ui.end.retry_seed"), () =>
            {
                S.Run = RunController.StartNew(S.Db, new StartOptions { ForgemasterId = fm, Seed = seed, Difficulty = diff, FavoriteRecipe = S.Profile.FavoriteRecipe });
                Flow.Continue(S);
            }, Theme.Ember);
            UiKit.Button(h2.transform, L("ui.end.new_run"), () => { S.Run = null; S.Ui.Show(() => new ForgemasterSelectScreen()); });
            UiKit.Button(h2.transform, L("ui.end.title"), () => { S.Run = null; S.Ui.Show(() => new TitleScreen()); });
            S.Audio.Stinger(won ? "sting_victory" : "sting_defeat");
        }

        private string DeathText(string reason)
        {
            var p = reason.Split(':');
            if (p.Length == 2 && S.Db.Enemy(p[1]) != null) return S.Loc.Name("enemy", p[1]);
            if (p.Length == 2 && p[0] == "event") return L("event." + p[1] + ".title");
            return L("death." + p[0]);
        }

        private string UnlockText(string u)
        {
            var p = u.Split(':');
            if (p[0] == "forgemaster") return LF("ui.end.unlock_fm", S.Loc.Name("forgemaster", p[1]));
            return LF("ui.end.achievement", L("achievement." + p[1]));
        }
    }

    /// <summary>
    /// Onboarding by doing: one contextual hint per concept, shown the first time it becomes relevant, dismissible,
    /// and disabled entirely by the "hints" setting. Seen hints are stored in the profile (as achievements "hint:*").
    /// </summary>
    public static class Hints
    {
        public static void Show(Services s, string id)
        {
            if (!s.Profile.Settings.HintsEnabled) return;
            string key = "hint:" + id;
            if (s.Profile.Achievements.Contains(key)) return;
            s.Profile.Achievements.Add(key);
            s.SaveProfile();
            s.Ui.Toast(s.L("hint." + id), 6f);
        }
    }
}
