using System.Collections.Generic;
using Forgebound.Core.Balance;
using Forgebound.Core.Content;
using Forgebound.Core.Economy;
using Forgebound.Core.Progression;
using Forgebound.Core.Run;
using Forgebound.Core.Save;
using Forgebound.Core.Weapons;
using NUnit.Framework;

namespace Forgebound.Tests
{
    public class MapAndRunTests
    {
        private ContentDatabase Db { get { return TestContent.Db; } }

        [Test]
        public void EveryForgemasterStartsAValidRun()
        {
            foreach (var fm in Db.Forgemasters)
            {
                var c = RunController.StartNew(Db, new StartOptions { ForgemasterId = fm.Id, Seed = 11 });
                Assert.AreEqual(3, c.Run.Maps.Count);
                Assert.Greater(c.Run.Hp, 0);
                Assert.AreEqual(1, c.Run.Weapons.Count, fm.Id);
                Assert.IsNotNull(c.Run.Board.FindWeapon(c.Run.Weapons[0].Uid));
                Assert.IsNotEmpty(c.AvailableNodes());
            }
        }

        [Test]
        public void MapHasFifteenStepsAndEveryNodeReachable()
        {
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var c = RunController.StartNew(Db, new StartOptions { ForgemasterId = "blacksmith", Seed = seed });
                foreach (var m in c.Run.Maps)
                {
                    var rows = new HashSet<int>();
                    foreach (var n in m.Nodes) rows.Add(n.Row);
                    Assert.AreEqual(RunState.RowsPerChapter, rows.Count);
                    var reach = new HashSet<string>();
                    var frontier = new List<string>();
                    foreach (var n in m.Nodes) if (n.Row == 0) { reach.Add(n.Id); frontier.Add(n.Id); }
                    while (frontier.Count > 0)
                    {
                        var id = frontier[frontier.Count - 1];
                        frontier.RemoveAt(frontier.Count - 1);
                        foreach (var nx in c.Run.FindNode(id).Next) if (reach.Add(nx)) frontier.Add(nx);
                    }
                    Assert.AreEqual(m.Nodes.Count, reach.Count, "unreachable node in seed " + seed);
                    foreach (var n in m.Nodes)
                    {
                        if (n.Type == NodeType.Battle || n.Type == NodeType.Elite || n.Type == NodeType.Boss) Assert.IsNotNull(Db.Encounter(n.EncounterId), n.Id);
                        if (n.Type == NodeType.Event) Assert.IsNotNull(Db.Event(n.EventId), n.Id);
                        if (n.Row < RunState.RowsPerChapter - 1) Assert.IsNotEmpty(n.Next);
                    }
                }
            }
        }

        [Test]
        public void SameSeedSameMap_DifferentSeedDifferentMap()
        {
            var a = RunController.StartNew(Db, new StartOptions { ForgemasterId = "alchemist", Seed = 5 });
            var b = RunController.StartNew(Db, new StartOptions { ForgemasterId = "alchemist", Seed = 5 });
            var c = RunController.StartNew(Db, new StartOptions { ForgemasterId = "alchemist", Seed = 6 });
            Assert.AreEqual(a.Run.StateHash(), b.Run.StateHash());
            Assert.AreNotEqual(a.Run.StateHash(), c.Run.StateHash());
        }

        [Test]
        public void BotRunsFinishAndReplayDeterministically()
        {
            foreach (var fm in new[] { "blacksmith", "runecrafter", "alchemist", "artificer" })
            {
                var c = RunController.StartNew(Db, new StartOptions { ForgemasterId = fm, Seed = 21 });
                new BotAgent(c, BotPolicy.Balanced, 21).PlayToEnd();
                Assert.AreEqual(RunPhase.Finished, c.Run.Phase, fm);
                Assert.AreNotEqual(RunOutcome.Abandoned, c.Run.Outcome, fm + " bot abandoned: " + c.Run.DeathReason);
                var replay = RunController.Replay(Db, c.Run.ActionLog);
                Assert.AreEqual(c.Run.StateHash(), replay.Run.StateHash(), fm + " replay diverged");
            }
        }

        [Test]
        public void SaveLoadMidRunContinuesIdentically()
        {
            var c = RunController.StartNew(Db, new StartOptions { ForgemasterId = "artificer", Seed = 33 });
            var nodes = c.AvailableNodes();
            c.ChooseNode(nodes[0].Id);
            if (c.Run.Phase == RunPhase.PreBattle) c.Fight();
            // save
            var fs = new MemoryFileStore();
            var save = new SaveSystem(fs);
            save.Save("run.sav", "run", c.Run.ToJson());
            var loaded = save.Load("run.sav", "run");
            Assert.AreEqual(LoadStatus.Ok, loaded.Status);
            var restored = new RunController(Db, RunState.FromJson(loaded.Data));
            Assert.AreEqual(c.Run.StateHash(), restored.Run.StateHash());
            // Continue both with the same bot policy: identical outcome.
            new BotAgent(c, BotPolicy.Balanced, 1).PlayToEnd();
            new BotAgent(restored, BotPolicy.Balanced, 1).PlayToEnd();
            Assert.AreEqual(c.Run.StateHash(), restored.Run.StateHash());
        }

