using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Forgebound.Core.Balance;
using Forgebound.Core.Content;

namespace Forgebound.Cli
{
    /// <summary>`forgebound sim` — batch Adventure runs with bot policies, write reports/balance/* and diff against baseline.</summary>
    public static class BalanceCommands
    {
        public static int Sim(string[] args)
        {
            var db = RepoPaths.LoadDb();
            int seeds = int.Parse(Program.Arg(args, "--seeds", "10"));
            ulong seedBase = ulong.Parse(Program.Arg(args, "--seed-base", "1000"));
            string fmArg = Program.Arg(args, "--fm", "all");
            string polArg = Program.Arg(args, "--policies", "Balanced,Aggressive,Cautious,Circuit,Status");
            string diffArg = Program.Arg(args, "--difficulties", "1");
            string outDir = Program.Arg(args, "--out", Path.Combine(RepoPaths.Root(), "reports", "balance"));
            string label = Program.Arg(args, "--label", "latest");
            bool writeBaseline = args.Contains("--write-baseline");
            int threads = int.Parse(Program.Arg(args, "--threads", Environment.ProcessorCount.ToString()));

            var fms = fmArg == "all" ? db.Forgemasters.Select(f => f.Id).ToList() : fmArg.Split(',').ToList();
            var policies = polArg.Split(',').Select(p => (BotPolicy)Enum.Parse(typeof(BotPolicy), p)).ToList();
            var diffs = diffArg.Split(',').Select(int.Parse).ToList();
            var jobs = new List<Tuple<string, BotPolicy, int, ulong>>();
            foreach (var fm in fms) foreach (var p in policies) foreach (var d in diffs) for (int s = 0; s < seeds; s++) jobs.Add(Tuple.Create(fm, p, d, seedBase + (ulong)s));

            Console.WriteLine("simulating " + jobs.Count + " runs on " + threads + " threads ...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var partials = new BalanceSimulator[threads];
            for (int i = 0; i < threads; i++) partials[i] = new BalanceSimulator(db);
            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
            {
                for (int j = t; j < jobs.Count; j += threads)
                {
                    var job = jobs[j];
                    partials[t].PlayOne(job.Item1, job.Item2, job.Item3, job.Item4);
                }
            });
            // Merge in deterministic job order.
            var sim = new BalanceSimulator(db);
            var runsByKey = new Dictionary<string, RunRecord>();
            foreach (var p in partials) foreach (var r in p.Runs) runsByKey[r.Forgemaster + "|" + r.Policy + "|" + r.Difficulty + "|" + r.Seed] = r;
            foreach (var job in jobs) sim.Runs.Add(runsByKey[job.Item1 + "|" + job.Item2 + "|" + job.Item3 + "|" + job.Item4]);
            foreach (var p in partials) sim.Battles.AddRange(p.Battles);
            sim.Battles.Sort((a, b) => string.CompareOrdinal(a.Forgemaster + a.Encounter + a.Ticks, b.Forgemaster + b.Encounter + b.Ticks));
            Console.WriteLine("done in " + sw.ElapsedMilliseconds + " ms");

            var summary = sim.Summary();
            summary.Set("label", label);
            summary.Set("config", JsonValue.Obj().Set("seeds", seeds).Set("seedBase", seedBase.ToString()).Set("forgemasters", string.Join(",", fms)).Set("policies", polArg).Set("difficulties", diffArg));
            summary.Set("contentVersion", ContentHash());
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, label + "_summary.json"), JsonWriter.Write(summary, true));
            WriteRunsCsv(sim, Path.Combine(outDir, label + "_runs.csv"));
            WriteBattlesCsv(sim, Path.Combine(outDir, label + "_battles.csv"));

            string baselinePath = Path.Combine(outDir, "baseline_summary.json");
            JsonValue baseline = File.Exists(baselinePath) ? JsonParser.Parse(File.ReadAllText(baselinePath)) : null;
            File.WriteAllText(Path.Combine(outDir, label + "_report.html"), HtmlReport.Build(summary, baseline));
            File.WriteAllText(Path.Combine(outDir, label + "_report.md"), MarkdownReport(summary, baseline));
            if (writeBaseline) File.WriteAllText(baselinePath, JsonWriter.Write(summary, true));

