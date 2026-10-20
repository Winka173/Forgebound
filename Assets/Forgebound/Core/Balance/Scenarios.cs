using System;
using System.Collections.Generic;
using Forgebound.Core.Combat;
using Forgebound.Core.Content;
using Forgebound.Core.Economy;
using Forgebound.Core.ForgeBoard;
using Forgebound.Core.Run;
using Forgebound.Core.Simulation;
using Forgebound.Core.Weapons;

namespace Forgebound.Core.Balance
{
    public sealed class ScenarioResult
    {
        public string Name;
        public bool Passed;
        public readonly List<string> Notes = new List<string>();
        public override string ToString() { return (Passed ? "PASS " : "FAIL ") + Name + " — " + string.Join("; ", Notes); }
    }

    /// <summary>
    /// Adversarial scenarios from the release spec, run against the shipped rules. Each asserts invariants
    /// (termination, proc caps, chain depth, leech budget, projectile budget, determinism, economy sanity) and records
    /// measured numbers so regressions are visible. Used by `forgebound scenarios` and AdversarialScenarioTests.
    /// </summary>
    public static class Scenarios
    {
        private static WeaponInstance W(int uid, string frame, string core, int tier, string[] mods, string[] runes = null, string legendary = null)
        {
            var w = new WeaponInstance { Uid = uid, FrameId = frame, CoreId = core, Tier = tier, LegendaryId = legendary };
            w.Modifiers.AddRange(mods);
            if (runes != null) w.Runes.AddRange(runes);
            return w;
        }

        private static BattleSetup Setup(ContentDatabase db, List<WeaponInstance> weapons, List<KeyValuePair<string, Cell>> pieces, string encounter, string fm, bool dummy, int seconds, ulong seed, List<string> blessings = null)
        {
            var board = new BoardState { Width = BoardState.MaxWidth, Height = BoardState.MaxHeight };
            var shapes = new ContentShapeResolver(db, uid => { foreach (var w in weapons) if (w.Uid == uid) return w; return null; });
            var ed = new BoardEditor(board, shapes);
            int y = 0;
            foreach (var w in weapons) { ed.Place(new PlacedPiece { Kind = PieceKind.Weapon, WeaponUid = w.Uid }, new Cell(0, y), 0); y += 2; }
            if (pieces != null)
                foreach (var p in pieces)
                {
                    var def = db.Piece(p.Key);
                    if (def != null) ed.Place(new PlacedPiece { Kind = def.Kind, PieceId = p.Key }, p.Value, 0);
                }
            var stats = BattleBuilder.HeroStats(db, fm, blessings);
            var analysis = BoardAnalyzer.Analyze(board, shapes, stats.SumUntagged(StatType.RelayCapacity));
            var enc = db.Encounter(encounter);
            var s = new BattleSetup { Db = db, Seed = seed, Encounter = enc, DummyMode = dummy, MaxTicks = seconds * 30, Chapter = enc != null ? Math.Min(3, enc.MinChapter) : 1, RecordEvents = true };
            int hp = db.Forgemaster(fm).MaxHp + stats.SumUntagged(StatType.MaxHp);
            BattleBuilder.FillHero(db, s.Hero, fm, blessings, hp, hp, weapons, board, analysis, shapes, stats);
            return s;
        }

        private static string Hash(List<CombatEvent> events)
        {
            ulong h = 1469598103934665603UL;
            foreach (var e in events)
            {
                h ^= (ulong)(e.Tick * 31 + (int)e.Type * 7 + e.A * 13 + e.B * 17 + e.Value);
                h *= 1099511628211UL;
            }
            return h.ToString("x16");
        }

