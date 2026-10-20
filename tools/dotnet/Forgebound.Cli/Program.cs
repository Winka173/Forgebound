using System;
using System.IO;
using Forgebound.Core.Content;

namespace Forgebound.Cli
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0) { Usage(); return 2; }
            try
            {
                switch (args[0])
                {
                    case "validate": return ValidateCmd(args);
                    case "play": return PlayCmd(args);
                    default: Usage(); return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FATAL: " + ex);
                return 1;
            }
        }

        private static void Usage()
        {
            Console.WriteLine("forgebound <command>\n  validate [--out reports/content]   content + proc-graph + localisation validation");
        }

        private static int ValidateCmd(string[] args)
        {
            var db = RepoPaths.LoadDb();
            var report = ContentValidator.Validate(db, RepoPaths.LoadLoc("en"), RepoPaths.LoadLoc("vi"));
            foreach (var i in report.Issues) Console.WriteLine(i);
            Console.WriteLine("content: frames=" + db.Frames.Count + " cores=" + db.Cores.Count + " modifiers=" + db.Modifiers.Count + " runes=" + db.Runes.Count +
                " pieces=" + db.Pieces.Count + " legendaries=" + db.Legendaries.Count + " forgemasters=" + db.Forgemasters.Count + " enemies=" + db.Enemies.Count +
                " encounters=" + db.Encounters.Count + " events=" + db.Events.Count + " blessings=" + db.Blessings.Count + " biomes=" + db.Biomes.Count + " challenges=" + db.Challenges.Count);
            Console.WriteLine("errors=" + report.ErrorCount + " warnings=" + report.WarningCount);
            string outDir = Arg(args, "--out", Path.Combine(RepoPaths.Root(), "reports", "content"));
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "content_validation.json"), JsonWriter.Write(report.ToJson(), true));
            return report.ErrorCount == 0 ? 0 : 1;
        }

        private static int PlayCmd(string[] args)
        {
            var db = RepoPaths.LoadDb();
            string fm = Arg(args, "--fm", "blacksmith");
            ulong seed = ulong.Parse(Arg(args, "--seed", "1"));
            var policy = (Forgebound.Core.Balance.BotPolicy)Enum.Parse(typeof(Forgebound.Core.Balance.BotPolicy), Arg(args, "--policy", "Balanced"));
            var c = Forgebound.Core.Run.RunController.StartNew(db, new Forgebound.Core.Run.StartOptions { ForgemasterId = fm, Seed = seed, Difficulty = int.Parse(Arg(args, "--difficulty", "1")) });
            var bot = new Forgebound.Core.Balance.BotAgent(c, policy, seed) { Verbose = true };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bot.PlayToEnd();
            foreach (var l in bot.Log) Console.WriteLine(l);
            var r = c.Run;
            Console.WriteLine("outcome=" + r.Outcome + " chapter=" + (r.Chapter + 1) + " step=" + r.Step + " hp=" + r.Hp + " gold=" + r.Gold + " essence=" + r.Essence + " battles=" + r.BattlesFought + " legendaries=" + r.LegendariesForged + " death=" + r.DeathReason + " ms=" + sw.ElapsedMilliseconds);
            foreach (var w in r.Weapons) Console.WriteLine("  weapon " + w.Signature() + (w.Uid == r.PrimaryWeaponUid ? " [primary]" : ""));
            Console.WriteLine("  blessings: " + string.Join(",", r.Blessings));
            return 0;
        }

        public static string Arg(string[] args, string name, string def)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return def;
        }
    }
}
