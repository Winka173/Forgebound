using Forgebound.Core.Balance;
using Forgebound.Core.Run;
using NUnit.Framework;

namespace Forgebound.Tests
{
    public class AdversarialScenarioTests
    {
        [Test]
        public void AllSpecScenariosHoldInvariants()
        {
            var failures = new System.Collections.Generic.List<string>();
            foreach (var r in Scenarios.RunAll(TestContent.Db)) if (!r.Passed) failures.Add(r.ToString());
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void BoardUndoRedoIsReplaySafe()
        {
            var c = RunController.StartNew(TestContent.Db, new StartOptions { ForgemasterId = "runecrafter", Seed = 5 });
            var relay = c.Run.Inventory.Find(i => i.Id == "echo_relay");
            Assert.IsTrue(c.PlacePiece(relay.Uid, 3, 1, 0).Ok);
            Assert.IsTrue(c.UndoBoard().Ok);
            Assert.IsNotNull(c.Run.Inventory.Find(i => i.Id == "echo_relay"), "undo returns the piece to inventory");
            Assert.IsTrue(c.RedoBoard().Ok);
            Assert.IsNull(c.Run.Inventory.Find(i => i.Id == "echo_relay"));
            var replay = RunController.Replay(TestContent.Db, c.Run.ActionLog);
            Assert.AreEqual(c.Run.StateHash(), replay.Run.StateHash());
        }
    }
}
