using System.Collections.Generic;
using System.Text;
using Forgebound.Core.Combat;
using Forgebound.Core.Content;
using Forgebound.Core.ForgeBoard;
using Forgebound.Core.Run;
using Forgebound.Core.Weapons;
using NUnit.Framework;

namespace Forgebound.Tests
{
    public static class TestBattles
    {
        public static BattleSetup Setup(List<WeaponInstance> weapons, BoardState board, string encounter, bool dummy, ulong seed = 1, string fm = "blacksmith", int seconds = 60, List<string> blessings = null)
        {
            var db = TestContent.Db;
            if (board == null)
            {
                board = new BoardState { Width = 7, Height = 5 };
                var ed = new BoardEditor(board, TestContent.Shapes(weapons));
                int y = 0;
                foreach (var w in weapons) { ed.Place(new PlacedPiece { Kind = PieceKind.Weapon, WeaponUid = w.Uid }, new Cell(0, y), 0); y += 2; if (y > 4) y = 1; }
            }
            var shapes = TestContent.Shapes(weapons);
            var stats = BattleBuilder.HeroStats(db, fm, blessings);
            var analysis = BoardAnalyzer.Analyze(board, shapes, stats.SumUntagged(StatType.RelayCapacity));
            var setup = new BattleSetup { Db = db, Seed = seed, Encounter = db.Encounter(encounter), DummyMode = dummy, MaxTicks = seconds * 30 };
            var active = new List<WeaponInstance>();
            foreach (var w in weapons) if (board.FindWeapon(w.Uid) != null) active.Add(w);
            BattleBuilder.FillHero(db, setup.Hero, fm, blessings, 100, 100, active, board, analysis, shapes, stats);
            return setup;
        }

        public static WeaponInstance W(int uid, string frame, string core = null, params string[] mods)
        {
            var w = new WeaponInstance { Uid = uid, FrameId = frame, CoreId = core, Tier = 3 };
            w.Modifiers.AddRange(mods);
            return w;
        }

        public static string EventHash(List<CombatEvent> events)
        {
            var sb = new StringBuilder();
            foreach (var e in events) sb.Append(e.ToString()).Append('\n');
            return Forgebound.Core.Save.SaveSystem.Checksum(sb.ToString());
        }

        public static Unit FirstEnemy(Battle b)
        {
            foreach (var u in b.Units) if (u.Team == 1) return u;
            return null;
        }
    }

    public class DamagePipelineTests
    {
        private Battle Make(string encounter)
        {
            return new Battle(TestBattles.Setup(new List<WeaponInstance> { TestBattles.W(1, "sword") }, null, encounter, true));
        }

        [Test]
        public void ArmorReducesPhysicalDamage()
        {
            var b = Make("training_armored");
            var e = TestBattles.FirstEnemy(b);
            int before = e.Hp;
            bool crit;
            int dealt = b.DealDamage(b.Hero, e, 160, DamageType.Physical, 0, false, 0, 0, 1000, 0, HitFlags.NoCrit, -1, out crit);
            Assert.AreEqual(100, dealt);   // 160 * 100 / (100 + 60)
            Assert.AreEqual(before - 100, e.Hp);
        }

        [Test]
        public void ArmorPenAndTrueDamage()
        {
            var b = Make("training_armored");
            var e = TestBattles.FirstEnemy(b);
            bool crit;
            Assert.AreEqual(160, b.DealDamage(b.Hero, e, 160, DamageType.True, 0, false, 0, 0, 1000, 0, HitFlags.NoCrit, -1, out crit));
            Assert.AreEqual(160 * 100 / 140, b.DealDamage(b.Hero, e, 160, DamageType.Physical, 0, false, 0, 0, 1000, 20, HitFlags.NoCrit, -1, out crit));
        }

        [Test]
        public void ElementalResistApplies()
        {
            var b = Make("training_status");
            var e = TestBattles.FirstEnemy(b);
            bool crit;
            Assert.AreEqual(50, b.DealDamage(b.Hero, e, 100, DamageType.Fire, 0, false, 0, 0, 1000, 0, HitFlags.NoCrit, -1, out crit));
        }

        [Test]
        public void ShieldAbsorbsBeforeHp()
        {
            var b = Make("training_shielded");
            var e = TestBattles.FirstEnemy(b);
            int hp = e.Hp;
            bool crit;
            int dealt = b.DealDamage(b.Hero, e, 1000, DamageType.True, 0, false, 0, 0, 1000, 0, HitFlags.NoCrit, -1, out crit);
            Assert.AreEqual(1000, dealt);
            Assert.AreEqual(500, e.Shield);
            Assert.AreEqual(hp, e.Hp);
        }

        [Test]
        public void CritAlwaysWithFullChance()
        {
            var b = Make("training_single");
            var e = TestBattles.FirstEnemy(b);
            bool crit;
            int dealt = b.DealDamage(b.Hero, e, 100, DamageType.True, 0, false, 0, 1000, 2000, 0, HitFlags.None, -1, out crit);
            Assert.IsTrue(crit);
            Assert.AreEqual(200, dealt);
        }

