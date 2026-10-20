using System;
using System.Collections.Generic;
using Forgebound.Core.Content;

namespace Forgebound.Core.ForgeBoard
{
    public struct PortConnection
    {
        public int FromUid, FromPort, ToUid, ToPort;
        public Cell FromCell, ToCell;
        public PortDir Dir;
    }

    /// <summary>
    /// A directed signal route from a source weapon through zero or more relays to a destination weapon
    /// (or to a terminal relay such as a Capacitor). Compiled into runtime link behaviour for combat.
    /// </summary>
    public sealed class BoardLink
    {
        public int SourceUid;
        public int SourceWeaponUid;
        public readonly List<int> RelayUids = new List<int>();
        public int DestUid = -1;
        public int DestWeaponUid = -1;
        public bool Active = true;
        public string InactiveReason;   // capacity_exceeded, no_destination, ...
        public string Describe()
        {
            return "w" + SourceWeaponUid + " -> [" + string.Join(",", RelayUids) + "] -> " + (DestWeaponUid >= 0 ? "w" + DestWeaponUid : "terminal") + (Active ? "" : " (inactive: " + InactiveReason + ")");
        }
    }

    public sealed class BoardIssue
    {
        public string Code;      // overlap, out_of_bounds, dead_relay, unconnected_port, capacity_exceeded, unknown_piece
        public int Uid;
        public string Detail;
        public override string ToString() { return Code + "#" + Uid + (Detail != null ? " " + Detail : ""); }
    }

    public sealed class BoardAnalysis
    {
        public readonly List<PortConnection> Connections = new List<PortConnection>();
        public readonly List<BoardLink> Links = new List<BoardLink>();
        public readonly List<BoardIssue> Issues = new List<BoardIssue>();
        /// <summary>Support piece uid -> list of weapon uids (WeaponUid) it buffs via edge adjacency.</summary>
        public readonly Dictionary<int, List<int>> SupportTargets = new Dictionary<int, List<int>>();
        public readonly Dictionary<int, int> RelayLoad = new Dictionary<int, int>();
        public bool IsValid;

        public int ActiveLinkCount
        {
            get { int n = 0; foreach (var l in Links) if (l.Active) n++; return n; }
        }

        public bool HasIssue(string code)
        {
            foreach (var i in Issues) if (i.Code == code) return true;
            return false;
        }
    }

    public static class BoardAnalyzer
    {
        public const int MaxRelaysPerPath = 6;

        public static int[] BuildOccupancy(BoardState board, IPieceShapeResolver shapes, int ignoreUid)
        {
            var occ = new int[board.Width * board.Height];
            foreach (var p in board.Pieces)
            {
                if (p.Uid == ignoreUid) continue;
                List<Cell> cells; List<PortDef> ports;
                if (!shapes.TryGetShape(p, out cells, out ports)) continue;
                foreach (var c in BoardGeometry.WorldCells(cells, p))
                {
                    if (c.X < 0 || c.Y < 0 || c.X >= board.Width || c.Y >= board.Height) continue;
                    if (occ[c.Y * board.Width + c.X] == 0) occ[c.Y * board.Width + c.X] = p.Uid;
                }
            }
            return occ;
        }

        private static bool CanOut(PortKind k) { return k == PortKind.Out || k == PortKind.Both; }
        private static bool CanIn(PortKind k) { return k == PortKind.In || k == PortKind.Both; }

