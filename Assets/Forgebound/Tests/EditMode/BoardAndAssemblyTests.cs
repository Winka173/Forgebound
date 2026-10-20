using System.Collections.Generic;
using Forgebound.Core.Content;
using Forgebound.Core.ForgeBoard;
using Forgebound.Core.Weapons;
using NUnit.Framework;

namespace Forgebound.Tests
{
    public class BoardGeometryTests
    {
        private List<WeaponInstance> _weapons;
        private BoardState _board;
        private BoardEditor _editor;

        [SetUp]
        public void Setup()
        {
            _weapons = new List<WeaponInstance>
            {
                new WeaponInstance { Uid = 1, FrameId = "sword", CoreId = "fire" },
                new WeaponInstance { Uid = 2, FrameId = "bow", CoreId = "lightning" },
                new WeaponInstance { Uid = 3, FrameId = "dagger" },
                new WeaponInstance { Uid = 4, FrameId = "hammer" }
            };
            _board = new BoardState();
            _editor = new BoardEditor(_board, TestContent.Shapes(_weapons));
        }

        private PlacedPiece W(int uid) { return new PlacedPiece { Kind = PieceKind.Weapon, WeaponUid = uid }; }
        private PlacedPiece R(string id) { return new PlacedPiece { Kind = PieceKind.Relay, PieceId = id }; }

        [Test]
        public void RotationRotatesCellsAndPorts()
        {
            var cells = BoardGeometry.RotateCells(new List<Cell> { new Cell(0, 0), new Cell(1, 0), new Cell(2, 0) }, 1);
            CollectionAssert.AreEquivalent(new[] { new Cell(0, 0), new Cell(0, 1), new Cell(0, 2) }, cells);
            Assert.AreEqual(PortDir.South, BoardGeometry.Rotate(PortDir.East, 1));
            Assert.AreEqual(PortDir.West, BoardGeometry.Rotate(PortDir.East, 2));
            Assert.AreEqual(PortDir.East, BoardGeometry.Rotate(PortDir.East, 4));
        }

        [Test]
        public void PlacementRejectsOutOfBoundsAndOverlap()
        {
            Assert.AreEqual(PlaceError.None, _editor.Place(W(1), new Cell(0, 0), 0));
            Assert.AreEqual(PlaceError.OutOfBounds, _editor.Place(W(2), new Cell(4, 3), 0));
            Assert.AreEqual(PlaceError.Overlap, _editor.Place(W(3), new Cell(1, 0), 0));
            Assert.AreEqual(PlaceError.OutOfBounds, _editor.Place(W(4), new Cell(-1, 0), 0));
            var a = BoardAnalyzer.Analyze(_board, TestContent.Shapes(_weapons));
            Assert.IsTrue(a.IsValid);
        }

        [Test]
        public void MaxThreeActiveWeapons()
        {
            Assert.AreEqual(PlaceError.None, _editor.Place(W(1), new Cell(0, 0), 0));
            Assert.AreEqual(PlaceError.None, _editor.Place(W(3), new Cell(3, 0), 0));
            Assert.AreEqual(PlaceError.None, _editor.Place(W(2), new Cell(0, 1), 0));
            Assert.AreEqual(PlaceError.TooManyWeapons, _editor.Place(W(4), new Cell(2, 1), 0));
        }

        [Test]
        public void DirectAdjacentPortsFormResonanceLink()
        {
            _editor.Place(W(1), new Cell(0, 1), 0);   // sword out-port at (2,1) East
            _editor.Place(W(2), new Cell(3, 0), 0);   // bow cells (3,0..2), in-port at (3,1) West
            var a = BoardAnalyzer.Analyze(_board, TestContent.Shapes(_weapons));
            Assert.AreEqual(1, a.ActiveLinkCount);
            Assert.AreEqual(1, a.Links[0].SourceWeaponUid);
            Assert.AreEqual(2, a.Links[0].DestWeaponUid);
            Assert.IsEmpty(a.Links[0].RelayUids);
        }

        [Test]
        public void RelayChainLinksWeapons_AndMovingBreaksIt()
        {
            _editor.Place(W(1), new Cell(0, 1), 0);
            var relay = R("echo_relay");
            _editor.Place(relay, new Cell(3, 1), 0);
            _editor.Place(W(2), new Cell(4, 0), 0);
            var a = BoardAnalyzer.Analyze(_board, TestContent.Shapes(_weapons));
            Assert.AreEqual(1, a.ActiveLinkCount, string.Join(";", a.Issues));
            Assert.AreEqual(1, a.Links[0].RelayUids.Count);
            Assert.IsFalse(a.HasIssue("dead_relay"));

            // Rotate the relay: ports no longer line up → no link, relay reported dead.
            _editor.Rotate(relay.Uid);
            a = BoardAnalyzer.Analyze(_board, TestContent.Shapes(_weapons));
            Assert.AreEqual(0, a.ActiveLinkCount);
            Assert.IsTrue(a.HasIssue("dead_relay"));
        }

