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
    public sealed class GeneratedBuild
    {
        public string Forgemaster;
        public readonly List<WeaponInstance> Weapons = new List<WeaponInstance>();
        public readonly List<string> Pieces = new List<string>();
        public BoardState Board;
        public int PrimaryUid;
        public string Archetype;
        public readonly List<string> Components = new List<string>();   // "frame:x", "core:y", "modifier:z", "rune:r", "piece:p", "legendary:l"
        public int Score;          // gauntlet points
        public int Wins;
        public long TotalTicks;
        public string Describe()
        {
            var parts = new List<string>();
            foreach (var w in Weapons) parts.Add(w.Signature());
            return Forgemaster + " [" + string.Join(" || ", parts) + "] pieces=" + string.Join(",", Pieces);
        }
    }

    /// <summary>
    /// Build generator + tournament: produces valid builds within constraints, runs each against a fixed gauntlet of
    /// encounters with the real simulation, then attributes results to components to find outliers/underperformers.
    /// </summary>
    public sealed class BuildTournament
    {
        private readonly ContentDatabase _db;
        private readonly DetRandom _rng;
        public readonly List<GeneratedBuild> Builds = new List<GeneratedBuild>();
        public readonly List<string> Gauntlet = new List<string>();
        public int Tier = 2;
        public int Chapter = 2;

        public BuildTournament(ContentDatabase db, ulong seed)
        {
            _db = db;
            _rng = new DetRandom(seed);
        }

        private T Pick<T>(List<T> list) { return list[_rng.Next(list.Count)]; }

        public GeneratedBuild Generate(int weaponCount, string archetype)
        {
            var b = new GeneratedBuild { Forgemaster = Pick(_db.Forgemasters).Id, Archetype = archetype };
            var frames = new List<FrameDef>(_db.Frames);
            for (int i = 0; i < weaponCount; i++)
            {
                var f = frames[_rng.Next(frames.Count)];
                var w = new WeaponInstance { Uid = i + 1, FrameId = f.Id, CoreId = Pick(_db.Cores).Id, Tier = Tier };
                // Fill modifier/rune slots with random legal choices.
                var mods = new List<ModifierDef>(_db.Modifiers);
                _rng.Shuffle(mods);
                foreach (var m in mods)
                {
                    if (AssemblyRules.PreviewAddModifier(_db, w, m.Id, null).Count == 0) w.Modifiers.Add(m.Id);
                    if (w.Modifiers.Count >= AssemblyRules.ModifierSlots(_db, w, null)) break;
                }
                var runes = new List<RuneDef>(_db.Runes);
                _rng.Shuffle(runes);
                foreach (var r in runes)
                {
                    if (AssemblyRules.PreviewAddRune(_db, w, r.Id, null).Count == 0) w.Runes.Add(r.Id);
                    if (w.Runes.Count >= AssemblyRules.RuneSlots(_db, w, null)) break;
                }
                // Sometimes evolve if a recipe matches after a forced alignment (exercise legendaries).
                if (archetype == "legendary" && i == 0)
                {
                    var cand = new List<LegendaryDef>();
                    foreach (var l in _db.Legendaries) if (l.Frame == f.Id) cand.Add(l);
                    if (cand.Count > 0)
                    {
                        var l = Pick(cand);
                        w.CoreId = l.Core;
                        w.Modifiers.Clear();
                        w.Modifiers.AddRange(l.RequiresModifiers);
                        w.Runes.Clear();
                        if (l.RequiresAnyRunes.Count > 0) w.Runes.Add(l.RequiresAnyRunes[0]);
                        w.Tier = Math.Max(w.Tier, l.MinTier);
                        w.LegendaryId = l.Id;
                    }
                }
                b.Weapons.Add(w);
            }
            int pieces = _rng.Next(4);
            for (int i = 0; i < pieces; i++) b.Pieces.Add(Pick(_db.Pieces).Id);
            foreach (var w in b.Weapons)
            {
                b.Components.Add("frame:" + w.FrameId);
                if (w.CoreId != null) b.Components.Add("core:" + w.CoreId);
                foreach (var m in w.Modifiers) b.Components.Add("modifier:" + m);
                foreach (var r in w.Runes) b.Components.Add("rune:" + r);
                if (w.LegendaryId != null) b.Components.Add("legendary:" + w.LegendaryId);
            }
            foreach (var p in b.Pieces) b.Components.Add("piece:" + p);
            b.Components.Add("forgemaster:" + b.Forgemaster);
            Arrange(b);
            return b;
        }

        /// <summary>Use the bot's auto-arrange on a scratch run so builds get a sensible board.</summary>
        private void Arrange(GeneratedBuild b)
        {
            var run = new RunState { ForgemasterId = b.Forgemaster, Seed = 1, MaxHpBase = 100, Hp = 100 };
            run.Board = new BoardState { Width = BoardState.MaxWidth, Height = BoardState.MaxHeight };
            foreach (var w in b.Weapons) { run.Weapons.Add(w); run.NextUid = Math.Max(run.NextUid, w.Uid + 1); }
            var c = new RunController(_db, run);
            foreach (var p in b.Pieces) c.AddItem(ItemCategory.Piece, p);
            BotAgent.AutoArrange(c, true);
            b.Board = run.Board;
            b.PrimaryUid = run.PrimaryWeaponUid;
        }

        public BattleResult Fight(GeneratedBuild b, string encounter, ulong seed)
        {
            var shapes = new ContentShapeResolver(_db, uid => { foreach (var w in b.Weapons) if (w.Uid == uid) return w; return null; });
            var stats = BattleBuilder.HeroStats(_db, b.Forgemaster, null);
            var analysis = BoardAnalyzer.Analyze(b.Board, shapes, stats.SumUntagged(StatType.RelayCapacity));
            var active = new List<WeaponInstance>();
            foreach (var w in b.Weapons) if (b.Board.FindWeapon(w.Uid) != null) { if (w.Uid == b.PrimaryUid) active.Insert(0, w); else active.Add(w); }
            var enc = _db.Encounter(encounter);
            var setup = new BattleSetup { Db = _db, Seed = seed, Encounter = enc, Chapter = enc.MinChapter, RecordEvents = false };
            var fm = _db.Forgemaster(b.Forgemaster);
            int maxHp = fm.MaxHp + stats.SumUntagged(StatType.MaxHp);
            BattleBuilder.FillHero(_db, setup.Hero, b.Forgemaster, null, maxHp, maxHp, active, b.Board, analysis, shapes, stats);
            return Battle.RunToEnd(setup);
        }

        public void RunAll(int buildCount, ulong seed)
        {
            string[] archetypes = { "single", "duo", "trio", "legendary" };
            for (int i = 0; i < buildCount; i++)
            {
                var arch = archetypes[i % archetypes.Length];
                int count = arch == "single" ? 1 : (arch == "duo" ? 2 : (arch == "trio" ? 3 : 1 + _rng.Next(3)));
                var b = Generate(count, arch);
                foreach (var enc in Gauntlet)
                {
                    var r = Fight(b, enc, DetRandom.DeriveSeed(seed, enc + i));
                    if (r.Victory) { b.Wins++; b.Score += 3; }
                    b.TotalTicks += r.Ticks;
                }
                Builds.Add(b);
            }
        }

        /// <summary>Marginal win-rate per component: mean win fraction of builds containing it vs overall mean.</summary>
        public JsonValue Attribution(int minSamples)
        {
            var sum = new Dictionary<string, double>();
            var n = new Dictionary<string, int>();
            double overall = 0;
            foreach (var b in Builds)
            {
                double wr = Gauntlet.Count > 0 ? b.Wins / (double)Gauntlet.Count : 0;
                overall += wr;
                var seen = new HashSet<string>();
                foreach (var c in b.Components)
                {
                    if (!seen.Add(c)) continue;
                    double s; sum.TryGetValue(c, out s); sum[c] = s + wr;
                    int k; n.TryGetValue(c, out k); n[c] = k + 1;
                }
            }
            overall /= Math.Max(1, Builds.Count);
            var o = JsonValue.Obj().Set("builds", Builds.Count).Set("gauntlet", string.Join(",", Gauntlet)).Set("overallWinFraction", JsonValue.Num(Math.Round(overall, 4)));
            var comps = new List<string>(n.Keys);
            comps.Sort(StringComparer.Ordinal);
            var arr = JsonValue.Obj();
            var flags = JsonValue.Arr();
            foreach (var c in comps)
            {
                if (n[c] < minSamples) continue;
                double mean = sum[c] / n[c];
                double delta = mean - overall;
                arr.Set(c, JsonValue.Obj().Set("n", n[c]).Set("winFraction", JsonValue.Num(Math.Round(mean, 4))).Set("delta", JsonValue.Num(Math.Round(delta, 4))));
                if (delta > 0.15) flags.Add(JsonValue.Str("outlier_strong " + c + " +" + Math.Round(delta * 100, 1) + "pp (n " + n[c] + ")"));
                if (delta < -0.15) flags.Add(JsonValue.Str("outlier_weak " + c + " " + Math.Round(delta * 100, 1) + "pp (n " + n[c] + ")"));
            }
            o.Set("components", arr).Set("flags", flags);
            var top = new List<GeneratedBuild>(Builds);
            top.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : a.TotalTicks.CompareTo(b.TotalTicks));
            var best = JsonValue.Arr();
            for (int i = 0; i < Math.Min(10, top.Count); i++) best.Add(JsonValue.Str(top[i].Wins + "/" + Gauntlet.Count + " " + top[i].Archetype + " " + top[i].Describe()));
            o.Set("topBuilds", best);
            var worst = JsonValue.Arr();
            for (int i = Math.Max(0, top.Count - 5); i < top.Count; i++) worst.Add(JsonValue.Str(top[i].Wins + "/" + Gauntlet.Count + " " + top[i].Archetype + " " + top[i].Describe()));
            o.Set("bottomBuilds", worst);
            return o;
        }
    }
}
