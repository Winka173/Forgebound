using System;
using System.Collections.Generic;
using Forgebound.Core.Combat;
using Forgebound.Core.Content;
using Forgebound.Core.ForgeBoard;
using Forgebound.Core.Run;
using Forgebound.Core.Simulation;
using Forgebound.Core.Weapons;

namespace Forgebound.Core.Progression
{
    /// <summary>Result of measuring a loadout on training dummies with the real combat simulation (not a formula).</summary>
    public sealed class ProbeResult
    {
        public string Dummy;
        public int Seconds;
        public long TotalDamage;
        public readonly long[] WeaponDamage = new long[3];
        public readonly Dictionary<string, int> RuneTriggers = new Dictionary<string, int>();
        public readonly Dictionary<int, int> LinkFires = new Dictionary<int, int>();
        public int ActiveLinks, DeadLinks;
        public long Dps { get { return Seconds > 0 ? TotalDamage / Seconds : 0; } }
        public BattleReport Report;
    }

    /// <summary>
    /// Shared "measure this loadout" probe used by: Forge Board before/after preview, Workshop Training Chamber,
    /// and balance tooling. Uses DummyMode (enemies passive) and a fixed seed so before/after comparisons isolate the change.
    /// </summary>
    public static class LoadoutProbe
    {
        public static readonly string[] Dummies = { "training_single", "training_swarm", "training_armored", "training_shielded", "training_status" };

        public static ProbeResult Measure(ContentDatabase db, string forgemasterId, IEnumerable<string> blessings, List<WeaponInstance> weapons, int primaryUid,
            BoardState board, string dummyEncounter, int seconds, ulong seed = 12345)
        {
            var enc = db.Encounter(dummyEncounter);
            if (enc == null) throw new ArgumentException("unknown dummy encounter " + dummyEncounter);
            Func<int, WeaponInstance> find = uid => { foreach (var w in weapons) if (w.Uid == uid) return w; return null; };
            var shapes = new ContentShapeResolver(db, find);
            var stats = BattleBuilder.HeroStats(db, forgemasterId, blessings);
            var analysis = BoardAnalyzer.Analyze(board, shapes, stats.SumUntagged(StatType.RelayCapacity));
            var active = new List<WeaponInstance>();
            var pieces = new List<PlacedPiece>(board.Pieces);
            pieces.Sort((a, b) => a.Uid.CompareTo(b.Uid));
            foreach (var p in pieces)
            {
                if (p.Kind != PieceKind.Weapon) continue;
                var w = find(p.WeaponUid);
                if (w == null || active.Contains(w)) continue;
                if (w.Uid == primaryUid) active.Insert(0, w); else active.Add(w);
            }
            var setup = new BattleSetup { Db = db, Seed = seed, Encounter = enc, DummyMode = true, MaxTicks = seconds * FMath.TicksPerSecond, RecordEvents = false };
            var fm = db.Forgemaster(forgemasterId);
            int maxHp = (fm != null ? fm.MaxHp : 100) + stats.SumUntagged(StatType.MaxHp);
            BattleBuilder.FillHero(db, setup.Hero, forgemasterId, blessings, maxHp, maxHp, active, board, analysis, shapes, stats);
            var result = Battle.RunToEnd(setup);
            var pr = new ProbeResult { Dummy = dummyEncounter, Seconds = seconds, TotalDamage = result.Report.TotalDamage, Report = result.Report, ActiveLinks = analysis.ActiveLinkCount };
            for (int i = 0; i < 3; i++) pr.WeaponDamage[i] = result.Report.Weapons[i].Damage;
            foreach (var kv in result.Report.RuneTriggers) pr.RuneTriggers[kv.Key] = kv.Value;
            foreach (var kv in result.Report.LinkFires) pr.LinkFires[kv.Key] = kv.Value;
            foreach (var l in analysis.Links) if (!l.Active) pr.DeadLinks++;
            return pr;
        }

        public static ProbeResult MeasureRun(ContentDatabase db, RunState run, string dummy = "training_single", int seconds = 15)
        {
            return Measure(db, run.ForgemasterId, run.Blessings, run.Weapons, run.PrimaryWeaponUid, run.Board, dummy, seconds);
        }
    }

