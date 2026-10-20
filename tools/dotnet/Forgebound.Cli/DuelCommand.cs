using System;
using System.Collections.Generic;
using System.Linq;
using Forgebound.Core.Combat;
using Forgebound.Core.Content;
using Forgebound.Core.ForgeBoard;
using Forgebound.Core.Run;
using Forgebound.Core.Weapons;

namespace Forgebound.Cli
{
    /// <summary>`forgebound duel --weapons sword:fire:sweep,bow:lightning --encounter ember_boss_kaldur [--fm blacksmith] [--hp 100] [--trace]`</summary>
    public static class DuelCommand
    {
        public static WeaponInstance ParseWeapon(ContentDatabase db, string spec, int uid)
        {
            // frame:core:mod+mod:rune+rune:tier
            var p = spec.Split(':');
            var w = new WeaponInstance { Uid = uid, FrameId = p[0], CoreId = p.Length > 1 && p[1] != "-" && p[1] != "" ? p[1] : null, Tier = p.Length > 4 ? int.Parse(p[4]) : 1 };
            if (p.Length > 2 && p[2] != "") w.Modifiers.AddRange(p[2].Split('+'));
            if (p.Length > 3 && p[3] != "") w.Runes.AddRange(p[3].Split('+'));
            return w;
        }

        public static int Run(string[] args)
        {
            var db = RepoPaths.LoadDb();
            var specs = Program.Arg(args, "--weapons", "sword:fire").Split(',');
            var weapons = new List<WeaponInstance>();
            for (int i = 0; i < specs.Length; i++) weapons.Add(ParseWeapon(db, specs[i], i + 1));
            var board = new BoardState { Width = 7, Height = 5 };
            Func<int, WeaponInstance> find = uid => weapons.FirstOrDefault(w => w.Uid == uid);
            var shapes = new ContentShapeResolver(db, find);
            var ed = new BoardEditor(board, shapes);
            int y = 0;
            foreach (var w in weapons) { var e = ed.Place(new PlacedPiece { Kind = PieceKind.Weapon, WeaponUid = w.Uid }, new Cell(0, y), 0); if (e != PlaceError.None) Console.WriteLine("place " + w.FrameId + ": " + e); y += 2; }
            string fm = Program.Arg(args, "--fm", "blacksmith");
            int hp = int.Parse(Program.Arg(args, "--hp", "0"));
            var stats = BattleBuilder.HeroStats(db, fm, null);
            var analysis = BoardAnalyzer.Analyze(board, shapes);
            var setup = new BattleSetup { Db = db, Seed = ulong.Parse(Program.Arg(args, "--seed", "1")), Encounter = db.Encounter(Program.Arg(args, "--encounter", "ember_boss_kaldur")), Difficulty = Difficulty.Stats(int.Parse(Program.Arg(args, "--difficulty", "1"))) };
            int maxHp = db.Forgemaster(fm).MaxHp + stats.SumUntagged(StatType.MaxHp);
            BattleBuilder.FillHero(db, setup.Hero, fm, null, hp > 0 ? hp : maxHp, maxHp, weapons, board, analysis, shapes, stats);
            foreach (var w in weapons) foreach (var issue in AssemblyRules.Validate(db, w, stats)) Console.WriteLine("assembly: " + issue);
            var b = new Battle(setup);
            bool trace = args.Contains("--trace");
            int lastSec = -1;
            while (!b.Finished)
            {
                b.Step();
                if (b.Tick / 30 != lastSec)
                {
                    lastSec = b.Tick / 30;
                    var enemies = b.Units.Where(u => u.Team == 1 && u.Alive).Select(u => (u.Def != null ? u.Def.Id : "?") + "@" + u.X / 100 + ":" + u.Hp + (u.Shield > 0 ? "+" + u.Shield : "") + "[" + u.State + "]");
                    Console.WriteLine("t=" + lastSec + "s hero x=" + b.Hero.X / 100 + " hp=" + b.Hero.Hp + "/" + b.Hero.MaxHp + " sh=" + b.Hero.Shield + " [" + b.Hero.State + "] cd=" + string.Join("/", b.Weapons.Select(w => w.Cooldown / 1000 + "w" + w.WindupLeft)) + " | " + string.Join(" ", enemies));
                }
            }
            if (trace) foreach (var e in b.Events.Where(e => e.Type == CombatEventType.Damage && e.B == b.Hero.Id || e.Type == CombatEventType.Telegraph || e.Type == CombatEventType.AbilityCast || e.Type == CombatEventType.PhaseChange)) Console.WriteLine("  " + e);
            var r = b.Result;
            Console.WriteLine("result victory=" + r.Victory + " t=" + r.Ticks / 30.0 + "s heroHp=" + r.HeroHp + " dmg=" + r.Report.TotalDamage + " taken=" + r.Report.DamageTaken);
            for (int i = 0; i < weapons.Count; i++) Console.WriteLine("  w" + i + " " + weapons[i].FrameId + " dmg=" + r.Report.Weapons[i].Damage + " attacks=" + r.Report.Weapons[i].Attacks + " hits=" + r.Report.Weapons[i].Hits);
            foreach (var kv in r.Report.DamageTakenBySource) Console.WriteLine("  taken from " + kv.Key + ": " + kv.Value);
            foreach (var i in r.Report.Insights) Console.WriteLine("  insight " + i);
            return 0;
        }
    }
}
