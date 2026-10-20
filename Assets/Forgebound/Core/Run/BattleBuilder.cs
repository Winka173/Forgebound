using System;
using System.Collections.Generic;
using Forgebound.Core.Combat;
using Forgebound.Core.Content;
using Forgebound.Core.ForgeBoard;
using Forgebound.Core.Weapons;

namespace Forgebound.Core.Run
{
    /// <summary>Resolves board piece shapes from content + a weapon lookup (run weapons or workshop blueprints).</summary>
    public sealed class ContentShapeResolver : IPieceShapeResolver
    {
        private readonly ContentDatabase _db;
        private readonly Func<int, WeaponInstance> _weapons;

        public ContentShapeResolver(ContentDatabase db, Func<int, WeaponInstance> weapons)
        {
            _db = db;
            _weapons = weapons;
        }

        public bool TryGetShape(PlacedPiece piece, out List<Cell> cells, out List<PortDef> ports)
        {
            cells = null;
            ports = null;
            if (piece.Kind == PieceKind.Weapon)
            {
                var w = _weapons(piece.WeaponUid);
                var f = w != null ? _db.Frame(w.FrameId) : null;
                if (f == null) return false;
                cells = f.Footprint;
                ports = f.Ports;
                return true;
            }
            var def = _db.Piece(piece.PieceId);
            if (def == null) return false;
            cells = def.Footprint;
            ports = def.Ports;
            return true;
        }

        public RelayDef GetRelay(PlacedPiece piece)
        {
            if (piece.Kind != PieceKind.Relay) return null;
            var def = _db.Piece(piece.PieceId);
            return def != null ? def.Relay : null;
        }

        public BoardPieceDef GetPieceDef(PlacedPiece piece)
        {
            return piece.Kind == PieceKind.Weapon ? null : _db.Piece(piece.PieceId);
        }
    }

    public static class Difficulty
    {
        public static readonly string[] Names = { "story", "normal", "hard", "forgemaster" };

        public static StatBlock Stats(int level)
        {
            var s = new StatBlock();
            switch (level)
            {
                case 0:
                    s.Add(new StatMod { Stat = StatType.EnemyHp, Value = -250 });
                    s.Add(new StatMod { Stat = StatType.EnemyDamage, Value = -250 });
                    break;
                case 2:
                    s.Add(new StatMod { Stat = StatType.EnemyHp, Value = 200 });
                    s.Add(new StatMod { Stat = StatType.EnemyDamage, Value = 150 });
                    break;
                case 3:
                    s.Add(new StatMod { Stat = StatType.EnemyHp, Value = 350 });
                    s.Add(new StatMod { Stat = StatType.EnemyDamage, Value = 300 });
                    s.Add(new StatMod { Stat = StatType.EnemySpeed, Value = 100 });
                    break;
            }
            return s;
        }
    }

    /// <summary>Builds the immutable battle configuration from a run (or workshop) loadout. Shared by game, tests and balance tools.</summary>
    public static class BattleBuilder
    {
        public static StatBlock HeroStats(ContentDatabase db, string forgemasterId, IEnumerable<string> blessings)
        {
            var s = new StatBlock();
            var fm = db.Forgemaster(forgemasterId);
            if (fm != null) s.AddRange(fm.Perks);
            if (blessings != null)
                foreach (var b in blessings)
                {
                    var def = db.Blessing(b);
                    if (def != null) s.AddRange(def.Stats);
                }
            return s;
        }

        public static int MaxHp(ContentDatabase db, RunState run)
        {
            var stats = HeroStats(db, run.ForgemasterId, run.Blessings);
            return Math.Max(10, run.MaxHpBase + stats.SumUntagged(StatType.MaxHp));
        }

        /// <summary>Weapons in battle order: primary first, then the other board weapons by board uid. Only weapons ON the board fight.</summary>
        public static List<WeaponInstance> ActiveWeapons(RunState run)
        {
            var list = new List<WeaponInstance>();
            var pieces = new List<PlacedPiece>(run.Board.Pieces);
            pieces.Sort((a, b) => a.Uid.CompareTo(b.Uid));
            foreach (var p in pieces)
            {
                if (p.Kind != PieceKind.Weapon) continue;
                var w = run.FindWeapon(p.WeaponUid);
                if (w == null || list.Contains(w)) continue;
                if (w.Uid == run.PrimaryWeaponUid) list.Insert(0, w); else list.Add(w);
                if (list.Count >= BoardEditor.MaxActiveWeapons) break;
            }
            return list;
        }

