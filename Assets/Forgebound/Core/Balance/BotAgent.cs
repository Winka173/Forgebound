using System;
using System.Collections.Generic;
using Forgebound.Core.Combat;
using Forgebound.Core.Content;
using Forgebound.Core.ForgeBoard;
using Forgebound.Core.Run;
using Forgebound.Core.Simulation;
using Forgebound.Core.Weapons;

namespace Forgebound.Core.Balance
{
    public enum BotPolicy { Balanced, Aggressive, Cautious, Circuit, Status, Random }

    /// <summary>
    /// Deterministic build agent that plays full Adventure runs through the real RunController/Battle code.
    /// It is deliberately "competent but not optimal" — balance numbers are trends, not proof of fun; human playtests
    /// remain required (docs/BALANCE.md). Policies bias decisions so the simulator samples different archetypes.
    /// </summary>
    public sealed class BotAgent
    {
        private readonly RunController _c;
        private readonly DetRandom _rng;
        public readonly BotPolicy Policy;
        public int MaxActions = 4000;
        public readonly List<string> Log = new List<string>();
        public bool Verbose;
        public int ShopVisits, ShopOffersSeen, ShopOffersDead, Rerolls, GoldSpent, ItemsBought, GoldFromSales;

        public BotAgent(RunController controller, BotPolicy policy, ulong seed)
        {
            _c = controller;
            Policy = policy;
            _rng = new DetRandom(DetRandom.DeriveSeed(seed, "bot:" + policy));
        }

        private ContentDatabase Db { get { return _c.Db; } }
        private RunState Run { get { return _c.Run; } }

        private void Note(string s) { if (Verbose) Log.Add(s); }

        public void PlayToEnd()
        {
            int guard = 0;
            while (Run.Phase != RunPhase.Finished && guard++ < MaxActions) Step();
            if (Run.Phase != RunPhase.Finished) { _c.Abandon(); Note("bot: action budget exhausted"); }
        }

        /// <summary>Take exactly one decision for the current phase.</summary>
        public void Step()
        {
            switch (Run.Phase)
            {
                case RunPhase.ChoosingNode: ChooseNode(); break;
                case RunPhase.PreBattle: PrepareAndFight(); break;
                case RunPhase.InShop: Shop(); break;
                case RunPhase.InForge: Forge(); break;
                case RunPhase.InRest: Rest(); break;
                case RunPhase.InEvent: Event(); break;
                case RunPhase.InReward: Reward(); break;
            }
        }

        // ------------------------------------------------------------------ route

        private int NodeScore(MapNode n)
        {
            int hpPct = Run.Hp * 100 / Math.Max(1, _c.MaxHp());
            int s = 0;
            switch (n.Type)
            {
                case NodeType.Battle: s = 50; break;
                case NodeType.Elite: s = hpPct > 65 ? 60 : 10; if (Policy == BotPolicy.Aggressive) s += 30; if (Policy == BotPolicy.Cautious) s -= 30; break;
                case NodeType.Shop: s = Run.Gold >= 50 ? Math.Min(140, 40 + Run.Gold / 2) : 10; break;
                case NodeType.Forge: s = 55 + (CanEvolveAny() ? 70 : 0) + (Run.Essence >= 2 ? 10 : 0); break;
                case NodeType.Rest: s = hpPct < 50 ? 90 : 20; if (Policy == BotPolicy.Cautious) s += 20; break;
                case NodeType.Event: s = 45; break;
                case NodeType.Treasure: s = 80; break;
                case NodeType.Boss: s = 100; break;
            }
            if (Policy == BotPolicy.Random) s = _rng.Next(100);
            return s * 10 + _rng.Next(10);
        }

        private void ChooseNode()
        {
            var nodes = _c.AvailableNodes();
            if (nodes.Count == 0) { _c.Abandon(); return; }
            // Look-ahead route planning over the chapter graph (players see the whole map): value = node + best continuation.
            var memo = new Dictionary<string, int>();
            MapNode best = null;
            int bestScore = int.MinValue;
            foreach (var n in nodes)
            {
                int s = PathValue(n, Run.Gold, memo);
                if (s > bestScore) { bestScore = s; best = n; }
            }
            var r = _c.ChooseNode(best.Id);
            Note("node " + best.Id + " " + best.Type + " -> " + r);
        }