        [Test]
        public void ActionsRejectedInWrongPhase()
        {
            var c = RunController.StartNew(Db, new StartOptions { ForgemasterId = "blacksmith", Seed = 2 });
            Assert.AreEqual(ActionError.WrongPhase, c.Buy(0).Error);
            Assert.AreEqual(ActionError.WrongPhase, c.Reroll().Error);
            Assert.AreEqual(ActionError.WrongPhase, c.ForgeUpgrade(1).Error);
            Assert.AreEqual(ActionError.WrongPhase, c.TakeReward(0).Error);
            Assert.AreEqual(ActionError.NotFound, c.ChooseNode("nope").Error);
            int logged = c.Run.ActionLog.Count;
            Assert.AreEqual(1, logged, "failed actions must not be logged");
        }

        [Test]
        public void ItemMovementAfterBattleStartDoesNotAffectRunningSim()
        {
            var c = RunController.StartNew(Db, new StartOptions { ForgemasterId = "blacksmith", Seed = 3 });
            c.ChooseNode(c.AvailableNodes()[0].Id);
            var battle = c.CreateBattle(true);
            Assert.IsNotNull(battle);
            for (int i = 0; i < 30; i++) battle.Step();
            // Mutate the run loadout mid-battle (e.g. a UI bug): the compiled battle config must be unaffected.
            c.Run.Weapons[0].Modifiers.Add("rapid");
            c.Run.Board.Pieces.Clear();
            var reference = c.CreateBattle(true);   // can't create: no weapon on board
            Assert.IsNull(reference);
            while (!battle.Finished) battle.Step();
            Assert.IsNotNull(battle.Result);
        }
    }

    public class EconomyTests
    {
        private ContentDatabase Db { get { return TestContent.Db; } }

        [Test]
        public void RerollCostEscalatesAndCaps()
        {
            Assert.AreEqual(EconomyRules.RerollBase, EconomyRules.RerollCost(0, 0));
            Assert.Greater(EconomyRules.RerollCost(1, 0), EconomyRules.RerollCost(0, 0));
            Assert.AreEqual(EconomyRules.RerollCap, EconomyRules.RerollCost(50, 0));
        }

        [Test]
        public void SellValueIsHalfCostAndPricesPositive()
        {
            foreach (var m in Db.Modifiers)
            {
                Assert.AreEqual(m.Cost / 2, EconomyRules.SellValue(Db, ItemCategory.Modifier, m.Id));
                Assert.Greater(EconomyRules.Price(Db, ItemCategory.Modifier, m.Id, 0), EconomyRules.SellValue(Db, ItemCategory.Modifier, m.Id));
            }
        }

        private RunController ShopRun(ulong seed)
        {
            var c = RunController.StartNew(Db, new StartOptions { ForgemasterId = "blacksmith", Seed = seed });
            c.Run.Phase = RunPhase.InShop;   // test harness: open a shop directly
            c.Run.Gold = 500;
            return c;
        }

        [Test]
        public void LockedOffersSurviveReroll()
        {
            var c = ShopRun(4);
            typeof(RunController).GetMethod("GenerateShop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(c, null);
            Assert.GreaterOrEqual(c.Run.ShopOffers.Count, EconomyRules.BaseShopOffers);
            var locked = c.Run.ShopOffers[2];
            Assert.IsTrue(c.ToggleLock(2).Ok);
            int gold = c.Run.Gold;
            Assert.IsTrue(c.Reroll().Ok);
            Assert.AreEqual(gold - EconomyRules.RerollBase, c.Run.Gold);
            Assert.IsTrue(c.Run.ShopOffers.Exists(o => o.Locked && o.Id == locked.Id && o.Category == locked.Category));
        }

        [Test]
        public void BuyChecksGoldAndInventoryCap()
        {
            var c = ShopRun(5);
            typeof(RunController).GetMethod("GenerateShop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(c, null);
            c.Run.Gold = 0;
            Assert.AreEqual(ActionError.NotEnoughGold, c.Buy(0).Error);
            c.Run.Gold = 1000;
            while (c.Run.Inventory.Count < RunState.InventoryCap) c.AddItem(ItemCategory.Rune, "ember_etch");
            Assert.AreEqual(ActionError.InventoryFull, c.Buy(0).Error);
        }

        [Test]
        public void PityGuaranteesCoreWhenWeaponCoreless()
        {
            for (ulong seed = 1; seed < 30; seed++)
            {
                var c = RunController.StartNew(Db, new StartOptions { ForgemasterId = "blacksmith", Seed = seed });   // hammer, no core
                var offers = ShopGenerator.Generate(Db, c.Run, c.Rng("shop"), 6, 0, null);
                Assert.IsTrue(offers.Exists(o => o.Category == ItemCategory.Core), "seed " + seed);
                Assert.IsTrue(offers.Exists(o => o.Category == ItemCategory.Frame), "seed " + seed);
            }
        }
    }

    public class SaveSystemTests
    {
        [Test]
        public void CorruptMainFallsBackToBackup()
        {
            var fs = new MemoryFileStore();
            var s = new SaveSystem(fs);
            s.Save("p.sav", "profile", JsonValue.Obj().Set("v", 1));
            s.Save("p.sav", "profile", JsonValue.Obj().Set("v", 2));
            fs.Files["p.sav"] = "{garbage";
            var r = s.Load("p.sav", "profile");
            Assert.AreEqual(LoadStatus.RecoveredFromBackup, r.Status);
            Assert.AreEqual(1, r.Data.Int("v"));
        }

        [Test]
        public void TamperedChecksumDetected()
        {
            var fs = new MemoryFileStore();
            var s = new SaveSystem(fs);
            s.Save("p.sav", "profile", JsonValue.Obj().Set("gold", 10));
            fs.Files["p.sav"] = fs.Files["p.sav"].Replace("\"gold\":10", "\"gold\":99999");
            var r = s.Load("p.sav", "profile");
            Assert.AreNotEqual(LoadStatus.Ok, r.Status);
        }

        [Test]
        public void CrashDuringWriteKeepsPreviousSave()
        {
            var fs = new MemoryFileStore();
            var s = new SaveSystem(fs);
            s.Save("r.sav", "run", JsonValue.Obj().Set("step", 3));
            fs.FailNextWrite = true;
            Assert.Throws<System.IO.IOException>(() => s.Save("r.sav", "run", JsonValue.Obj().Set("step", 4)));
            var r = s.Load("r.sav", "run");
            Assert.AreEqual(LoadStatus.Ok, r.Status);
            Assert.AreEqual(3, r.Data.Int("step"));
        }

        [Test]
        public void MigrationRunsForOldVersions()
        {
            var fs = new MemoryFileStore();
            var payload = JsonValue.Obj().Set("old", 1);
            string body = JsonWriter.Write(payload, false);
            fs.Files["x.sav"] = JsonWriter.Write(JsonValue.Obj().Set("kind", "profile").Set("saveVersion", 0).Set("checksum", SaveSystem.Checksum(body)).Set("payload", payload), false);
            var s = new SaveSystem(fs);
            s.AddMigration(0, p => p.Set("migrated", true));
            var r = s.Load("x.sav", "profile");
            Assert.AreEqual(LoadStatus.Migrated, r.Status);
            Assert.IsTrue(r.Data.BoolOf("migrated"));
        }

        [Test]
        public void WrongKindAndNewerVersionRejected()
        {
            var fs = new MemoryFileStore();
            var s = new SaveSystem(fs);
            s.Save("x.sav", "run", JsonValue.Obj());
            Assert.AreEqual(LoadStatus.Corrupt, s.Load("x.sav", "profile").Status);
        }

        [Test]
        public void CorruptRunJsonNeverThrows()
        {
            var bad = JsonParser.Parse("{\"board\":{\"w\":999,\"h\":-5,\"pieces\":[{\"kind\":\"Banana\"},{\"kind\":\"Relay\",\"rot\":-7}]},\"weapons\":[{}],\"rng\":{\"map\":[1]}}");
            RunState r = null;
            Assert.DoesNotThrow(() => r = RunState.FromJson(bad));
            Assert.LessOrEqual(r.Board.Width, 7);
            Assert.GreaterOrEqual(r.Board.Height, 3);
            foreach (var p in r.Board.Pieces) Assert.That(p.Rotation, Is.InRange(0, 3));
        }
    }

    public class WorkshopTests
    {
        private ContentDatabase Db { get { return TestContent.Db; } }

        [Test]
        public void ProfileRecordsKnowledgeNotPower()
        {
            var profile = Profile.CreateNew(Db);
            Assert.IsTrue(profile.IsUnlocked("blacksmith"));
            Assert.IsFalse(profile.IsUnlocked("hexblade"));
            var c = RunController.StartNew(Db, new StartOptions { ForgemasterId = "blacksmith", Seed = 8 });
            new BotAgent(c, BotPolicy.Balanced, 8).PlayToEnd();
            profile.RecordRun(Db, c.Run, "2026-10-20");
            Assert.AreEqual(1, profile.Runs);
            Assert.IsNotEmpty(profile.Discovered);
            Assert.AreEqual(1, profile.History.Count);
            Assert.IsTrue(profile.Achievements.Contains("first_run"));
        }

        [Test]
        public void SandboxNeverLeaksIntoAdventure()
        {
            var profile = Profile.CreateNew(Db);
            profile.Discovered.Add("blueprint:greatsword|fire");
            profile.Discovered.Add("item:Modifier:explosion");
            var ws = new WorkshopSession(Db, profile, "blacksmith");
            string err;
            var w = ws.CreateWeapon("greatsword", "fire", out err);
            Assert.IsNotNull(w, err);
            Assert.IsNull(ws.AddModifier(w, "explosion"));
            Assert.AreEqual("locked", ws.AddModifier(w, "split"));
            ws.SaveDesign(w, "My Inferno");
            var run = RunController.StartNew(Db, new StartOptions { ForgemasterId = "blacksmith", Seed = 1, FavoriteRecipe = "infernal_sovereign" });
            Assert.AreEqual(1, run.Run.Weapons.Count);
            Assert.AreEqual("hammer", run.Run.Weapons[0].FrameId);
            Assert.IsEmpty(run.Run.Weapons[0].Modifiers);
            foreach (var it in run.Run.Inventory) Assert.AreNotEqual("explosion", it.Id);
        }

        [Test]
        public void TrainingChamberMeasuresRealSimulation()
        {
            var profile = Profile.CreateNew(Db);
            profile.Discovered.Add("blueprint:sword|fire");
            var ws = new WorkshopSession(Db, profile, "blacksmith");
            string err;
            var w = ws.CreateWeapon("sword", "fire", out err);
            ws.PlaceWeapon(w, 0, 0, 0);
            var p = ws.Train("training_single", 10);
            Assert.Greater(p.TotalDamage, 0);
            Assert.AreEqual(p.TotalDamage, p.WeaponDamage[0]);
        }

        [Test]
        public void TrialRewardsCosmeticOnlyOnWin()
        {
            var profile = Profile.CreateNew(Db);
            var c = Db.Challenge("trial_first_forge");
            var s = Trials.Begin(Db, profile, c);
            Assert.IsNotEmpty(s.Weapons);
            var r = Trials.Fight(Db, profile, c, s, 1);
            Assert.AreEqual(r.Victory, profile.TrialsCompleted.Contains(c.Id));
            Assert.AreEqual(r.Victory, profile.Cosmetics.Contains(c.RewardCosmetic));
        }

        [Test]
        public void BlueprintExportImportRoundTripAndRejectsTampering()
        {
            var w = new WeaponInstance { FrameId = "bow", CoreId = "fire", Tier = 2 };
            w.Modifiers.Add("split");
            var code = BlueprintCodec.Export(w);
            List<string> errors;
            var back = BlueprintCodec.Import(Db, code, out errors);
            Assert.IsNotNull(back, string.Join(",", errors));
            Assert.AreEqual("bow", back.FrameId);
            Assert.IsNull(BlueprintCodec.Import(Db, code.Replace("split", "pierce"), out errors));
            Assert.Contains("checksum", errors);
            var evil = new WeaponInstance { FrameId = "sword", Tier = 1 };
            for (int i = 0; i < 10; i++) evil.Modifiers.Add("rapid");
            Assert.IsNull(BlueprintCodec.Import(Db, BlueprintCodec.Export(evil), out errors));
            Assert.IsNull(BlueprintCodec.Import(Db, "FBP1:xx:" + new string('a', 5000), out errors));
        }

        [Test]
        public void ProfileRoundTripsThroughSave()
        {
            var p = Profile.CreateNew(Db);
            p.Settings.Language = "vi";
            p.Settings.TextScalePercent = 130;
            p.Achievements.Add("first_run");
            p.Designs.Add(new SavedDesign { Name = "x", Weapon = new WeaponInstance { FrameId = "sword", Tier = 1 } });
            var fs = new MemoryFileStore();
            var save = new SaveSystem(fs);
            save.Save("profile.sav", "profile", p.ToJson());
            var back = Profile.FromJson(Db, save.Load("profile.sav", "profile").Data);
            Assert.AreEqual("vi", back.Settings.Language);
            Assert.AreEqual(130, back.Settings.TextScalePercent);
            Assert.IsTrue(back.Achievements.Contains("first_run"));
            Assert.AreEqual(1, back.Designs.Count);
        }
    }
}