        public static BattleSetup Build(ContentDatabase db, RunState run, EncounterDef encounter, ulong seed, out BoardAnalysis analysis)
        {
            var shapes = new ContentShapeResolver(db, run.FindWeapon);
            var heroStats = HeroStats(db, run.ForgemasterId, run.Blessings);
            var setup = new BattleSetup
            {
                Db = db,
                Seed = seed,
                Encounter = encounter,
                Chapter = run.Chapter + 1,
                Difficulty = Difficulty.Stats(run.Difficulty)
            };
            foreach (var b in run.Blessings)
            {
                var def = db.Blessing(b);
                if (def == null) continue;
                // Curses / blessings may carry enemy-side stats.
                foreach (var m in def.Stats)
                    if (m.Stat == StatType.EnemyHp || m.Stat == StatType.EnemyDamage || m.Stat == StatType.EnemySpeed) setup.Difficulty.Add(m);
            }
            var weapons = ActiveWeapons(run);
            analysis = BoardAnalyzer.Analyze(run.Board, shapes, heroStats.SumUntagged(StatType.RelayCapacity));
            FillHero(db, setup.Hero, run.ForgemasterId, run.Blessings, Math.Max(1, run.Hp), MaxHp(db, run), weapons, run.Board, analysis, shapes, heroStats);
            setup.Hero.Targeting = run.Targeting;
            return setup;
        }

        public static void FillHero(ContentDatabase db, HeroSetup hero, string forgemasterId, IEnumerable<string> blessings, int hp, int maxHp,
            List<WeaponInstance> weapons, BoardState board, BoardAnalysis analysis, IPieceShapeResolver shapes, StatBlock heroStats)
        {
            var fm = db.Forgemaster(forgemasterId);
            hero.ForgemasterId = forgemasterId;
            hero.MaxHp = maxHp;
            hero.Hp = Math.Min(hp, maxHp);
            hero.Armor = fm != null ? fm.Armor : 0;
            hero.Stats = heroStats;
            if (fm != null) hero.HeroRunes.AddRange(fm.InnateRunes);
            if (blessings != null)
                foreach (var b in blessings)
                {
                    var def = db.Blessing(b);
                    if (def != null) hero.HeroRunes.AddRange(def.Runes);
                }

            // Support adjacency stats per weapon uid (only from supports that actually touch the weapon).
            var supportStats = new Dictionary<int, StatBlock>();
            int adjPower = heroStats.SumUntagged(StatType.AdjacencyPower);
            foreach (var kv in analysis.SupportTargets)
            {
                var piece = board.Find(kv.Key);
                var def = piece != null ? shapes.GetPieceDef(piece) : null;
                if (def == null) continue;
                foreach (var wuid in kv.Value)
                {
                    StatBlock sb;
                    if (!supportStats.TryGetValue(wuid, out sb)) { sb = new StatBlock(); supportStats[wuid] = sb; }
                    foreach (var m in def.AdjacentStats)
                        sb.Add(adjPower == 0 ? m : new StatMod { Stat = m.Stat, Tag = m.Tag, Value = m.Value * (1000 + adjPower) / 1000 });
                }
            }
            for (int i = 0; i < weapons.Count; i++)
            {
                StatBlock sb;
                supportStats.TryGetValue(weapons[i].Uid, out sb);
                hero.Weapons.Add(WeaponCompiler.Compile(db, weapons[i], heroStats, sb, i == 0));
            }
            hero.Links.AddRange(LinkCompiler.Compile(analysis, board, shapes, hero.Weapons, heroStats));
            foreach (var l in analysis.Links) if (!l.Active) hero.DeadLinks.Add(l.Describe());
            foreach (var issue in analysis.Issues) if (issue.Code == "dead_relay" || issue.Code == "idle_support") hero.DeadLinks.Add(issue.ToString());
        }

        /// <summary>Derive the combat seed for the Nth battle of a run (stable under save/load and replay).</summary>
        public static ulong BattleSeed(ulong runSeed, int battleIndex, string nodeId)
        {
            return Simulation.DetRandom.DeriveSeed(runSeed, "battle:" + battleIndex + ":" + nodeId);
        }
    }
}
