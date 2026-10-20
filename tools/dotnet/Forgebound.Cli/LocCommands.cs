using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Forgebound.Core.Content;
using Forgebound.Core.Localization;

namespace Forgebound.Cli
{
    public static class LocCommands
    {
        public static Loc Load(string lang)
        {
            var loc = new Loc { Language = lang };
            foreach (var l in Loc.Languages)
            {
                var t = RepoPaths.LoadLoc(l);
                if (t != null) loc.AddTable(l, t);
            }
            return loc;
        }

        /// <summary>Keys referenced from presentation code: L("key") / Loc.Get("key") / Format("key".</summary>
        public static SortedSet<string> CodeKeys()
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            var dir = Path.Combine(RepoPaths.Root(), "Assets", "Forgebound", "Presentation");
            if (!Directory.Exists(dir)) return set;
            var rx = new Regex("(?:\\bL|\\bLF|Get|Format)\\(\\s*\"([a-z0-9_]+\\.[A-Za-z0-9_.]+)\"");
            foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                foreach (Match m in rx.Matches(File.ReadAllText(f))) set.Add(m.Groups[1].Value);
            return set;
        }

        /// <summary>Keys built at runtime from enums/content ("prefix." + value) — enumerated so coverage is provable.</summary>
        public static IEnumerable<string> DynamicKeys(ContentDatabase db)
        {
            foreach (Forgebound.Core.Run.ActionError e in Enum.GetValues(typeof(Forgebound.Core.Run.ActionError))) yield return "error." + e;
            foreach (NodeType t in Enum.GetValues(typeof(NodeType))) { yield return "ui.node." + t; yield return "ui.node." + t + ".desc"; yield return "ui.node." + t + ".reward"; yield return "ui.node.glyph." + t; }
            foreach (var e in db.Encounters) yield return "style." + e.Style;
            foreach (TargetPriority t in Enum.GetValues(typeof(TargetPriority))) yield return "targeting." + t;
            foreach (EnemyRank r in Enum.GetValues(typeof(EnemyRank))) yield return "rank." + r;
            foreach (ItemCategory c in Enum.GetValues(typeof(ItemCategory))) yield return "category." + c;
            foreach (Rarity r in Enum.GetValues(typeof(Rarity))) yield return "rarity." + r;
            foreach (Forgebound.Core.Run.RunPhase p in Enum.GetValues(typeof(Forgebound.Core.Run.RunPhase))) yield return "ui.forge.title." + p;
            foreach (Forgebound.Core.Run.RunOutcome o in Enum.GetValues(typeof(Forgebound.Core.Run.RunOutcome))) yield return "ui.end." + o;
            foreach (var c in new[] { "slot_overflow", "rune_overflow", "requires_tag", "excluded_tag", "conflict", "duplicate", "unknown", "bad_tier" }) yield return "assembly." + c;
            foreach (var f in db.Forgemasters) if (f.Unlock != null) yield return "ui.unlock." + f.Unlock;
            foreach (var e in db.Enemies) foreach (var ph in e.Phases) if (ph.NameKey != null) yield return ph.NameKey;
            foreach (var d in Forgebound.Core.Run.Difficulty.Names) yield return "difficulty." + d;
            foreach (var d in Forgebound.Core.Progression.LoadoutProbe.Dummies) yield return "ui.sandbox.dummy." + d;
            foreach (var c in db.Challenges) if (c.RewardCosmetic != null) yield return "cosmetic." + c.RewardCosmetic;
            foreach (var a in new[] { "first_run", "first_victory", "first_legendary", "hard_victory", "forgemaster_victory", "all_frames", "triple_threat" }) yield return "achievement." + a;
        }

        public static int Check(string[] args)
        {
            var db = RepoPaths.LoadDb();
            int missing = 0;
            foreach (var lang in Loc.Languages)
            {
                var table = RepoPaths.LoadLoc(lang) ?? new Dictionary<string, string>();
                var required = new List<string>(ContentValidator.RequiredLocKeys(db));
                required.AddRange(CodeKeys());
                required.AddRange(DynamicKeys(db));
                var miss = required.Distinct().Where(k => !table.ContainsKey(k) || string.IsNullOrWhiteSpace(table[k])).ToList();
                missing += miss.Count;
                Console.WriteLine(lang + ": " + table.Count + " strings, " + miss.Count + " missing");
                foreach (var k in miss.Take(int.Parse(Program.Arg(args, "--show", "30")))) Console.WriteLine("  missing " + k);
                // placeholder parity with English
                if (lang != "en")
                {
                    var en = RepoPaths.LoadLoc("en") ?? new Dictionary<string, string>();
                    foreach (var kv in table)
                    {
                        string e;
                        if (!en.TryGetValue(kv.Key, out e)) continue;
                        var pa = Regex.Matches(e, "\\{\\d\\}").Select(m => m.Value).OrderBy(x => x).ToList();
                        var pb = Regex.Matches(kv.Value, "\\{\\d\\}").Select(m => m.Value).OrderBy(x => x).ToList();
                        if (!pa.SequenceEqual(pb)) { Console.WriteLine("  placeholder mismatch " + kv.Key); missing++; }
                    }
                }
            }
            return missing == 0 ? 0 : 1;
        }

        /// <summary>Render every tooltip in a language for proofreading (reports/loc/tooltips_{lang}.md).</summary>
        public static int Tooltips(string[] args)
        {
            var db = RepoPaths.LoadDb();
            string lang = Program.Arg(args, "--lang", "en");
            var loc = Load(lang);
            var tb = new TooltipBuilder(loc, db);
            var sb = new StringBuilder();
            sb.AppendLine("# Tooltips (" + lang + ") — generated from content data");
            Action<string, string, List<string>> sec = (cat, id, lines) =>
            {
                sb.AppendLine("### " + loc.Name(cat, id) + " (`" + cat + ":" + id + "`)");
                foreach (var l in lines) sb.AppendLine("- " + l);
            };
            foreach (var x in db.Forgemasters) sec("forgemaster", x.Id, tb.Forgemaster(x));
            foreach (var x in db.Frames) sec("frame", x.Id, tb.Frame(x));
            foreach (var x in db.Cores) sec("core", x.Id, tb.Core(x));
            foreach (var x in db.Modifiers) sec("modifier", x.Id, tb.Modifier(x));
            foreach (var x in db.Runes) sec("rune", x.Id, new List<string> { tb.Rune(x) });
            foreach (var x in db.Pieces) sec("piece", x.Id, tb.Piece(x));
            foreach (var x in db.Legendaries) sec("legendary", x.Id, tb.Legendary(x));
            foreach (var x in db.Blessings) sec("blessing", x.Id, tb.Blessing(x));
            foreach (var x in db.Enemies) sec("enemy", x.Id, tb.Enemy(x));
            var dir = Path.Combine(RepoPaths.Root(), "reports", "loc");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "tooltips_" + lang + ".md"), sb.ToString());
            Console.WriteLine("wrote reports/loc/tooltips_" + lang + ".md; unresolved keys: " + loc.MissingKeys.Count);
            foreach (var k in loc.MissingKeys.Take(40)) Console.WriteLine("  " + k);
            return loc.MissingKeys.Count == 0 ? 0 : 1;
        }
    }
}