        /// <summary>Run a battle twice and check invariants that must hold for every build.</summary>
        private static ScenarioResult BattleScenario(ContentDatabase db, string name, Func<BattleSetup> make)
        {
            var r = new ScenarioResult { Name = name, Passed = true };
            try
            {
                var s1 = make();
                var b1 = new Battle(s1);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!b1.Finished) b1.Step();
                double ms = sw.Elapsed.TotalMilliseconds;
                var b2 = new Battle(make());
                while (!b2.Finished) b2.Step();
                var rep = b1.Result.Report;
                Check(r, b1.Finished && b1.Result.Ticks <= s1.MaxTicks, "terminates within MaxTicks");
                Check(r, rep.MaxProcsInOneTick <= s1.MaxProcsPerTick, "procs/tick " + rep.MaxProcsInOneTick + " <= cap " + s1.MaxProcsPerTick);
                Check(r, rep.MaxChainDepthReached <= s1.MaxProcDepth, "chain depth " + rep.MaxChainDepthReached + " <= " + s1.MaxProcDepth);
                Check(r, rep.PeakProjectiles <= s1.MaxProjectiles, "peak projectiles " + rep.PeakProjectiles + " <= " + s1.MaxProjectiles);
                Check(r, rep.PeakZones <= s1.MaxZones, "peak zones " + rep.PeakZones + " <= " + s1.MaxZones);
                long leechCap = (long)s1.Hero.MaxHp * db.Tuning.LeechCapPerSecondPermille / 1000 * (b1.Result.Ticks / 30 + 1);
                Check(r, rep.LeechHealing <= leechCap, "leech " + rep.LeechHealing + " <= budget " + leechCap);
                Check(r, Hash(b1.Events) == Hash(b2.Events), "deterministic event stream");
                r.Notes.Add("victory=" + b1.Result.Victory + " t=" + (b1.Result.Ticks / 30.0).ToString("0.0") + "s dmg=" + rep.TotalDamage + " procsSuppressed=" + (rep.ProcsSuppressedDepth + rep.ProcsSuppressedTickCap + rep.ProcsSuppressedBudget) + " events=" + b1.Events.Count + " simMs=" + ms.ToString("0.0"));
                return r;
            }
            catch (Exception e)
            {
                r.Passed = false;
                r.Notes.Add("exception: " + e.GetType().Name + " " + e.Message);
                return r;
            }
        }

        private static void Check(ScenarioResult r, bool ok, string what)
        {
            if (!ok) { r.Passed = false; r.Notes.Add("VIOLATED " + what); }
        }