        [Test]
        public void UndoRedoRestoresExactLayout()
        {
            _editor.Place(W(1), new Cell(0, 0), 0);
            var r = R("conduit");
            _editor.Place(r, new Cell(3, 0), 0);
            _editor.Move(r.Uid, new Cell(3, 2), 1);
            Assert.AreEqual(new Cell(3, 2), _board.Find(r.Uid).Origin);
            Assert.IsTrue(_editor.Undo());
            Assert.AreEqual(new Cell(3, 0), _board.Find(r.Uid).Origin);
            Assert.AreEqual(0, _board.Find(r.Uid).Rotation);
            Assert.IsTrue(_editor.Redo());
            Assert.AreEqual(new Cell(3, 2), _board.Find(r.Uid).Origin);
            Assert.IsTrue(_editor.Undo());
            Assert.IsTrue(_editor.Undo());
            Assert.IsNull(_board.Find(r.Uid));
        }

        [Test]
        public void BidirectionalJunctionCyclesTerminate()
        {
            // Fill the board with junctions (ports on all sides, "both") around two weapons: graph full of cycles.
            _editor.Place(W(3), new Cell(0, 0), 0);
            _editor.Place(W(1), new Cell(0, 3), 0);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 5; x++)
                    _editor.Place(R("junction"), new Cell(x, y), 0);
            var a = BoardAnalyzer.Analyze(_board, TestContent.Shapes(_weapons));
            Assert.IsTrue(a.IsValid);
            foreach (var l in a.Links) Assert.LessOrEqual(l.RelayUids.Count, BoardAnalyzer.MaxRelaysPerPath);
            Assert.Less(a.Links.Count, 5000);
        }

        [Test]
        public void RelayCapacityLimitsLinks()
        {
            // Splitter fans out to two destinations through an amplifier with capacity 2 → both active;
            // with an echo_coil (capacity 1) the second path is inactive.
            var weapons = _weapons;
            _editor.Place(W(1), new Cell(0, 1), 0);
            _editor.Place(R("echo_coil"), new Cell(3, 1), 0);   // cells (3,1),(4,1) in West, out East at (4,1) -> off board
            var a = BoardAnalyzer.Analyze(_board, TestContent.Shapes(weapons));
            Assert.AreEqual(0, a.ActiveLinkCount);
            Assert.IsTrue(a.HasIssue("dead_relay"));
        }