        [Test]
        public void ChillStacksIntoFreezeThenImmunity()
        {
            var b = Make("training_single");
            var e = TestBattles.FirstEnemy(b);
            var chill = new StatusApply { Status = StatusType.Chill, ChancePermille = 1000, Stacks = 1, DurationTicks = 90 };
            for (int i = 0; i < 4; i++) b.ApplyStatus(e, chill, b.Weapons[0], false, 0, 0);
            Assert.AreEqual(4, e.Status.Get(StatusType.Chill));
            Assert.IsFalse(e.Status.Has(StatusType.Freeze));
            b.ApplyStatus(e, chill, b.Weapons[0], false, 0, 0);
            Assert.IsTrue(e.Status.Has(StatusType.Freeze));
            Assert.AreEqual(0, e.Status.Get(StatusType.Chill));
            for (int i = 0; i < 50; i++) b.Step();
            Assert.IsFalse(e.Status.Has(StatusType.Freeze));
            Assert.Greater(e.Status.FreezeImmuneTicks, 0);
        }

        [Test]
        public void BurnDealsDamageOverTime()
        {
            var b = Make("training_single");
            var e = TestBattles.FirstEnemy(b);
            b.ApplyStatus(e, new StatusApply { Status = StatusType.Burn, ChancePermille = 1000, Stacks = 3, DurationTicks = 90 }, b.Weapons[0], false, 0, 0);
            long dotBefore = b.Report.Weapons[0].DotDamage;
            for (int i = 0; i < 45; i++) b.Step();
            Assert.Greater(b.Report.Weapons[0].DotDamage, dotBefore);
        }
    }

    public class SimulationDeterminismTests
    {
        private BattleSetup Rich(ulong seed, bool record)
        {
            var weapons = new List<WeaponInstance>
            {
                TestBattles.W(1, "sword", "lightning", "sweep", "repeat"),
                TestBattles.W(2, "bow", "fire", "split", "pierce"),
                TestBattles.W(3, "staff", "frost", "explosion")
            };
            var s = TestBattles.Setup(weapons, null, "frost_shard_storm", false, seed);
            s.RecordEvents = record;
            return s;
        }

        [Test]
        public void SameSeedIdenticalEventStream()
        {
            var a = new Battle(Rich(77, true));
            var b = new Battle(Rich(77, true));
            while (!a.Finished) a.Step();
            while (!b.Finished) b.Step();
            Assert.AreEqual(a.Result.Ticks, b.Result.Ticks);
            Assert.AreEqual(TestBattles.EventHash(a.Events), TestBattles.EventHash(b.Events));
            Assert.Greater(a.Events.Count, 100);
        }

        [Test]
        public void RecordingEventsDoesNotChangeOutcome()
        {
            var a = Battle.RunToEnd(Rich(5, true));
            var b = Battle.RunToEnd(Rich(5, false));
            Assert.AreEqual(a.Victory, b.Victory);
            Assert.AreEqual(a.Ticks, b.Ticks);
            Assert.AreEqual(a.HeroHp, b.HeroHp);
            Assert.AreEqual(a.Report.TotalDamage, b.Report.TotalDamage);
        }

        [Test]
        public void SteppingCadenceInvariant_2xSpeed()
        {
            // 2x speed in presentation = two Step() calls per frame; the simulation result must be identical.
            var a = new Battle(Rich(9, true));
            var b = new Battle(Rich(9, true));
            while (!a.Finished) a.Step();
            while (!b.Finished) { b.Step(); if (!b.Finished) b.Step(); }
            Assert.AreEqual(TestBattles.EventHash(a.Events), TestBattles.EventHash(b.Events));
        }

        [Test]
        public void ThirtyThousandTickSoakIsStable()
        {
            var weapons = new List<WeaponInstance>
            {
                TestBattles.W(1, "dagger", "lightning", "repeat"),
                TestBattles.W(2, "bow", "venom", "split", "ricochet"),
                TestBattles.W(3, "tome", "arcane")
            };
            weapons[0].Runes.Add("echo_glyph");
            weapons[0].Runes.Add("crit_spark");
            var setup = TestBattles.Setup(weapons, null, "training_swarm", true, 3, "runecrafter", 1000);
            setup.RecordEvents = false;
            var b = new Battle(setup);
            while (!b.Finished) b.Step();
            Assert.AreEqual(30000, b.Result.Ticks);
            Assert.LessOrEqual(b.Report.PeakProjectiles, setup.MaxProjectiles);
            Assert.LessOrEqual(b.Report.MaxProcsInOneTick, setup.MaxProcsPerTick);
            Assert.LessOrEqual(b.Report.MaxChainDepthReached, setup.MaxProcDepth);
        }
    }