        private int PathValue(MapNode n, int gold, Dictionary<string, int> memo)
        {
            int own = NodeScoreWithGold(n, gold);
            int projected = gold + (n.Type == NodeType.Battle ? 18 : n.Type == NodeType.Elite ? 30 : 0) - (n.Type == NodeType.Shop ? gold : 0);
            int bestNext = 0;
            foreach (var id in n.Next)
            {
                var nx = Run.FindNode(id);
                if (nx == null) continue;
                bestNext = Math.Max(bestNext, PathValue(nx, Math.Max(0, projected), memo));
            }
            return own + bestNext * 9 / 10;
        }

        private int NodeScoreWithGold(MapNode n, int gold)
        {
            int saved = Run.Gold;
            Run.Gold = gold;
            int s = NodeScore(n);
            Run.Gold = saved;
            return s;
        }

        // ------------------------------------------------------------------ build management

        private bool CanEvolveAny()
        {
            foreach (var w in Run.Weapons)
                foreach (var st in LegendaryRules.Candidates(Db, w, Run.Essence))
                    if (st.Eligible) return true;
            return false;
        }

        /// <summary>How useful is an item right now (0 = dead offer for the current build).</summary>
        public int ItemValue(ItemCategory cat, string id)
        {
            var stats = _c.HeroStats();
            switch (cat)
            {
                case ItemCategory.Frame:
                    {
                        int owned = Run.Weapons.Count;
                        int v = owned < 3 ? 80 - owned * 15 : 10;
                        foreach (var w in Run.Weapons) if (w.FrameId == id) v -= 15;
                        return Math.Max(0, v);
                    }
                case ItemCategory.Core:
                    {
                        foreach (var w in Run.Weapons) if (w.CoreId == null) return 85;
                        return Policy == BotPolicy.Status ? 25 : 12;
                    }
                case ItemCategory.Modifier:
                    {
                        int best = 0;
                        foreach (var w in Run.Weapons)
                        {
                            if (AssemblyRules.PreviewAddModifier(Db, w, id, stats).Count == 0)
                            {
                                int v = 60 + RecipeBonus(w, "modifier:" + id);
                                if (w.Uid == Run.PrimaryWeaponUid) v += 10;
                                best = Math.Max(best, v);
                            }
                        }
                        return best;
                    }
                case ItemCategory.Rune:
                    {
                        int best = 0;
                        foreach (var w in Run.Weapons)
                            if (AssemblyRules.PreviewAddRune(Db, w, id, stats).Count == 0) best = Math.Max(best, 50 + RecipeBonus(w, "rune:" + id) + (Policy == BotPolicy.Status ? 10 : 0));
                        return best;
                    }
                case ItemCategory.Piece:
                    {
                        var def = Db.Piece(id);
                        if (def == null) return 0;
                        int weapons = BattleBuilder.ActiveWeapons(Run).Count;
                        int pieces = 0;
                        foreach (var p in Run.Board.Pieces) if (p.Kind != PieceKind.Weapon) pieces++;
                        foreach (var it in Run.Inventory) if (it.Category == ItemCategory.Piece) pieces++;
                        int v = def.Kind == PieceKind.Support ? 45 : (weapons >= 2 || def.Relay != null && (def.Relay.Type == RelayType.Capacitor || def.Relay.Type == RelayType.Siphon) ? 50 : 20);
                        if (Policy == BotPolicy.Circuit && def.Kind == PieceKind.Relay) v += 30;
                        v -= pieces * 6;
                        return Math.Max(0, v);
                    }
            }
            return 0;
        }