            Console.WriteLine(MarkdownReport(summary, baseline));
            return 0;
        }

        private static string ContentHash()
        {
            var sb = new StringBuilder();
            foreach (var n in ContentDatabase.FileNames)
            {
                var p = Path.Combine(RepoPaths.ContentDir(), n + ".json");
                if (File.Exists(p)) sb.Append(File.ReadAllText(p));
            }
            return Forgebound.Core.Save.SaveSystem.Checksum(sb.ToString());
        }

        private static string F(double d) { return d.ToString("0.###", CultureInfo.InvariantCulture); }

        private static void WriteRunsCsv(BalanceSimulator sim, string path)
        {
            var sb = new StringBuilder("forgemaster,policy,difficulty,seed,outcome,chapter,step,battles,legendaries,rerolls,offers_seen,offers_dead,gold_spent,gold_final,primary_frame,primary_core,death_encounter,blessings,weapons,ms\n");
            foreach (var r in sim.Runs)
                sb.Append(string.Join(",", r.Forgemaster, r.Policy, r.Difficulty, r.Seed, r.Outcome, r.ChapterReached, r.StepReached, r.Battles, r.Legendaries, r.Rerolls,
                    r.ShopOffersSeen, r.ShopOffersDead, r.GoldSpent, r.GoldFinal, r.PrimaryFrame, r.PrimaryCore, r.DeathEncounter ?? "", string.Join(";", r.Blessings),
                    "\"" + string.Join(";", r.FinalWeapons) + "\"", F(r.Ms))).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteBattlesCsv(BalanceSimulator sim, string path)
        {
            var sb = new StringBuilder("forgemaster,encounter,kind,chapter,won,seconds,hp_before,hp_after,max_hp,total_damage,healing,shield,damage_taken,frames,weapon_damage,active_links,link_fires,max_procs_tick,max_depth,suppressed,peak_projectiles,killed_by,sim_ms\n");
            foreach (var b in sim.Battles)
                sb.Append(string.Join(",", b.Forgemaster, b.Encounter, b.Kind, b.Chapter, b.Won ? 1 : 0, F(b.Ticks / 30.0), b.HpBefore, b.HpAfter, b.MaxHp, b.TotalDamage, b.Healing, b.Shield, b.DamageTaken,
                    string.Join(";", b.WeaponFrames), string.Join(";", b.WeaponDamage), b.ActiveLinks, b.LinkFires, b.MaxProcsTick, b.MaxDepth, b.Suppressed, b.PeakProjectiles, b.KilledBy ?? "", F(b.SimMs))).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }

        public static string MarkdownReport(JsonValue s, JsonValue baseline)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Balance report: " + s.Str("label"));
            sb.AppendLine();
            sb.AppendLine("Runs: " + s.Int("runs") + ", battles: " + s.Int("battles") + ", content hash `" + s.Str("contentVersion") + "`.");
            sb.AppendLine("Bots are deterministic heuristic agents; these are trends, not proof of fun. Human playtests are still required.");
            sb.AppendLine();
            var ov = s["overall"];
            sb.AppendLine("**Overall win rate:** " + Pct(ov.Dbl("rate")) + " (95% CI " + Pct(ov["ci95"][0].AsDouble()) + "–" + Pct(ov["ci95"][1].AsDouble()) + ")" + Delta(baseline, b => b["overall"].Dbl("rate"), ov.Dbl("rate")));
            sb.AppendLine();
            Table(sb, "Win rate by Forgemaster", s["byForgemaster"], baseline != null ? baseline["byForgemaster"] : null);
            Table(sb, "Win rate by policy", s["byPolicy"], baseline != null ? baseline["byPolicy"] : null);
            Table(sb, "Win rate by primary frame", s["byPrimaryFrame"], baseline != null ? baseline["byPrimaryFrame"] : null);
            Table(sb, "Win rate by primary core", s["byPrimaryCore"], baseline != null ? baseline["byPrimaryCore"] : null);
            sb.AppendLine("## Chapter progression");
            foreach (var kv in s["chapters"].Fields) sb.AppendLine("- " + kv.Key + ": reached " + kv.Value.Int("reached") + ", clear rate " + Pct(kv.Value.Dbl("clearRate")));
            sb.AppendLine();
            sb.AppendLine("## Hardest encounters (loss rate, n ≥ 5)");
            var encs = s["encounters"].Fields.Where(f => f.Value.Int("n") >= 5).OrderByDescending(f => f.Value.Dbl("lossRate")).Take(10);
            sb.AppendLine("| encounter | n | loss | median s | p90 s | median HP loss % |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (var kv in encs) sb.AppendLine("| " + kv.Key + " | " + kv.Value.Int("n") + " | " + Pct(kv.Value.Dbl("lossRate")) + " | " + F(kv.Value.Dbl("medianSeconds")) + " | " + F(kv.Value.Dbl("p90Seconds")) + " | " + F(kv.Value.Dbl("medianHpLossPct")) + " |");
            sb.AppendLine();
            sb.AppendLine("## Weapon DPS by frame (median per battle)");
            foreach (var kv in s["dpsByFrame"].Fields) sb.AppendLine("- " + kv.Key + ": " + F(kv.Value.Dbl("median")) + " (p90 " + F(kv.Value.Dbl("p90")) + ", n " + kv.Value.Int("n") + "), per cell " + F(s["dpsPerCellByFrame"][kv.Key].Dbl("median")));
            sb.AppendLine();
            sb.AppendLine("## Economy");
            foreach (var kv in s["economy"].Fields) sb.AppendLine("- " + kv.Key + ": " + kv.Value);
            sb.AppendLine("## Sustain");
            foreach (var kv in s["sustain"].Fields) sb.AppendLine("- " + kv.Key + ": " + kv.Value);
            sb.AppendLine("## Diversity");
            foreach (var kv in s["diversity"].Fields) sb.AppendLine("- " + kv.Key + ": " + kv.Value);
            sb.AppendLine("## Procs & simulation performance");
            foreach (var kv in s["procs"].Fields) sb.AppendLine("- procs." + kv.Key + ": " + kv.Value);
            foreach (var kv in s["performance"].Fields) sb.AppendLine("- performance." + kv.Key + ": " + kv.Value);
            sb.AppendLine("## Runes never triggered while equipped (≥3 battles)");
            sb.AppendLine(string.Join(", ", s["runesNeverTriggered"].Elements().Select(e => e.AsString())));
            sb.AppendLine("## Runes never equipped by bots");
            sb.AppendLine(string.Join(", ", s["runesNeverEquipped"].Elements().Select(e => e.AsString())));
            sb.AppendLine("## Legendary outcomes");
            foreach (var kv in s["byLegendary"].Fields) sb.AppendLine("- " + kv.Key + ": n " + kv.Value.Int("n") + ", win " + Pct(kv.Value.Dbl("rate")));
            sb.AppendLine("## Flags");
            foreach (var f in s["flags"].Elements()) sb.AppendLine("- " + f.AsString());
            return sb.ToString();
        }

        private static string Pct(double d) { return (d * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%"; }

        private static string Delta(JsonValue baseline, Func<JsonValue, double> get, double now)
        {
            if (baseline == null) return "";
            double b = get(baseline);
            double d = (now - b) * 100;
            return " (Δ vs baseline " + (d >= 0 ? "+" : "") + d.ToString("0.0", CultureInfo.InvariantCulture) + " pp)";
        }

        private static void Table(StringBuilder sb, string title, JsonValue groups, JsonValue baseline)
        {
            sb.AppendLine("## " + title);
            sb.AppendLine("| key | n | win | 95% CI | Δ baseline |");
            sb.AppendLine("|---|---:|---:|---|---:|");
            foreach (var kv in groups.Fields)
            {
                string delta = "";
                if (baseline != null && baseline.Has(kv.Key)) delta = ((kv.Value.Dbl("rate") - baseline[kv.Key].Dbl("rate")) * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " pp";
                sb.AppendLine("| " + kv.Key + " | " + kv.Value.Int("n") + " | " + Pct(kv.Value.Dbl("rate")) + " | " + Pct(kv.Value["ci95"][0].AsDouble()) + "–" + Pct(kv.Value["ci95"][1].AsDouble()) + " | " + delta + " |");
            }
            sb.AppendLine();
        }
    }

    /// <summary>Self-contained HTML with inline SVG bar charts (no external libraries).</summary>
    public static class HtmlReport
    {
        public static string Build(JsonValue s, JsonValue baseline)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Forgebound Balance</title><style>")
              .Append("body{font-family:system-ui,sans-serif;margin:16px;background:#14110f;color:#eee}h1,h2{color:#ffb347}.chart{margin:8px 0 24px}svg text{fill:#ddd;font-size:11px}")
              .Append(".flag{color:#ff8a65}table{border-collapse:collapse}td,th{border:1px solid #444;padding:2px 6px}</style></head><body>");
            sb.Append("<h1>Forgebound balance report — ").Append(Esc(s.Str("label"))).Append("</h1>");
            sb.Append("<p>").Append(s.Int("runs")).Append(" runs, ").Append(s.Int("battles")).Append(" battles. Overall win rate ")
              .Append(P(s["overall"].Dbl("rate"))).Append(" (95% CI ").Append(P(s["overall"]["ci95"][0].AsDouble())).Append("–").Append(P(s["overall"]["ci95"][1].AsDouble())).Append("). Bot results are trends; human playtests are still required.</p>");
            Chart(sb, "Win rate by Forgemaster (bars) with 95% CI (lines)", s["byForgemaster"], baseline != null ? baseline["byForgemaster"] : null, true);
            Chart(sb, "Win rate by primary frame", s["byPrimaryFrame"], baseline != null ? baseline["byPrimaryFrame"] : null, true);
            Chart(sb, "Win rate by primary core", s["byPrimaryCore"], baseline != null ? baseline["byPrimaryCore"] : null, true);
            var dps = JsonValue.Obj();
            foreach (var kv in s["dpsByFrame"].Fields) dps.Set(kv.Key, JsonValue.Obj().Set("rate", kv.Value["median"]));
            Chart(sb, "Median DPS per weapon by frame", dps, null, false);
            var loss = JsonValue.Obj();
            foreach (var kv in s["encounters"].Fields) if (kv.Value.Int("n") >= 5) loss.Set(kv.Key, JsonValue.Obj().Set("rate", kv.Value["lossRate"]));
            Chart(sb, "Encounter loss rate (n ≥ 5)", loss, null, true);
            sb.Append("<h2>Flags</h2><ul>");
            foreach (var f in s["flags"].Elements()) sb.Append("<li class='flag'>").Append(Esc(f.AsString())).Append("</li>");
            sb.Append("</ul><h2>Raw summary</h2><pre>").Append(Esc(JsonWriter.Write(s, true))).Append("</pre></body></html>");
            return sb.ToString();
        }

        private static string P(double d) { return (d * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%"; }
        private static string Esc(string s) { return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"); }

        private static void Chart(StringBuilder sb, string title, JsonValue groups, JsonValue baseline, bool isRate)
        {
            var items = groups.Fields;
            if (items.Count == 0) return;
            double max = isRate ? 1.0 : Math.Max(1e-6, items.Max(kv => kv.Value.Dbl("rate")));
            int rowH = 20, labelW = 170, barW = 420;
            int h = items.Count * rowH + 10;
            sb.Append("<h2>").Append(Esc(title)).Append("</h2><div class='chart'><svg width='").Append(labelW + barW + 120).Append("' height='").Append(h).Append("' role='img'>");
            int y = 4;
            foreach (var kv in items)
            {
                double v = kv.Value.Dbl("rate");
                int w = (int)(barW * Math.Min(1, v / max));
                sb.Append("<text x='0' y='").Append(y + 13).Append("'>").Append(Esc(kv.Key)).Append("</text>");
                sb.Append("<rect x='").Append(labelW).Append("' y='").Append(y + 2).Append("' width='").Append(w).Append("' height='14' fill='#e07a2f'/>");
                if (kv.Value.Has("ci95"))
                {
                    int x1 = labelW + (int)(barW * kv.Value["ci95"][0].AsDouble() / max), x2 = labelW + (int)(barW * kv.Value["ci95"][1].AsDouble() / max);
                    sb.Append("<line x1='").Append(x1).Append("' x2='").Append(x2).Append("' y1='").Append(y + 9).Append("' y2='").Append(y + 9).Append("' stroke='#fff' stroke-width='2'/>");
                }
                if (baseline != null && baseline.Has(kv.Key))
                {
                    int bx = labelW + (int)(barW * Math.Min(1, baseline[kv.Key].Dbl("rate") / max));
                    sb.Append("<line x1='").Append(bx).Append("' x2='").Append(bx).Append("' y1='").Append(y).Append("' y2='").Append(y + 18).Append("' stroke='#7fd8ff' stroke-width='2'><title>baseline</title></line>");
                }
                sb.Append("<text x='").Append(labelW + barW + 8).Append("' y='").Append(y + 13).Append("'>").Append(isRate ? P(v) : v.ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(kv.Value.Has("n") ? " (n " + kv.Value.Int("n") + ")" : "").Append("</text>");
                y += rowH;
            }
            sb.Append("</svg></div>");
        }
    }
}