    /// <summary>
    /// Collection Workshop sandbox: a separate loadout built only from discovered components. It shares rules and
    /// simulation with Adventure but never writes into a RunState (isolation tested in WorkshopIsolationTests).
    /// </summary>
    public sealed class WorkshopSession
    {
        public readonly ContentDatabase Db;
        public readonly Profile Profile;
        public string ForgemasterId;
        public readonly List<WeaponInstance> Weapons = new List<WeaponInstance>();
        public BoardState Board = new BoardState { Width = BoardState.MaxWidth, Height = BoardState.MaxHeight };
        public int PrimaryUid = -1;
        private int _nextUid = 1;
        public readonly bool Unrestricted;   // trials provide their own item pool

        public WorkshopSession(ContentDatabase db, Profile profile, string forgemasterId, bool unrestricted = false)
        {
            Db = db;
            Profile = profile;
            ForgemasterId = forgemasterId;
            Unrestricted = unrestricted;
        }

        public ContentShapeResolver Shapes() { return new ContentShapeResolver(Db, Find); }
        public WeaponInstance Find(int uid) { foreach (var w in Weapons) if (w.Uid == uid) return w; return null; }
        private bool Allowed(ItemCategory cat, string id) { return Unrestricted || Profile.CanUseInWorkshop(cat, id); }

        public WeaponInstance CreateWeapon(string frame, string core, out string error)
        {
            error = null;
            if (Db.Frame(frame) == null || !Allowed(ItemCategory.Frame, frame)) { error = "frame_locked"; return null; }
            if (core != null && (Db.Core(core) == null || !Allowed(ItemCategory.Core, core))) { error = "core_locked"; return null; }
            var w = new WeaponInstance { Uid = _nextUid++, FrameId = frame, CoreId = core, Tier = 1 };
            Weapons.Add(w);
            if (PrimaryUid < 0) PrimaryUid = w.Uid;
            return w;
        }

        public string AddModifier(WeaponInstance w, string id)
        {
            if (!Allowed(ItemCategory.Modifier, id)) return "locked";
            var issues = AssemblyRules.PreviewAddModifier(Db, w, id, null);
            if (issues.Count > 0) return issues[0].ToString();
            w.Modifiers.Add(id);
            return null;
        }

        public string AddRune(WeaponInstance w, string id)
        {
            if (!Allowed(ItemCategory.Rune, id)) return "locked";
            var issues = AssemblyRules.PreviewAddRune(Db, w, id, null);
            if (issues.Count > 0) return issues[0].ToString();
            w.Runes.Add(id);
            return null;
        }

        public BoardEditor Editor() { return new BoardEditor(Board, Shapes()); }

        public PlaceError PlaceWeapon(WeaponInstance w, int x, int y, int rot)
        {
            var existing = Board.FindWeapon(w.Uid);
            if (existing != null) return Editor().Move(existing.Uid, new Cell(x, y), rot);
            return Editor().Place(new PlacedPiece { Kind = PieceKind.Weapon, WeaponUid = w.Uid }, new Cell(x, y), rot);
        }

        public PlaceError PlacePiece(string pieceId, int x, int y, int rot)
        {
            var def = Db.Piece(pieceId);
            if (def == null || !Allowed(ItemCategory.Piece, pieceId)) return PlaceError.UnknownShape;
            return Editor().Place(new PlacedPiece { Kind = def.Kind, PieceId = pieceId }, new Cell(x, y), rot);
        }

        /// <summary>Training Chamber: measure the current sandbox loadout on a dummy category.</summary>
        public ProbeResult Train(string dummy, int seconds)
        {
            return LoadoutProbe.Measure(Db, ForgemasterId, null, Weapons, PrimaryUid, Board, dummy, seconds);
        }

