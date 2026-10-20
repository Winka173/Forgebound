using System;
using System.Collections.Generic;
using Forgebound.Core.Combat;
using Forgebound.Core.Content;
using Forgebound.Core.Economy;
using Forgebound.Core.ForgeBoard;
using Forgebound.Core.Simulation;
using Forgebound.Core.Weapons;

namespace Forgebound.Core.Run
{
    public enum ActionError
    {
        None, WrongPhase, NotFound, NotEnoughGold, NotEnoughEssence, Invalid, InventoryFull, BoardError,
        AlreadyUsed, Incompatible, RunOver, TooManyWeapons, Requirement
    }

    public sealed class ActionResult
    {
        public bool Ok;
        public ActionError Error;
        public string Detail;
        public static readonly ActionResult Success = new ActionResult { Ok = true };
        public static ActionResult Fail(ActionError e, string detail = null) { return new ActionResult { Ok = false, Error = e, Detail = detail }; }
        public override string ToString() { return Ok ? "ok" : Error + (Detail != null ? ": " + Detail : ""); }
    }

    public sealed class StartOptions
    {
        public string ForgemasterId;
        public ulong Seed;
        public int Difficulty = 1;
        public string FavoriteRecipe;
        public List<string> Challenge = new List<string>();   // challenge ids applied as curses (adventure modifiers)
    }

    /// <summary>
    /// The only way gameplay mutates a RunState. Every successful action is appended to the action log, so a run can be
    /// replayed deterministically from (seed, forgemaster, difficulty, actions) — used by tests, bug reports and QA.
    /// </summary>
    public sealed class RunController
    {
        public readonly ContentDatabase Db;
        public readonly RunState Run;
        public string FavoriteRecipe;
        private readonly Dictionary<string, DetRandom> _rngs = new Dictionary<string, DetRandom>();
        private bool _replaying;
        public BattleResult LastBattle { get; private set; }
        public BoardAnalysis LastAnalysis { get; private set; }

        public RunController(ContentDatabase db, RunState run)
        {
            Db = db;
            Run = run;
        }

        // ------------------------------------------------------------------ start

        public static RunController StartNew(ContentDatabase db, StartOptions opt)
        {
            var fm = db.Forgemaster(opt.ForgemasterId);
            if (fm == null) throw new ArgumentException("Unknown forgemaster " + opt.ForgemasterId);
            var run = new RunState { Seed = opt.Seed, ForgemasterId = fm.Id, Difficulty = FMath.Clamp(opt.Difficulty, 0, 3) };
            var c = new RunController(db, run) { FavoriteRecipe = opt.FavoriteRecipe };
            var mapRng = c.Rng("map");
            var biomes = MapGenerator.PickBiomes(db, mapRng);
            for (int ch = 0; ch < RunState.ChapterCount; ch++) run.Maps.Add(MapGenerator.Generate(db, ch, biomes[ch], mapRng));
            foreach (var ch in opt.Challenge) if (db.Blessing(ch) != null) run.Blessings.Add(ch);

            run.MaxHpBase = fm.MaxHp;
            var stats = BattleBuilder.HeroStats(db, fm.Id, run.Blessings);
            run.Hp = BattleBuilder.MaxHp(db, run);
            run.Gold = fm.StartGold + stats.SumUntagged(StatType.StartGold);
            int extraW = stats.SumUntagged(StatType.BoardWidth);
            int extraH = stats.SumUntagged(StatType.BoardHeight);
            // Challenge rules may shrink the board (Blueprint Trials); never below 3x3.
            run.Board.Width = FMath.Clamp(BoardState.MinWidth + extraW, BoardState.AbsoluteMin, BoardState.MaxWidth);
            run.Board.Height = FMath.Clamp(BoardState.MinHeight + extraH, BoardState.AbsoluteMin, BoardState.MaxHeight);

            if (fm.StartFrame != null && db.Frame(fm.StartFrame) != null)
            {
                var w = new WeaponInstance { Uid = run.NextUid++, FrameId = fm.StartFrame, CoreId = fm.StartCore != null && db.Core(fm.StartCore) != null ? fm.StartCore : null };
                run.Weapons.Add(w);
                run.PrimaryWeaponUid = w.Uid;
                var editor = c.Editor();
                editor.Place(new PlacedPiece { Kind = PieceKind.Weapon, WeaponUid = w.Uid }, new Cell(0, 1), 0);
                c.Discover(w);
            }
            foreach (var pid in fm.StartPieces) if (db.Piece(pid) != null) c.AddItem(ItemCategory.Piece, pid);
            foreach (var item in fm.StartItems)
            {
                var parts = item.Split(':');
                ItemCategory cat;
                if (parts.Length == 2 && Enum.TryParse(parts[0], true, out cat) && db.ItemExists(cat, parts[1])) c.AddItem(cat, parts[1]);
            }
            run.ActionLog.Add(JsonValue.Obj().Set("a", "start").Set("fm", fm.Id).Set("seed", opt.Seed.ToString()).Set("difficulty", run.Difficulty)
                .Set("favorite", JsonValue.Str(opt.FavoriteRecipe)).Set("challenge", ToArr(opt.Challenge)));
            c.SyncRng();
            return c;
        }

        private static JsonValue ToArr(List<string> list)
        {
            var a = JsonValue.Arr();
            if (list != null) foreach (var s in list) a.Add(JsonValue.Str(s));
            return a;
        }

        // ------------------------------------------------------------------ helpers

        public DetRandom Rng(string stream)
        {
            DetRandom r;
            if (_rngs.TryGetValue(stream, out r)) return r;
            uint[] st;
            r = Run.RngStates.TryGetValue(stream, out st) ? DetRandom.FromState(st) : new DetRandom(DetRandom.DeriveSeed(Run.Seed, stream));
            _rngs[stream] = r;
            return r;
        }

        public void SyncRng()
        {
            foreach (var kv in _rngs) Run.RngStates[kv.Key] = kv.Value.GetState();
        }

        public StatBlock HeroStats() { return BattleBuilder.HeroStats(Db, Run.ForgemasterId, Run.Blessings); }
        public int MaxHp() { return BattleBuilder.MaxHp(Db, Run); }

        public ContentShapeResolver Shapes() { return new ContentShapeResolver(Db, Run.FindWeapon); }
        public BoardEditor Editor() { return new BoardEditor(Run.Board, Shapes()); }

        public BoardAnalysis AnalyzeBoard()
        {
            LastAnalysis = BoardAnalyzer.Analyze(Run.Board, Shapes(), HeroStats().SumUntagged(StatType.RelayCapacity));
            return LastAnalysis;
        }