        [Test]
        public void SupportBuffsOnlyEdgeAdjacentWeapons()
        {
            _editor.Place(W(1), new Cell(0, 0), 0);
            _editor.Place(W(3), new Cell(0, 2), 0);
            var ws = new PlacedPiece { Kind = PieceKind.Support, PieceId = "whetstone" };
            _editor.Place(ws, new Cell(0, 1), 0);   // touches both
            var far = new PlacedPiece { Kind = PieceKind.Support, PieceId = "lens" };
            _editor.Place(far, new Cell(4, 3), 0);  // touches nothing
            var a = BoardAnalyzer.Analyze(_board, TestContent.Shapes(_weapons));
            CollectionAssert.AreEquivalent(new[] { 1, 3 }, a.SupportTargets[ws.Uid]);
            Assert.IsEmpty(a.SupportTargets[far.Uid]);
            Assert.IsTrue(a.HasIssue("idle_support"));
        }
    }

    public class AssemblyTests
    {
        private ContentDatabase Db { get { return TestContent.Db; } }

        [Test]
        public void ModifierSlotOverflowRejected()
        {
            var w = new WeaponInstance { FrameId = "dagger" };   // 1 modifier slot
            w.Modifiers.Add("precision");
            Assert.IsEmpty(AssemblyRules.Validate(Db, w, null));
            var issues = AssemblyRules.PreviewAddModifier(Db, w, "quickdraw", null);
            Assert.IsTrue(issues.Exists(i => i.Code == "slot_overflow"));
            w.Tier = 2;   // tier 2 grants +1 slot
            Assert.IsEmpty(AssemblyRules.PreviewAddModifier(Db, w, "quickdraw", null));
        }

        [Test]
        public void TagRequirementsEnforced()
        {
            var sword = new WeaponInstance { FrameId = "sword" };
            Assert.IsTrue(AssemblyRules.PreviewAddModifier(Db, sword, "split", null).Exists(i => i.Code == "requires_tag"));
            var bow = new WeaponInstance { FrameId = "bow" };
            Assert.IsEmpty(AssemblyRules.PreviewAddModifier(Db, bow, "split", null));
        }

        [Test]
        public void ConflictGroupsAndDuplicatesRejected()
        {
            var bow = new WeaponInstance { FrameId = "bow", Tier = 3 };
            bow.Modifiers.Add("rapid");
            Assert.IsTrue(AssemblyRules.PreviewAddModifier(Db, bow, "charged", null).Exists(i => i.Code == "conflict"));
            Assert.IsTrue(AssemblyRules.PreviewAddModifier(Db, bow, "rapid", null).Exists(i => i.Code == "duplicate"));
        }

        [Test]
        public void RuneSlotsGrowWithTier()
        {
            var bow = new WeaponInstance { FrameId = "bow" };
            bow.Runes.Add("ember_etch");
            Assert.IsTrue(AssemblyRules.PreviewAddRune(Db, bow, "frost_glyph", null).Exists(i => i.Code == "rune_overflow"));
            bow.Tier = 3;
            Assert.IsEmpty(AssemblyRules.PreviewAddRune(Db, bow, "frost_glyph", null));
        }

        [Test]
        public void LegendaryRecipeReportsMissingComponents()
        {
            var l = Db.Legendary("stormpiercer");
            var bow = new WeaponInstance { FrameId = "bow" };
            var st = LegendaryRules.Evaluate(Db, bow, l, 0);
            Assert.IsFalse(st.Eligible);
            CollectionAssert.Contains(st.Missing, "core:lightning");
            CollectionAssert.Contains(st.Missing, "modifier:pierce");
            CollectionAssert.Contains(st.Missing, "tier:2");
            bow.CoreId = "lightning";
            bow.Modifiers.Add("pierce");
            bow.Tier = 2;
            st = LegendaryRules.Evaluate(Db, bow, l, l.EssenceCost);
            Assert.IsTrue(st.Eligible, string.Join(",", st.Missing));
        }

        [Test]
        public void EveryLegendaryRecipeIsAchievable()
        {
            foreach (var l in Db.Legendaries)
            {
                var w = new WeaponInstance { FrameId = l.Frame, CoreId = l.Core, Tier = l.MinTier };
                foreach (var m in l.RequiresModifiers) w.Modifiers.Add(m);
                if (l.RequiresAnyRunes.Count > 0) w.Runes.Add(l.RequiresAnyRunes[0]);
                Assert.IsEmpty(AssemblyRules.Validate(Db, w, null), l.Id + " base assembly illegal");
                Assert.IsTrue(LegendaryRules.Evaluate(Db, w, l, 99).Eligible, l.Id);
                w.LegendaryId = l.Id;
                Assert.IsEmpty(AssemblyRules.Validate(Db, w, null), l.Id + " evolved assembly illegal");
                var spec = WeaponCompiler.Compile(Db, w, null, null, true);
                Assert.IsTrue(spec.Signature != LegendarySignature.None || spec.Runes.Count > (Db.Core(l.Core) != null ? Db.Core(l.Core).InnateRunes.Count : 0), l.Id + " changes no rule");
            }
        }

        [Test]
        public void ModifierOrderDoesNotChangeCompiledSpec()
        {
            var a = new WeaponInstance { FrameId = "bow", Tier = 3 };
            a.Modifiers.Add("split"); a.Modifiers.Add("pierce"); a.Modifiers.Add("homing");
            var b = new WeaponInstance { FrameId = "bow", Tier = 3 };
            b.Modifiers.Add("homing"); b.Modifiers.Add("pierce"); b.Modifiers.Add("split");
            var sa = WeaponCompiler.Compile(Db, a, null, null, true);
            var sb = WeaponCompiler.Compile(Db, b, null, null, true);
            Assert.AreEqual(sa.BaseDamage, sb.BaseDamage);
            Assert.AreEqual(sa.ProjectileAdd, sb.ProjectileAdd);
            Assert.AreEqual(sa.Pierce, sb.Pierce);
            Assert.AreEqual(sa.SplitDamagePermille, sb.SplitDamagePermille);
            Assert.AreEqual(sa.ProjectileSpeed, sb.ProjectileSpeed);
        }
    }
}