        /// <summary>Fully analyse a board: geometry validity, port connections, signal links, relay capacity, support adjacency.</summary>
        public static BoardAnalysis Analyze(BoardState board, IPieceShapeResolver shapes, int relayCapacityBonus = 0)
        {
            var a = new BoardAnalysis();
            int w = board.Width, h = board.Height;
            var occ = new int[w * h];
            var pieceCells = new Dictionary<int, List<Cell>>();
            var piecePorts = new Dictionary<int, List<WorldPort>>();
            var pieces = new List<PlacedPiece>(board.Pieces);
            pieces.Sort((x, y) => x.Uid.CompareTo(y.Uid));   // deterministic order regardless of list order
            var byUid = new Dictionary<int, PlacedPiece>();

            foreach (var p in pieces)
            {
                byUid[p.Uid] = p;
                List<Cell> cells; List<PortDef> ports;
                if (!shapes.TryGetShape(p, out cells, out ports))
                {
                    a.Issues.Add(new BoardIssue { Code = "unknown_piece", Uid = p.Uid });
                    continue;
                }
                var wc = BoardGeometry.WorldCells(cells, p);
                pieceCells[p.Uid] = wc;
                piecePorts[p.Uid] = BoardGeometry.WorldPorts(cells, ports, p);
                foreach (var c in wc)
                {
                    if (c.X < 0 || c.Y < 0 || c.X >= w || c.Y >= h)
                    {
                        a.Issues.Add(new BoardIssue { Code = "out_of_bounds", Uid = p.Uid, Detail = c.ToString() });
                        continue;
                    }
                    int idx = c.Y * w + c.X;
                    if (occ[idx] != 0) a.Issues.Add(new BoardIssue { Code = "overlap", Uid = p.Uid, Detail = "with #" + occ[idx] + " at " + c });
                    else occ[idx] = p.Uid;
                }
            }

            // Port connections (deterministic: pieces by uid, ports by index).
            var outEdges = new Dictionary<int, List<PortConnection>>();
            foreach (var p in pieces)
            {
                List<WorldPort> ports;
                if (!piecePorts.TryGetValue(p.Uid, out ports)) continue;
                foreach (var port in ports)
                {
                    if (!CanOut(port.Kind)) continue;
                    int nx = port.Cell.X + BoardGeometry.DX[(int)port.Dir];
                    int ny = port.Cell.Y + BoardGeometry.DY[(int)port.Dir];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int other = occ[ny * w + nx];
                    if (other == 0 || other == p.Uid) continue;
                    List<WorldPort> otherPorts;
                    if (!piecePorts.TryGetValue(other, out otherPorts)) continue;
                    foreach (var op in otherPorts)
                    {
                        if (op.Cell.X == nx && op.Cell.Y == ny && op.Dir == BoardGeometry.Opposite(port.Dir) && CanIn(op.Kind))
                        {
                            var conn = new PortConnection { FromUid = p.Uid, FromPort = port.PortIndex, ToUid = other, ToPort = op.PortIndex, FromCell = port.Cell, ToCell = op.Cell, Dir = port.Dir };
                            a.Connections.Add(conn);
                            List<PortConnection> list;
                            if (!outEdges.TryGetValue(p.Uid, out list)) { list = new List<PortConnection>(); outEdges[p.Uid] = list; }
                            list.Add(conn);
                            break;
                        }
                    }
                }
            }

            // Signal links from each weapon source.
            foreach (var p in pieces)
            {
                if (p.Kind != PieceKind.Weapon || !pieceCells.ContainsKey(p.Uid)) continue;
                var path = new List<int>();
                var visited = new HashSet<int> { p.Uid };
                Explore(p, p.Uid, outEdges, byUid, shapes, path, visited, a);
            }

            // Relay capacity (first-come in deterministic link order).
            foreach (var link in a.Links)
            {
                if (!link.Active) continue;
                foreach (var r in link.RelayUids)
                {
                    var relay = shapes.GetRelay(byUid[r]);
                    int cap = relay == null ? 1 : relay.Capacity + relayCapacityBonus;
                    int load;
                    a.RelayLoad.TryGetValue(r, out load);
                    if (load >= cap)
                    {
                        link.Active = false;
                        link.InactiveReason = "capacity_exceeded";
                        a.Issues.Add(new BoardIssue { Code = "capacity_exceeded", Uid = r, Detail = link.Describe() });
                        break;
                    }
                }
                if (!link.Active) continue;
                foreach (var r in link.RelayUids)
                {
                    int load;
                    a.RelayLoad.TryGetValue(r, out load);
                    a.RelayLoad[r] = load + 1;
                }
            }

            // Relays that ended up on no active link are dead (shown to the player; they never apply effects).
            foreach (var p in pieces)
            {
                if (p.Kind != PieceKind.Relay) continue;
                int load;
                if (!a.RelayLoad.TryGetValue(p.Uid, out load) || load == 0)
                    a.Issues.Add(new BoardIssue { Code = "dead_relay", Uid = p.Uid, Detail = p.PieceId });
            }

            // Support adjacency (edge-sharing cells).
            foreach (var p in pieces)
            {
                if (p.Kind != PieceKind.Support || !pieceCells.ContainsKey(p.Uid)) continue;
                var def = shapes.GetPieceDef(p);
                int maxAdj = def == null ? 4 : def.MaxAdjacent;
                var targets = new List<int>();
                var seen = new HashSet<int>();
                foreach (var c in pieceCells[p.Uid])
                {
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = c.X + BoardGeometry.DX[d], ny = c.Y + BoardGeometry.DY[d];
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int other = occ[ny * w + nx];
                        if (other == 0 || other == p.Uid || seen.Contains(other)) continue;
                        var op = byUid[other];
                        if (op.Kind != PieceKind.Weapon) continue;
                        seen.Add(other);
                        if (targets.Count < maxAdj) targets.Add(op.WeaponUid);
                    }
                }
                targets.Sort();
                a.SupportTargets[p.Uid] = targets;
                if (targets.Count == 0) a.Issues.Add(new BoardIssue { Code = "idle_support", Uid = p.Uid, Detail = p.PieceId });
            }

