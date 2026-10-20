using System.Collections.Generic;
using System.Text;
using Forgebound.Core.Content;
using Forgebound.Core.ForgeBoard;
using Forgebound.Core.Progression;
using Forgebound.Core.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace Forgebound.Presentation
{
    /// <summary>Board host for the Workshop sandbox / trials (local BoardEditor undo, no run log).</summary>
    public sealed class WorkshopBoardHost : IBoardHost
    {
        private readonly Services _s;
        private readonly WorkshopSession _w;
        private readonly BoardEditor _editor;

        public WorkshopBoardHost(Services s, WorkshopSession w)
        {
            _s = s;
            _w = w;
            _editor = w.Editor();
        }

        public BoardState Board { get { return _w.Board; } }
        public IPieceShapeResolver Shapes { get { return _w.Shapes(); } }
        public BoardAnalysis Analyze() { return BoardAnalyzer.Analyze(_w.Board, _w.Shapes()); }
        public bool Move(int uid, int x, int y, int rot) { return _editor.Move(uid, new Cell(x, y), rot) == PlaceError.None; }
        public bool Remove(int uid) { return _editor.Remove(uid); }
        public bool Undo() { return _editor.Undo(); }
        public bool Redo() { return _editor.Redo(); }
        public bool CanUndo { get { return _editor.CanUndo; } }
        public bool CanRedo { get { return _editor.CanRedo; } }
        public int PrimaryWeaponUid { get { return _w.PrimaryUid; } }
        public BoardEditor Editor { get { return _editor; } }

        public Sprite SpriteFor(PlacedPiece p)
        {
            if (p.Kind == PieceKind.Weapon) { var w = _w.Find(p.WeaponUid); return w != null ? _s.Sprites.Weapon(w.FrameId, w.CoreId, w.Tier, w.LegendaryId) : null; }
            return _s.Sprites.Piece(p.PieceId);
        }

        public string Tooltip(PlacedPiece p)
        {
            if (p.Kind == PieceKind.Weapon) { var w = _w.Find(p.WeaponUid); return w != null ? RunBoardHost.WeaponTooltip(_s, w, null) : ""; }
            var def = _s.Db.Piece(p.PieceId);
            return def == null ? "" : _s.Loc.Name("piece", def.Id) + "\n" + string.Join("\n", _s.Tips.Piece(def));
        }

        public string Label(PlacedPiece p)
        {
            if (p.Kind == PieceKind.Weapon) { var w = _w.Find(p.WeaponUid); return w != null ? RunBoardHost.WeaponName(_s, w) : ""; }
            return _s.Loc.Name("piece", p.PieceId);
        }
    }

    /// <summary>
    /// Collection Workshop hub: Gallery (discovered blueprints + legendaries + saved designs), Sandbox + Training Chamber,
    /// Blueprint Trials, Run History, blueprint import/export. Nothing here can enter an Adventure run.
    /// </summary>
    public sealed class WorkshopScreen : UiScreen
    {
        private RectTransform _content;
        private string _tab = "gallery";

        protected override void Build()
        {
            UiKit.Stretch(UiKit.Panel(Root, "Bg", Theme.Bg).rectTransform);
            var title = UiKit.Label(Root, L("ui.workshop.title"), 48, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, 0.02f, 0.9f, 0.5f, 0.99f);
            var tabs = UiKit.HBox(Root, "Tabs", 10);
            UiKit.Anchor((RectTransform)tabs.transform, 0.02f, 0.82f, 0.98f, 0.89f);
            foreach (var t in new[] { "gallery", "sandbox", "trials", "history" })
            {
                var tab = t;
                UiKit.Button(tabs.transform, L("ui.workshop.tab." + t), () => { _tab = tab; Rebuild(); }, _tab == t ? Theme.Ember : Theme.PanelLight);
            }
            _content = UiKit.Rect(Root, "Content");
            UiKit.Anchor(_content, 0.02f, 0.1f, 0.98f, 0.81f);
            var back = UiKit.Button(Root, L("ui.back"), () => S.Ui.Show(() => new TitleScreen()));
            UiKit.Anchor((RectTransform)back.transform, 0.02f, 0.015f, 0.2f, 0.085f);
            Rebuild();
            S.Audio.Music("mus_workshop");
        }

        private void Rebuild()
        {
            UiKit.Clear(_content);
            switch (_tab)
            {
                case "gallery": Gallery(); break;
                case "sandbox": S.Ui.Show(() => new SandboxScreen(), true); return;
                case "trials": TrialsTab(); break;
                case "history": History(); break;
            }
        }

        private void Gallery()
        {
            ScrollRect sr;
            var list = UiKit.ScrollList(_content, "Gallery", out sr);
            UiKit.Stretch((RectTransform)list.parent.parent);
            UiKit.Label(list, LF("ui.workshop.discovered", CountDiscovered("blueprint:"), S.Db.Frames.Count * S.Db.Cores.Count, CountDiscovered("legendary:"), S.Db.Legendaries.Count), 26, Theme.Gold);
            foreach (var l in S.Db.Legendaries)
            {
                bool known = S.Profile.Discovered.Contains("legendary:" + l.Id);
                var row = UiKit.HBox(list, "Leg", 10);
                UiKit.Size(row, -1, 90, -1, 90);
                var ic = UiKit.Icon(row.transform, known ? S.Sprites.Weapon(l.Frame, l.Core, 3, l.Id) : SpriteLibrary.Primitive(SpriteLibrary.Shape.Diamond), known ? Color.white : new Color(0.15f, 0.15f, 0.15f), 84);
                UiKit.Size(ic, 84, 84, 84, 84, 0);
                var legendary = l;
                var t = UiKit.Label(row.transform, known ? S.Loc.Name("legendary", l.Id) + "\n<size=18>" + string.Join("  ", S.Tips.Legendary(l)) + "</size>" : L("ui.workshop.undiscovered") + "\n<size=18>" + S.Loc.Name("frame", l.Frame) + " + ?</size>", 24, known ? Theme.Gold : Theme.Muted);
                t.supportRichText = true;
                t.raycastTarget = true;
                UiKit.Size(t, -1, -1, -1, -1, 1);
                if (known) UiKit.Tooltip(t.gameObject, () => L("legendary." + legendary.Id + ".lore"));
            }
            UiKit.Label(list, L("ui.workshop.designs"), 26, Theme.Gold);
            foreach (var d in S.Profile.Designs)
            {
                var design = d;
                var row = UiKit.HBox(list, "Design", 10);
                UiKit.Size(row, -1, 70, -1, 70);
                var ic = UiKit.Icon(row.transform, S.Sprites.Weapon(d.Weapon.FrameId, d.Weapon.CoreId, d.Weapon.Tier, d.Weapon.LegendaryId), Color.white, 64);
                UiKit.Size(ic, 64, 64, 64, 64, 0);
                var t = UiKit.Label(row.transform, (d.Favorite ? "★ " : "") + d.Name + "  —  " + RunBoardHost.WeaponName(S, d.Weapon), 22, Theme.Text);
                UiKit.Size(t, -1, -1, -1, -1, 1);
                UiKit.Button(row.transform, L("ui.workshop.export"), () =>
                {
                    GUIUtility.systemCopyBuffer = BlueprintCodec.Export(design.Weapon);
                    S.Ui.Toast(L("ui.workshop.exported"));
                }, null, 18);
                UiKit.Button(row.transform, design.Favorite ? "★" : "☆", () => { design.Favorite = !design.Favorite; S.SaveProfile(); Rebuild(); }, null, 22);
            }
            UiKit.Button(list, L("ui.workshop.import"), () =>
            {
                List<string> errors;
                var w = BlueprintCodec.Import(S.Db, GUIUtility.systemCopyBuffer, out errors);
                if (w == null) { S.Ui.Toast(LF("ui.workshop.import_failed", string.Join(", ", errors))); return; }
                S.Profile.Designs.Add(new SavedDesign { Name = L("ui.workshop.imported_name"), Weapon = w });
                S.SaveProfile();
                Rebuild();
            });
        }

        private int CountDiscovered(string prefix)
        {
            int n = 0;
            foreach (var d in S.Profile.Discovered) if (d.StartsWith(prefix)) n++;
            return n;
        }

        private void TrialsTab()
        {
            ScrollRect sr;
            var list = UiKit.ScrollList(_content, "Trials", out sr);
            UiKit.Stretch((RectTransform)list.parent.parent);
            foreach (var c in S.Db.Challenges)
            {
                var challenge = c;
                bool done = S.Profile.TrialsCompleted.Contains(c.Id);
                var row = UiKit.HBox(list, "Trial", 10);
                UiKit.Size(row, -1, 100, -1, 100);
                var t = UiKit.Label(row.transform, (done ? "✓ " : "") + S.Loc.Name("challenge", c.Id) + "  [" + LF("ui.workshop.tier", c.Tier) + "]\n<size=18>" + L("challenge." + c.Id + ".desc") + "</size>", 24, done ? Theme.Ok : Theme.Text);
                t.supportRichText = true;
                UiKit.Size(t, -1, -1, -1, -1, 1);
                UiKit.Button(row.transform, L("ui.workshop.start_trial"), () => S.Ui.Show(() => new SandboxScreen(challenge), true), Theme.Ember);
            }
        }

        private void History()
        {
            ScrollRect sr;
            var list = UiKit.ScrollList(_content, "History", out sr);
            UiKit.Stretch((RectTransform)list.parent.parent);
            UiKit.Label(list, LF("ui.workshop.history_stats", S.Profile.Runs, S.Profile.Wins, S.Profile.HighestChapter, S.Profile.LegendariesCrafted), 24, Theme.Gold);
            foreach (var h in S.Profile.History)
            {
                var run = h;
                var row = UiKit.HBox(list, "Run", 10);
                UiKit.Size(row, -1, 80, -1, 80);
                var t = UiKit.Label(row.transform, h.Date + "  " + S.Loc.Name("forgemaster", h.Forgemaster) + "  " + L("ui.end." + h.Outcome) + "  " + LF("ui.workshop.history_line", h.Chapter, h.Step, h.Battles, h.Seed) + "\n<size=16>" + string.Join(" | ", h.FinalWeapons) + "</size>", 20, h.Outcome == "Victory" ? Theme.Gold : Theme.Text);
                t.supportRichText = true;
                UiKit.Size(t, -1, -1, -1, -1, 1);
                UiKit.Button(row.transform, L("ui.workshop.replay_seed"), () =>
                {
                    ulong seed;
                    if (!ulong.TryParse(run.Seed, out seed) || !S.Profile.IsUnlocked(run.Forgemaster)) return;
                    S.Run = Core.Run.RunController.StartNew(S.Db, new Core.Run.StartOptions { ForgemasterId = run.Forgemaster, Seed = seed, Difficulty = run.Difficulty, FavoriteRecipe = S.Profile.FavoriteRecipe });
                    Flow.Continue(S);
                }, null, 18);
            }
        }
    }

    /// <summary>
    /// Sandbox / Training Chamber (and Blueprint Trial setup). Build from discovered parts, measure on dummy categories with
    /// the real simulation, save designs. Trials use the trial's own item pool and fight its encounter for a cosmetic reward.
    /// </summary>
    public sealed class SandboxScreen : UiScreen
    {
        private readonly ChallengeDef _trial;
        private WorkshopSession _session;
        private WorkshopBoardHost _host;
        private BoardWidget _board;
        private RectTransform _parts;
        private Text _result;
        private WeaponInstance _selected;
        private string _dummy = "training_single";
        private readonly List<KeyValuePair<ItemCategory, string>> _trialPool = new List<KeyValuePair<ItemCategory, string>>();

        public SandboxScreen(ChallengeDef trial = null) { _trial = trial; }

        protected override void Build()
        {
            UiKit.Stretch(UiKit.Panel(Root, "Bg", Theme.Bg).rectTransform);
            if (_trial != null)
            {
                _session = Trials.Begin(S.Db, S.Profile, _trial);
                _trialPool.AddRange(Trials.Items(_trial));
            }
            else
            {
                var fm = S.Db.Forgemasters[0].Id;
                _session = new WorkshopSession(S.Db, S.Profile, fm);
            }
            _host = new WorkshopBoardHost(S, _session);
            var title = UiKit.Label(Root, _trial != null ? S.Loc.Name("challenge", _trial.Id) : L("ui.sandbox.title"), 40, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, 0.02f, 0.91f, 0.7f, 0.99f);
            var partsPanel = UiKit.Panel(Root, "Parts", Theme.Panel);
            UiKit.Anchor(partsPanel.rectTransform, 0.005f, 0.12f, 0.24f, 0.9f);
            ScrollRect sr;
            _parts = UiKit.ScrollList(partsPanel.transform, "List", out sr);
            UiKit.Stretch((RectTransform)_parts.parent.parent);
            var boardArea = UiKit.Rect(Root, "BoardArea");
            UiKit.Anchor(boardArea, 0.25f, 0.2f, 0.7f, 0.9f);
            _board = new BoardWidget(S, boardArea, _host);
            _board.OnSelect = p => { if (p != null && p.Kind == PieceKind.Weapon) { _selected = _session.Find(p.WeaponUid); RebuildParts(); } };
            var tools = UiKit.HBox(Root, "Tools", 8);
            UiKit.Anchor((RectTransform)tools.transform, 0.25f, 0.12f, 0.7f, 0.19f);
            UiKit.Button(tools.transform, L("ui.board.rotate"), () => _board.RotateSelected());
            UiKit.Button(tools.transform, L("ui.board.remove"), () => _board.RemoveSelected());
            UiKit.Button(tools.transform, L("ui.board.undo"), () => _board.Undo());
            UiKit.Button(tools.transform, L("ui.board.redo"), () => _board.Redo());
            var right = UiKit.VBox(Root, "Right", 8, 10);
            UiKit.Anchor((RectTransform)right.transform, 0.71f, 0.12f, 0.995f, 0.9f);
            _result = UiKit.Label(right.transform, _trial != null ? L("ui.sandbox.trial_hint") : L("ui.sandbox.hint"), 22, Theme.Text, TextAnchor.UpperLeft);
            UiKit.Size(_result, -1, 300, -1, -1, -1, 1);
            if (_trial == null)
            {
                foreach (var d in Core.Progression.LoadoutProbe.Dummies)
                {
                    var dummy = d;
                    UiKit.Button(right.transform, L("ui.sandbox.dummy." + d), () => { _dummy = dummy; Train(); }, _dummy == d ? Theme.Ember : null, 20);
                }
                UiKit.Button(right.transform, L("ui.sandbox.save_design"), () =>
                {
                    if (_selected != null && _session.SaveDesign(_selected, RunBoardHost.WeaponName(S, _selected))) { S.SaveProfile(); S.Ui.Toast(L("ui.sandbox.saved")); }
                }, null, 20);
            }
            else UiKit.Button(right.transform, L("ui.sandbox.fight_trial"), FightTrial, Theme.Ember);
            var back = UiKit.Button(Root, L("ui.back"), () => S.Ui.Show(() => new WorkshopScreen()));
            UiKit.Anchor((RectTransform)back.transform, 0.005f, 0.02f, 0.2f, 0.1f);
            RebuildParts();
        }

        private IEnumerable<KeyValuePair<ItemCategory, string>> AvailableParts()
        {
            if (_trial != null) { foreach (var kv in _trialPool) yield return kv; yield break; }
            foreach (var f in S.Db.Frames) if (S.Profile.CanUseInWorkshop(ItemCategory.Frame, f.Id)) yield return new KeyValuePair<ItemCategory, string>(ItemCategory.Frame, f.Id);
            foreach (var c in S.Db.Cores) if (S.Profile.CanUseInWorkshop(ItemCategory.Core, c.Id)) yield return new KeyValuePair<ItemCategory, string>(ItemCategory.Core, c.Id);
            foreach (var m in S.Db.Modifiers) if (S.Profile.CanUseInWorkshop(ItemCategory.Modifier, m.Id)) yield return new KeyValuePair<ItemCategory, string>(ItemCategory.Modifier, m.Id);
            foreach (var r in S.Db.Runes) if (S.Profile.CanUseInWorkshop(ItemCategory.Rune, r.Id)) yield return new KeyValuePair<ItemCategory, string>(ItemCategory.Rune, r.Id);
            foreach (var p in S.Db.Pieces) if (S.Profile.CanUseInWorkshop(ItemCategory.Piece, p.Id)) yield return new KeyValuePair<ItemCategory, string>(ItemCategory.Piece, p.Id);
        }

        private void RebuildParts()
        {
            UiKit.Clear(_parts);
            int count = 0;
            foreach (var kv in AvailableParts())
            {
                count++;
                var part = kv;
                string cat = kv.Key.ToString().ToLowerInvariant();
                var b = UiKit.Button(_parts, S.Loc.Name(cat, kv.Value) + "  <size=14>" + L("category." + kv.Key) + "</size>", () => UsePart(part), null, 20);
                b.GetComponentInChildren<Text>().supportRichText = true;
                UiKit.Tooltip(b.gameObject, () => new ForgeTooltipHelper(S).Item(part.Key, part.Value));
            }
            if (count == 0) UiKit.Label(_parts, L("ui.sandbox.nothing_discovered"), 20, Theme.Muted);
        }

        private void UsePart(KeyValuePair<ItemCategory, string> part)
        {
            string err;
            switch (part.Key)
            {
                case ItemCategory.Frame:
                    {
                        if (_trial != null && _trial.AllowedFrames.Count > 0 && !_trial.AllowedFrames.Contains(part.Value)) { S.Ui.Toast(L("ui.sandbox.frame_not_allowed")); return; }
                        var w = _session.CreateWeapon(part.Value, null, out err);
                        if (w == null) { S.Ui.Toast(L("error." + err)); return; }
                        _selected = w;
                        _board.PendingPlace = (x, y, rot) => _session.PlaceWeapon(w, x, y, rot) == PlaceError.None;
                        S.Ui.Toast(L("ui.forge.tap_cell"));
                        break;
                    }
                case ItemCategory.Core:
                    if (_selected == null) { S.Ui.Toast(L("ui.sandbox.select_weapon")); return; }
                    if (_selected.LegendaryId == null) { _selected.CoreId = part.Value; _board.Refresh(); }
                    break;
                case ItemCategory.Modifier:
                    if (_selected == null) { S.Ui.Toast(L("ui.sandbox.select_weapon")); return; }
                    err = _session.AddModifier(_selected, part.Value);
                    if (err != null) S.Ui.Toast(err);
                    break;
                case ItemCategory.Rune:
                    if (_selected == null) { S.Ui.Toast(L("ui.sandbox.select_weapon")); return; }
                    err = _session.AddRune(_selected, part.Value);
                    if (err != null) S.Ui.Toast(err);
                    break;
                case ItemCategory.Piece:
                    _board.PendingPlace = (x, y, rot) => _session.PlacePiece(part.Value, x, y, rot) == PlaceError.None;
                    S.Ui.Toast(L("ui.forge.tap_cell"));
                    break;
            }
            _board.Refresh();
        }

        private void Train()
        {
            if (_session.Weapons.Count == 0) { S.Ui.Toast(L("ui.sandbox.select_weapon")); return; }
            var p = _session.Train(_dummy, 20);
            var sb = new StringBuilder();
            sb.AppendLine(LF("ui.sandbox.result", L("ui.sandbox.dummy." + _dummy), p.Dps, p.TotalDamage, p.ActiveLinks, p.DeadLinks));
            for (int i = 0; i < 3; i++) if (p.WeaponDamage[i] > 0) sb.AppendLine("  W" + (i + 1) + ": " + p.WeaponDamage[i]);
            foreach (var kv in p.RuneTriggers) sb.AppendLine("  " + (S.Loc.Has("rune." + kv.Key + ".name") ? S.Loc.Name("rune", kv.Key) : kv.Key) + " ×" + kv.Value);
            sb.AppendLine(L("ui.measure.explain"));
            _result.text = sb.ToString();
        }

        private void FightTrial()
        {
            if (_session.Board.CountWeapons() == 0) { S.Ui.Toast(L("error.Invalid")); return; }
            try
            {
                var r = Trials.Fight(S.Db, S.Profile, _trial, _session, (ulong)System.DateTime.UtcNow.Ticks);
                S.SaveProfile();
                _result.text = (r.Victory ? L("ui.report.victory") : L("ui.report.defeat")) + "\n" + LF("ui.report.summary", (r.Ticks / 30f).ToString("0.0"), r.Report.EnemiesKilled, r.Report.TotalDamage, r.Report.DamageTaken)
                    + (r.Victory && _trial.RewardCosmetic != null ? "\n" + LF("ui.sandbox.cosmetic", L("cosmetic." + _trial.RewardCosmetic)) : "");
            }
            catch (System.Exception e) { S.Ui.Toast(e.Message); }
        }
    }

    /// <summary>Codex: every piece of content with data-generated descriptions; undiscovered entries are marked.</summary>
    public sealed class CodexScreen : UiScreen
    {
        private RectTransform _list;
        private string _cat = "frame";

        protected override void Build()
        {
            UiKit.Stretch(UiKit.Panel(Root, "Bg", Theme.Bg).rectTransform);
            var tabs = UiKit.HBox(Root, "Tabs", 6);
            UiKit.Anchor((RectTransform)tabs.transform, 0.01f, 0.9f, 0.99f, 0.98f);
            foreach (var c in new[] { "frame", "core", "modifier", "rune", "piece", "legendary", "enemy", "blessing", "forgemaster" })
            {
                var cat = c;
                UiKit.Button(tabs.transform, L("codex." + c), () => { _cat = cat; Fill(); }, _cat == c ? Theme.Ember : null, 18);
            }
            ScrollRect sr;
            _list = UiKit.ScrollList(Root, "List", out sr);
            UiKit.Anchor((RectTransform)_list.parent.parent, 0.02f, 0.1f, 0.98f, 0.88f);
            var back = UiKit.Button(Root, L("ui.back"), () => S.Ui.Show(() => new TitleScreen()));
            UiKit.Anchor((RectTransform)back.transform, 0.02f, 0.015f, 0.2f, 0.085f);
            Fill();
        }

        private void Entry(string cat, string id, IEnumerable<string> lines, bool known)
        {
            var t = UiKit.Label(_list, (known ? "" : "◌ ") + S.Loc.Name(cat, id) + "\n<size=18>" + string.Join("\n", lines) + "</size>", 24, known ? Theme.Text : Theme.Muted, TextAnchor.UpperLeft);
            t.supportRichText = true;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            UiKit.Size(t, -1, 150, -1, -1);
        }

        private void Fill()
        {
            UiKit.Clear(_list);
            var d = S.Profile.Discovered;
            switch (_cat)
            {
                case "frame": foreach (var x in S.Db.Frames) Entry("frame", x.Id, S.Tips.Frame(x), S.Profile.CanUseInWorkshop(ItemCategory.Frame, x.Id)); break;
                case "core": foreach (var x in S.Db.Cores) Entry("core", x.Id, S.Tips.Core(x), S.Profile.CanUseInWorkshop(ItemCategory.Core, x.Id)); break;
                case "modifier": foreach (var x in S.Db.Modifiers) Entry("modifier", x.Id, S.Tips.Modifier(x), d.Contains("item:Modifier:" + x.Id)); break;
                case "rune": foreach (var x in S.Db.Runes) Entry("rune", x.Id, new[] { S.Tips.Rune(x) }, d.Contains("item:Rune:" + x.Id)); break;
                case "piece": foreach (var x in S.Db.Pieces) Entry("piece", x.Id, S.Tips.Piece(x), d.Contains("item:Piece:" + x.Id)); break;
                case "legendary": foreach (var x in S.Db.Legendaries) Entry("legendary", x.Id, S.Tips.Legendary(x), d.Contains("legendary:" + x.Id)); break;
                case "enemy": foreach (var x in S.Db.Enemies) if (!x.Tags.Contains("training")) Entry("enemy", x.Id, S.Tips.Enemy(x), true); break;
                case "blessing": foreach (var x in S.Db.Blessings) if (!x.Tags.Contains("challenge_only")) Entry("blessing", x.Id, S.Tips.Blessing(x), true); break;
                case "forgemaster": foreach (var x in S.Db.Forgemasters) Entry("forgemaster", x.Id, S.Tips.Forgemaster(x), S.Profile.IsUnlocked(x.Id)); break;
            }
        }
    }
}