        private int RecipeBonus(WeaponInstance w, string component)
        {
            foreach (var st in LegendaryRules.Candidates(Db, w, 99))
                foreach (var m in st.Missing)
                    if (m == component || m.StartsWith(component.Split(':')[0] + ":") && m.Contains(component.Split(':')[1])) return 25;
            return 0;
        }

        private void AutoAssemble()
        {
            var stats = _c.HeroStats();
            // 1) Cores into coreless weapons.
            foreach (var w in new List<WeaponInstance>(Run.Weapons))
            {
                if (w.CoreId != null) continue;
                foreach (var it in new List<InventoryItem>(Run.Inventory))
                    if (it.Category == ItemCategory.Core && _c.SocketCore(w.Uid, it.Uid).Ok) { Note("core " + it.Id + " -> w" + w.Uid); break; }
            }
            // 2) Assemble new weapons from frames while < 3 weapons (with a spare core if available).
            while (Run.Weapons.Count < 3)
            {
                InventoryItem frame = null, core = null;
                foreach (var it in Run.Inventory) if (it.Category == ItemCategory.Frame) { frame = it; break; }
                if (frame == null) break;
                foreach (var it in Run.Inventory) if (it.Category == ItemCategory.Core) { core = it; break; }
                if (!_c.AssembleWeapon(frame.Uid, core != null ? core.Uid : -1).Ok) break;
                Note("assemble " + frame.Id + "+" + (core != null ? core.Id : "-"));
            }
            // 3) Modifiers and runes: best legal weapon (primary first, recipe-aware).
            foreach (var it in new List<InventoryItem>(Run.Inventory))
            {
                if (it.Category != ItemCategory.Modifier && it.Category != ItemCategory.Rune) continue;
                WeaponInstance best = null;
                int bestScore = -1;
                foreach (var w in Run.Weapons)
                {
                    var issues = it.Category == ItemCategory.Modifier ? AssemblyRules.PreviewAddModifier(Db, w, it.Id, stats) : AssemblyRules.PreviewAddRune(Db, w, it.Id, stats);
                    if (issues.Count > 0) continue;
                    int s = RecipeBonus(w, (it.Category == ItemCategory.Modifier ? "modifier:" : "rune:") + it.Id) + (w.Uid == Run.PrimaryWeaponUid ? 10 : 0) + (Run.Board.FindWeapon(w.Uid) != null ? 20 : 0);
                    if (s > bestScore) { bestScore = s; best = w; }
                }
                if (best == null) continue;
                var r = it.Category == ItemCategory.Modifier ? _c.AddModifier(best.Uid, it.Uid) : _c.AddRune(best.Uid, it.Uid);
                if (r.Ok) Note("socket " + it.Id + " -> w" + best.Uid);
            }
            // 4) Sell loose items that can never be used to keep inventory healthy.
            if (Run.Inventory.Count >= RunState.InventoryCap - 2)
                foreach (var it in new List<InventoryItem>(Run.Inventory))
                    if (ItemValue(it.Category, it.Id) == 0 && it.Category != ItemCategory.Piece) { GoldFromSales += EconomyHelper.Sell(_c, it); }
        }

        /// <summary>Greedy board layout: maximise active links + support adjacency, never leaves overlaps. Exposed for tests/UI "auto-arrange suggestion".</summary>
        public static void AutoArrange(RunController c, bool circuitBias)
        {
            var run = c.Run;
            var weapons = new List<WeaponInstance>(run.Weapons);
            // Primary choice: highest base DPS estimate.
            weapons.Sort((a, b) =>
            {
                long da = Score(c.Db, a), dbb = Score(c.Db, b);
                return da != dbb ? dbb.CompareTo(da) : a.Uid.CompareTo(b.Uid);
            });
            if (weapons.Count > BoardEditor.MaxActiveWeapons) weapons.RemoveRange(BoardEditor.MaxActiveWeapons, weapons.Count - BoardEditor.MaxActiveWeapons);
            // Pull everything off the board (relays back to inventory), then re-place.
            foreach (var p in new List<PlacedPiece>(run.Board.Pieces)) c.RemovePiece(p.Uid);
            foreach (var w in weapons) BestPlace(c, PieceKind.Weapon, w.Uid, -1, circuitBias);
            if (weapons.Count > 0) c.SetPrimary(weapons[0].Uid);
            var pieceItems = new List<InventoryItem>();
            foreach (var it in run.Inventory) if (it.Category == ItemCategory.Piece) pieceItems.Add(it);
            foreach (var it in pieceItems) BestPlace(c, PieceKind.Relay, -1, it.Uid, circuitBias);
        }