        public bool SaveDesign(WeaponInstance w, string name)
        {
            if (Profile.Designs.Count >= Profile.MaxDesigns) return false;
            var copy = w.Clone();
            copy.Uid = 0;
            Profile.Designs.Add(new SavedDesign { Name = string.IsNullOrEmpty(name) ? copy.FrameId : (name.Length > 40 ? name.Substring(0, 40) : name), Weapon = copy });
            return true;
        }
    }

    /// <summary>Blueprint Trials: restricted loadout + fixed encounter; cosmetic reward only.</summary>
    public static class Trials
    {
        public static WorkshopSession Begin(ContentDatabase db, Profile profile, ChallengeDef c)
        {
            var fm = db.Forgemaster(c.Forgemaster ?? "blacksmith");
            var s = new WorkshopSession(db, profile, fm.Id, true);
            var stats = new StatBlock();
            stats.AddRange(c.Rules);
            int w = Math.Max(BoardState.AbsoluteMin, Math.Min(BoardState.MaxWidth, BoardState.MinWidth + stats.SumUntagged(StatType.BoardWidth)));
            int h = Math.Max(BoardState.AbsoluteMin, Math.Min(BoardState.MaxHeight, BoardState.MinHeight + stats.SumUntagged(StatType.BoardHeight)));
            s.Board = new BoardState { Width = w, Height = h };
            if (fm.StartFrame != null && (c.AllowedFrames.Count == 0 || c.AllowedFrames.Contains(fm.StartFrame)))
            {
                string err;
                var wpn = s.CreateWeapon(fm.StartFrame, fm.StartCore, out err);
                if (wpn != null) s.PlaceWeapon(wpn, 0, 0, 0);
            }
            return s;
        }

        /// <summary>Items granted by the trial (category, id) — the UI lets the player assemble them.</summary>
        public static List<KeyValuePair<ItemCategory, string>> Items(ChallengeDef c)
        {
            var list = new List<KeyValuePair<ItemCategory, string>>();
            foreach (var spec in c.GivenItems)
            {
                var parts = spec.Split(':');
                ItemCategory cat;
                if (parts.Length == 2 && Enum.TryParse(parts[0], true, out cat)) list.Add(new KeyValuePair<ItemCategory, string>(cat, parts[1]));
            }
            return list;
        }

        public static BattleResult Fight(ContentDatabase db, Profile profile, ChallengeDef c, WorkshopSession s, ulong seed)
        {
            foreach (var w in s.Weapons)
                if (c.AllowedFrames.Count > 0 && !c.AllowedFrames.Contains(w.FrameId)) throw new InvalidOperationException("frame not allowed in trial");
            var shapes = s.Shapes();
            var stats = BattleBuilder.HeroStats(db, s.ForgemasterId, null);
            stats.AddRange(c.Rules);
            var analysis = BoardAnalyzer.Analyze(s.Board, shapes, stats.SumUntagged(StatType.RelayCapacity));
            var active = new List<WeaponInstance>();
            foreach (var p in s.Board.Pieces) if (p.Kind == PieceKind.Weapon) { var w = s.Find(p.WeaponUid); if (w != null && !active.Contains(w)) { if (w.Uid == s.PrimaryUid) active.Insert(0, w); else active.Add(w); } }
            if (active.Count == 0) throw new InvalidOperationException("no weapon on board");
            var setup = new BattleSetup { Db = db, Seed = seed, Encounter = db.Encounter(c.Encounter), RecordEvents = false };
            foreach (var m in c.Rules) if (m.Stat == StatType.EnemyHp || m.Stat == StatType.EnemyDamage || m.Stat == StatType.EnemySpeed) setup.Difficulty.Add(m);
            var fm = db.Forgemaster(s.ForgemasterId);
            int maxHp = Math.Max(10, fm.MaxHp + stats.SumUntagged(StatType.MaxHp));
            BattleBuilder.FillHero(db, setup.Hero, s.ForgemasterId, null, maxHp, maxHp, active, s.Board, analysis, shapes, stats);
            var result = Battle.RunToEnd(setup);
            if (result.Victory)
            {
                profile.TrialsCompleted.Add(c.Id);
                if (c.RewardCosmetic != null) profile.Cosmetics.Add(c.RewardCosmetic);
            }
            return result;
        }
    }
}
