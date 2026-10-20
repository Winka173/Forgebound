using System;
using System.Collections.Generic;
using Forgebound.Core.Content;

namespace Forgebound.Core.ForgeBoard
{
    /// <summary>A piece placed on the Forge Board. Weapons reference a run weapon by uid; relays/supports reference a piece def id.</summary>
    public sealed class PlacedPiece
    {
        public int Uid;
        public PieceKind Kind;
        public string PieceId;      // for relay/support
        public int WeaponUid = -1;  // for weapon pieces
        public Cell Origin;
        public int Rotation;        // 0..3, clockwise quarter turns

        public PlacedPiece Clone()
        {
            return new PlacedPiece { Uid = Uid, Kind = Kind, PieceId = PieceId, WeaponUid = WeaponUid, Origin = Origin, Rotation = Rotation };
        }
    }

    /// <summary>Resolves the base (unrotated) shape for a placed piece.</summary>
    public interface IPieceShapeResolver
    {
        bool TryGetShape(PlacedPiece piece, out List<Cell> cells, out List<PortDef> ports);
        RelayDef GetRelay(PlacedPiece piece);
        BoardPieceDef GetPieceDef(PlacedPiece piece);
    }

    public struct WorldPort
    {
        public Cell Cell;
        public PortDir Dir;
        public PortKind Kind;
        public int PortIndex;
    }

    public static class BoardGeometry
    {
        public static readonly int[] DX = { 0, 1, 0, -1 };
        public static readonly int[] DY = { -1, 0, 1, 0 };   // y grows downward (row index)

        public static PortDir Rotate(PortDir d, int rot) { return (PortDir)(((int)d + ((rot % 4) + 4) % 4) % 4); }
        public static PortDir Opposite(PortDir d) { return (PortDir)(((int)d + 2) % 4); }

        /// <summary>Rotate cells clockwise `rot` quarter turns and normalise to a non-negative bounding box.</summary>
        public static List<Cell> RotateCells(List<Cell> cells, int rot)
        {
            rot = ((rot % 4) + 4) % 4;
            var result = new List<Cell>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                int x = cells[i].X, y = cells[i].Y;
                for (int r = 0; r < rot; r++) { int nx = -y; int ny = x; x = nx; y = ny; }
                result.Add(new Cell(x, y));
            }
            int minX = int.MaxValue, minY = int.MaxValue;
            for (int i = 0; i < result.Count; i++) { minX = Math.Min(minX, result[i].X); minY = Math.Min(minY, result[i].Y); }
            for (int i = 0; i < result.Count; i++) result[i] = new Cell(result[i].X - minX, result[i].Y - minY);
            return result;
        }

        public static List<Cell> WorldCells(List<Cell> baseCells, PlacedPiece p)
        {
            var rotated = RotateCells(baseCells, p.Rotation);
            for (int i = 0; i < rotated.Count; i++) rotated[i] = new Cell(rotated[i].X + p.Origin.X, rotated[i].Y + p.Origin.Y);
            return rotated;
        }