        private static long Score(ContentDatabase db, WeaponInstance w)
        {
            var f = db.Frame(w.FrameId);
            long s = f == null ? 0 : ContentValidator.EstimateDps(f);
            s = s * WeaponCompiler.TierDamagePermille(w.Tier) / 1000;
            s += w.Modifiers.Count * 150 + w.Runes.Count * 100 + (w.CoreId != null ? 200 : 0) + (w.LegendaryId != null ? 1500 : 0);
            return s;
        }

        private static int BoardScore(RunController c)
        {
            var a = c.AnalyzeBoard();
            if (!a.IsValid) return int.MinValue;
            int s = a.ActiveLinkCount * 30;
            foreach (var kv in a.SupportTargets) s += kv.Value.Count * 12;
            foreach (var i in a.Issues)
            {
                if (i.Code == "dead_relay") s -= 10;
                if (i.Code == "idle_support") s -= 8;
                if (i.Code == "capacity_exceeded") s -= 4;
            }
            return s;
        }

        private static void BestPlace(RunController c, PieceKind kind, int weaponUid, int itemUid, bool circuitBias)
        {
            var run = c.Run;
            int bestScore = int.MinValue, bx = -1, by = -1, br = 0;
            for (int rot = 0; rot < 4; rot++)
                for (int y = 0; y < run.Board.Height; y++)
                    for (int x = 0; x < run.Board.Width; x++)
                    {
                        var probe = run.Board.Clone();
                        var editor = new BoardEditor(probe, c.Shapes());
                        PlaceError e;
                        if (kind == PieceKind.Weapon) e = editor.Place(new PlacedPiece { Kind = PieceKind.Weapon, WeaponUid = weaponUid }, new Cell(x, y), rot);
                        else
                        {
                            var it = run.FindItem(itemUid);
                            var def = it != null ? c.Db.Piece(it.Id) : null;
                            if (def == null) return;
                            e = editor.Place(new PlacedPiece { Kind = def.Kind, PieceId = it.Id }, new Cell(x, y), rot);
                        }
                        if (e != PlaceError.None) continue;
                        var saved = run.Board;
                        run.Board = probe;
                        int s = BoardScore(c);
                        run.Board = saved;
                        // Prefer compact layouts toward the top-left for determinism and free space.
                        s = s * 100 - (x + y);
                        if (s > bestScore) { bestScore = s; bx = x; by = y; br = rot; }
                    }
            if (bx < 0) return;
            if (kind == PieceKind.Weapon) c.PlaceWeapon(weaponUid, bx, by, br);
            else
            {
                // Don't place a relay/support that adds nothing (keep it in inventory for later).
                int before = BoardScore(c);
                var probe = run.Board.Clone();
                var it = run.FindItem(itemUid);
                var def = c.Db.Piece(it.Id);
                new BoardEditor(probe, c.Shapes()).Place(new PlacedPiece { Kind = def.Kind, PieceId = it.Id }, new Cell(bx, by), br);
                var saved = run.Board;
                run.Board = probe;
                int after = BoardScore(c);
                run.Board = saved;
                if (after > before) c.PlacePiece(itemUid, bx, by, br);
            }
        }

        private void PrepareAndFight()
        {
            AutoAssemble();
            AutoArrange(_c, Policy == BotPolicy.Circuit);
            ChooseTargeting();
            var check = _c.CanStartBattle();
            if (!check.Ok) { Note("cannot fight: " + check); _c.Abandon(); return; }
            var r = _c.Fight();
            var lb = _c.LastBattle;
            Note("fight " + Run.CurrentNodeId + " -> " + (lb != null && lb.Victory ? "WIN" : "LOSS") + " hp=" + Run.Hp + " ticks=" + (lb != null ? lb.Ticks : 0));
        }

