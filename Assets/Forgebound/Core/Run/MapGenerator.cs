using System;
using System.Collections.Generic;
using Forgebound.Core.Content;
using Forgebound.Core.Simulation;

namespace Forgebound.Core.Run
{
    /// <summary>
    /// Generates the 3-chapter branching route. Each chapter has 5 rows (= 15 meaningful steps per run):
    /// rows 0-3 offer 2-3 choices, row 4 is the chapter boss. Every row keeps at least one fight so the
    /// route can't skip all combat, and every node has 1-2 outgoing edges to nearby columns.
    /// Encounters/events are pre-rolled at generation so node previews are truthful.
    /// </summary>
    public static class MapGenerator
    {
        // Row templates: weights for node types per row (Battle, Elite, Shop, Forge, Rest, Event, Treasure)
        private static readonly NodeType[] TypeOrder = { NodeType.Battle, NodeType.Elite, NodeType.Shop, NodeType.Forge, NodeType.Rest, NodeType.Event, NodeType.Treasure };

        private static int[] RowWeights(int chapter, int row)
        {
            switch (row)
            {
                case 0: return chapter == 0 ? new[] { 100, 0, 0, 0, 0, 0, 0 } : new[] { 60, 0, 15, 0, 0, 25, 0 };
                case 1: return new[] { 35, 0, 25, 10, 0, 30, 0 };
                case 2: return new[] { 30, 25, 10, 15, 0, 20, 0 };
                case 3: return new[] { 20, 15, 20, 20, 20, 0, 5 };
                default: return new[] { 100, 0, 0, 0, 0, 0, 0 };
            }
        }

        public static List<string> PickBiomes(ContentDatabase db, DetRandom rng)
        {
            var result = new List<string>();
            for (int order = 1; order <= RunState.ChapterCount; order++)
            {
                var pool = new List<string>();
                foreach (var b in db.Biomes) if (b.Order == order) pool.Add(b.Id);
                if (pool.Count == 0) foreach (var b in db.Biomes) if (!result.Contains(b.Id)) pool.Add(b.Id);
                if (pool.Count == 0) { result.Add(null); continue; }
                result.Add(pool[rng.Next(pool.Count)]);
            }
            return result;
        }

        public static ChapterMap Generate(ContentDatabase db, int chapter, string biome, DetRandom rng)
        {
            var map = new ChapterMap { Chapter = chapter, Biome = biome };
            var rows = new List<List<MapNode>>();
            var usedEncounters = new HashSet<string>();
            var usedEvents = new HashSet<string>();
            for (int row = 0; row < RunState.RowsPerChapter; row++)
            {
                var list = new List<MapNode>();
                int width = row == RunState.RowsPerChapter - 1 ? 1 : (row == 0 ? 2 : 2 + rng.Next(2));
                for (int c = 0; c < width; c++)
                {
                    NodeType type;
                    if (row == RunState.RowsPerChapter - 1) type = NodeType.Boss;
                    else
                    {
                        int idx = rng.WeightedIndex(RowWeights(chapter, row));
                        type = TypeOrder[Math.Max(0, idx)];
                    }
                    list.Add(new MapNode { Id = "c" + chapter + "r" + row + "n" + c, Type = type, Chapter = chapter, Row = row, Column = c });
                }
                // Guarantee a fight option on every non-boss row, and no duplicate service types in one row.
                bool hasFight = false;
                foreach (var n in list) if (n.Type == NodeType.Battle || n.Type == NodeType.Elite || n.Type == NodeType.Boss) hasFight = true;
                if (!hasFight) list[0].Type = NodeType.Battle;
                var seen = new HashSet<NodeType>();
                foreach (var n in list)
                {
                    if (n.Type != NodeType.Battle && !seen.Add(n.Type)) n.Type = NodeType.Battle;
                }
                foreach (var n in list) Fill(db, n, biome, chapter, rng, usedEncounters, usedEvents);
                rows.Add(list);
                map.Nodes.AddRange(list);
            }
            // Guarantee services per chapter: at least one Shop (rows 1-2) and one Forge or Rest (row 3) so every
            // chapter offers a spending/crafting opportunity regardless of RNG.
            EnsureType(rows, new[] { 1, 2 }, NodeType.Shop, rng);
            if (!RowHas(rows[3], NodeType.Forge) && !RowHas(rows[3], NodeType.Rest)) EnsureType(rows, new[] { 3 }, chapter == 0 ? NodeType.Rest : NodeType.Forge, rng);
            if (chapter > 0) EnsureType(rows, new[] { 1, 2, 3 }, NodeType.Forge, rng);

            // Edges: each node links to the column-nearest nodes in the next row; every next-row node gets an incoming edge.
            for (int r = 0; r < rows.Count - 1; r++)
            {
                var cur = rows[r];
                var next = rows[r + 1];
                var hasIncoming = new bool[next.Count];
                for (int i = 0; i < cur.Count; i++)
                {
                    int a = cur.Count == 1 ? 0 : (int)Math.Round(i * (next.Count - 1) / (double)(cur.Count - 1));
                    cur[i].Next.Add(next[a].Id);
                    hasIncoming[a] = true;
                    int b = a + (rng.Next(2) == 0 ? -1 : 1);
                    if (b >= 0 && b < next.Count && b != a && rng.Next(100) < 55)
                    {
                        cur[i].Next.Add(next[b].Id);
                        hasIncoming[b] = true;
                    }
                }
                for (int j = 0; j < next.Count; j++)
                {
                    if (hasIncoming[j]) continue;
                    int src = Math.Min(cur.Count - 1, (int)Math.Round(j * (cur.Count - 1) / (double)Math.Max(1, next.Count - 1)));
                    cur[src].Next.Add(next[j].Id);
                }
                foreach (var n in cur) n.Next.Sort(StringComparer.Ordinal);
            }
            return map;
        }