        public static List<WorldPort> WorldPorts(List<Cell> baseCells, List<PortDef> ports, PlacedPiece p)
        {
            var cells = WorldCells(baseCells, p);
            var list = new List<WorldPort>(ports.Count);
            for (int i = 0; i < ports.Count; i++)
            {
                var pd = ports[i];
                if (pd.CellIndex < 0 || pd.CellIndex >= cells.Count) continue;
                list.Add(new WorldPort { Cell = cells[pd.CellIndex], Dir = Rotate(pd.Dir, p.Rotation), Kind = pd.Kind, PortIndex = i });
            }
            return list;
        }
    }

    public sealed class BoardState
    {
        public const int MinWidth = 5, MinHeight = 4, MaxWidth = 7, MaxHeight = 5, AbsoluteMin = 3;

        public int Width = MinWidth;
        public int Height = MinHeight;
        public readonly List<PlacedPiece> Pieces = new List<PlacedPiece>();
        public int NextUid = 1;

        public BoardState Clone()
        {
            var b = new BoardState { Width = Width, Height = Height, NextUid = NextUid };
            foreach (var p in Pieces) b.Pieces.Add(p.Clone());
            return b;
        }

        public PlacedPiece Find(int uid)
        {
            for (int i = 0; i < Pieces.Count; i++) if (Pieces[i].Uid == uid) return Pieces[i];
            return null;
        }

        public PlacedPiece FindWeapon(int weaponUid)
        {
            for (int i = 0; i < Pieces.Count; i++) if (Pieces[i].Kind == PieceKind.Weapon && Pieces[i].WeaponUid == weaponUid) return Pieces[i];
            return null;
        }

        public int CountWeapons()
        {
            int n = 0;
            for (int i = 0; i < Pieces.Count; i++) if (Pieces[i].Kind == PieceKind.Weapon) n++;
            return n;
        }
    }

    public enum PlaceError { None, OutOfBounds, Overlap, UnknownShape, TooManyWeapons, NotFound }

    /// <summary>All board mutations go through this editor so they can be validated and undone.</summary>
    public sealed class BoardEditor
    {
        public const int MaxActiveWeapons = 3;

        private readonly IPieceShapeResolver _shapes;
        private readonly Stack<BoardState> _undo = new Stack<BoardState>();
        private readonly Stack<BoardState> _redo = new Stack<BoardState>();
        public BoardState Board { get; private set; }

        public BoardEditor(BoardState board, IPieceShapeResolver shapes)
        {
            Board = board;
            _shapes = shapes;
        }

        public bool CanUndo { get { return _undo.Count > 0; } }
        public bool CanRedo { get { return _redo.Count > 0; } }

        public PlaceError CheckPlacement(PlacedPiece piece, Cell origin, int rotation, int ignoreUid)
        {
            List<Cell> cells; List<PortDef> ports;
            if (!_shapes.TryGetShape(piece, out cells, out ports)) return PlaceError.UnknownShape;
            var probe = piece.Clone();
            probe.Origin = origin;
            probe.Rotation = rotation;
            var world = BoardGeometry.WorldCells(cells, probe);
            var occ = BoardAnalyzer.BuildOccupancy(Board, _shapes, ignoreUid);
            foreach (var c in world)
            {
                if (c.X < 0 || c.Y < 0 || c.X >= Board.Width || c.Y >= Board.Height) return PlaceError.OutOfBounds;
                if (occ[c.Y * Board.Width + c.X] != 0) return PlaceError.Overlap;
            }
            return PlaceError.None;
        }

        private void Snapshot()
        {
            _undo.Push(Board.Clone());
            _redo.Clear();
            if (_undo.Count > 64)
            {
                // keep memory bounded: drop the oldest by rebuilding the stack
                var arr = _undo.ToArray();
                _undo.Clear();
                for (int i = Math.Min(arr.Length, 64) - 1; i >= 0; i--) _undo.Push(arr[i]);
            }
        }

        public PlaceError Place(PlacedPiece piece, Cell origin, int rotation)
        {
            if (piece.Kind == PieceKind.Weapon && Board.CountWeapons() >= MaxActiveWeapons && Board.FindWeapon(piece.WeaponUid) == null)
                return PlaceError.TooManyWeapons;
            var err = CheckPlacement(piece, origin, rotation, piece.Uid);
            if (err != PlaceError.None) return err;
            Snapshot();
            var existing = piece.Uid > 0 ? Board.Find(piece.Uid) : null;
            if (existing != null)
            {
                existing.Origin = origin;
                existing.Rotation = rotation;
            }
            else
            {
                var p = piece.Clone();
                p.Uid = Board.NextUid++;
                p.Origin = origin;
                p.Rotation = ((rotation % 4) + 4) % 4;
                Board.Pieces.Add(p);
                piece.Uid = p.Uid;
            }
            return PlaceError.None;
        }

        public PlaceError Move(int uid, Cell origin, int rotation)
        {
            var p = Board.Find(uid);
            if (p == null) return PlaceError.NotFound;
            var err = CheckPlacement(p, origin, rotation, uid);
            if (err != PlaceError.None) return err;
            Snapshot();
            p.Origin = origin;
            p.Rotation = ((rotation % 4) + 4) % 4;
            return PlaceError.None;
        }

        public PlaceError Rotate(int uid)
        {
            var p = Board.Find(uid);
            if (p == null) return PlaceError.NotFound;
            return Move(uid, p.Origin, (p.Rotation + 1) % 4);
        }

        public bool Remove(int uid)
        {
            var p = Board.Find(uid);
            if (p == null) return false;
            Snapshot();
            Board.Pieces.Remove(p);
            return true;
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            _redo.Push(Board.Clone());
            ReplaceBoard(_undo.Pop());
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            _undo.Push(Board.Clone());
            ReplaceBoard(_redo.Pop());
            return true;
        }

        private void ReplaceBoard(BoardState b)
        {
            // Keep the same BoardState instance (the run state owns it) and copy contents.
            Board.Width = b.Width;
            Board.Height = b.Height;
            Board.NextUid = Math.Max(Board.NextUid, b.NextUid);
            Board.Pieces.Clear();
            foreach (var p in b.Pieces) Board.Pieces.Add(p.Clone());
        }
    }
}