    public class RuneAndLinkTests
    {
        [Test]
        public void EveryNthRuneFiresAtExpectedRate()
        {
            var w = TestBattles.W(1, "sword", "arcane");   // core_arcane_echo: every 4th attack
            var b = new Battle(TestBattles.Setup(new List<WeaponInstance> { w }, null, "training_single", true, 1, "blacksmith", 30));
            while (!b.Finished) b.Step();
            int attacks = b.Report.Weapons[0].Attacks;
            int triggers;
            b.Report.RuneTriggers.TryGetValue("core_arcane_echo", out triggers);
            Assert.Greater(attacks, 8);
            Assert.AreEqual(attacks / 4, triggers);
        }

        [Test]
        public void MaxPerBattleRespected()
        {
            var w = TestBattles.W(1, "sword", "steel");
            w.Runes.Add("bounty_rune");   // max 8 per battle
            var b = new Battle(TestBattles.Setup(new List<WeaponInstance> { w }, null, "training_swarm", false, 1, "blacksmith", 60));
            while (!b.Finished) b.Step();
            int n;
            b.Report.RuneTriggers.TryGetValue("bounty_rune", out n);
            Assert.LessOrEqual(n, 8);
            Assert.LessOrEqual(b.Result.GoldBonus, 30);
        }

        private BoardState ConnectedBoard(List<WeaponInstance> ws, bool connect)
        {
            var board = new BoardState();
            var ed = new BoardEditor(board, TestContent.Shapes(ws));
            ed.Place(new PlacedPiece { Kind = PieceKind.Weapon, WeaponUid = 1 }, new Cell(0, 1), 0);
            ed.Place(new PlacedPiece { Kind = PieceKind.Relay, PieceId = "echo_relay" }, new Cell(3, 1), connect ? 0 : 1);
            ed.Place(new PlacedPiece { Kind = PieceKind.Weapon, WeaponUid = 2 }, new Cell(4, 0), 0);
            return board;
        }

        [Test]
        public void DisconnectedRelayNeverApplies_ConnectedRelayAltersAttacks()
        {
            var ws = new List<WeaponInstance> { TestBattles.W(1, "sword", "fire"), TestBattles.W(2, "bow", "lightning") };
            var off = new Battle(TestBattles.Setup(ws, ConnectedBoard(ws, false), "training_single", true, 4, "blacksmith", 30));
            while (!off.Finished) off.Step();
            Assert.IsEmpty(off.Report.LinkFires);
            foreach (var e in off.Events) Assert.AreNotEqual(CombatEventType.LinkFire, e.Type);
            Assert.IsNotEmpty(off.Report.DeadLinks);

            var on = new Battle(TestBattles.Setup(ws, ConnectedBoard(ws, true), "training_single", true, 4, "blacksmith", 30));
            while (!on.Finished) on.Step();
            int fires;
            on.Report.LinkFires.TryGetValue(0, out fires);
            Assert.Greater(fires, 0);
            Assert.Greater(on.Report.Weapons[1].ProcDamage, off.Report.Weapons[1].ProcDamage);
            Assert.Greater(on.Report.TotalDamage, off.Report.TotalDamage);
        }

        [Test]
        public void ProcAttacksNeverChargeLinks()
        {
            // Two weapons linked both ways via resonance cannot ping-pong: only non-proc events charge links.
            var ws = new List<WeaponInstance> { TestBattles.W(1, "sword", "fire", "repeat"), TestBattles.W(2, "dagger", "steel") };
            var board = new BoardState();
            var ed = new BoardEditor(board, TestContent.Shapes(ws));
            ed.Place(new PlacedPiece { Kind = PieceKind.Weapon, WeaponUid = 1 }, new Cell(0, 1), 0);
            ed.Place(new PlacedPiece { Kind = PieceKind.Relay, PieceId = "junction" }, new Cell(3, 1), 0);
            ed.Place(new PlacedPiece { Kind = PieceKind.Weapon, WeaponUid = 2 }, new Cell(4, 1), 1);
            var b = new Battle(TestBattles.Setup(ws, board, "training_single", true, 2, "runecrafter", 30));
            int attacks = 0;
            while (!b.Finished) b.Step();
            foreach (var w in b.Report.Weapons) attacks += w.Attacks;
            int fires = 0;
            foreach (var kv in b.Report.LinkFires) fires += kv.Value;
            Assert.LessOrEqual(fires, attacks * 2, "link fires must be bounded by real (non-proc) source events");
        }

        [Test]
        public void SelfTriggerGuardStopsRuneFeedingItself()
        {
            // corpse_blast (OnKill → nova, allowFromProc) on a swarm: its own nova kills must not retrigger it.
            var w = TestBattles.W(1, "greatsword", "fire");
            w.Runes.Add("corpse_blast");
            var b = new Battle(TestBattles.Setup(new List<WeaponInstance> { w }, null, "training_swarm", false, 8, "blacksmith", 60));
            while (!b.Finished) b.Step();
            int self;
            b.Report.RuneFailures.TryGetValue("corpse_blast:self_trigger", out self);
            int fired;
            b.Report.RuneTriggers.TryGetValue("corpse_blast", out fired);
            Assert.LessOrEqual(fired, b.Report.EnemiesKilled);
        }
    }
}