        private ActionResult Log(ActionResult r, string action, params object[] kv)
        {
            if (r.Ok && action != "placeWeapon" && action != "placePiece" && action != "move" && action != "remove" && action != "undo" && action != "redo" && action != "primary" && action != "targeting")
                ClearBoardHistory();
            if (r.Ok && !_replaying)
            {
                var o = JsonValue.Obj().Set("a", action);
                for (int i = 0; i + 1 < kv.Length; i += 2)
                {
                    var v = kv[i + 1];
                    if (v is int) o.Set((string)kv[i], (int)v);
                    else if (v is string) o.Set((string)kv[i], (string)v);
                    else if (v is bool) o.Set((string)kv[i], (bool)v);
                    else if (v == null) o.Set((string)kv[i], JsonValue.Null);
                    else o.Set((string)kv[i], v.ToString());
                }
                Run.ActionLog.Add(o);
            }
            SyncRng();
            return r;
        }

        private bool Over { get { return Run.Phase == RunPhase.Finished; } }

        public InventoryItem AddItem(ItemCategory cat, string id)
        {
            if (Run.Inventory.Count >= RunState.InventoryCap) return null;
            var it = new InventoryItem { Uid = Run.NextUid++, Category = cat, Id = id };
            Run.Inventory.Add(it);
            string key = "item:" + cat + ":" + id;
            if (!Run.Discovered.Contains(key)) Run.Discovered.Add(key);
            return it;
        }

        private void Discover(WeaponInstance w)
        {
            string sig = "blueprint:" + w.FrameId + "|" + (w.CoreId ?? "-");
            if (!Run.Discovered.Contains(sig)) Run.Discovered.Add(sig);
            if (w.LegendaryId != null && !Run.Discovered.Contains("legendary:" + w.LegendaryId)) Run.Discovered.Add("legendary:" + w.LegendaryId);
        }

        public MapNode CurrentNode { get { return Run.CurrentNodeId != null ? Run.FindNode(Run.CurrentNodeId) : null; } }

        public List<MapNode> AvailableNodes()
        {
            var list = new List<MapNode>();
            if (Over || Run.Phase != RunPhase.ChoosingNode) return list;
            var cur = CurrentNode;
            var map = Run.CurrentMap;
            if (map == null) return list;
            if (cur == null || cur.Chapter != Run.Chapter)
            {
                foreach (var n in map.Nodes) if (n.Row == 0) list.Add(n);
            }
            else
            {
                foreach (var id in cur.Next) { var n = Run.FindNode(id); if (n != null) list.Add(n); }
            }
            return list;
        }

        // ------------------------------------------------------------------ route

        public ActionResult ChooseNode(string nodeId)
        {
            if (Over) return ActionResult.Fail(ActionError.RunOver);
            if (Run.Phase != RunPhase.ChoosingNode) return ActionResult.Fail(ActionError.WrongPhase);
            MapNode node = null;
            foreach (var n in AvailableNodes()) if (n.Id == nodeId) node = n;
            if (node == null) return ActionResult.Fail(ActionError.NotFound, nodeId);
            Run.CurrentNodeId = node.Id;
            node.Visited = true;
            Run.ForgeActionUsed = false;
            Run.RestUsed = false;
            Run.ShopRerolls = 0;
            switch (node.Type)
            {
                case NodeType.Battle:
                case NodeType.Elite:
                case NodeType.Boss:
                    Run.Phase = RunPhase.PreBattle;
                    break;
                case NodeType.Shop:
                    Run.Phase = RunPhase.InShop;
                    GenerateShop();
                    break;
                case NodeType.Forge:
                    Run.Phase = RunPhase.InForge;
                    break;
                case NodeType.Rest:
                    Run.Phase = RunPhase.InRest;
                    break;
                case NodeType.Event:
                    Run.Phase = RunPhase.InEvent;
                    Run.PendingEventId = node.EventId;
                    break;
                case NodeType.Treasure:
                    GenerateRewards(NodeType.Treasure);
                    Run.Phase = RunPhase.InReward;
                    break;
            }
            return Log(ActionResult.Success, "node", "id", nodeId);
        }

        /// <summary>Leave a service node (shop, forge, rest) and return to the map.</summary>
        public ActionResult Leave()
        {
            if (Run.Phase != RunPhase.InShop && Run.Phase != RunPhase.InForge && Run.Phase != RunPhase.InRest) return ActionResult.Fail(ActionError.WrongPhase);
            if (Run.Phase == RunPhase.InShop)
            {
                Run.LockedCarry.Clear();
                foreach (var o in Run.ShopOffers) if (o.Locked && !o.Sold) Run.LockedCarry.Add(new ShopOffer { Category = o.Category, Id = o.Id, Price = o.Price, Locked = true });
                Run.ShopOffers.Clear();
                Run.ShopDiscountPermille = 0;
            }
            CompleteNode();
            return Log(ActionResult.Success, "leave");
        }

        private void CompleteNode()
        {
            var node = CurrentNode;
            Run.Phase = RunPhase.ChoosingNode;
            if (node != null && node.Type == NodeType.Boss)
            {
                if (Run.Chapter >= RunState.ChapterCount - 1)
                {
                    Run.Outcome = RunOutcome.Victory;
                    Run.Phase = RunPhase.Finished;
                    return;
                }
                Run.Chapter++;
            }
        }

        // ------------------------------------------------------------------ shop

        private void GenerateShop()
        {
            var stats = HeroStats();
            int count = FMath.Clamp(EconomyRules.BaseShopOffers + stats.SumUntagged(StatType.ShopOffers), 3, EconomyRules.MaxShopOffers);
            int discount = stats.SumUntagged(StatType.ShopDiscount) + Run.ShopDiscountPermille;
            Run.ShopOffers.Clear();
            Run.ShopOffers.AddRange(ShopGenerator.Generate(Db, Run, Rng("shop"), count, discount, FavoriteRecipe));
            Run.LockedCarry.Clear();
        }

        public int RerollCost()
        {
            return EconomyRules.RerollCost(Run.ShopRerolls, HeroStats().SumUntagged(StatType.RerollDiscount));
        }

        public ActionResult Reroll()
        {
            if (Run.Phase != RunPhase.InShop) return ActionResult.Fail(ActionError.WrongPhase);
            int cost = RerollCost();
            if (Run.Gold < cost) return ActionResult.Fail(ActionError.NotEnoughGold, cost.ToString());
            Run.Gold -= cost;
            Run.ShopRerolls++;
            var keep = new List<ShopOffer>();
            foreach (var o in Run.ShopOffers) if (o.Locked && !o.Sold) keep.Add(o);
            Run.LockedCarry.Clear();
            Run.LockedCarry.AddRange(keep);
            var stats = HeroStats();
            int count = FMath.Clamp(EconomyRules.BaseShopOffers + stats.SumUntagged(StatType.ShopOffers), 3, EconomyRules.MaxShopOffers);
            Run.ShopOffers.Clear();
            Run.ShopOffers.AddRange(ShopGenerator.Generate(Db, Run, Rng("shop"), count, stats.SumUntagged(StatType.ShopDiscount) + Run.ShopDiscountPermille, FavoriteRecipe));
            Run.LockedCarry.Clear();
            return Log(ActionResult.Success, "reroll");
        }

