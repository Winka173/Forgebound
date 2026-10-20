using System;
using System.Collections.Generic;
using Forgebound.Core.Combat;
using Forgebound.Core.Content;
using Forgebound.Core.ForgeBoard;
using Forgebound.Core.Run;
using Forgebound.Core.Weapons;

namespace Forgebound.Core.Balance
{
    public sealed class BattleRecord
    {
        public string Forgemaster, Encounter, Kind, Biome;
        public int Chapter;
        public bool Won;
        public int Ticks, HpBefore, HpAfter, MaxHp;
        public long TotalDamage, Healing, Shield, DamageTaken;
        public readonly List<string> WeaponFrames = new List<string>();
        public readonly List<string> WeaponCores = new List<string>();
        public readonly List<long> WeaponDamage = new List<long>();
        public readonly List<int> WeaponCells = new List<int>();
        public readonly Dictionary<string, int> RuneTriggers = new Dictionary<string, int>();
        public readonly List<string> RunesEquipped = new List<string>();
        public int ActiveLinks, LinkFires, MaxProcsTick, MaxDepth, Suppressed, PeakProjectiles;
        public string KilledBy;
        public double SimMs;
    }

    public sealed class RunRecord
    {
        public string Forgemaster, Policy, Outcome, DeathEncounter, DeathReason;
        public int Difficulty;
        public ulong Seed;
        public int ChapterReached, StepReached, Battles, Legendaries, Rerolls, ShopOffersSeen, ShopOffersDead, ItemsBought;
        public int GoldSpent, GoldFinal, EssenceFinal, FirstLegendaryStep = -1;
        public readonly List<string> FinalWeapons = new List<string>();
        public readonly List<string> LegendaryIds = new List<string>();
        public readonly List<string> Blessings = new List<string>();
        public string PrimaryFrame, PrimaryCore;
        public double Ms;
    }

    /// <summary>
    /// Automated balance simulator: plays many full Adventure runs with deterministic bots through the shipped
    /// RunController + Battle code (no spreadsheet model). Reports are trends with confidence intervals, not truth.
    /// </summary>
    public sealed class BalanceSimulator
    {
        private readonly ContentDatabase _db;
        public readonly List<RunRecord> Runs = new List<RunRecord>();
        public readonly List<BattleRecord> Battles = new List<BattleRecord>();

        public BalanceSimulator(ContentDatabase db) { _db = db; }

        public RunRecord PlayOne(string forgemaster, BotPolicy policy, int difficulty, ulong seed)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var c = RunController.StartNew(_db, new StartOptions { ForgemasterId = forgemaster, Seed = seed, Difficulty = difficulty });
            var bot = new BotAgent(c, policy, seed);
            var rec = new RunRecord { Forgemaster = forgemaster, Policy = policy.ToString(), Difficulty = difficulty, Seed = seed };
            int battlesSeen = 0;
            int legendariesSeen = 0;
            // Observe battles by polling history growth after each bot step (bot drives the controller).
            int guard = 0;
            var last = new System.Diagnostics.Stopwatch();
            while (c.Run.Phase != RunPhase.Finished && guard++ < 5000)
            {
                last.Restart();
                var hpBefore = c.Run.Hp;
                var weaponsBefore = BattleBuilder.ActiveWeapons(c.Run);
                var boardBefore = c.Run.Board.Clone();
                bot.Step();
                if (c.Run.BattlesFought > battlesSeen && c.LastBattle != null)
                {
                    battlesSeen = c.Run.BattlesFought;
                    Battles.Add(MakeBattleRecord(c, forgemaster, c.LastBattle, hpBefore, weaponsBefore, boardBefore, last.Elapsed.TotalMilliseconds));
                }
                if (c.Run.LegendariesForged > legendariesSeen)
                {
                    legendariesSeen = c.Run.LegendariesForged;
                    if (rec.FirstLegendaryStep < 0) rec.FirstLegendaryStep = c.Run.Step;
                }
            }
            if (c.Run.Phase != RunPhase.Finished) c.Abandon();
            var r = c.Run;
            rec.Outcome = r.Outcome.ToString();
            rec.ChapterReached = r.Chapter + 1;
            rec.StepReached = r.Step;
            rec.Battles = r.BattlesFought;
            rec.Legendaries = r.LegendariesForged;
            rec.Rerolls = bot.Rerolls;
            rec.ShopOffersSeen = bot.ShopOffersSeen;
            rec.ShopOffersDead = bot.ShopOffersDead;
            rec.ItemsBought = bot.ItemsBought;
            rec.GoldSpent = bot.GoldSpent;
            rec.GoldFinal = r.Gold;
            rec.EssenceFinal = r.Essence;
            rec.DeathReason = r.DeathReason;
            if (r.Outcome == RunOutcome.Defeat && r.History.Count > 0) rec.DeathEncounter = r.History[r.History.Count - 1].EncounterId;
            foreach (var w in r.Weapons)
            {
                rec.FinalWeapons.Add(w.Signature());
                if (w.LegendaryId != null) rec.LegendaryIds.Add(w.LegendaryId);
            }
            var primary = r.FindWeapon(r.PrimaryWeaponUid);
            rec.PrimaryFrame = primary != null ? primary.FrameId : "-";
            rec.PrimaryCore = primary != null ? (primary.CoreId ?? "-") : "-";
            rec.Blessings.AddRange(r.Blessings);
            rec.Ms = sw.Elapsed.TotalMilliseconds;
            Runs.Add(rec);
            return rec;
        }

