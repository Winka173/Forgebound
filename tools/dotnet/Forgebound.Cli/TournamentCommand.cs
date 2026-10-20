using System;
using System.IO;
using System.Linq;
using System.Text;
using Forgebound.Core.Balance;
using Forgebound.Core.Content;

namespace Forgebound.Cli
{
    public static class TournamentCommand
    {
        public static int Run(string[] args)
        {
            var db = RepoPaths.LoadDb();
            ulong seed = ulong.Parse(Program.Arg(args, "--seed", "7"));
            var t = new BuildTournament(db, seed) { Tier = int.Parse(Program.Arg(args, "--tier", "2")) };
            t.Gauntlet.AddRange(Program.Arg(args, "--gauntlet", "ember_foreman,verdant_boss_rootmother,frost_countess,crypt_boss_lich,storm_paragon").Split(','));
            int builds = int.Parse(Program.Arg(args, "--builds", "400"));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            t.RunAll(builds, seed);
            var res = t.Attribution(int.Parse(Program.Arg(args, "--min-samples", "12")));
            res.Set("ms", (long)sw.ElapsedMilliseconds);
            string outDir = Program.Arg(args, "--out", Path.Combine(RepoPaths.Root(), "reports", "balance"));
            Directory.CreateDirectory(outDir);
            string label = Program.Arg(args, "--label", "tournament");
            File.WriteAllText(Path.Combine(outDir, label + ".json"), JsonWriter.Write(res, true));
            var sb = new StringBuilder();
            sb.AppendLine("# Build tournament (" + builds + " generated builds, tier " + t.Tier + ", gauntlet " + string.Join(", ", t.Gauntlet) + ")");
            sb.AppendLine("Overall win fraction: " + res.Dbl("overallWinFraction") + " — " + sw.ElapsedMilliseconds + " ms");
            sb.AppendLine("## Flags (|Δ| > 15pp)");
            foreach (var f in res["flags"].Elements()) sb.AppendLine("- " + f.AsString());
            foreach (var cat in new[] { "frame", "core", "forgemaster", "legendary" })
            {
                sb.AppendLine("## " + cat);
                foreach (var kv in res["components"].Fields.Where(f => f.Key.StartsWith(cat + ":")).OrderByDescending(f => f.Value.Dbl("delta")))
                    sb.AppendLine("- " + kv.Key + ": win " + kv.Value.Dbl("winFraction") + " (Δ " + kv.Value.Dbl("delta") + ", n " + kv.Value.Int("n") + ")");
            }
            sb.AppendLine("## Top builds");
            foreach (var b in res["topBuilds"].Elements()) sb.AppendLine("- " + b.AsString());
            sb.AppendLine("## Bottom builds");
            foreach (var b in res["bottomBuilds"].Elements()) sb.AppendLine("- " + b.AsString());
            File.WriteAllText(Path.Combine(outDir, label + ".md"), sb.ToString());
            Console.WriteLine(sb.ToString());
            return 0;
        }
    }
}