        public static List<ScenarioResult> RunAll(ContentDatabase db)
        {
            var list = new List<ScenarioResult>();
            list.Add(BattleScenario(db, "dagger_echo_repeat_leech", () =>
                Setup(db, new List<WeaponInstance> { W(1, "dagger", "lightning", 3, new[] { "repeat", "siphon" }, new[] { "echo_glyph", "crit_spark", "soul_siphon" }), W(2, "dagger", "steel", 3, new[] { "twin_strike", "siphon" }, new[] { "bleeding_edge" }) },
                    new List<KeyValuePair<string, Cell>> { new KeyValuePair<string, Cell>("echo_relay", new Cell(2, 0)), new KeyValuePair<string, Cell>("blood_basin", new Cell(2, 1)) }, "frost_shard_storm", "duelist", false, 120, 11)));
            list.Add(BattleScenario(db, "split_pierce_chain", () =>
                Setup(db, new List<WeaponInstance> { W(1, "bow", "lightning", 3, new[] { "split", "pierce", "forked" }, new[] { "static_sigil", "conductor" }) },
                    new List<KeyValuePair<string, Cell>> { new KeyValuePair<string, Cell>("tesla_coil", new Cell(1, 0)) }, "storm_hound_pack", "runecrafter", false, 120, 12)));
            list.Add(BattleScenario(db, "three_weapons_dense_relays", () =>
            {
                var ws = new List<WeaponInstance> { W(1, "sword", "fire", 3, new[] { "repeat", "explosion" }, new[] { "corpse_blast" }), W(2, "staff", "frost", 3, new[] { "lingering", "explosion" }, new[] { "deep_freeze", "kindling" }), W(3, "tome", "venom", 3, new[] { "spectral_bond" }, new[] { "plague_seed", "harvest_rune" }) };
                var pcs = new List<KeyValuePair<string, Cell>>();
                for (int x = 4; x < 7; x++) for (int y = 0; y < 5; y++) pcs.Add(new KeyValuePair<string, Cell>(((x + y) % 3 == 0) ? "junction" : ((x + y) % 3 == 1 ? "echo_relay" : "splitter"), new Cell(x, y)));
                return Setup(db, ws, pcs, "storm_hound_pack", "runecrafter", false, 120, 13);
            }));
            list.Add(BattleScenario(db, "high_shield_leech", () =>
                Setup(db, new List<WeaponInstance> { W(1, "bulwark", "life", 3, new[] { "guarding", "siphon" }, new[] { "ward_glyph", "aegis_rune", "bulwark_oath" }) },
                    new List<KeyValuePair<string, Cell>> { new KeyValuePair<string, Cell>("blood_basin", new Cell(2, 0)) }, "crypt_boss_lich", "warden", false, 200, 14, new List<string> { "vampiric_draught", "guardian_angel", "healers_touch" })));
            list.Add(BattleScenario(db, "all_fire_vs_resistant_boss", () =>
                Setup(db, new List<WeaponInstance> { W(1, "greatsword", "fire", 3, new[] { "explosion", "cauterize" }), W(2, "staff", "fire", 3, new[] { "lingering" }, new[] { "ember_etch", "detonator" }), W(3, "bow", "fire", 3, new[] { "split" }) },
                    null, "ember_boss_titan", "alchemist", false, 240, 15, new List<string> { "flame_heart", "pyromancy" })));
            list.Add(BattleScenario(db, "single_massive_frame", () =>
                Setup(db, new List<WeaponInstance> { W(1, "greatsword", "earth", 3, new[] { "charged", "sundering" }, new[] { "rending_glyph", "giant_slayer" }) },
                    new List<KeyValuePair<string, Cell>> { new KeyValuePair<string, Cell>("heavy_anvil", new Cell(3, 0)), new KeyValuePair<string, Cell>("anvil", new Cell(3, 1)) }, "storm_boss_colossus", "blacksmith", false, 240, 16)));
            list.Add(BattleScenario(db, "fx_stress_swarm_100_hits", () =>
            {
                // 8 swarm dummies + three multi-hit/AoE weapons ⇒ hundreds of hit events per second for the FX budget.
                var ws = new List<WeaponInstance> { W(1, "bow", "lightning", 3, new[] { "split", "ricochet", "barrage" }, new[] { "volley_glyph" }), W(2, "staff", "fire", 3, new[] { "explosion", "lingering" }, new[] { "pulse_clock" }), W(3, "dagger", "venom", 3, new[] { "twin_strike", "repeat" }, new[] { "echo_glyph", "plague_seed" }) };
                return Setup(db, ws, null, "training_swarm", "artificer", true, 60, 17);
            }));
            list.Add(BattleScenario(db, "legendary_signatures_all", () =>
            {
                var ws = new List<WeaponInstance>();
                int uid = 1;
                foreach (var l in new[] { "infernal_sovereign", "thousandfold_edge", "voidlance" })
                {
                    var def = db.Legendary(l);
                    ws.Add(W(uid++, def.Frame, def.Core, 3, def.RequiresModifiers.ToArray(), null, l));
                }
                return Setup(db, ws, null, "storm_boss_tyrant", "blacksmith", false, 240, 18);
            }));
            foreach (var l in db.Legendaries)
            {
                var def = l;
                list.Add(BattleScenario(db, "legendary_" + l.Id, () => Setup(db, new List<WeaponInstance> { W(1, def.Frame, def.Core, Math.Max(2, def.MinTier), def.RequiresModifiers.ToArray(), def.RequiresAnyRunes.Count > 0 ? new[] { def.RequiresAnyRunes[0] } : null, def.Id) }, null, "frost_shard_storm", "blacksmith", false, 90, 19)));
            }

            list.Add(EconomyLowRollShop(db));
            list.Add(NoEarlyCore(db));
            list.Add(SpendAllGold(db));
            list.Add(CorruptData(db));
            return list;
        }