        private static bool RowHas(List<MapNode> row, NodeType t)
        {
            foreach (var n in row) if (n.Type == t) return true;
            return false;
        }

        private static void EnsureType(List<List<MapNode>> rows, int[] candidateRows, NodeType t, DetRandom rng)
        {
            foreach (var r in candidateRows) if (RowHas(rows[r], t)) return;
            // Convert a node that keeps at least one fight in the row (rows always have >= 2 nodes before the boss).
            int ri = candidateRows[rng.Next(candidateRows.Length)];
            var row = rows[ri];
            MapNode pick = null;
            for (int i = row.Count - 1; i >= 0; i--)
            {
                var n = row[i];
                int fights = 0;
                foreach (var m in row) if (m != n && (m.Type == NodeType.Battle || m.Type == NodeType.Elite)) fights++;
                if (fights > 0 && n.Type != t) { pick = n; break; }
            }
            if (pick == null) return;
            pick.Type = t;
            pick.EncounterId = null;
            pick.EventId = null;
        }

        private static void Fill(ContentDatabase db, MapNode n, string biome, int chapter, DetRandom rng, HashSet<string> usedEnc, HashSet<string> usedEvt)
        {
            int ch = chapter + 1;
            if (n.Type == NodeType.Battle || n.Type == NodeType.Elite || n.Type == NodeType.Boss)
            {
                n.EncounterId = PickEncounter(db, n.Type, biome, ch, rng, usedEnc);
                if (n.EncounterId == null && n.Type == NodeType.Elite)
                {
                    n.Type = NodeType.Battle;
                    n.EncounterId = PickEncounter(db, n.Type, biome, ch, rng, usedEnc);
                }
            }
            else if (n.Type == NodeType.Event)
            {
                var pool = new List<EventDef>();
                var weights = new List<int>();
                foreach (var e in db.Events)
                {
                    if (usedEvt.Contains(e.Id) || e.MinChapter > ch) continue;
                    if (e.Biome != null && e.Biome != biome) continue;
                    pool.Add(e);
                    weights.Add(e.Biome == biome ? e.Weight * 2 : e.Weight);
                }
                if (pool.Count == 0) { n.Type = NodeType.Battle; n.EncounterId = PickEncounter(db, NodeType.Battle, biome, ch, rng, usedEnc); return; }
                var pick = pool[Math.Max(0, rng.WeightedIndex(weights.ToArray()))];
                n.EventId = pick.Id;
                usedEvt.Add(pick.Id);
            }
        }

        public static string PickEncounter(ContentDatabase db, NodeType kind, string biome, int chapter, DetRandom rng, HashSet<string> used)
        {
            var pool = new List<EncounterDef>();
            var weights = new List<int>();
            for (int pass = 0; pass < 2 && pool.Count == 0; pass++)
            {
                foreach (var e in db.Encounters)
                {
                    if (e.Kind != kind) continue;
                    if (chapter < e.MinChapter || chapter > e.MaxChapter) continue;
                    if (pass == 0 && e.Biome != null && e.Biome != biome) continue;
                    if (used != null && used.Contains(e.Id) && pass == 0) continue;
                    pool.Add(e);
                    weights.Add(Math.Max(1, e.Weight));
                }
            }
            if (pool.Count == 0) return null;
            var pick = pool[Math.Max(0, rng.WeightedIndex(weights.ToArray()))];
            if (used != null) used.Add(pick.Id);
            return pick.Id;
        }
    }
}