        private BattleRecord MakeBattleRecord(RunController c, string fm, BattleResult res, int hpBefore, List<WeaponInstance> weapons, BoardState board, double ms)
        {
            var node = c.Run.History[c.Run.History.Count - 1];
            var enc = _db.Encounter(node.EncounterId);
            var br = new BattleRecord
            {
                Forgemaster = fm,
                Encounter = node.EncounterId,
                Kind = node.Type.ToString(),
                Biome = enc != null ? enc.Biome : null,
                Chapter = c.Run.FindNode(node.NodeId) != null ? c.Run.FindNode(node.NodeId).Chapter + 1 : 0,
                Won = res.Victory,
                Ticks = res.Ticks,
                HpBefore = hpBefore,
                HpAfter = res.HeroHp,
                MaxHp = c.MaxHp(),
                TotalDamage = res.Report.TotalDamage,
                Healing = res.Report.Healing,
                Shield = res.Report.ShieldGained,
                DamageTaken = res.Report.DamageTaken,
                MaxProcsTick = res.Report.MaxProcsInOneTick,
                MaxDepth = res.Report.MaxChainDepthReached,
                Suppressed = res.Report.ProcsSuppressedDepth + res.Report.ProcsSuppressedTickCap + res.Report.ProcsSuppressedBudget,
                PeakProjectiles = res.Report.PeakProjectiles,
                KilledBy = res.Report.KilledBy,
                SimMs = ms
            };
            var resolver = new ContentShapeResolver(_db, c.Run.FindWeapon);
            for (int i = 0; i < weapons.Count && i < 3; i++)
            {
                var w = weapons[i];
                br.WeaponFrames.Add(w.FrameId);
                br.WeaponCores.Add(w.CoreId ?? "-");
                br.WeaponDamage.Add(res.Report.Weapons[i].Damage);
                var f = _db.Frame(w.FrameId);
                br.WeaponCells.Add(f != null ? f.Footprint.Count : 1);
                br.RunesEquipped.AddRange(w.Runes);
            }
            foreach (var kv in res.Report.RuneTriggers) br.RuneTriggers[kv.Key] = kv.Value;
            foreach (var kv in res.Report.LinkFires) br.LinkFires += kv.Value;
            var a = BoardAnalyzer.Analyze(board, resolver);
            br.ActiveLinks = a.ActiveLinkCount;
            return br;
        }

        // ------------------------------------------------------------------ aggregation

        public static double[] Wilson(int successes, int n)
        {
            if (n == 0) return new[] { 0.0, 0.0, 0.0 };
            double z = 1.96, p = successes / (double)n;
            double denom = 1 + z * z / n;
            double centre = (p + z * z / (2 * n)) / denom;
            double half = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / denom;
            return new[] { p, Math.Max(0, centre - half), Math.Min(1, centre + half) };
        }

        private static JsonValue Rate(int wins, int n)
        {
            var w = Wilson(wins, n);
            return JsonValue.Obj().Set("n", n).Set("wins", wins).Set("rate", JsonValue.Num(Math.Round(w[0], 4))).Set("ci95", JsonValue.Arr().Add(JsonValue.Num(Math.Round(w[1], 4))).Add(JsonValue.Num(Math.Round(w[2], 4))));
        }

        private static double Percentile(List<double> values, double p)
        {
            if (values.Count == 0) return 0;
            var v = new List<double>(values);
            v.Sort();
            int idx = (int)Math.Ceiling(p * v.Count) - 1;
            return v[Math.Max(0, Math.Min(v.Count - 1, idx))];
        }