        public ActionResult ToggleLock(int index)
        {
            if (Run.Phase != RunPhase.InShop) return ActionResult.Fail(ActionError.WrongPhase);
            if (index < 0 || index >= Run.ShopOffers.Count || Run.ShopOffers[index].Sold) return ActionResult.Fail(ActionError.NotFound);
            Run.ShopOffers[index].Locked = !Run.ShopOffers[index].Locked;
            return Log(ActionResult.Success, "lock", "i", index);
        }

        public ActionResult Buy(int index)
        {
            if (Run.Phase != RunPhase.InShop) return ActionResult.Fail(ActionError.WrongPhase);
            if (index < 0 || index >= Run.ShopOffers.Count) return ActionResult.Fail(ActionError.NotFound);
            var o = Run.ShopOffers[index];
            if (o.Sold) return ActionResult.Fail(ActionError.AlreadyUsed);
            if (Run.Gold < o.Price) return ActionResult.Fail(ActionError.NotEnoughGold, o.Price.ToString());
            if (Run.Inventory.Count >= RunState.InventoryCap) return ActionResult.Fail(ActionError.InventoryFull);
            Run.Gold -= o.Price;
            o.Sold = true;
            o.Locked = false;
            AddItem(o.Category, o.Id);
            return Log(ActionResult.Success, "buy", "i", index);
        }

        /// <summary>Shop service: temper a weapon one tier for gold (gold sink; costs more than a Forge node's free upgrade).</summary>
        public int ShopUpgradeCost(WeaponInstance w) { return EconomyRules.TierUpgradeCost(w.Tier) + EconomyRules.ShopUpgradeSurcharge; }

        public ActionResult ShopUpgrade(int weaponUid)
        {
            if (Run.Phase != RunPhase.InShop) return ActionResult.Fail(ActionError.WrongPhase);
            var w = Run.FindWeapon(weaponUid);
            if (w == null) return ActionResult.Fail(ActionError.NotFound);
            if (w.Tier >= AssemblyRules.MaxTier) return ActionResult.Fail(ActionError.Invalid, "max_tier");
            int cost = ShopUpgradeCost(w);
            if (Run.Gold < cost) return ActionResult.Fail(ActionError.NotEnoughGold, cost.ToString());
            Run.Gold -= cost;
            w.Tier++;
            return Log(ActionResult.Success, "shopUpgrade", "w", weaponUid);
        }

        /// <summary>Sell a loose inventory item (allowed anywhere outside battle; value is half the base cost).</summary>
        public ActionResult Sell(int itemUid)
        {
            if (Over) return ActionResult.Fail(ActionError.RunOver);
            var it = Run.FindItem(itemUid);
            if (it == null) return ActionResult.Fail(ActionError.NotFound);
            Run.Inventory.Remove(it);
            Run.Gold += EconomyRules.SellValue(Db, it.Category, it.Id);
            return Log(ActionResult.Success, "sell", "uid", itemUid);
        }

        // ------------------------------------------------------------------ assembly

        public ActionResult AssembleWeapon(int frameItemUid, int coreItemUid)
        {
            if (Over) return ActionResult.Fail(ActionError.RunOver);
            var frame = Run.FindItem(frameItemUid);
            if (frame == null || frame.Category != ItemCategory.Frame) return ActionResult.Fail(ActionError.NotFound, "frame");
            InventoryItem core = null;
            if (coreItemUid >= 0)
            {
                core = Run.FindItem(coreItemUid);
                if (core == null || core.Category != ItemCategory.Core) return ActionResult.Fail(ActionError.NotFound, "core");
            }
            var w = new WeaponInstance { Uid = Run.NextUid++, FrameId = frame.Id, CoreId = core != null ? core.Id : null };
            Run.Inventory.Remove(frame);
            if (core != null) Run.Inventory.Remove(core);
            Run.Weapons.Add(w);
            Discover(w);
            return Log(ActionResult.Success, "assemble", "frame", frameItemUid, "core", coreItemUid);
        }

        public ActionResult SocketCore(int weaponUid, int coreItemUid)
        {
            if (Over) return ActionResult.Fail(ActionError.RunOver);
            var w = Run.FindWeapon(weaponUid);
            var core = Run.FindItem(coreItemUid);
            if (w == null || core == null || core.Category != ItemCategory.Core) return ActionResult.Fail(ActionError.NotFound);
            if (w.LegendaryId != null) return ActionResult.Fail(ActionError.Invalid, "legendary_core_locked");
            var probe = w.Clone();
            probe.CoreId = core.Id;
            var issues = AssemblyRules.Validate(Db, probe, HeroStats());
            if (issues.Count > 0) return ActionResult.Fail(ActionError.Incompatible, issues[0].ToString());
            Run.Inventory.Remove(core);
            if (w.CoreId != null) AddItemForce(ItemCategory.Core, w.CoreId);
            w.CoreId = core.Id;
            Discover(w);
            return Log(ActionResult.Success, "core", "w", weaponUid, "item", coreItemUid);
        }

        /// <summary>Returned components always come back, even above the inventory cap (never destroy player items silently).</summary>
        private void AddItemForce(ItemCategory cat, string id)
        {
            Run.Inventory.Add(new InventoryItem { Uid = Run.NextUid++, Category = cat, Id = id });
        }

        public ActionResult AddModifier(int weaponUid, int itemUid)
        {
            if (Over) return ActionResult.Fail(ActionError.RunOver);
            var w = Run.FindWeapon(weaponUid);
            var it = Run.FindItem(itemUid);
            if (w == null || it == null || it.Category != ItemCategory.Modifier) return ActionResult.Fail(ActionError.NotFound);
            var issues = AssemblyRules.PreviewAddModifier(Db, w, it.Id, HeroStats());
            if (issues.Count > 0) return ActionResult.Fail(ActionError.Incompatible, issues[0].ToString());
            Run.Inventory.Remove(it);
            w.Modifiers.Add(it.Id);
            return Log(ActionResult.Success, "mod", "w", weaponUid, "item", itemUid);
        }

        public ActionResult AddRune(int weaponUid, int itemUid)
        {
            if (Over) return ActionResult.Fail(ActionError.RunOver);
            var w = Run.FindWeapon(weaponUid);
            var it = Run.FindItem(itemUid);
            if (w == null || it == null || it.Category != ItemCategory.Rune) return ActionResult.Fail(ActionError.NotFound);
            var issues = AssemblyRules.PreviewAddRune(Db, w, it.Id, HeroStats());
            if (issues.Count > 0) return ActionResult.Fail(ActionError.Incompatible, issues[0].ToString());
            Run.Inventory.Remove(it);
            w.Runes.Add(it.Id);
            return Log(ActionResult.Success, "rune", "w", weaponUid, "item", itemUid);
        }

