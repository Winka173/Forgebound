using System.Collections.Generic;
using Forgebound.Core.Content;
using Forgebound.Core.Simulation;
using NUnit.Framework;

namespace Forgebound.Tests
{
    public class RandomTests
    {
        [Test]
        public void SameSeedSameSequence()
        {
            var a = new DetRandom(42);
            var b = new DetRandom(42);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void StateRoundTripContinuesSequence()
        {
            var a = new DetRandom(7);
            for (int i = 0; i < 17; i++) a.Next(100);
            var b = DetRandom.FromState(a.GetState());
            for (int i = 0; i < 200; i++) Assert.AreEqual(a.Next(1000), b.Next(1000));
        }

        [Test]
        public void NextIsUniformEnoughAndInRange()
        {
            var r = new DetRandom(99);
            var buckets = new int[10];
            for (int i = 0; i < 100000; i++)
            {
                int v = r.Next(10);
                Assert.That(v, Is.InRange(0, 9));
                buckets[v]++;
            }
            foreach (var b in buckets) Assert.That(b, Is.InRange(9400, 10600));
        }

        [Test]
        public void ChanceExtremes()
        {
            var r = new DetRandom(1);
            for (int i = 0; i < 100; i++) { Assert.IsFalse(r.Chance(0)); Assert.IsTrue(r.Chance(1000)); }
        }

        [Test]
        public void DerivedStreamsDiffer()
        {
            Assert.AreNotEqual(DetRandom.DeriveSeed(1, "shop"), DetRandom.DeriveSeed(1, "map"));
            Assert.AreEqual(DetRandom.DeriveSeed(1, "shop"), DetRandom.DeriveSeed(1, "shop"));
        }
    }

    public class JsonTests
    {
        [Test]
        public void RoundTripPreservesValuesAndOrder()
        {
            var o = JsonValue.Obj().Set("b", 2).Set("a", "x\"y\n").Set("c", true).Set("d", JsonValue.Arr().Add(JsonValue.Num(1.5)).Add(JsonValue.Null));
            var text = JsonWriter.Write(o, true);
            var back = JsonParser.Parse(text);
            Assert.AreEqual("b", back.Fields[0].Key);
            Assert.AreEqual(2, back.Int("b"));
            Assert.AreEqual("x\"y\n", back.Str("a"));
            Assert.IsTrue(back.BoolOf("c"));
            Assert.AreEqual(1.5, back["d"][0].AsDouble());
            Assert.IsTrue(back["d"][1].IsNull);
        }

        [Test]
        public void CommentsAndTrailingCommasAccepted()
        {
            var v = JsonParser.Parse("// header\n{ \"a\": [1,2,], }");
            Assert.AreEqual(2, v["a"].Count);
        }

        [TestCase("{")]
        [TestCase("{\"a\":}")]
        [TestCase("[1,2")]
        [TestCase("\"unterminated")]
        [TestCase("{\"a\":1} extra")]
        [TestCase("nul")]
        public void MalformedInputRejected(string text)
        {
            JsonValue v;
            string err;
            Assert.IsFalse(JsonParser.TryParse(text, out v, out err));
            Assert.IsNotNull(err);
        }

        [Test]
        public void DepthLimitPreventsStackOverflow()
        {
            var deep = new string('[', 500) + new string(']', 500);
            JsonValue v;
            string err;
            Assert.IsFalse(JsonParser.TryParse(deep, out v, out err));
        }

        [Test]
        public void UnitConversions()
        {
            var v = JsonParser.Parse("{\"t\":1.5,\"p\":1.25,\"m\":2.5}");
            Assert.AreEqual(45, v.Ticks("t"));
            Assert.AreEqual(1250, v.Permille("p"));
            Assert.AreEqual(2500, v.Milli("m"));
        }
    }

    public class ContentTests
    {
        [Test]
        public void ShippedContentLoadsWithoutErrors()
        {
            var db = TestContent.Db;
            Assert.IsEmpty(db.LoadErrors, string.Join("\n", db.LoadErrors));
        }