        private static JsonValue GroupRate(List<RunRecord> runs, Func<RunRecord, IEnumerable<string>> key)
        {
            var n = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var w = new Dictionary<string, int>();
            foreach (var r in runs)
                foreach (var k in key(r))
                {
                    int c; n.TryGetValue(k, out c); n[k] = c + 1;
                    if (r.Outcome == "Victory") { w.TryGetValue(k, out c); w[k] = c + 1; }
                }
            var o = JsonValue.Obj();
            foreach (var kv in n) { int wins; w.TryGetValue(kv.Key, out wins); o.Set(kv.Key, Rate(wins, kv.Value)); }
            return o;
        }

        public JsonValue Summary()
        {
            var o = JsonValue.Obj();
            int wins = 0;
            foreach (var r in Runs) if (r.Outcome == "Victory") wins++;
            o.Set("runs", Runs.Count).Set("battles", Battles.Count).Set("overall", Rate(wins, Runs.Count));
            o.Set("byForgemaster", GroupRate(Runs, r => new[] { r.Forgemaster }));
            o.Set("byDifficulty", GroupRate(Runs, r => new[] { "d" + r.Difficulty }));
            o.Set("byPolicy", GroupRate(Runs, r => new[] { r.Policy }));
            o.Set("byPrimaryFrame", GroupRate(Runs, r => new[] { r.PrimaryFrame }));
            o.Set("byPrimaryCore", GroupRate(Runs, r => new[] { r.PrimaryCore }));
            o.Set("byLegendary", GroupRate(Runs, r => r.LegendaryIds.Count > 0 ? r.LegendaryIds : (IEnumerable<string>)new[] { "(none)" }));
            o.Set("byBlessing", GroupRate(Runs, r => r.Blessings));

            // Chapter progression
            var chapters = JsonValue.Obj();
            for (int ch = 1; ch <= 3; ch++)
            {
                int reached = 0, cleared = 0;
                foreach (var r in Runs)
                {
                    if (r.ChapterReached >= ch) reached++;
                    if (r.ChapterReached > ch || r.Outcome == "Victory") cleared++;
                }
                chapters.Set("chapter" + ch, JsonValue.Obj().Set("reached", reached).Set("cleared", cleared).Set("clearRate", JsonValue.Num(reached > 0 ? Math.Round(cleared / (double)reached, 4) : 0)));
            }
            o.Set("chapters", chapters);

            // Encounter difficulty (loss rate per encounter) + death attribution
            var encN = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var encLoss = new Dictionary<string, int>();
            var encTicks = new Dictionary<string, List<double>>();
            var encHpLoss = new Dictionary<string, List<double>>();
            foreach (var b in Battles)
            {
                int c; encN.TryGetValue(b.Encounter, out c); encN[b.Encounter] = c + 1;
                if (!b.Won) { encLoss.TryGetValue(b.Encounter, out c); encLoss[b.Encounter] = c + 1; }
                List<double> l;
                if (!encTicks.TryGetValue(b.Encounter, out l)) { l = new List<double>(); encTicks[b.Encounter] = l; }
                l.Add(b.Ticks / 30.0);
                if (!encHpLoss.TryGetValue(b.Encounter, out l)) { l = new List<double>(); encHpLoss[b.Encounter] = l; }
                l.Add(b.MaxHp > 0 ? (b.HpBefore - b.HpAfter) * 100.0 / b.MaxHp : 0);
            }
            var enc = JsonValue.Obj();
            foreach (var kv in encN)
            {
                int loss; encLoss.TryGetValue(kv.Key, out loss);
                enc.Set(kv.Key, JsonValue.Obj().Set("n", kv.Value).Set("lossRate", JsonValue.Num(Math.Round(loss / (double)kv.Value, 4)))
                    .Set("medianSeconds", JsonValue.Num(Math.Round(Percentile(encTicks[kv.Key], 0.5), 1))).Set("p90Seconds", JsonValue.Num(Math.Round(Percentile(encTicks[kv.Key], 0.9), 1)))
                    .Set("medianHpLossPct", JsonValue.Num(Math.Round(Percentile(encHpLoss[kv.Key], 0.5), 1))));
            }
            o.Set("encounters", enc);

            // Weapon DPS by frame / core and damage per occupied cell
            var frameDps = new Dictionary<string, List<double>>();
            var coreDps = new Dictionary<string, List<double>>();
            var cellEff = new Dictionary<string, List<double>>();
            foreach (var b in Battles)
            {
                double secs = Math.Max(1, b.Ticks / 30.0);
                for (int i = 0; i < b.WeaponFrames.Count; i++)
                {
                    double dps = b.WeaponDamage[i] / secs;
                    Add(frameDps, b.WeaponFrames[i], dps);
                    Add(coreDps, b.WeaponCores[i], dps);
                    Add(cellEff, b.WeaponFrames[i], dps / Math.Max(1, b.WeaponCells[i]));
                }
            }
            o.Set("dpsByFrame", Medians(frameDps));
            o.Set("dpsByCore", Medians(coreDps));
            o.Set("dpsPerCellByFrame", Medians(cellEff));

            // Rune activation
            var runeEquipped = new Dictionary<string, int>();
            var runeTriggers = new Dictionary<string, int>();
            foreach (var b in Battles)
            {
                foreach (var rid in b.RunesEquipped) { int c; runeEquipped.TryGetValue(rid, out c); runeEquipped[rid] = c + 1; }
                foreach (var kv in b.RuneTriggers) { int c; runeTriggers.TryGetValue(kv.Key, out c); runeTriggers[kv.Key] = c + kv.Value; }
            }
            var runes = JsonValue.Obj();
            var never = JsonValue.Arr();
            var rkeys = new List<string>(runeEquipped.Keys);
            rkeys.Sort(StringComparer.Ordinal);
            foreach (var k in rkeys)
            {
                int t; runeTriggers.TryGetValue(k, out t);
                runes.Set(k, JsonValue.Obj().Set("battlesEquipped", runeEquipped[k]).Set("triggers", t).Set("perBattle", JsonValue.Num(Math.Round(t / (double)runeEquipped[k], 2))));
                if (t == 0 && runeEquipped[k] >= 3) never.Add(JsonValue.Str(k));
            }
            o.Set("runes", runes).Set("runesNeverTriggered", never);
            var unpicked = JsonValue.Arr();
            foreach (var r in _db.Runes) if (!runeEquipped.ContainsKey(r.Id)) unpicked.Add(JsonValue.Str(r.Id));
            o.Set("runesNeverEquipped", unpicked);

            // Procs / performance
            var procs = new List<double>();
            var depth = new List<double>();
            var simMs = new List<double>();
            var proj = new List<double>();
            long suppressed = 0;
            foreach (var b in Battles) { procs.Add(b.MaxProcsTick); depth.Add(b.MaxDepth); simMs.Add(b.SimMs); proj.Add(b.PeakProjectiles); suppressed += b.Suppressed; }
            o.Set("procs", JsonValue.Obj().Set("maxPerTickMedian", JsonValue.Num(Percentile(procs, 0.5))).Set("maxPerTickP90", JsonValue.Num(Percentile(procs, 0.9))).Set("maxPerTickMax", JsonValue.Num(Percentile(procs, 1.0)))
                .Set("maxChainDepth", JsonValue.Num(Percentile(depth, 1.0))).Set("suppressedTotal", suppressed));
            o.Set("performance", JsonValue.Obj().Set("battleSimMsMedian", JsonValue.Num(Math.Round(Percentile(simMs, 0.5), 2))).Set("battleSimMsP90", JsonValue.Num(Math.Round(Percentile(simMs, 0.9), 2)))
                .Set("peakProjectilesP90", JsonValue.Num(Percentile(proj, 0.9))).Set("peakProjectilesMax", JsonValue.Num(Percentile(proj, 1.0)))
                .Set("note", "Headless desktop CPU timing of the simulation only; not a device frame-rate measurement."));

            // Economy
            int seen = 0, dead = 0, rerolls = 0, spent = 0, final = 0, bought = 0;
            var firstLeg = new List<double>();
            foreach (var r in Runs) { seen += r.ShopOffersSeen; dead += r.ShopOffersDead; rerolls += r.Rerolls; spent += r.GoldSpent; final += r.GoldFinal; bought += r.ItemsBought; if (r.FirstLegendaryStep >= 0) firstLeg.Add(r.FirstLegendaryStep); }
            int legendRuns = firstLeg.Count;
            o.Set("economy", JsonValue.Obj().Set("shopDeadOfferRate", JsonValue.Num(seen > 0 ? Math.Round(dead / (double)seen, 4) : 0))
                .Set("avgRerollsPerRun", JsonValue.Num(Runs.Count > 0 ? Math.Round(rerolls / (double)Runs.Count, 2) : 0))
                .Set("avgGoldSpent", JsonValue.Num(Runs.Count > 0 ? Math.Round(spent / (double)Runs.Count, 1) : 0))
                .Set("avgGoldUnspentAtEnd", JsonValue.Num(Runs.Count > 0 ? Math.Round(final / (double)Runs.Count, 1) : 0))
                .Set("avgItemsBought", JsonValue.Num(Runs.Count > 0 ? Math.Round(bought / (double)Runs.Count, 2) : 0))
                .Set("runsWithLegendary", legendRuns).Set("medianFirstLegendaryStep", JsonValue.Num(Percentile(firstLeg, 0.5))));

            // Sustain share
            long heal = 0, shield = 0, taken = 0;
            foreach (var b in Battles) { heal += b.Healing; shield += b.Shield; taken += b.DamageTaken; }
            o.Set("sustain", JsonValue.Obj().Set("healingPctOfDamageTaken", JsonValue.Num(taken > 0 ? Math.Round(heal * 100.0 / taken, 1) : 0)).Set("shieldPctOfDamageTaken", JsonValue.Num(taken > 0 ? Math.Round(shield * 100.0 / taken, 1) : 0)));

            // Build diversity (primary frame+core) — Simpson's diversity index and dominance flag.
            var combos = new Dictionary<string, int>();
            foreach (var r in Runs) { string k = r.PrimaryFrame + "+" + r.PrimaryCore; int c; combos.TryGetValue(k, out c); combos[k] = c + 1; }
            double simpson = 0;
            string top = null;
            int topN = 0;
            foreach (var kv in combos)
            {
                double p = kv.Value / (double)Math.Max(1, Runs.Count);
                simpson += p * p;
                if (kv.Value > topN || (kv.Value == topN && string.CompareOrdinal(kv.Key, top) < 0)) { topN = kv.Value; top = kv.Key; }
            }
            o.Set("diversity", JsonValue.Obj().Set("uniquePrimaryBuilds", combos.Count).Set("simpsonIndex", JsonValue.Num(Math.Round(1 - simpson, 4)))
                .Set("mostCommon", JsonValue.Str(top)).Set("mostCommonShare", JsonValue.Num(Runs.Count > 0 ? Math.Round(topN / (double)Runs.Count, 4) : 0)));

            // Flags: outliers that need designer attention.
            var flags = JsonValue.Arr();
            double overall = Runs.Count > 0 ? wins / (double)Runs.Count : 0;
            FlagGroups(flags, o["byForgemaster"], overall, "forgemaster");
            FlagGroups(flags, o["byPrimaryFrame"], overall, "frame");
            FlagGroups(flags, o["byPrimaryCore"], overall, "core");
            foreach (var kv in o["encounters"].Fields)
                if (kv.Value.Int("n") >= 10 && kv.Value.Dbl("lossRate") > 0.35) flags.Add(JsonValue.Str("difficulty_spike: encounter " + kv.Key + " loss rate " + kv.Value.Dbl("lossRate")));
            if (o["economy"].Dbl("shopDeadOfferRate") > 0.3) flags.Add(JsonValue.Str("economy: dead shop offer rate above 30%"));
            if (o["diversity"].Dbl("mostCommonShare") > 0.25 && Runs.Count >= 40) flags.Add(JsonValue.Str("diversity: one primary build exceeds 25% of runs (" + top + ")"));
            if (Percentile(procs, 1.0) >= 48) flags.Add(JsonValue.Str("procs: per-tick proc cap reached in at least one battle"));
            o.Set("flags", flags);
            return o;
        }