        public ActionResult RemoveComponent(int weaponUid, ItemCategory cat, string id)
        {
            if (Over) return ActionResult.Fail(ActionError.RunOver);
            var w = Run.FindWeapon(weaponUid);
            if (w == null) return ActionResult.Fail(ActionError.NotFound);
            if (w.LegendaryId != null) return ActionResult.Fail(ActionError.Invalid, "legendary_components_fused");
            bool ok = false;
            if (cat == ItemCategory.Modifier) ok = w.Modifiers.Remove(id);
            else if (cat == ItemCategory.Rune) ok = w.Runes.Remove(id);
            else if (cat == ItemCategory.Core && w.CoreId == id) { w.CoreId = null; ok = true; }
            if (!ok) return ActionResult.Fail(ActionError.NotFound);
            AddItemForce(cat, id);
            return Log(ActionResult.Success, "unsocket", "w", weaponUid, "cat", cat.ToString(), "id", id);
        }

        public ActionResult Dismantle(int weaponUid)
        {
            if (Over) return ActionResult.Fail(ActionError.RunOver);
            var w = Run.FindWeapon(weaponUid);
            if (w == null) return ActionResult.Fail(ActionError.NotFound);
            if (w.LegendaryId != null) return ActionResult.Fail(ActionError.Invalid, "legendary_cannot_dismantle");
            if (Run.Weapons.Count <= 1) return ActionResult.Fail(ActionError.Invalid, "last_weapon");
            var piece = Run.Board.FindWeapon(weaponUid);
            if (piece != null) Run.Board.Pieces.Remove(piece);
            Run.Weapons.Remove(w);
            AddItemForce(ItemCategory.Frame, w.FrameId);
            if (w.CoreId != null) AddItemForce(ItemCategory.Core, w.CoreId);
            foreach (var m in w.Modifiers) AddItemForce(ItemCategory.Modifier, m);
            foreach (var r in w.Runes) AddItemForce(ItemCategory.Rune, r);
            FixPrimary();
            return Log(ActionResult.Success, "dismantle", "w", weaponUid);
        }

        /// <summary>Sell a whole weapon (components included) for half of their summed base cost.</summary>
        public ActionResult SellWeapon(int weaponUid)
        {
            if (Over) return ActionResult.Fail(ActionError.RunOver);
            var w = Run.FindWeapon(weaponUid);
            if (w == null) return ActionResult.Fail(ActionError.NotFound);
            if (Run.Weapons.Count <= 1) return ActionResult.Fail(ActionError.Invalid, "last_weapon");
            int value = EconomyRules.SellValue(Db, ItemCategory.Frame, w.FrameId);
            if (w.CoreId != null) value += EconomyRules.SellValue(Db, ItemCategory.Core, w.CoreId);
            foreach (var m in w.Modifiers) value += EconomyRules.SellValue(Db, ItemCategory.Modifier, m);
            foreach (var r in w.Runes) value += EconomyRules.SellValue(Db, ItemCategory.Rune, r);
            var piece = Run.Board.FindWeapon(weaponUid);
            if (piece != null) Run.Board.Pieces.Remove(piece);
            Run.Weapons.Remove(w);
            Run.Gold += value;
            FixPrimary();
            return Log(ActionResult.Success, "sellWeapon", "w", weaponUid);
        }

        // ------------------------------------------------------------------ board

        // ---- board undo/redo: snapshots (board + inventory + primary) kept in the controller; the undo/redo actions are
        // themselves logged, so replays rebuild the identical snapshot stacks and stay deterministic.
        private sealed class BoardSnapshot
        {
            public BoardState Board;
            public List<InventoryItem> Inventory;
            public int Primary;
        }

        private readonly List<BoardSnapshot> _undo = new List<BoardSnapshot>();
        private readonly List<BoardSnapshot> _redo = new List<BoardSnapshot>();
        public bool CanUndoBoard { get { return _undo.Count > 0; } }
        public bool CanRedoBoard { get { return _redo.Count > 0; } }

        private BoardSnapshot Capture()
        {
            var inv = new List<InventoryItem>();
            foreach (var it in Run.Inventory) inv.Add(new InventoryItem { Uid = it.Uid, Category = it.Category, Id = it.Id });
            return new BoardSnapshot { Board = Run.Board.Clone(), Inventory = inv, Primary = Run.PrimaryWeaponUid };
        }

        private void Restore(BoardSnapshot snap)
        {
            Run.Board = snap.Board.Clone();
            Run.Inventory.Clear();
            foreach (var it in snap.Inventory) Run.Inventory.Add(new InventoryItem { Uid = it.Uid, Category = it.Category, Id = it.Id });
            Run.PrimaryWeaponUid = snap.Primary;
        }

        private void PushUndo(BoardSnapshot before)
        {
            _undo.Add(before);
            if (_undo.Count > 40) _undo.RemoveAt(0);
            _redo.Clear();
        }