        private static ScenarioResult EconomyLowRollShop(ContentDatabase db)
        {
            var r = new ScenarioResult { Name = "low_roll_shop_pity", Passed = true };
            int pityFrames = 0, total = 0;
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var c = RunController.StartNew(db, new StartOptions { ForgemasterId = "blacksmith", Seed = seed });
                var offers = ShopGenerator.Generate(db, c.Run, c.Rng("shop"), 3, 0, null);   // smallest shop (curse "scarce_wares")
                total++;
                bool core = false, frame = false;
                foreach (var o in offers) { if (o.Category == ItemCategory.Core) core = true; if (o.Category == ItemCategory.Frame) frame = true; }
                if (core && frame) pityFrames++;
            }
            Check(r, pityFrames == total, "3-offer shop still guarantees a core + frame for a coreless single-weapon build (" + pityFrames + "/" + total + ")");
            return r;
        }

        private static ScenarioResult NoEarlyCore(ContentDatabase db)
        {
            var r = new ScenarioResult { Name = "no_early_core_ch1_viable", Passed = true };
            int wins = 0, n = 0;
            for (ulong seed = 1; seed <= 30; seed++)
            {
                // Coreless tier-1 hammer vs every chapter-1 normal battle: early game must stay winnable without a core.
                foreach (var enc in db.Encounters)
                {
                    if (enc.Kind != NodeType.Battle || enc.MinChapter != 1) continue;
                    var b = new Battle(Setup(db, new List<WeaponInstance> { W(1, "hammer", null, 1, new string[0]) }, null, enc.Id, "blacksmith", false, 240, seed));
                    while (!b.Finished) b.Step();
                    n++;
                    if (b.Result.Victory) wins++;
                }
                if (seed >= 3) break;
            }
            r.Notes.Add("coreless hammer wins " + wins + "/" + n + " chapter-1 battles");
            Check(r, wins * 10 >= n * 7, "at least 70% of chapter-1 battles winnable with the coreless starter");
            return r;
        }

        private static ScenarioResult SpendAllGold(ContentDatabase db)
        {
            var r = new ScenarioResult { Name = "spend_all_gold", Passed = true };
            var c = RunController.StartNew(db, new StartOptions { ForgemasterId = "artificer", Seed = 99 });
            c.Run.Gold = 10000;
            c.Run.Phase = RunPhase.InShop;
            c.Run.ShopOffers.AddRange(ShopGenerator.Generate(db, c.Run, c.Rng("shop"), 6, 0, null));
            int rerolls = 0;
            while (c.Run.Gold > 0 && rerolls < 500)
            {
                bool bought = false;
                for (int i = 0; i < c.Run.ShopOffers.Count; i++) if (c.Buy(i).Ok) bought = true;
                foreach (var it in new List<InventoryItem>(c.Run.Inventory)) c.Sell(it.Uid);
                if (!c.Reroll().Ok) break;
                rerolls++;
                if (!bought && c.Run.Gold < 5) break;
            }
            Check(r, c.Run.Gold >= 0, "gold never negative");
            Check(r, c.Run.Inventory.Count <= RunState.InventoryCap, "inventory cap respected");
            Check(r, EconomyRules.RerollCost(rerolls, 0) <= EconomyRules.RerollCap, "reroll cost capped");
            r.Notes.Add("rerolls=" + rerolls + " gold=" + c.Run.Gold);
            return r;
        }

        private static ScenarioResult CorruptData(ContentDatabase db)
        {
            var r = new ScenarioResult { Name = "negative_corrupt_data", Passed = true };
            try
            {
                var bad = JsonParser.Parse("{\"board\":{\"w\":-3,\"h\":999,\"pieces\":[{\"kind\":\"Weapon\",\"weapon\":424242,\"x\":-9,\"y\":99,\"rot\":17}]},\"weapons\":[{\"uid\":424242,\"frame\":\"nope\",\"tier\":-5,\"mods\":[\"split\",\"split\",\"split\"]}],\"hp\":-50,\"gold\":-100}");
                var state = RunState.FromJson(bad);
                var c = new RunController(db, state);
                var a = c.AnalyzeBoard();
                Check(r, !a.IsValid || a.Issues.Count > 0, "corrupt board reported as invalid");
                state.Phase = RunPhase.PreBattle;
                Check(r, !c.CanStartBattle().Ok, "battle refused for corrupt loadout");
                var files = new Dictionary<string, string>();
                foreach (var n in ContentDatabase.FileNames) files[n] = "{\"version\":1,\"items\":[{\"id\":\"x\",\"hp\":-1,\"steps\":[],\"effects\":[]}]}";
                var broken = ContentDatabase.Load(new DictionaryContentSource(files));
                Check(r, ContentValidator.Validate(broken).ErrorCount > 0, "validator flags negative/corrupt content");
            }
            catch (Exception e)
            {
                r.Passed = false;
                r.Notes.Add("exception: " + e.Message);
            }
            return r;
        }
    }
}
