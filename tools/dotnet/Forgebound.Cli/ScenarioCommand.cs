using System;
using System.IO;
using System.Text;
using Forgebound.Core.Balance;

namespace Forgebound.Cli
{
    public static class ScenarioCommand
    {
        public static int Run(string[] args)
        {
            var db = RepoPaths.LoadDb();
            var results = Scenarios.RunAll(db);
            var sb = new StringBuilder("# Adversarial scenarios\n\n");
            int fails = 0;
            foreach (var r in results) { Console.WriteLine(r); sb.AppendLine("- " + r); if (!r.Passed) fails++; }
            sb.AppendLine("\n" + (results.Count - fails) + "/" + results.Count + " passed");
            var dir = Path.Combine(RepoPaths.Root(), "reports", "balance");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scenarios.md"), sb.ToString());
            Console.WriteLine((results.Count - fails) + "/" + results.Count + " passed");
            return fails == 0 ? 0 : 1;
        }
    }
}