        [Test]
        public void ShippedContentPassesValidator()
        {
            var report = ContentValidator.Validate(TestContent.Db);
            var errors = new List<string>();
            foreach (var i in report.Issues) if (i.Severity == "error") errors.Add(i.ToString());
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void FullReleaseCountTargetsTracked()
        {
            var db = TestContent.Db;
            int enemies = 0, bosses = 0;
            foreach (var e in db.Enemies) { if (e.Tags.Contains("training")) continue; if (e.Rank == EnemyRank.Boss) bosses++; else enemies++; }
            int curses = 0;
            foreach (var b in db.Blessings) if (b.IsCurse) curses++;
            Assert.GreaterOrEqual(db.Forgemasters.Count, 8);
            Assert.GreaterOrEqual(db.Frames.Count, 12);
            Assert.GreaterOrEqual(db.Cores.Count, 12);
            Assert.GreaterOrEqual(db.Modifiers.Count, 40);
            Assert.GreaterOrEqual(db.Runes.Count, 50);
            Assert.GreaterOrEqual(db.Pieces.Count, 30);
            Assert.GreaterOrEqual(db.Legendaries.Count, 40);
            Assert.GreaterOrEqual(enemies, 40);
            Assert.GreaterOrEqual(bosses, 10);
            Assert.GreaterOrEqual(db.Biomes.Count, 5);
            Assert.GreaterOrEqual(db.Events.Count, 40);
            Assert.GreaterOrEqual(db.Blessings.Count, 100);
            Assert.GreaterOrEqual(curses, 30);
        }

        [Test]
        public void ValidatorCatchesBrokenContent()
        {
            var files = new Dictionary<string, string>();
            foreach (var n in ContentDatabase.FileNames) files[n] = "{\"version\":1,\"items\":[]}";
            files["frames"] = "{\"version\":1,\"items\":[{\"id\":\"x\",\"steps\":[{\"kind\":\"melee\"}],\"footprint\":[[0,0],[0,0]],\"ports\":[{\"cell\":5,\"dir\":\"east\"}]},{\"id\":\"x\",\"steps\":[],\"footprint\":[]}]}";
            files["modifiers"] = "{\"version\":1,\"items\":[{\"id\":\"m1\",\"requiresTags\":[\"nonexistent\"],\"ops\":[{\"op\":\"pierceAdd\",\"value\":1}]},{\"id\":\"m2\",\"requiresTags\":[\"nonexistent\"],\"ops\":[{\"op\":\"pierceAdd\",\"value\":1}]}]}";
            files["runes"] = "{\"version\":1,\"items\":[{\"id\":\"r1\",\"trigger\":\"onStatusApplied\",\"condition\":{\"statusIs\":\"regen\"},\"effects\":[{\"type\":\"damage\",\"power\":0}]}]}";
            files["enemies"] = "{\"version\":1,\"items\":[{\"id\":\"e1\",\"hp\":10,\"attack\":{\"damage\":1},\"abilities\":[{\"type\":\"summon\",\"enemy\":\"e1\",\"cap\":3}]}]}";
            var db = ContentDatabase.Load(new DictionaryContentSource(files));
            var report = ContentValidator.Validate(db);
            var codes = new HashSet<string>();
            foreach (var i in report.Issues) codes.Add(i.Code);
            foreach (var expected in new[] { "load", "footprint", "ports", "impossible", "clone", "dead_rune", "no_effect", "unbounded", "campaign" })
                Assert.IsTrue(codes.Contains(expected), "validator should report " + expected + "; got " + string.Join(",", codes));
        }

        [Test]
        public void ProcGraphFindsSyntheticLoop()
        {
            var files = new Dictionary<string, string>();
            foreach (var n in ContentDatabase.FileNames) files[n] = "{\"version\":1,\"items\":[]}";
            files["runes"] = "{\"version\":1,\"items\":[" +
                "{\"id\":\"a\",\"trigger\":\"onHit\",\"condition\":{\"allowFromProc\":true},\"effects\":[{\"type\":\"nova\",\"power\":0.5}]}," +
                "{\"id\":\"b\",\"trigger\":\"onKill\",\"condition\":{\"allowFromProc\":true},\"effects\":[{\"type\":\"chain\",\"power\":0.5,\"count\":2}]}]}";
            var db = ContentDatabase.Load(new DictionaryContentSource(files));
            var res = ProcGraphAnalyzer.Analyze(db);
            Assert.IsNotEmpty(res.Cycles);
            bool storm = false;
            foreach (var i in res.Issues) if (i.Code == "proc_storm") storm = true;
            Assert.IsTrue(storm);
        }
    }
}