        private void ChooseTargeting()
        {
            var enc = _c.CurrentEncounter();
            if (enc == null) return;
            TargetPriority p = TargetPriority.Nearest;
            if (enc.Style == "healer" || enc.Style == "support" || enc.Style == "summoner") p = TargetPriority.HighestThreat;
            else if (enc.Style == "swarm") p = TargetPriority.Cluster;
            else if (enc.Style == "boss" || enc.Style == "elite") p = TargetPriority.HighestThreat;
            if (Policy == BotPolicy.Random) p = (TargetPriority)_rng.Next(6);
            if (Run.Targeting != p) _c.SetTargeting(p);
        }

        // ------------------------------------------------------------------ nodes

        private void Shop()
        {
            ShopVisits++;
            AutoAssemble();
            Note("shop gold=" + Run.Gold + " offers=" + Run.ShopOffers.Count);
            int reserve = Policy == BotPolicy.Aggressive ? 0 : 15;
            for (int pass = 0; pass < 8; pass++)
            {
                bool bought = false;
                for (int i = 0; i < Run.ShopOffers.Count; i++)
                {
                    var o = Run.ShopOffers[i];
                    if (pass == 0) { ShopOffersSeen++; if (ItemValue(o.Category, o.Id) == 0) ShopOffersDead++; }
                }
                // Buy best-value affordable offer.
                int bestIdx = -1, bestVal = 0;
                for (int i = 0; i < Run.ShopOffers.Count; i++)
                {
                    var o = Run.ShopOffers[i];
                    if (o.Sold || o.Price > Run.Gold - reserve) continue;
                    int v = ItemValue(o.Category, o.Id) * 100 / Math.Max(10, o.Price);
                    if (Policy == BotPolicy.Random) v = _rng.Next(100);
                    if (v > bestVal) { bestVal = v; bestIdx = i; }
                }
                if (bestIdx >= 0 && bestVal >= 30)
                {
                    int price = Run.ShopOffers[bestIdx].Price;
                    var o2 = Run.ShopOffers[bestIdx];
                    if (_c.Buy(bestIdx).Ok) { GoldSpent += price; ItemsBought++; bought = true; Note("buy " + o2.Category + ":" + o2.Id + " for " + price); AutoAssemble(); }
                }
                if (!bought)
                {
                    // Spend on tier upgrades for active weapons (primary first) before rerolling.
                    foreach (var w in BattleBuilder.ActiveWeapons(Run))
                    {
                        if (w.Tier >= AssemblyRules.MaxTier) continue;
                        int up = _c.ShopUpgradeCost(w);
                        if (Run.Gold - up >= reserve && _c.ShopUpgrade(w.Uid).Ok) { GoldSpent += up; bought = true; Note("temper w" + w.Uid + " for " + up); break; }
                    }
                    if (bought) continue;
                    int cost = _c.RerollCost();
                    if (Run.Gold - cost >= 45 && Rerolls < 60 && pass < 3)
                    {
                        if (_c.Reroll().Ok) { Rerolls++; GoldSpent += cost; continue; }
                    }
                    break;
                }
            }
            _c.Leave();
        }

        private void Forge()
        {
            AutoAssemble();
            // Evolve when possible (choose the recipe for the strongest weapon).
            foreach (var w in Run.Weapons)
                foreach (var st in LegendaryRules.Candidates(Db, w, Run.Essence))
                    if (st.Eligible && _c.ForgeEvolve(w.Uid, st.Recipe.Id).Ok) { Note("EVOLVE " + st.Recipe.Id); goto done; }
            {
                WeaponInstance target = null;
                foreach (var w in BattleBuilder.ActiveWeapons(Run)) if (w.Tier < AssemblyRules.MaxTier) { target = w; break; }
                bool wantsBoard = Policy == BotPolicy.Circuit && Run.Board.Width < BoardState.MaxWidth;
                if (target != null && !wantsBoard) _c.ForgeUpgrade(target.Uid);
                else _c.ForgeExpandBoard();
                // Paid extra upgrade if rich.
                if (target != null && Run.Gold > 140 && target.Tier < AssemblyRules.MaxTier) _c.ForgeUpgrade(target.Uid);
            }
        done:
            _c.Leave();
        }