        public ActionResult UndoBoard()
        {
            if (_undo.Count == 0) return ActionResult.Fail(ActionError.Invalid, "nothing_to_undo");
            _redo.Add(Capture());
            Restore(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
            return Log(ActionResult.Success, "undo");
        }

        public ActionResult RedoBoard()
        {
            if (_redo.Count == 0) return ActionResult.Fail(ActionError.Invalid, "nothing_to_redo");
            _undo.Add(Capture());
            Restore(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
            return Log(ActionResult.Success, "redo");
        }

        /// <summary>Any non-board mutation invalidates board history (items may have been consumed).</summary>
        private void ClearBoardHistory() { _undo.Clear(); _redo.Clear(); }

        private ActionResult BoardResult(PlaceError e)
        {
            if (e == PlaceError.None) return ActionResult.Success;
            if (e == PlaceError.TooManyWeapons) return ActionResult.Fail(ActionError.TooManyWeapons);
            return ActionResult.Fail(ActionError.BoardError, e.ToString());
        }

        public ActionResult PlaceWeapon(int weaponUid, int x, int y, int rot)
        {
            var __before = Capture();
            if (Over) return ActionResult.Fail(ActionError.RunOver);
            if (Run.FindWeapon(weaponUid) == null) return ActionResult.Fail(ActionError.NotFound);
            var existing = Run.Board.FindWeapon(weaponUid);
            PlaceError e;
            if (existing != null) e = Editor().Move(existing.Uid, new Cell(x, y), rot);
            else e = Editor().Place(new PlacedPiece { Kind = PieceKind.Weapon, WeaponUid = weaponUid }, new Cell(x, y), rot);
            if (e == PlaceError.None) { FixPrimary(); PushUndo(__before); }
            return Log(BoardResult(e), "placeWeapon", "w", weaponUid, "x", x, "y", y, "rot", rot);
        }

        public ActionResult PlacePiece(int itemUid, int x, int y, int rot)
        {
            var __before = Capture();
            if (Over) return ActionResult.Fail(ActionError.RunOver);
            var it = Run.FindItem(itemUid);
            if (it == null || it.Category != ItemCategory.Piece) return ActionResult.Fail(ActionError.NotFound);
            var def = Db.Piece(it.Id);
            if (def == null) return ActionResult.Fail(ActionError.NotFound);
            var e = Editor().Place(new PlacedPiece { Kind = def.Kind == PieceKind.Support ? PieceKind.Support : PieceKind.Relay, PieceId = it.Id }, new Cell(x, y), rot);
            if (e == PlaceError.None) { Run.Inventory.Remove(it); PushUndo(__before); }
            return Log(BoardResult(e), "placePiece", "item", itemUid, "x", x, "y", y, "rot", rot);
        }

        public ActionResult MovePiece(int boardUid, int x, int y, int rot)
        {
            var __before = Capture();
            if (Over) return ActionResult.Fail(ActionError.RunOver);
            var e = Editor().Move(boardUid, new Cell(x, y), rot);
            if (e == PlaceError.None) PushUndo(__before);
            return Log(BoardResult(e), "move", "uid", boardUid, "x", x, "y", y, "rot", rot);
        }

        /// <summary>Remove a piece from the board. Relays/supports return to inventory; weapons go to the bench.</summary>
        public ActionResult RemovePiece(int boardUid)
        {
            var __before = Capture();
            if (Over) return ActionResult.Fail(ActionError.RunOver);
            var p = Run.Board.Find(boardUid);
            if (p == null) return ActionResult.Fail(ActionError.NotFound);
            Run.Board.Pieces.Remove(p);
            if (p.Kind != PieceKind.Weapon) AddItemForce(ItemCategory.Piece, p.PieceId);
            FixPrimary();
            PushUndo(__before);
            return Log(ActionResult.Success, "remove", "uid", boardUid);
        }

        public ActionResult SetPrimary(int weaponUid)
        {
            if (Run.Board.FindWeapon(weaponUid) == null) return ActionResult.Fail(ActionError.Invalid, "weapon_not_on_board");
            Run.PrimaryWeaponUid = weaponUid;
            return Log(ActionResult.Success, "primary", "w", weaponUid);
        }

        public ActionResult SetTargeting(TargetPriority p)
        {
            Run.Targeting = p;
            return Log(ActionResult.Success, "targeting", "p", p.ToString());
        }

        private void FixPrimary()
        {
            if (Run.PrimaryWeaponUid >= 0 && Run.Board.FindWeapon(Run.PrimaryWeaponUid) != null) return;
            Run.PrimaryWeaponUid = -1;
            var pieces = new List<PlacedPiece>(Run.Board.Pieces);
            pieces.Sort((a, b) => a.Uid.CompareTo(b.Uid));
            foreach (var p in pieces) if (p.Kind == PieceKind.Weapon) { Run.PrimaryWeaponUid = p.WeaponUid; break; }
        }

        // ------------------------------------------------------------------ forge / rest

        public ActionResult ForgeUpgrade(int weaponUid)
        {
            if (Run.Phase != RunPhase.InForge) return ActionResult.Fail(ActionError.WrongPhase);
            var w = Run.FindWeapon(weaponUid);
            if (w == null) return ActionResult.Fail(ActionError.NotFound);
            if (w.Tier >= AssemblyRules.MaxTier) return ActionResult.Fail(ActionError.Invalid, "max_tier");
            int cost = Run.ForgeActionUsed ? EconomyRules.TierUpgradeCost(w.Tier) : 0;
            if (Run.Gold < cost) return ActionResult.Fail(ActionError.NotEnoughGold, cost.ToString());
            Run.Gold -= cost;
            Run.ForgeActionUsed = true;
            w.Tier++;
            return Log(ActionResult.Success, "upgrade", "w", weaponUid);
        }

        public ActionResult ForgeExpandBoard()
        {
            if (Run.Phase != RunPhase.InForge) return ActionResult.Fail(ActionError.WrongPhase);
            if (Run.Board.Width >= BoardState.MaxWidth && Run.Board.Height >= BoardState.MaxHeight) return ActionResult.Fail(ActionError.Invalid, "max_size");
            int cost = Run.ForgeActionUsed ? EconomyRules.BoardExpandCost : 0;
            if (Run.Gold < cost) return ActionResult.Fail(ActionError.NotEnoughGold, cost.ToString());
            Run.Gold -= cost;
            Run.ForgeActionUsed = true;
            if (Run.Board.Width < BoardState.MaxWidth) Run.Board.Width++; else Run.Board.Height++;
            return Log(ActionResult.Success, "expand");
        }

        public ActionResult ForgeEvolve(int weaponUid, string legendaryId)
        {
            if (Run.Phase != RunPhase.InForge) return ActionResult.Fail(ActionError.WrongPhase);
            var w = Run.FindWeapon(weaponUid);
            var l = Db.Legendary(legendaryId);
            if (w == null || l == null) return ActionResult.Fail(ActionError.NotFound);
            var st = LegendaryRules.Evaluate(Db, w, l, Run.Essence);
            if (!st.Eligible) return ActionResult.Fail(ActionError.Requirement, string.Join(",", st.Missing));
            Run.Essence -= l.EssenceCost;
            w.LegendaryId = l.Id;
            if (w.Tier < l.MinTier) w.Tier = l.MinTier;
            Run.LegendariesForged++;
            Discover(w);
            return Log(ActionResult.Success, "evolve", "w", weaponUid, "id", legendaryId);
        }

        public ActionResult RestHeal()
        {
            if (Run.Phase != RunPhase.InRest) return ActionResult.Fail(ActionError.WrongPhase);
            if (Run.RestUsed) return ActionResult.Fail(ActionError.AlreadyUsed);
            int max = MaxHp();
            int heal = FMath.Permille(max, EconomyRules.RestHealPermille + HeroStats().SumUntagged(StatType.RestHeal));
            Run.Hp = Math.Min(max, Run.Hp + heal);
            Run.RestUsed = true;
            CompleteNode();
            return Log(ActionResult.Success, "restHeal");
        }

        public ActionResult RestTemper(int weaponUid)
        {
            if (Run.Phase != RunPhase.InRest) return ActionResult.Fail(ActionError.WrongPhase);
            if (Run.RestUsed) return ActionResult.Fail(ActionError.AlreadyUsed);
            var w = Run.FindWeapon(weaponUid);
            if (w == null) return ActionResult.Fail(ActionError.NotFound);
            if (w.Tier >= AssemblyRules.MaxTier) return ActionResult.Fail(ActionError.Invalid, "max_tier");
            w.Tier++;
            Run.RestUsed = true;
            CompleteNode();
            return Log(ActionResult.Success, "restTemper", "w", weaponUid);
        }

        // ------------------------------------------------------------------ battle

        public EncounterDef CurrentEncounter()
        {
            var n = CurrentNode;
            return n != null && n.EncounterId != null ? Db.Encounter(n.EncounterId) : null;
        }

        /// <summary>Validates the loadout: at least one weapon on the board and all weapon assemblies legal.</summary>
        public ActionResult CanStartBattle()
        {
            if (Run.Phase != RunPhase.PreBattle) return ActionResult.Fail(ActionError.WrongPhase);
            if (BattleBuilder.ActiveWeapons(Run).Count == 0) return ActionResult.Fail(ActionError.Invalid, "no_weapon_on_board");
            var stats = HeroStats();
            foreach (var w in Run.Weapons)
            {
                var issues = AssemblyRules.Validate(Db, w, stats);
                if (issues.Count > 0) return ActionResult.Fail(ActionError.Incompatible, w.Uid + ":" + issues[0]);
            }
            var a = AnalyzeBoard();
            if (!a.IsValid) return ActionResult.Fail(ActionError.BoardError, a.Issues.Count > 0 ? a.Issues[0].ToString() : "invalid");
            if (CurrentEncounter() == null) return ActionResult.Fail(ActionError.NotFound, "encounter");
            return ActionResult.Success;
        }

        /// <summary>Create the battle for presentation to step (Unity) — call ResolveBattle with its result.</summary>
        public Battle CreateBattle(bool recordEvents)
        {
            if (!CanStartBattle().Ok) return null;
            BoardAnalysis analysis;
            var setup = BattleBuilder.Build(Db, Run, CurrentEncounter(), BattleBuilder.BattleSeed(Run.Seed, Run.BattlesFought, Run.CurrentNodeId), out analysis);
            setup.RecordEvents = recordEvents;
            LastAnalysis = analysis;
            return new Battle(setup);
        }

        /// <summary>Headless: simulate the current battle to the end and resolve it.</summary>
        public ActionResult Fight()
        {
            var check = CanStartBattle();
            if (!check.Ok) return check;
            var b = CreateBattle(false);
            while (!b.Finished) b.Step();
            return ResolveBattle(b.Result);
        }

        public ActionResult ResolveBattle(BattleResult result)
        {
            if (Run.Phase != RunPhase.PreBattle || result == null) return ActionResult.Fail(ActionError.WrongPhase);
            LastBattle = result;
            var node = CurrentNode;
            var rec = new NodeRecord
            {
                NodeId = node.Id, Type = node.Type, EncounterId = node.EncounterId, Won = result.Victory, Ticks = result.Ticks,
                HpBefore = Run.Hp, HpAfter = result.HeroHp, DamageDealt = result.Report.TotalDamage, KilledBy = result.Report.KilledBy
            };
            foreach (var w in BattleBuilder.ActiveWeapons(Run)) rec.WeaponSignatures.Add(w.Signature());
            Run.BattlesFought++;
            Run.TotalDamage += result.Report.TotalDamage;
            if (!result.Victory)
            {
                Run.Hp = 0;
                rec.GoldAfter = Run.Gold;
                Run.History.Add(rec);
                Run.Outcome = RunOutcome.Defeat;
                Run.DeathReason = result.DeathReason + (result.Report.KilledBy != null ? ":" + result.Report.KilledBy : "");
                Run.Phase = RunPhase.Finished;
                return Log(ActionResult.Success, "fight");
            }
            Run.Hp = Math.Max(1, result.HeroHp);
            if (Db.Tuning.PostBattleHealPermille > 0) Run.Hp = Math.Min(MaxHp(), Run.Hp + FMath.Permille(MaxHp(), Db.Tuning.PostBattleHealPermille));
            var stats = HeroStats();
            var enc = Db.Encounter(node.EncounterId);
            int gold = 0;
            if (enc != null)
                foreach (var wave in enc.Waves)
                    foreach (var sp in wave.Spawns) { var e = Db.Enemy(sp.Enemy); if (e != null) gold += e.Gold * sp.Count; }
            gold += node.Type == NodeType.Elite ? 15 : (node.Type == NodeType.Boss ? 35 : 6);
            gold += result.GoldBonus;
            gold = FMath.Permille(gold, 1000 + stats.SumUntagged(StatType.GoldGain));
            Run.Gold += Math.Max(0, gold);
            int essence = node.Type == NodeType.Elite ? 1 : (node.Type == NodeType.Boss ? 2 : 0);
            if (essence > 0) essence += stats.SumUntagged(StatType.EssenceGain) > 0 && Rng("reward").Chance(stats.SumUntagged(StatType.EssenceGain)) ? 1 : 0;
            Run.Essence += essence;
            if (node.Type == NodeType.Elite) Run.ElitesKilled++;
            if (node.Type == NodeType.Boss)
            {
                Run.BossesKilled++;
                Run.Hp = Math.Min(MaxHp(), Run.Hp + FMath.Permille(MaxHp(), 300));
            }
            rec.GoldAfter = Run.Gold;
            Run.History.Add(rec);
            GenerateRewards(node.Type);
            Run.Phase = RunPhase.InReward;
            return Log(ActionResult.Success, "fight");
        }

        // ------------------------------------------------------------------ rewards

        private void GenerateRewards(NodeType kind)
        {
            Run.PendingRewards.Clear();
            var rng = Rng("reward");
            var stats = HeroStats();
            int choices = FMath.Clamp(3 + stats.SumUntagged(StatType.RewardChoices), 2, 5);
            bool forced = Run.NextRewardBlessing;
            Run.NextRewardBlessing = false;
            if (forced || kind == NodeType.Boss || kind == NodeType.Elite && rng.Chance(350))
            {
                foreach (var b in BlessingChoices(rng, choices, false)) Run.PendingRewards.Add(new RewardChoice { Id = b, IsBlessing = true });
                if (Run.PendingRewards.Count > 0) return;
            }
            var taken = new HashSet<string>();
            int chapterBoost = kind == NodeType.Elite || kind == NodeType.Treasure ? 1 : 0;
            int[] cw = { 10, 14, 30, 30, 16 };
            ItemCategory[] cats = { ItemCategory.Frame, ItemCategory.Core, ItemCategory.Modifier, ItemCategory.Rune, ItemCategory.Piece };
            int guard = 0;
            while (Run.PendingRewards.Count < choices && guard++ < 50)
            {
                var cat = cats[Math.Max(0, rng.WeightedIndex(cw))];
                var rarity = (Rarity)FMath.Clamp(rng.WeightedIndex(EconomyRules.RarityWeights(Math.Min(2, Run.Chapter + chapterBoost))) + chapterBoost, 0, 3);
                var id = ShopGenerator.PickItem(Db, Run, cat, rarity, rng, taken);
                if (id == null) continue;
                taken.Add(cat + ":" + id);
                Run.PendingRewards.Add(new RewardChoice { Category = cat, Id = id });
            }
        }

        public List<string> BlessingChoices(DetRandom rng, int count, bool curses)
        {
            var groups = new HashSet<string>();
            foreach (var b in Run.Blessings) { var d = Db.Blessing(b); if (d != null && d.Group != null) groups.Add(d.Group); }
            var pool = new List<BlessingDef>();
            var weights = new List<int>();
            foreach (var b in Db.Blessings)
            {
                if (b.IsCurse != curses || Run.Blessings.Contains(b.Id)) continue;
                if (b.Group != null && groups.Contains(b.Group)) continue;
                if (b.Tags.Contains("challenge_only")) continue;
                pool.Add(b);
                weights.Add(b.Rarity == Rarity.Common ? 10 : b.Rarity == Rarity.Uncommon ? 6 : b.Rarity == Rarity.Rare ? 3 : 1);
            }
            var result = new List<string>();
            var usedGroups = new HashSet<string>();
            while (result.Count < count && pool.Count > 0)
            {
                int i = Math.Max(0, rng.WeightedIndex(weights.ToArray()));
                var b = pool[i];
                pool.RemoveAt(i);
                weights.RemoveAt(i);
                if (b.Group != null && !usedGroups.Add(b.Group)) continue;
                result.Add(b.Id);
            }
            return result;
        }

        public ActionResult TakeReward(int index)
        {
            if (Run.Phase != RunPhase.InReward) return ActionResult.Fail(ActionError.WrongPhase);
            if (index < 0 || index >= Run.PendingRewards.Count) return ActionResult.Fail(ActionError.NotFound);
            var r = Run.PendingRewards[index];
            if (r.IsBlessing)
            {
                int oldMax = MaxHp();
                Run.Blessings.Add(r.Id);
                int newMax = MaxHp();
                if (newMax > oldMax) Run.Hp += newMax - oldMax;
                Run.Hp = Math.Min(Run.Hp, newMax);
            }
            else
            {
                if (Run.Inventory.Count >= RunState.InventoryCap) Run.Gold += EconomyRules.SellValue(Db, r.Category, r.Id);
                else AddItem(r.Category, r.Id);
            }
            Run.PendingRewards.Clear();
            CompleteNode();
            return Log(ActionResult.Success, "take", "i", index);
        }

        public ActionResult SkipReward()
        {
            if (Run.Phase != RunPhase.InReward) return ActionResult.Fail(ActionError.WrongPhase);
            Run.Gold += EconomyRules.SkipRewardGold;
            Run.PendingRewards.Clear();
            CompleteNode();
            return Log(ActionResult.Success, "skip");
        }

        // ------------------------------------------------------------------ events

        public bool OptionAvailable(EventOptionDef o)
        {
            if (Run.Gold < o.RequireGold || Run.Essence < o.RequireEssence || Run.Hp <= o.RequireHp) return false;
            if (o.RequireTag != null)
            {
                if (o.RequireTag.StartsWith("fm:")) return Run.ForgemasterId == o.RequireTag.Substring(3);
                if (o.RequireTag.StartsWith("core:")) { foreach (var w in Run.Weapons) if (w.CoreId == o.RequireTag.Substring(5)) return true; return false; }
                if (o.RequireTag.StartsWith("frame:")) { foreach (var w in Run.Weapons) if (w.FrameId == o.RequireTag.Substring(6)) return true; return false; }
                if (o.RequireTag == "has_curse") { foreach (var b in Run.Blessings) { var d = Db.Blessing(b); if (d != null && d.IsCurse) return true; } return false; }
                if (o.RequireTag == "weapon_upgradable") { foreach (var w in Run.Weapons) if (w.Tier < AssemblyRules.MaxTier) return true; return false; }
            }
            return true;
        }

        public ActionResult ChooseEventOption(int index)
        {
            if (Run.Phase != RunPhase.InEvent) return ActionResult.Fail(ActionError.WrongPhase);
            var ev = Db.Event(Run.PendingEventId);
            if (ev == null) { CompleteNode(); return Log(ActionResult.Success, "event", "i", index); }
            if (index < 0 || index >= ev.Options.Count) return ActionResult.Fail(ActionError.NotFound);
            var opt = ev.Options[index];
            if (!OptionAvailable(opt)) return ActionResult.Fail(ActionError.Requirement);
            Run.Gold -= opt.RequireGold;
            Run.Essence -= opt.RequireEssence;
            var rng = Rng("event");
            bool success = rng.Chance(opt.SuccessPermille);
            var outcomes = success ? opt.Outcomes : opt.FailOutcomes;
            string battle = null;
            Run.PendingEventId = null;
            Run.Phase = RunPhase.ChoosingNode;
            bool rewardPending = false;
            foreach (var oc in outcomes)
            {
                if (!rng.Chance(oc.ChancePermille)) continue;
                string b = ApplyOutcome(oc, rng, ref rewardPending);
                if (b != null) battle = b;
            }
            if (Run.Hp <= 0)
            {
                Run.Hp = 0;
                Run.Outcome = RunOutcome.Defeat;
                Run.DeathReason = "event:" + ev.Id;
                Run.Phase = RunPhase.Finished;
            }
            else if (battle != null)
            {
                if (rewardPending) { Run.NextRewardBlessing = true; Run.PendingRewards.Clear(); }
                CurrentNode.EncounterId = battle;
                Run.Phase = RunPhase.PreBattle;
            }
            else if (rewardPending) Run.Phase = RunPhase.InReward;
            else CompleteNode();
            return Log(ActionResult.Success, "event", "i", index);
        }

        private string ApplyOutcome(OutcomeDef oc, DetRandom rng, ref bool rewardPending)
        {
            int max = MaxHp();
            switch (oc.Type)
            {
                case OutcomeType.Gold: Run.Gold = Math.Max(0, Run.Gold + oc.Value); break;
                case OutcomeType.Essence: Run.Essence = Math.Max(0, Run.Essence + oc.Value); break;
                case OutcomeType.Heal: Run.Hp = Math.Min(max, Run.Hp + Math.Max(1, max * oc.Value / 100)); break;
                case OutcomeType.Damage: Run.Hp -= Math.Max(1, max * oc.Value / 100); break;
                case OutcomeType.MaxHp: Run.MaxHpBase = Math.Max(10, Run.MaxHpBase + oc.Value); Run.Hp = Math.Max(1, Math.Min(MaxHp(), Run.Hp + Math.Max(0, oc.Value))); break;
                case OutcomeType.GainItem:
                    if (oc.Id != null && Db.ItemExists(oc.Category, oc.Id)) { if (AddItem(oc.Category, oc.Id) == null) Run.Gold += EconomyRules.SellValue(Db, oc.Category, oc.Id); }
                    break;
                case OutcomeType.GainRandomItem:
                    {
                        var id = ShopGenerator.PickItem(Db, Run, oc.Category, oc.MinRarity, rng, new HashSet<string>());
                        if (id != null && AddItem(oc.Category, id) == null) Run.Gold += EconomyRules.SellValue(Db, oc.Category, id);
                        break;
                    }
                case OutcomeType.GainBlessing:
                    {
                        if (oc.Id != null && Db.Blessing(oc.Id) != null && !Run.Blessings.Contains(oc.Id)) Run.Blessings.Add(oc.Id);
                        else
                        {
                            Run.PendingRewards.Clear();
                            foreach (var b in BlessingChoices(rng, 3, false)) Run.PendingRewards.Add(new RewardChoice { Id = b, IsBlessing = true });
                            rewardPending = Run.PendingRewards.Count > 0;
                        }
                        break;
                    }
                case OutcomeType.GainCurse:
                    {
                        string id = oc.Id;
                        if (id == null || Db.Blessing(id) == null)
                        {
                            var list = BlessingChoices(rng, 1, true);
                            id = list.Count > 0 ? list[0] : null;
                        }
                        if (id != null && !Run.Blessings.Contains(id)) Run.Blessings.Add(id);
                        Run.Hp = Math.Min(Run.Hp, MaxHp());
                        break;
                    }
                case OutcomeType.RemoveCurse:
                    for (int i = Run.Blessings.Count - 1; i >= 0; i--)
                    {
                        var d = Db.Blessing(Run.Blessings[i]);
                        if (d != null && d.IsCurse && !d.Tags.Contains("challenge_only")) { Run.Blessings.RemoveAt(i); break; }
                    }
                    break;
                case OutcomeType.UpgradeWeapon:
                    {
                        WeaponInstance best = null;
                        foreach (var w in BattleBuilder.ActiveWeapons(Run)) if (w.Tier < AssemblyRules.MaxTier && (best == null || w.Tier < best.Tier)) best = w;
                        if (best == null) foreach (var w in Run.Weapons) if (w.Tier < AssemblyRules.MaxTier) { best = w; break; }
                        if (best != null) best.Tier++;
                        else Run.Gold += 30;
                        break;
                    }
                case OutcomeType.Battle:
                    return oc.Id ?? MapGenerator.PickEncounter(Db, NodeType.Elite, Run.CurrentMap.Biome, Run.Chapter + 1, rng, null);
                case OutcomeType.ExpandBoard:
                    if (Run.Board.Width < BoardState.MaxWidth) Run.Board.Width++;
                    else if (Run.Board.Height < BoardState.MaxHeight) Run.Board.Height++;
                    else Run.Gold += 40;
                    break;
                case OutcomeType.LoseItem:
                    if (Run.Inventory.Count > 0) Run.Inventory.RemoveAt(rng.Next(Run.Inventory.Count));
                    break;
                case OutcomeType.ShopDiscount: Run.ShopDiscountPermille = oc.Value; break;
                case OutcomeType.RevealMap: Run.RevealMapChapters++; break;
            }
            return null;
        }

        public ActionResult Abandon()
        {
            if (Over) return ActionResult.Fail(ActionError.RunOver);
            Run.Outcome = RunOutcome.Abandoned;
            Run.Phase = RunPhase.Finished;
            return Log(ActionResult.Success, "abandon");
        }

        // ------------------------------------------------------------------ replay

        /// <summary>Re-apply a logged action. Returns false if it fails (desync / corrupt log).</summary>
        public bool Apply(JsonValue a)
        {
            _replaying = true;
            try
            {
                ActionResult r;
                switch (a.Str("a"))
                {
                    case "start": return true;
                    case "node": r = ChooseNode(a.Str("id")); break;
                    case "leave": r = Leave(); break;
                    case "reroll": r = Reroll(); break;
                    case "lock": r = ToggleLock(a.Int("i")); break;
                    case "buy": r = Buy(a.Int("i")); break;
                    case "sell": r = Sell(a.Int("uid")); break;
                    case "assemble": r = AssembleWeapon(a.Int("frame"), a.Int("core")); break;
                    case "core": r = SocketCore(a.Int("w"), a.Int("item")); break;
                    case "mod": r = AddModifier(a.Int("w"), a.Int("item")); break;
                    case "rune": r = AddRune(a.Int("w"), a.Int("item")); break;
                    case "unsocket":
                        {
                            ItemCategory cat;
                            Enum.TryParse(a.Str("cat"), out cat);
                            r = RemoveComponent(a.Int("w"), cat, a.Str("id"));
                            break;
                        }
                    case "dismantle": r = Dismantle(a.Int("w")); break;
                    case "sellWeapon": r = SellWeapon(a.Int("w")); break;
                    case "placeWeapon": r = PlaceWeapon(a.Int("w"), a.Int("x"), a.Int("y"), a.Int("rot")); break;
                    case "placePiece": r = PlacePiece(a.Int("item"), a.Int("x"), a.Int("y"), a.Int("rot")); break;
                    case "move": r = MovePiece(a.Int("uid"), a.Int("x"), a.Int("y"), a.Int("rot")); break;
                    case "remove": r = RemovePiece(a.Int("uid")); break;
                    case "primary": r = SetPrimary(a.Int("w")); break;
                    case "targeting":
                        {
                            TargetPriority p;
                            Enum.TryParse(a.Str("p"), out p);
                            r = SetTargeting(p);
                            break;
                        }
                    case "upgrade": r = ForgeUpgrade(a.Int("w")); break;
                    case "shopUpgrade": r = ShopUpgrade(a.Int("w")); break;
                    case "expand": r = ForgeExpandBoard(); break;
                    case "evolve": r = ForgeEvolve(a.Int("w"), a.Str("id")); break;
                    case "restHeal": r = RestHeal(); break;
                    case "restTemper": r = RestTemper(a.Int("w")); break;
                    case "fight": r = Fight(); break;
                    case "take": r = TakeReward(a.Int("i")); break;
                    case "skip": r = SkipReward(); break;
                    case "event": r = ChooseEventOption(a.Int("i")); break;
                    case "abandon": r = Abandon(); break;
                    case "undo": r = UndoBoard(); break;
                    case "redo": r = RedoBoard(); break;
                    default: return false;
                }
                if (r.Ok) Run.ActionLog.Add(a);
                return r.Ok;
            }
            finally { _replaying = false; }
        }

        public static RunController Replay(ContentDatabase db, List<JsonValue> actions)
        {
            if (actions == null || actions.Count == 0 || actions[0].Str("a") != "start") throw new ArgumentException("Replay log must begin with a start action");
            var s = actions[0];
            ulong seed;
            ulong.TryParse(s.Str("seed", "0"), out seed);
            var opt = new StartOptions { ForgemasterId = s.Str("fm"), Seed = seed, Difficulty = s.Int("difficulty", 1), FavoriteRecipe = s.Str("favorite") };
            opt.Challenge.AddRange(s.StrList("challenge"));
            var c = StartNew(db, opt);
            for (int i = 1; i < actions.Count; i++)
                if (!c.Apply(actions[i])) throw new InvalidOperationException("Replay desync at action " + i + ": " + actions[i]);
            return c;
        }
    }
}