            a.IsValid = !a.HasIssue("overlap") && !a.HasIssue("out_of_bounds") && !a.HasIssue("unknown_piece");
            return a;
        }

        private static bool IsTerminalRelay(RelayDef r)
        {
            return r != null && (r.Type == RelayType.Capacitor || r.Type == RelayType.Siphon || r.Type == RelayType.Beacon);
        }

        private static void Explore(PlacedPiece source, int currentUid, Dictionary<int, List<PortConnection>> outEdges,
            Dictionary<int, PlacedPiece> byUid, IPieceShapeResolver shapes, List<int> path, HashSet<int> visited, BoardAnalysis a)
        {
            List<PortConnection> edges;
            if (!outEdges.TryGetValue(currentUid, out edges)) edges = new List<PortConnection>();
            var current = byUid[currentUid];
            var currentRelay = current.Kind == PieceKind.Relay ? shapes.GetRelay(current) : null;
            bool fanOut = current.Kind == PieceKind.Weapon || (currentRelay != null && currentRelay.Type == RelayType.Splitter);
            bool extended = false;

            foreach (var e in edges)
            {
                if (visited.Contains(e.ToUid)) continue;  // cycle guard: never revisit a node on the same path
                var next = byUid[e.ToUid];
                if (next.Kind == PieceKind.Support) continue;
                if (next.Kind == PieceKind.Weapon)
                {
                    var link = new BoardLink { SourceUid = source.Uid, SourceWeaponUid = source.WeaponUid, DestUid = next.Uid, DestWeaponUid = next.WeaponUid };
                    link.RelayUids.AddRange(path);
                    a.Links.Add(link);
                    extended = true;
                }
                else if (next.Kind == PieceKind.Relay)
                {
                    if (path.Count >= MaxRelaysPerPath) continue;
                    path.Add(next.Uid);
                    visited.Add(next.Uid);
                    Explore(source, next.Uid, outEdges, byUid, shapes, path, visited, a);
                    visited.Remove(next.Uid);
                    path.RemoveAt(path.Count - 1);
                    extended = true;
                }
                if (extended && !fanOut) break;   // non-splitter relays forward along their first valid output only
            }

            if (!extended && current.Kind == PieceKind.Relay)
            {
                var link = new BoardLink { SourceUid = source.Uid, SourceWeaponUid = source.WeaponUid };
                link.RelayUids.AddRange(path);
                if (IsTerminalRelay(currentRelay)) a.Links.Add(link);
                else
                {
                    link.Active = false;
                    link.InactiveReason = "no_destination";
                    a.Links.Add(link);
                }
            }
        }
    }
}