        private static void FlagGroups(JsonValue flags, JsonValue groups, double overall, string label)
        {
            foreach (var kv in groups.Fields)
            {
                int n = kv.Value.Int("n");
                if (n < 12) continue;
                var ci = kv.Value["ci95"];
                if (ci[0].AsDouble() > overall + 0.05) flags.Add(JsonValue.Str("overperformer: " + label + " " + kv.Key + " win " + kv.Value.Dbl("rate") + " (CI low " + ci[0].AsDouble() + ") vs overall " + Math.Round(overall, 3)));
                if (ci[1].AsDouble() < overall - 0.05) flags.Add(JsonValue.Str("underperformer: " + label + " " + kv.Key + " win " + kv.Value.Dbl("rate") + " (CI high " + ci[1].AsDouble() + ") vs overall " + Math.Round(overall, 3)));
            }
        }

        private static void Add(Dictionary<string, List<double>> d, string k, double v)
        {
            List<double> l;
            if (!d.TryGetValue(k, out l)) { l = new List<double>(); d[k] = l; }
            l.Add(v);
        }

        private static JsonValue Medians(Dictionary<string, List<double>> d)
        {
            var o = JsonValue.Obj();
            var keys = new List<string>(d.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
                o.Set(k, JsonValue.Obj().Set("n", d[k].Count).Set("median", JsonValue.Num(Math.Round(Percentile(d[k], 0.5), 2))).Set("p90", JsonValue.Num(Math.Round(Percentile(d[k], 0.9), 2))));
            return o;
        }
    }
}
