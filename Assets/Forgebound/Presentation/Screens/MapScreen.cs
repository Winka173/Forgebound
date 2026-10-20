using System.Collections.Generic;
using Forgebound.Core.Content;
using Forgebound.Core.Run;
using UnityEngine;
using UnityEngine.UI;

namespace Forgebound.Presentation
{
    /// <summary>
    /// Route map for the current chapter: rows bottom→top, edges, node previews (type, reward class, enemy roster / event
    /// title, risk). Only reachable nodes are clickable. Run status bar shows HP, gold, essence, weapons, blessings.
    /// </summary>
    public sealed class MapScreen : UiScreen
    {
        private RectTransform _map;
        private Text _preview;

        protected override void Build()
        {
            var run = S.Run.Run;
            var biome = S.Db.Biome(run.CurrentMap.Biome);
            UiKit.Stretch(UiKit.Panel(Root, "Bg", biome != null && biome.Palette.Count > 0 ? Color.Lerp(Theme.Bg, Theme.Hex(biome.Palette[0]), 0.6f) : Theme.Bg).rectTransform);
            RunHud.Build(S, Root);
            var title = UiKit.Label(Root, LF("ui.map.chapter", run.Chapter + 1, RunState.ChapterCount, S.Loc.Name("biome", run.CurrentMap.Biome)), 40, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, 0.02f, 0.82f, 0.6f, 0.9f);
            _map = UiKit.Rect(Root, "Map");
            UiKit.Anchor(_map, 0.04f, 0.04f, 0.64f, 0.8f);
            var side = UiKit.Panel(Root, "Preview", Theme.Panel);
            UiKit.Anchor(side.rectTransform, 0.67f, 0.18f, 0.98f, 0.8f);
            _preview = UiKit.Label(side.transform, L("ui.map.pick_hint"), 24, Theme.Text, TextAnchor.UpperLeft);
            _preview.supportRichText = true;
            UiKit.Stretch(_preview.rectTransform, 18, 18, 18, 18);
            var buttons = UiKit.HBox(Root, "Buttons", 12);
            UiKit.Anchor((RectTransform)buttons.transform, 0.67f, 0.04f, 0.98f, 0.15f);
            UiKit.Button(buttons.transform, L("ui.map.loadout"), () => S.Ui.Show(() => new ForgeScreen(true), true));
            UiKit.Button(buttons.transform, L("ui.map.menu"), () => S.Ui.Show(() => new PauseMenuScreen(), true));
            if (biome != null) S.Audio.Music(biome.Music);
            DrawMap();
        }

        private void DrawMap()
        {
            var run = S.Run.Run;
            var map = run.CurrentMap;
            var available = new HashSet<string>();
            foreach (var n in S.Run.AvailableNodes()) available.Add(n.Id);
            int rows = RunState.RowsPerChapter;
            var pos = new Dictionary<string, Vector2>();
            var byRow = new Dictionary<int, List<MapNode>>();
            foreach (var n in map.Nodes)
            {
                List<MapNode> l;
                if (!byRow.TryGetValue(n.Row, out l)) { l = new List<MapNode>(); byRow[n.Row] = l; }
                l.Add(n);
            }
            // Layout in a 1150x780 local space (anchored rect); rows bottom to top.
            float w = 1150, h = 760;
            foreach (var kv in byRow)
                for (int i = 0; i < kv.Value.Count; i++)
                    pos[kv.Value[i].Id] = new Vector2((i + 1) * w / (kv.Value.Count + 1), 60 + kv.Key * (h - 120) / (rows - 1));
            foreach (var n in map.Nodes)
                foreach (var nx in n.Next)
                {
                    bool hot = n.Id == run.CurrentNodeId && available.Contains(nx);
                    UiKit.Line(_map, pos[n.Id], pos[nx], hot ? 6 : 3, hot ? Theme.Gold : new Color(1, 1, 1, 0.18f));
                }
            foreach (var n in map.Nodes)
            {
                var node = n;
                bool can = available.Contains(n.Id);
                bool current = n.Id == run.CurrentNodeId;
                var img = UiKit.Panel(_map, n.Id, NodeColor(n.Type));
                img.sprite = SpriteLibrary.Primitive(n.Type == NodeType.Boss ? SpriteLibrary.Shape.Diamond : SpriteLibrary.Shape.Circle);
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                float size = n.Type == NodeType.Boss ? 120 : 84;
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = pos[n.Id];
                if (!can && !n.Visited) img.color = Color.Lerp(img.color, Color.black, 0.55f);
                if (n.Visited) img.color = Color.Lerp(img.color, Color.gray, 0.6f);
                var glyph = UiKit.Label(img.transform, L("ui.node.glyph." + n.Type), 34, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Stretch(glyph.rectTransform);
                if (current)
                {
                    var ring = UiKit.Panel(img.transform, "Current", Theme.Gold);
                    ring.sprite = SpriteLibrary.Primitive(SpriteLibrary.Shape.Ring);
                    UiKit.Stretch(ring.rectTransform, -14, -14, -14, -14);
                }
                var btn = img.gameObject.AddComponent<Button>();
                btn.interactable = can;
                btn.onClick.AddListener(() => Choose(node));
                UiKit.Tooltip(img.gameObject, () => Describe(node));
                // Desktop hover shows the preview too; on touch, first tap previews, second tap commits.
                var trig = img.gameObject.GetComponent<TooltipTrigger>();
                trig.Provider = () => { _preview.text = Describe(node); return null; };
            }
        }

        private string _pending;

        private void Choose(MapNode n)
        {
            if (_pending != n.Id) { _pending = n.Id; _preview.text = Describe(n) + "\n\n<color=#ffb347>" + L("ui.map.tap_again") + "</color>"; return; }
            if (!Flow.Report(S, S.Run.ChooseNode(n.Id))) return;
            S.Audio.Ui("ui_node");
            Flow.Continue(S);
        }

        private string Describe(MapNode n)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>" + L("ui.node." + n.Type) + "</b>");
            sb.AppendLine(L("ui.node." + n.Type + ".desc"));
            if (n.EncounterId != null)
            {
                var enc = S.Db.Encounter(n.EncounterId);
                if (enc != null)
                {
                    sb.AppendLine();
                    sb.AppendLine(LF("ui.map.style", L("style." + enc.Style)));
                    var counts = new Dictionary<string, int>();
                    foreach (var w in enc.Waves) foreach (var sp in w.Spawns) { int c; counts.TryGetValue(sp.Enemy, out c); counts[sp.Enemy] = c + sp.Count; }
                    foreach (var kv in counts)
                    {
                        var e = S.Db.Enemy(kv.Key);
                        string rank = e != null && e.Rank != EnemyRank.Minion ? " [" + L("rank." + e.Rank) + "]" : "";
                        sb.AppendLine("• " + kv.Value + "× " + S.Loc.Name("enemy", kv.Key) + rank);
                    }
                    sb.AppendLine(LF("ui.map.waves", enc.Waves.Count));
                }
            }
            if (n.EventId != null) sb.AppendLine("\n" + L("event." + n.EventId + ".title"));
            sb.AppendLine();
            sb.AppendLine(L("ui.node." + n.Type + ".reward"));
            return sb.ToString();
        }