        private void Rest()
        {
            int hpPct = Run.Hp * 100 / Math.Max(1, _c.MaxHp());
            WeaponInstance target = null;
            foreach (var w in BattleBuilder.ActiveWeapons(Run)) if (w.Tier < AssemblyRules.MaxTier) { target = w; break; }
            if (hpPct < 70 || target == null) _c.RestHeal();
            else _c.RestTemper(target.Uid);
            if (Run.Phase == RunPhase.InRest) _c.Leave();
        }

        private void Event()
        {
            var ev = Db.Event(Run.PendingEventId);
            if (ev == null) { _c.ChooseEventOption(0); return; }
            int hpPct = Run.Hp * 100 / Math.Max(1, _c.MaxHp());
            int best = -1, bestScore = int.MinValue;
            for (int i = 0; i < ev.Options.Count; i++)
            {
                var o = ev.Options[i];
                if (!_c.OptionAvailable(o)) continue;
                int s = 0;
                foreach (var oc in o.Outcomes)
                {
                    switch (oc.Type)
                    {
                        case OutcomeType.Damage: s -= hpPct < 45 ? oc.Value * 4 : oc.Value; break;
                        case OutcomeType.Heal: s += hpPct < 60 ? oc.Value * 3 : oc.Value / 2; break;
                        case OutcomeType.GainCurse: s -= Policy == BotPolicy.Aggressive ? 10 : 30; break;
                        case OutcomeType.Battle: s += hpPct > 60 && Policy != BotPolicy.Cautious ? 15 : -40; break;
                        case OutcomeType.Gold: s += oc.Value / 2; break;
                        case OutcomeType.Essence: s += oc.Value * 25; break;
                        case OutcomeType.UpgradeWeapon: s += 35; break;
                        case OutcomeType.GainBlessing: s += 35; break;
                        case OutcomeType.GainItem:
                        case OutcomeType.GainRandomItem: s += 25; break;
                        case OutcomeType.ExpandBoard: s += 20; break;
                        case OutcomeType.RemoveCurse: s += 30; break;
                        case OutcomeType.MaxHp: s += oc.Value * 2; break;
                    }
                }
                s -= o.RequireGold / 3;
                if (o.SuccessPermille < 1000) s = s * o.SuccessPermille / 1000;
                if (Policy == BotPolicy.Random) s = _rng.Next(100);
                if (s > bestScore) { bestScore = s; best = i; }
            }
            if (best < 0) best = 0;
            var r = _c.ChooseEventOption(best);
            if (!r.Ok)
                for (int i = 0; i < ev.Options.Count && Run.Phase == RunPhase.InEvent; i++) _c.ChooseEventOption(i);
        }

        private void Reward()
        {
            int best = -1, bestScore = -1;
            for (int i = 0; i < Run.PendingRewards.Count; i++)
            {
                var r = Run.PendingRewards[i];
                int s = r.IsBlessing ? 50 + (int)Db.Blessing(r.Id).Rarity * 10 : ItemValue(r.Category, r.Id);
                if (Policy == BotPolicy.Random) s = _rng.Next(100);
                if (s > bestScore) { bestScore = s; best = i; }
            }
            if (best >= 0 && bestScore > 0) _c.TakeReward(best);
            else _c.SkipReward();
        }
    }

    internal static class EconomyHelper
    {
        public static int Sell(RunController c, InventoryItem it)
        {
            int before = c.Run.Gold;
            c.Sell(it.Uid);
            return c.Run.Gold - before;
        }
    }
}
