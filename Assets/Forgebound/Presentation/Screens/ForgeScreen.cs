using System.Collections.Generic;
using System.Text;
using Forgebound.Core.Combat;
using Forgebound.Core.Content;
using Forgebound.Core.Economy;
using Forgebound.Core.ForgeBoard;
using Forgebound.Core.Progression;
using Forgebound.Core.Run;
using Forgebound.Core.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace Forgebound.Presentation
{
    /// <summary>Board host for Adventure: every edit goes through RunController (validated + logged).</summary>
    public sealed class RunBoardHost : IBoardHost
    {
        private readonly Services _s;
        private RunController C { get { return _s.Run; } }
        public RunBoardHost(Services s) { _s = s; }
        public BoardState Board { get { return C.Run.Board; } }
        public IPieceShapeResolver Shapes { get { return C.Shapes(); } }
        public BoardAnalysis Analyze() { return C.AnalyzeBoard(); }
        public bool Move(int uid, int x, int y, int rot) { var r = C.MovePiece(uid, x, y, rot); return r.Ok; }
        public bool Remove(int uid) { return Flow.Report(_s, C.RemovePiece(uid)); }
        public bool Undo() { return C.UndoBoard().Ok; }
        public bool Redo() { return C.RedoBoard().Ok; }
        public bool CanUndo { get { return C.CanUndoBoard; } }
        public bool CanRedo { get { return C.CanRedoBoard; } }
        public int PrimaryWeaponUid { get { return C.Run.PrimaryWeaponUid; } }

        public Sprite SpriteFor(PlacedPiece p)
        {
            if (p.Kind == PieceKind.Weapon)
            {
                var w = C.Run.FindWeapon(p.WeaponUid);
                return w != null ? _s.Sprites.Weapon(w.FrameId, w.CoreId, w.Tier, w.LegendaryId) : null;
            }
            return _s.Sprites.Piece(p.PieceId);
        }

        public string Label(PlacedPiece p)
        {
            if (p.Kind == PieceKind.Weapon)
            {
                var w = C.Run.FindWeapon(p.WeaponUid);
                return w == null ? "" : (w.Uid == C.Run.PrimaryWeaponUid ? "★ " : "") + WeaponName(_s, w);
            }
            return _s.Loc.Name("piece", p.PieceId);
        }

        public string Tooltip(PlacedPiece p)
        {
            if (p.Kind == PieceKind.Weapon) { var w = C.Run.FindWeapon(p.WeaponUid); return w != null ? WeaponTooltip(_s, w, C.HeroStats()) : ""; }
            var def = _s.Db.Piece(p.PieceId);
            return def == null ? "" : _s.Loc.Name("piece", def.Id) + "\n" + string.Join("\n", _s.Tips.Piece(def));
        }

        public static string WeaponName(Services s, WeaponInstance w)
        {
            if (w.LegendaryId != null) return s.Loc.Name("legendary", w.LegendaryId);
            var name = s.Loc.Name("frame", w.FrameId);
            if (w.CoreId != null) name = s.Loc.Format("ui.weapon.name_core", s.Loc.Name("core", w.CoreId), name);
            return name + (w.Tier > 1 ? " +" + (w.Tier - 1) : "");
        }

        public static string WeaponTooltip(Services s, WeaponInstance w, StatBlock stats)
        {
            var sb = new StringBuilder();
            sb.AppendLine(WeaponName(s, w));
            var f = s.Db.Frame(w.FrameId);
            foreach (var l in s.Tips.Frame(f)) sb.AppendLine("• " + l);
            if (w.CoreId != null) foreach (var l in s.Tips.Core(s.Db.Core(w.CoreId))) sb.AppendLine("◆ " + l);
            foreach (var m in w.Modifiers) sb.AppendLine("▲ " + s.Loc.Name("modifier", m) + ": " + string.Join(", ", s.Tips.Modifier(s.Db.Modifier(m))));
            foreach (var r in w.Runes) sb.AppendLine("ᚱ " + s.Loc.Name("rune", r) + ": " + s.Tips.Rune(s.Db.Rune(r)));
            if (w.LegendaryId != null) foreach (var l in s.Tips.Legendary(s.Db.Legendary(w.LegendaryId))) sb.AppendLine("✦ " + l);
            return sb.ToString();
        }
    }

    /// <summary>
    /// The Forge: Forge Board + inventory + weapon assembly + contextual node panel (pre-battle, shop, forge, rest).
    /// Also used read-only-ish from the map ("loadout" view) so players can rearrange at any safe moment.
    /// </summary>
    public sealed class ForgeScreen : UiScreen
    {
        private readonly bool _fromMap;
        private BoardWidget _board;
        private RectTransform _inventory, _weaponPanel, _context;
        private int _selectedItem = -1;
        private int _selectedWeapon = -1;
        private Text _measure;
        private ProbeResult _lastProbe;
        private RunController C { get { return S.Run; } }

        public ForgeScreen(bool fromMap = false) { _fromMap = fromMap; }

        protected override void Build()
        {
            UiKit.Stretch(UiKit.Panel(Root, "Bg", Theme.Bg).rectTransform);
            RunHud.Build(S, Root);
            var phase = C.Run.Phase;
            var title = UiKit.Label(Root, L("ui.forge.title." + (_fromMap ? "Loadout" : phase.ToString())), 34, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, 0.01f, 0.85f, 0.5f, 0.91f);

            // Left: inventory
            var invPanel = UiKit.Panel(Root, "InventoryPanel", Theme.Panel);
            UiKit.Anchor(invPanel.rectTransform, 0.005f, 0.2f, 0.215f, 0.85f);
            var invTitle = UiKit.Label(invPanel.transform, "", 24, Theme.Muted, TextAnchor.UpperCenter);
            UiKit.Anchor(invTitle.rectTransform, 0, 0.93f, 1, 1);
            invTitle.text = LF("ui.forge.inventory", C.Run.Inventory.Count, RunState.InventoryCap);
            ScrollRect sr;
            _inventory = UiKit.ScrollList(invPanel.transform, "Items", out sr);
            UiKit.Anchor((RectTransform)_inventory.parent.parent, 0, 0, 1, 0.93f);

            // Centre: board
            var boardArea = UiKit.Rect(Root, "BoardArea");
            UiKit.Anchor(boardArea, 0.22f, 0.2f, 0.665f, 0.85f);
            _board = new BoardWidget(S, boardArea, new RunBoardHost(S));
            _board.OnSelect = p =>
            {
                if (p != null && p.Kind == PieceKind.Weapon) { _selectedWeapon = p.WeaponUid; RebuildWeaponPanel(); }
            };
            _board.OnChanged = () => { if (_measure != null) _measure.text = MeasureSummary(); };
            var tools = UiKit.HBox(Root, "BoardTools", 8);
            UiKit.Anchor((RectTransform)tools.transform, 0.22f, 0.12f, 0.665f, 0.195f);
            UiKit.Button(tools.transform, L("ui.board.rotate"), () => _board.RotateSelected());
            UiKit.Button(tools.transform, L("ui.board.remove"), () => { _board.RemoveSelected(); RebuildInventory(); });
            UiKit.Button(tools.transform, L("ui.board.undo"), () => { _board.Undo(); RebuildInventory(); });
            UiKit.Button(tools.transform, L("ui.board.redo"), () => { _board.Redo(); RebuildInventory(); });
            UiKit.Button(tools.transform, L("ui.board.measure"), Measure);

            // Right: weapon assembly
            var wp = UiKit.Panel(Root, "WeaponPanel", Theme.Panel);
            UiKit.Anchor(wp.rectTransform, 0.67f, 0.2f, 0.995f, 0.85f);
            _weaponPanel = wp.rectTransform;

            // Bottom: context actions + measure readout
            var ctx = UiKit.Panel(Root, "Context", new Color(0, 0, 0, 0.35f));
            UiKit.Anchor(ctx.rectTransform, 0.005f, 0.005f, 0.995f, 0.115f);
            _context = ctx.rectTransform;
            _measure = UiKit.Label(Root, "", 20, Theme.Muted, TextAnchor.MiddleLeft);
            UiKit.Anchor(_measure.rectTransform, 0.005f, 0.12f, 0.215f, 0.195f);

            if (C.Run.Weapons.Count > 0) _selectedWeapon = C.Run.PrimaryWeaponUid >= 0 ? C.Run.PrimaryWeaponUid : C.Run.Weapons[0].Uid;
            RebuildInventory();
            RebuildWeaponPanel();
            RebuildContext();
            Hints.Show(S, "board_intro");
        }

        // ------------------------------------------------------------------ inventory

        private void RebuildInventory()
        {
            UiKit.Clear(_inventory);
            // Bench weapons (owned but not on the board)
            foreach (var w in C.Run.Weapons)
            {
                if (C.Run.Board.FindWeapon(w.Uid) != null) continue;
                var weapon = w;
                Row(_inventory, S.Sprites.Weapon(w.FrameId, w.CoreId, w.Tier, w.LegendaryId), RunBoardHost.WeaponName(S, w) + "  <color=#a89a8c>" + L("ui.forge.bench") + "</color>",
                    Theme.Gold, () =>
                    {
                        _selectedWeapon = weapon.Uid;
                        RebuildWeaponPanel();
                        _board.PendingRotation = 0;
                        _board.PendingPlace = (x, y, rot) => Flow.Report(S, C.PlaceWeapon(weapon.Uid, x, y, rot));
                        S.Ui.Toast(L("ui.forge.tap_cell"));
                    }, () => RunBoardHost.WeaponTooltip(S, weapon, C.HeroStats()), false);
            }
            // Loose items
            var items = new List<InventoryItem>(C.Run.Inventory);
            items.Sort((a, b) => a.Category != b.Category ? a.Category.CompareTo(b.Category) : string.CompareOrdinal(a.Id, b.Id));   // auto-sort display only; never touches the board
            foreach (var it in items)
            {
                var item = it;
                var rarity = S.Db.ItemRarity(it.Category, it.Id);
                Sprite icon = it.Category == ItemCategory.Piece ? S.Sprites.Piece(it.Id) : S.Sprites.Icon(it.Category.ToString().ToLowerInvariant(), it.Id);
                Row(_inventory, icon, S.Loc.Name(it.Category.ToString().ToLowerInvariant(), it.Id) + "\n<size=16><color=#a89a8c>" + L("category." + it.Category) + "</color></size>",
                    Theme.Rarity(rarity), () => SelectItem(item), () => ItemTooltip(item.Category, item.Id), item.Uid == _selectedItem);
            }
        }

        private void Row(Transform parent, Sprite icon, string text, Color accent, System.Action onClick, System.Func<string> tip, bool selected)
        {
            var row = UiKit.Panel(parent, "Row", selected ? Theme.PanelLight : Theme.Bg);
            UiKit.Size(row, -1, 72, -1, 72);
            var btn = row.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick());
            var stripe = UiKit.Panel(row.transform, "Rarity", accent);
            UiKit.Anchor(stripe.rectTransform, 0, 0, 0.02f, 1);
            var ic = UiKit.Panel(row.transform, "Icon", Color.white);
            ic.sprite = icon;
            ic.preserveAspect = true;
            ic.raycastTarget = false;
            UiKit.Anchor(ic.rectTransform, 0.03f, 0.05f, 0.25f, 0.95f);
            var t = UiKit.Label(row.transform, text, 20, Theme.Text);
            t.supportRichText = true;
            UiKit.Anchor(t.rectTransform, 0.28f, 0, 1, 1);
            UiKit.Tooltip(row.gameObject, tip);
        }

        public string ItemTooltip(ItemCategory cat, string id)
        {
            var sb = new StringBuilder();
            sb.AppendLine(S.Loc.Name(cat.ToString().ToLowerInvariant(), id) + "  (" + L("rarity." + S.Db.ItemRarity(cat, id)) + ")");
            List<string> lines = null;
            switch (cat)
            {
                case ItemCategory.Frame: lines = S.Tips.Frame(S.Db.Frame(id)); break;
                case ItemCategory.Core: lines = S.Tips.Core(S.Db.Core(id)); break;
                case ItemCategory.Modifier: lines = S.Tips.Modifier(S.Db.Modifier(id)); break;
                case ItemCategory.Rune: lines = new List<string> { S.Tips.Rune(S.Db.Rune(id)) }; break;
                case ItemCategory.Piece: lines = S.Tips.Piece(S.Db.Piece(id)); break;
            }
            if (lines != null) foreach (var l in lines) sb.AppendLine("• " + l);
            sb.AppendLine(LF("ui.forge.sell_value", EconomyRules.SellValue(S.Db, cat, id)));
            return sb.ToString();
        }

        private void SelectItem(InventoryItem it)
        {
            _selectedItem = it.Uid;
            if (it.Category == ItemCategory.Piece)
            {
                _board.PendingRotation = 0;
                _board.PendingPlace = (x, y, rot) =>
                {
                    bool ok = Flow.Report(S, C.PlacePiece(it.Uid, x, y, rot));
                    if (ok) { _selectedItem = -1; RebuildInventory(); }
                    return ok;
                };
                S.Ui.Toast(L("ui.forge.tap_cell"));
                Hints.Show(S, "relay_intro");
            }
            RebuildInventory();
            RebuildWeaponPanel();
        }

        // ------------------------------------------------------------------ weapon assembly panel

        private void RebuildWeaponPanel()
        {
            UiKit.Clear(_weaponPanel);
            var v = UiKit.VBox(_weaponPanel, "V", 6, 12);
            UiKit.Stretch((RectTransform)v.transform);
            var w = C.Run.FindWeapon(_selectedWeapon);
            var item = C.Run.FindItem(_selectedItem);
            if (w == null)
            {
                UiKit.Label(v.transform, L("ui.forge.no_weapon"), 24, Theme.Muted);
                AssembleButton(v.transform, item);
                return;
            }
            var header = UiKit.HBox(v.transform, "Header", 8);
            UiKit.Size(header, -1, 110, -1, 110);
            var ic = UiKit.Icon(header.transform, S.Sprites.Weapon(w.FrameId, w.CoreId, w.Tier, w.LegendaryId), Color.white, 110);
            UiKit.Size(ic, 110, 110, 110, 110, 0);
            var nm = UiKit.Label(header.transform, RunBoardHost.WeaponName(S, w) + "\n<size=18>" + LF("ui.forge.tier", w.Tier) + (w.Uid == C.Run.PrimaryWeaponUid ? "  ★ " + L("ui.forge.primary") : "") + "</size>", 28, w.LegendaryId != null ? Theme.Gold : Theme.Text);
            nm.supportRichText = true;
            UiKit.Size(nm, -1, -1, -1, -1, 1);

            // Compiled stats are derived from the same WeaponCompiler the battle uses (no invented numbers).
            var spec = WeaponCompiler.Compile(S.Db, w, C.HeroStats(), null, w.Uid == C.Run.PrimaryWeaponUid);
            var stats = UiKit.Label(v.transform, LF("ui.forge.spec", spec.BaseDamage, (spec.CooldownTicks / 30f).ToString("0.00"), (spec.RangeMilli / 1000f).ToString("0.0"),
                (spec.CritChance / 10f).ToString("0"), S.Tips.DamageTypeName(spec.DamageType)), 20, Theme.Muted);
            UiKit.Size(stats, -1, 56);

            var stats2 = C.HeroStats();
            SlotRow(v.transform, L("slot.core"), w.CoreId != null ? new List<string> { w.CoreId } : new List<string>(), 1, ItemCategory.Core, w);
            SlotRow(v.transform, L("slot.modifiers"), w.Modifiers, AssemblyRules.ModifierSlots(S.Db, w, stats2), ItemCategory.Modifier, w);
            SlotRow(v.transform, L("slot.runes"), w.Runes, AssemblyRules.RuneSlots(S.Db, w, stats2), ItemCategory.Rune, w);

            // Socket the selected inventory item (with readable incompatibility reasons).
            if (item != null && (item.Category == ItemCategory.Core || item.Category == ItemCategory.Modifier || item.Category == ItemCategory.Rune))
            {
                List<AssemblyIssue> issues;
                if (item.Category == ItemCategory.Modifier) issues = AssemblyRules.PreviewAddModifier(S.Db, w, item.Id, stats2);
                else if (item.Category == ItemCategory.Rune) issues = AssemblyRules.PreviewAddRune(S.Db, w, item.Id, stats2);
                else { var probe = w.Clone(); probe.CoreId = item.Id; issues = AssemblyRules.Validate(S.Db, probe, stats2); }
                string label = LF("ui.forge.socket", S.Loc.Name(item.Category.ToString().ToLowerInvariant(), item.Id));
                var b = UiKit.Button(v.transform, label, () => Socket(w, item), issues.Count == 0 ? Theme.Ember : Theme.Disabled);
                if (issues.Count > 0)
                {
                    var why = new List<string>();
                    foreach (var i in issues) why.Add(LF("assembly." + i.Code, i.Detail ?? ""));
                    UiKit.Label(v.transform, string.Join("\n", why), 18, Theme.Danger);
                }
            }
            AssembleButton(v.transform, item);

            // Legendary recipe hints (progressive disclosure: closest 2, can be turned off in settings).
            if (S.Profile.Settings.HintsEnabled && w.LegendaryId == null)
            {
                var cands = LegendaryRules.Candidates(S.Db, w, C.Run.Essence);
                int shown = 0;
                foreach (var st in cands)
                {
                    if (shown >= 2) break;
                    shown++;
                    var recipe = st.Recipe;
                    var missing = new List<string>();
                    foreach (var m in st.Missing) missing.Add(MissingText(m));
                    var line = UiKit.Label(v.transform, (st.Eligible ? "✦ " : "◇ ") + S.Loc.Name("legendary", recipe.Id) + (st.Eligible ? "  " + L("ui.forge.ready_at_forge") : "\n<size=16>" + LF("ui.forge.missing", string.Join(", ", missing)) + "</size>"), 20, st.Eligible ? Theme.Gold : Theme.Muted);
                    line.supportRichText = true;
                    line.raycastTarget = true;
                    UiKit.Tooltip(line.gameObject, () => S.Loc.Name("legendary", recipe.Id) + "\n" + L("legendary." + recipe.Id + ".lore") + "\n" + string.Join("\n", S.Tips.Legendary(recipe)));
                    var fav = UiKit.Button(v.transform, S.Profile.FavoriteRecipe == recipe.Id ? L("ui.forge.unfavorite") : L("ui.forge.favorite"), () =>
                    {
                        S.Profile.FavoriteRecipe = S.Profile.FavoriteRecipe == recipe.Id ? null : recipe.Id;
                        C.FavoriteRecipe = S.Profile.FavoriteRecipe;
                        S.SaveProfile();
                        RebuildWeaponPanel();
                    }, null, 18);
                    UiKit.Size(fav, -1, 44, -1, 44);
                }
            }

            var actions = UiKit.HBox(v.transform, "Actions", 6);
            UiKit.Size(actions, -1, UiKit.MinTouch);
            if (C.Run.Board.FindWeapon(w.Uid) != null && w.Uid != C.Run.PrimaryWeaponUid)
                UiKit.Button(actions.transform, L("ui.forge.make_primary"), () => { Flow.Report(S, C.SetPrimary(w.Uid)); _board.Refresh(); RebuildWeaponPanel(); }, null, 20);
            UiKit.Button(actions.transform, L("ui.forge.dismantle"), () => S.Ui.Confirm(L("ui.forge.dismantle_confirm"), L("ui.yes"), L("ui.no"), () =>
            {
                if (Flow.Report(S, C.Dismantle(w.Uid))) { _selectedWeapon = -1; Refresh(); }
            }), null, 20);
            UiKit.Button(actions.transform, L("ui.forge.sell_weapon"), () => S.Ui.Confirm(L("ui.forge.sell_confirm"), L("ui.yes"), L("ui.no"), () =>
            {
                if (Flow.Report(S, C.SellWeapon(w.Uid))) { _selectedWeapon = -1; Refresh(); }
            }), Theme.Danger, 20);
        }

        private string MissingText(string m)
        {
            var p = m.Split(':');
            if (p.Length < 2) return L("recipe." + m);
            switch (p[0])
            {
                case "core": return S.Loc.Name("core", p[1]);
                case "modifier": return S.Loc.Name("modifier", p[1]);
                case "frame": return S.Loc.Name("frame", p[1]);
                case "rune":
                    var names = new List<string>();
                    foreach (var r in p[1].Split('|')) names.Add(S.Loc.Name("rune", r));
                    return string.Join("/", names);
                case "tier": return LF("ui.forge.tier", p[1]);
                case "essence": return LF("ui.hud.essence", p[1]);
                default: return m;
            }
        }

        private void AssembleButton(Transform parent, InventoryItem item)
        {
            if (item == null || item.Category != ItemCategory.Frame) return;
            InventoryItem core = null;
            foreach (var it in C.Run.Inventory) if (it.Category == ItemCategory.Core) { core = it; break; }
            UiKit.Button(parent, LF("ui.forge.assemble", S.Loc.Name("frame", item.Id)), () =>
            {
                if (Flow.Report(S, C.AssembleWeapon(item.Uid, -1)))
                {
                    _selectedItem = -1;
                    _selectedWeapon = C.Run.Weapons[C.Run.Weapons.Count - 1].Uid;
                    S.Audio.Play("sfx_forge_assemble");
                    Hints.Show(S, "core_intro");
                    Refresh();
                }
            }, Theme.Ember);
        }

        private void SlotRow(Transform parent, string title, List<string> ids, int slots, ItemCategory cat, WeaponInstance w)
        {
            var row = UiKit.HBox(parent, title, 6);
            UiKit.Size(row, -1, 64, -1, 64);
            var t = UiKit.Label(row.transform, title + " " + ids.Count + "/" + slots, 20, Theme.Muted);
            UiKit.Size(t, 150, -1, 150, -1, 0);
            for (int i = 0; i < Mathf.Max(slots, ids.Count); i++)
            {
                if (i < ids.Count)
                {
                    string id = ids[i];
                    var b = UiKit.Button(row.transform, S.Loc.Name(cat.ToString().ToLowerInvariant(), id), () =>
                    {
                        if (Flow.Report(S, C.RemoveComponent(w.Uid, cat, id))) { S.Audio.Play("sfx_unsocket"); Refresh(); }
                    }, Theme.PanelLight, 18);
                    UiKit.Tooltip(b.gameObject, () => ItemTooltip(cat, id) + "\n" + L("ui.forge.tap_to_remove"));
                }
                else
                {
                    var empty = UiKit.Panel(row.transform, "Empty", new Color(1, 1, 1, 0.06f));
                    var et = UiKit.Label(empty.transform, L("ui.forge.empty_slot"), 16, Theme.Muted, TextAnchor.MiddleCenter);
                    UiKit.Stretch(et.rectTransform);
                }
            }
        }

        private void Socket(WeaponInstance w, InventoryItem item)
        {
            ActionResult r;
            string sfx;
            switch (item.Category)
            {
                case ItemCategory.Core: r = C.SocketCore(w.Uid, item.Uid); sfx = "sfx_core_attach"; break;
                case ItemCategory.Modifier: r = C.AddModifier(w.Uid, item.Uid); sfx = "sfx_modifier_attach"; break;
                default: r = C.AddRune(w.Uid, item.Uid); sfx = "sfx_rune_etch"; break;
            }
            if (!Flow.Report(S, r)) return;
            S.Audio.Play(sfx);
            if (item.Category == ItemCategory.Rune) Hints.Show(S, "rune_intro");
            _selectedItem = -1;
            Refresh();
        }

        private void Refresh()
        {
            RebuildInventory();
            RebuildWeaponPanel();
            RebuildContext();
            _board.Refresh();
        }

        // ------------------------------------------------------------------ measured preview (training dummy)

        private void Measure()
        {
            var before = _lastProbe;
            _lastProbe = LoadoutProbe.MeasureRun(S.Db, C.Run, "training_single", 15);
            var swarm = LoadoutProbe.MeasureRun(S.Db, C.Run, "training_swarm", 15);
            var sb = new StringBuilder(LF("ui.measure.result", _lastProbe.Dps, swarm.Dps, _lastProbe.ActiveLinks));
            if (before != null) sb.Append("\n").Append(LF("ui.measure.delta", (_lastProbe.Dps - before.Dps).ToString("+0;-0;0")));
            _measure.text = sb.ToString();
            _measure.raycastTarget = true;
            UiKit.Tooltip(_measure.gameObject, () => L("ui.measure.explain"));
        }

        private string MeasureSummary()
        {
            return _lastProbe == null ? L("ui.measure.hint") : LF("ui.measure.stale", _lastProbe.Dps);
        }

        // ------------------------------------------------------------------ context panel (node-specific)

        private void RebuildContext()
        {
            UiKit.Clear(_context);
            var h = UiKit.HBox(_context, "H", 10, 8);
            UiKit.Stretch((RectTransform)h.transform);
            if (_fromMap) { UiKit.Button(h.transform, L("ui.back"), () => S.Ui.Back(), Theme.Ember); return; }
            switch (C.Run.Phase)
            {
                case RunPhase.PreBattle:
                    UiKit.Button(h.transform, LF("ui.forge.targeting", L("targeting." + C.Run.Targeting)), () =>
                    {
                        C.SetTargeting((TargetPriority)(((int)C.Run.Targeting + 1) % 6));
                        RebuildContext();
                    });
                    var enc = C.CurrentEncounter();
                    var info = UiKit.Label(h.transform, enc != null ? LF("ui.forge.next_fight", L("style." + enc.Style)) : "", 22, Theme.Muted, TextAnchor.MiddleCenter);
                    UiKit.Size(info, -1, -1, -1, -1, 2);
                    UiKit.Button(h.transform, L("ui.forge.fight"), StartBattle, Theme.Ember);
                    break;
                case RunPhase.InShop: BuildShop(h.transform); break;
                case RunPhase.InForge: BuildForgeNode(h.transform); break;
                case RunPhase.InRest: BuildRest(h.transform); break;
                default:
                    UiKit.Button(h.transform, L("ui.back"), () => Flow.Continue(S));
                    break;
            }
        }

        private void StartBattle()
        {
            var check = C.CanStartBattle();
            if (!Flow.Report(S, check)) return;
            S.Ui.Show(() => new BattleScreen());
        }

        private void BuildShop(Transform parent)
        {
            for (int i = 0; i < C.Run.ShopOffers.Count; i++)
            {
                int idx = i;
                var o = C.Run.ShopOffers[i];
                string name = S.Loc.Name(o.Category.ToString().ToLowerInvariant(), o.Id);
                var b = UiKit.Button(parent, o.Sold ? L("ui.shop.sold") : name + "\n" + o.Price + "g" + (o.Locked ? " 🔒" : ""), () =>
                {
                    if (o.Sold) return;
                    if (Flow.Report(S, C.Buy(idx))) { S.Audio.Play("sfx_buy"); Refresh(); }
                }, o.Sold ? Theme.Disabled : (C.Run.Gold >= o.Price ? Theme.Rarity(S.Db.ItemRarity(o.Category, o.Id)) * 0.55f + Theme.Panel * 0.45f : Theme.PanelLight), 18);
                UiKit.Tooltip(b.gameObject, () => ItemTooltip(o.Category, o.Id) + "\n" + L("ui.shop.lock_hint"));
                // Long-press/secondary lock toggle button
                var lockBtn = UiKit.Button(parent, o.Locked ? "🔒" : "🔓", () => { if (Flow.Report(S, C.ToggleLock(idx))) RebuildContext(); }, null, 18);
                UiKit.Size(lockBtn, 56, -1, 56, -1, 0);
            }
            var w = C.Run.FindWeapon(_selectedWeapon);
            if (w != null && w.Tier < AssemblyRules.MaxTier)
                UiKit.Button(parent, LF("ui.shop.temper", C.ShopUpgradeCost(w)), () => { if (Flow.Report(S, C.ShopUpgrade(w.Uid))) { S.Audio.Play("sfx_temper"); Refresh(); } }, null, 18);
            UiKit.Button(parent, LF("ui.shop.reroll", C.RerollCost()), () => { if (Flow.Report(S, C.Reroll())) { S.Audio.Play("sfx_reroll"); RebuildContext(); } }, null, 18);
            var item = C.Run.FindItem(_selectedItem);
            if (item != null)
                UiKit.Button(parent, LF("ui.shop.sell", EconomyRules.SellValue(S.Db, item.Category, item.Id)), () => S.Ui.Confirm(L("ui.shop.sell_confirm"), L("ui.yes"), L("ui.no"), () =>
                {
                    if (Flow.Report(S, C.Sell(item.Uid))) { _selectedItem = -1; S.Audio.Play("sfx_sell"); Refresh(); }
                }), Theme.Danger, 18);
            UiKit.Button(parent, L("ui.leave"), () => { if (Flow.Report(S, C.Leave())) Flow.Continue(S); }, Theme.Ember);
        }

        private void BuildForgeNode(Transform parent)
        {
            var w = C.Run.FindWeapon(_selectedWeapon);
            bool free = !C.Run.ForgeActionUsed;
            if (w != null)
            {
                string cost = free ? L("ui.forge.free") : EconomyRules.TierUpgradeCost(w.Tier) + "g";
                UiKit.Button(parent, LF("ui.forgenode.upgrade", cost), () => { if (Flow.Report(S, C.ForgeUpgrade(w.Uid))) { S.Audio.Play("sfx_temper"); Refresh(); } });
                foreach (var st in LegendaryRules.Candidates(S.Db, w, C.Run.Essence))
                {
                    if (!st.Eligible) continue;
                    var recipe = st.Recipe;
                    UiKit.Button(parent, LF("ui.forgenode.evolve", S.Loc.Name("legendary", recipe.Id), recipe.EssenceCost), () =>
                    {
                        if (Flow.Report(S, C.ForgeEvolve(w.Uid, recipe.Id))) S.Ui.Show(() => new EvolutionScreen(w.Uid, recipe.Id));
                    }, Theme.Gold);
                }
            }
            string expandCost = free ? L("ui.forge.free") : EconomyRules.BoardExpandCost + "g";
            UiKit.Button(parent, LF("ui.forgenode.expand", expandCost), () => { if (Flow.Report(S, C.ForgeExpandBoard())) { S.Audio.Play("sfx_board_expand"); Refresh(); } });
            UiKit.Button(parent, L("ui.leave"), () => { if (Flow.Report(S, C.Leave())) Flow.Continue(S); }, Theme.Ember);
        }

        private void BuildRest(Transform parent)
        {
            int heal = Mathf.Min(C.MaxHp() - C.Run.Hp, C.MaxHp() * (EconomyRules.RestHealPermille + C.HeroStats().SumUntagged(StatType.RestHeal)) / 1000);
            UiKit.Button(parent, LF("ui.rest.heal", heal), () => { if (Flow.Report(S, C.RestHeal())) { S.Audio.Play("sfx_rest"); Flow.Continue(S); } }, Theme.Ok);
            var w = C.Run.FindWeapon(_selectedWeapon);
            if (w != null && w.Tier < AssemblyRules.MaxTier)
                UiKit.Button(parent, LF("ui.rest.temper", RunBoardHost.WeaponName(S, w)), () => { if (Flow.Report(S, C.RestTemper(w.Uid))) { S.Audio.Play("sfx_temper"); Flow.Continue(S); } });
            UiKit.Button(parent, L("ui.leave"), () => { if (Flow.Report(S, C.Leave())) Flow.Continue(S); });
        }

        public override void Tick(float dt)
        {
            if (Input.GetKeyDown(KeyCode.R)) _board.RotateSelected();
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.LeftCommand)) && Input.GetKeyDown(KeyCode.Z)) { _board.Undo(); RebuildInventory(); }
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.LeftCommand)) && Input.GetKeyDown(KeyCode.Y)) { _board.Redo(); RebuildInventory(); }
            if (Input.GetKeyDown(KeyCode.Delete)) { _board.RemoveSelected(); RebuildInventory(); }
        }

        public override bool OnBack()
        {
            if (_board.PendingPlace != null) { _board.PendingPlace = null; return true; }
            if (_fromMap) { S.Ui.Back(); return true; }
            S.Ui.Show(() => new PauseMenuScreen(), true);
            return true;
        }
    }
}