        public static Color NodeColor(NodeType t)
        {
            switch (t)
            {
                case NodeType.Battle: return Theme.Hex("b0503a");
                case NodeType.Elite: return Theme.Hex("d0302a");
                case NodeType.Boss: return Theme.Hex("8a1a1a");
                case NodeType.Shop: return Theme.Hex("d6a23a");
                case NodeType.Forge: return Theme.Hex("e07a2f");
                case NodeType.Rest: return Theme.Hex("4a9a5a");
                case NodeType.Event: return Theme.Hex("5a6ad0");
                case NodeType.Treasure: return Theme.Hex("c9a227");
                default: return Theme.Muted;
            }
        }
    }

    /// <summary>Top bar shown on run screens: HP, gold, essence, chapter step, blessings (with tooltips).</summary>
    public static class RunHud
    {
        public static void Build(Services s, RectTransform root)
        {
            var run = s.Run.Run;
            var bar = UiKit.HBox(root, "RunHud", 18, 8);
            UiKit.Anchor((RectTransform)bar.transform, 0.0f, 0.91f, 1f, 1f);
            bar.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.45f);
            int max = s.Run.MaxHp();
            Stat(bar.transform, s.Loc.Format("ui.hud.hp", run.Hp, max), run.Hp * 3 < max ? Theme.Danger : Theme.Ok, s.L("ui.hud.hp_tip"));
            Stat(bar.transform, s.Loc.Format("ui.hud.gold", run.Gold), Theme.Gold, s.L("ui.hud.gold_tip"));
            Stat(bar.transform, s.Loc.Format("ui.hud.essence", run.Essence), Theme.Link, s.L("ui.hud.essence_tip"));
            Stat(bar.transform, s.Loc.Format("ui.hud.step", run.Step, RunState.ChapterCount * RunState.RowsPerChapter), Theme.Text, s.L("ui.hud.step_tip"));
            int curses = 0, blessings = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var b in run.Blessings)
            {
                var d = s.Db.Blessing(b);
                if (d == null) continue;
                if (d.IsCurse) curses++; else blessings++;
                sb.AppendLine((d.IsCurse ? "✖ " : "✦ ") + s.Loc.Name("blessing", b) + ": " + string.Join("; ", s.Tips.Blessing(d)));
            }
            Stat(bar.transform, s.Loc.Format("ui.hud.boons", blessings, curses), Theme.Muted, sb.Length > 0 ? sb.ToString() : s.L("ui.hud.no_boons"));
        }

        private static void Stat(Transform parent, string text, Color c, string tip)
        {
            var t = UiKit.Label(parent, text, 26, c, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.raycastTarget = true;
            UiKit.Tooltip(t.gameObject, () => tip);
        }
    }

    /// <summary>In-run menu: resume, settings, abandon (confirmed), save & quit to title.</summary>
    public sealed class PauseMenuScreen : UiScreen
    {
        protected override void Build()
        {
            UiKit.Stretch(UiKit.Panel(Root, "Bg", new Color(0, 0, 0, 0.85f)).rectTransform);
            var v = UiKit.VBox(Root, "Menu", 14, 0);
            UiKit.Anchor((RectTransform)v.transform, 0.36f, 0.2f, 0.64f, 0.75f);
            UiKit.Button(v.transform, L("ui.pause.resume"), () => Flow.Continue(S), Theme.Ember);
            UiKit.Button(v.transform, L("ui.title.settings"), () => S.Ui.Show(() => new SettingsScreen(), true));
            UiKit.Button(v.transform, L("ui.pause.seed") + " " + S.Run.Run.Seed, () => { GUIUtility.systemCopyBuffer = S.Run.Run.Seed.ToString(); S.Ui.Toast(L("ui.pause.seed_copied")); });
            UiKit.Button(v.transform, L("ui.pause.save_quit"), () => { S.SaveRun(); S.SaveProfile(); S.Run = null; S.Ui.Show(() => new TitleScreen()); });
            UiKit.Button(v.transform, L("ui.pause.abandon"), () => S.Ui.Confirm(L("ui.pause.abandon_confirm"), L("ui.yes"), L("ui.no"), () =>
            {
                S.Run.Abandon();
                Flow.Continue(S);
            }), Theme.Danger);
        }
    }
}
