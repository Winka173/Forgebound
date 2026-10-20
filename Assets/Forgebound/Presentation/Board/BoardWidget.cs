using System;
using System.Collections.Generic;
using Forgebound.Core.Content;
using Forgebound.Core.ForgeBoard;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Forgebound.Presentation
{
    /// <summary>Abstraction so the same board UI drives Adventure (RunController, logged) and the Workshop sandbox.</summary>
    public interface IBoardHost
    {
        BoardState Board { get; }
        IPieceShapeResolver Shapes { get; }
        BoardAnalysis Analyze();
        bool Move(int uid, int x, int y, int rot);
        bool Remove(int uid);
        bool Undo();
        bool Redo();
        bool CanUndo { get; }
        bool CanRedo { get; }
        Sprite SpriteFor(PlacedPiece p);
        string Tooltip(PlacedPiece p);
        string Label(PlacedPiece p);
        int PrimaryWeaponUid { get; }
    }

    /// <summary>
    /// Forge Board UI: grid, pieces with footprints, port nubs, live link lines (cyan = active, red = inactive),
    /// drag-to-move with ghost preview (green valid / red invalid), tap-select + tap-cell for touch, rotate/remove,
    /// undo/redo. Placement validity is computed with the same BoardEditor rules as the simulation.
    /// </summary>
    public sealed class BoardWidget
    {
        public const float Cell = 104f;
        private readonly Services _s;
        private readonly IBoardHost _host;
        private readonly RectTransform _root, _grid, _pieces, _links, _ghost;
        public int SelectedUid = -1;
        public Action<PlacedPiece> OnSelect;
        public Action OnChanged;
        /// <summary>When set, the next cell tap places this pending piece (inventory relay/support or bench weapon).</summary>
        public Func<int, int, int, bool> PendingPlace;
        public int PendingRotation;
        private BoardAnalysis _analysis;
        private readonly Text _issues;

        public BoardWidget(Services s, RectTransform parent, IBoardHost host)
        {
            _s = s;
            _host = host;
            _root = UiKit.Rect(parent, "Board");
            UiKit.Stretch(_root);
            _grid = UiKit.Rect(_root, "Grid");
            _links = UiKit.Rect(_root, "Links");
            _pieces = UiKit.Rect(_root, "Pieces");
            _ghost = UiKit.Rect(_root, "Ghost");
            foreach (var rt in new[] { _grid, _links, _pieces, _ghost })
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
            }
            _issues = UiKit.Label(_root, "", 20, Theme.Muted, TextAnchor.LowerCenter);
            UiKit.Anchor(_issues.rectTransform, 0, 0, 1, 0.08f);
            Refresh();
        }

        private Vector2 Origin { get { return new Vector2(-_host.Board.Width * Cell * 0.5f, _host.Board.Height * Cell * 0.5f); } }

        /// <summary>Board cell (x,y) top-left origin → local UI position of the cell centre (y grows downward on the board).</summary>
        public Vector2 CellCenter(int x, int y) { return Origin + new Vector2((x + 0.5f) * Cell, -(y + 0.5f) * Cell); }

        public bool LocalToCell(Vector2 local, out int x, out int y)
        {
            var o = Origin;
            x = Mathf.FloorToInt((local.x - o.x) / Cell);
            y = Mathf.FloorToInt((o.y - local.y) / Cell);
            return x >= 0 && y >= 0 && x < _host.Board.Width && y < _host.Board.Height;
        }

        public void Refresh()
        {
            _analysis = _host.Analyze();
            UiKit.Clear(_grid);
            UiKit.Clear(_pieces);
            UiKit.Clear(_links);
            var b = _host.Board;
            var frame = UiKit.Panel(_grid, "Frame", Theme.Border);
            frame.rectTransform.sizeDelta = new Vector2(b.Width * Cell + 16, b.Height * Cell + 16);
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                {
                    int cx = x, cy = y;
                    var cell = UiKit.Panel(_grid, "Cell", (x + y) % 2 == 0 ? Theme.Panel : Theme.PanelLight);
                    cell.rectTransform.sizeDelta = new Vector2(Cell - 4, Cell - 4);
                    cell.rectTransform.anchoredPosition = CellCenter(x, y);
                    var btn = cell.gameObject.AddComponent<Button>();
                    btn.transition = Selectable.Transition.None;
                    btn.onClick.AddListener(() => TapCell(cx, cy));
                }
            foreach (var p in b.Pieces) DrawPiece(p);
            DrawLinks();
            var issues = new List<string>();
            int dead = 0, idle = 0, cap = 0;
            foreach (var i in _analysis.Issues)
            {
                if (i.Code == "dead_relay") dead++;
                if (i.Code == "idle_support") idle++;
                if (i.Code == "capacity_exceeded") cap++;
            }
            if (dead > 0) issues.Add(_s.Loc.Format("ui.board.dead_relays", dead));
            if (idle > 0) issues.Add(_s.Loc.Format("ui.board.idle_supports", idle));
            if (cap > 0) issues.Add(_s.Loc.Format("ui.board.capacity", cap));
            issues.Add(_s.Loc.Format("ui.board.links", _analysis.ActiveLinkCount));
            _issues.text = string.Join("   •   ", issues);
            OnChanged?.Invoke();
        }

        private void DrawPiece(PlacedPiece p)
        {
            List<Cell> cells; List<PortDef> ports;
            if (!_host.Shapes.TryGetShape(p, out cells, out ports)) return;
            var world = BoardGeometry.WorldCells(cells, p);
            bool selected = p.Uid == SelectedUid;
            Color baseColor = p.Kind == PieceKind.Weapon ? Theme.Hex("5a3a28") : (p.Kind == PieceKind.Relay ? Theme.Hex("23455a") : Theme.Hex("3d4a2a"));
            if (p.Kind == PieceKind.Weapon && p.WeaponUid == _host.PrimaryWeaponUid) baseColor = Theme.Hex("6e3f1e");
            var holder = UiKit.Rect(_pieces, "Piece" + p.Uid);
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var c in world)
            {
                var img = UiKit.Panel(holder, "C", selected ? Color.Lerp(baseColor, Theme.Gold, 0.35f) : baseColor);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                img.rectTransform.sizeDelta = new Vector2(Cell - 10, Cell - 10);
                var pos = CellCenter(c.X, c.Y);
                img.rectTransform.anchoredPosition = pos;
                min = Vector2.Min(min, pos);
                max = Vector2.Max(max, pos);
                var drag = img.gameObject.AddComponent<PieceDragHandler>();
                drag.Widget = this;
                drag.Uid = p.Uid;
                UiKit.Tooltip(img.gameObject, () => _host.Tooltip(p));
            }
            // Ports: small nubs on the edge (gold = out, cyan = in, white = both)
            foreach (var wp in BoardGeometry.WorldPorts(cells, ports, p))
            {
                var nub = UiKit.Panel(holder, "Port", wp.Kind == PortKind.Out ? Theme.Gold : (wp.Kind == PortKind.In ? Theme.Link : Color.white));
                nub.raycastTarget = false;
                nub.rectTransform.anchorMin = nub.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                nub.rectTransform.sizeDelta = wp.Dir == PortDir.North || wp.Dir == PortDir.South ? new Vector2(30, 10) : new Vector2(10, 30);
                var cpos = CellCenter(wp.Cell.X, wp.Cell.Y);
                var off = new Vector2(BoardGeometry.DX[(int)wp.Dir], -BoardGeometry.DY[(int)wp.Dir]) * (Cell * 0.5f - 6);
                nub.rectTransform.anchoredPosition = cpos + off;
            }
            // Icon spans the footprint bounding box (rotated with the piece).
            var icon = UiKit.Panel(holder, "Icon", Color.white);
            icon.sprite = _host.SpriteFor(p);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            icon.rectTransform.anchoredPosition = (min + max) * 0.5f;
            var span = max - min + new Vector2(Cell, Cell) * 0.8f;
            icon.rectTransform.sizeDelta = new Vector2(Mathf.Max(span.x, span.y), Mathf.Min(span.x, span.y));
            icon.rectTransform.localRotation = Quaternion.Euler(0, 0, span.y > span.x ? 90 : 0);
            var label = UiKit.Label(holder, _host.Label(p), 18, Theme.Text, TextAnchor.LowerCenter, FontStyle.Bold);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            label.rectTransform.sizeDelta = new Vector2(span.x + 20, 30);
            label.rectTransform.anchoredPosition = new Vector2((min.x + max.x) * 0.5f, min.y - Cell * 0.42f);
        }

        private void DrawLinks()
        {
            var b = _host.Board;
            // Port-to-port connections first (thin), then highlight active signal paths (thick), inactive in red.
            foreach (var c in _analysis.Connections)
            {
                var a = CellCenter(c.FromCell.X, c.FromCell.Y);
                var d = CellCenter(c.ToCell.X, c.ToCell.Y);
                UiKit.Line(_links, a, d, 4, new Color(1, 1, 1, 0.25f));
            }
            foreach (var link in _analysis.Links)
            {
                var chain = new List<int> { link.SourceUid };
                chain.AddRange(link.RelayUids);
                if (link.DestUid >= 0) chain.Add(link.DestUid);
                for (int i = 0; i + 1 < chain.Count; i++)
                {
                    var a = Centroid(b.Find(chain[i]));
                    var d = Centroid(b.Find(chain[i + 1]));
                    var line = UiKit.Line(_links, a, d, link.Active ? 8 : 5, link.Active ? Theme.Link : Theme.Danger);
                    if (!link.Active) line.color = new Color(Theme.Danger.r, Theme.Danger.g, Theme.Danger.b, 0.7f);
                }
            }
            _links.SetAsLastSibling();
            _ghost.SetAsLastSibling();
        }

        private Vector2 Centroid(PlacedPiece p)
        {
            List<Cell> cells; List<PortDef> ports;
            if (p == null || !_host.Shapes.TryGetShape(p, out cells, out ports)) return Vector2.zero;
            var sum = Vector2.zero;
            var world = BoardGeometry.WorldCells(cells, p);
            foreach (var c in world) sum += CellCenter(c.X, c.Y);
            return sum / Mathf.Max(1, world.Count);
        }

        private void TapCell(int x, int y)
        {
            if (PendingPlace != null)
            {
                var place = PendingPlace;
                if (place(x, y, PendingRotation)) { PendingPlace = null; _s.Audio.Ui("sfx_slot_click"); Refresh(); }
                else _s.Audio.Ui("ui_error");
                return;
            }
            var occupant = PieceAt(x, y);
            if (occupant != null) { Select(occupant.Uid); return; }
            if (SelectedUid >= 0)
            {
                var p = _host.Board.Find(SelectedUid);
                if (p != null && _host.Move(p.Uid, x, y, p.Rotation)) { _s.Audio.Ui("sfx_slot_click"); Refresh(); }
                else _s.Audio.Ui("ui_error");
            }
        }

        public PlacedPiece PieceAt(int x, int y)
        {
            foreach (var p in _host.Board.Pieces)
            {
                List<Cell> cells; List<PortDef> ports;
                if (!_host.Shapes.TryGetShape(p, out cells, out ports)) continue;
                foreach (var c in BoardGeometry.WorldCells(cells, p)) if (c.X == x && c.Y == y) return p;
            }
            return null;
        }

        public void Select(int uid)
        {
            SelectedUid = uid;
            Refresh();
            OnSelect?.Invoke(_host.Board.Find(uid));
        }

        public void RotateSelected()
        {
            if (PendingPlace != null) { PendingRotation = (PendingRotation + 1) % 4; _s.Ui.Toast(_s.Loc.Format("ui.board.rotation", PendingRotation * 90)); return; }
            var p = _host.Board.Find(SelectedUid);
            if (p == null) return;
            // Try rotating in place, then nudge to keep it on the board (touch-friendly).
            int[,] nudges = { { 0, 0 }, { -1, 0 }, { 0, -1 }, { -1, -1 }, { 1, 0 }, { 0, 1 } };
            for (int i = 0; i < nudges.GetLength(0); i++)
                if (_host.Move(p.Uid, p.Origin.X + nudges[i, 0], p.Origin.Y + nudges[i, 1], (p.Rotation + 1) % 4)) { _s.Audio.Ui("sfx_rotate"); Refresh(); return; }
            _s.Audio.Ui("ui_error");
            _s.Ui.Toast(_s.L("ui.board.cannot_rotate"));
        }

        public void RemoveSelected()
        {
            if (SelectedUid < 0) return;
            if (_host.Remove(SelectedUid)) { SelectedUid = -1; Refresh(); OnSelect?.Invoke(null); }
        }

        public void Undo() { if (_host.Undo()) Refresh(); }
        public void Redo() { if (_host.Redo()) Refresh(); }

        // ---- drag & drop

        private int _dragUid = -1;
        private Vector2 _dragOffsetCells;

        public void BeginDrag(int uid, PointerEventData e)
        {
            _dragUid = uid;
            SelectedUid = uid;
            var p = _host.Board.Find(uid);
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_pieces, e.position, e.pressEventCamera, out local);
            int cx, cy;
            LocalToCell(local, out cx, out cy);
            _dragOffsetCells = new Vector2(cx - p.Origin.X, cy - p.Origin.Y);
        }

        public void Drag(PointerEventData e)
        {
            UiKit.Clear(_ghost);
            var p = _host.Board.Find(_dragUid);
            if (p == null) return;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_pieces, e.position, e.pressEventCamera, out local);
            int cx, cy;
            LocalToCell(local, out cx, out cy);
            int ox = cx - (int)_dragOffsetCells.x, oy = cy - (int)_dragOffsetCells.y;
            List<Cell> cells; List<PortDef> ports;
            if (!_host.Shapes.TryGetShape(p, out cells, out ports)) return;
            var probe = p.Clone();
            probe.Origin = new Cell(ox, oy);
            var editor = new BoardEditor(_host.Board.Clone(), _host.Shapes);
            bool ok = editor.CheckPlacement(p, probe.Origin, p.Rotation, p.Uid) == PlaceError.None;
            foreach (var c in BoardGeometry.WorldCells(cells, probe))
            {
                var g = UiKit.Panel(_ghost, "G", ok ? new Color(0.4f, 1f, 0.5f, 0.35f) : new Color(1f, 0.3f, 0.3f, 0.35f));
                g.raycastTarget = false;
                g.rectTransform.anchorMin = g.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                g.rectTransform.sizeDelta = new Vector2(Cell - 6, Cell - 6);
                g.rectTransform.anchoredPosition = CellCenter(c.X, c.Y);
            }
        }

        public void EndDrag(PointerEventData e)
        {
            UiKit.Clear(_ghost);
            var p = _host.Board.Find(_dragUid);
            _dragUid = -1;
            if (p == null) return;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_pieces, e.position, e.pressEventCamera, out local);
            int cx, cy;
            if (!LocalToCell(local, out cx, out cy)) { Refresh(); return; }
            int ox = cx - (int)_dragOffsetCells.x, oy = cy - (int)_dragOffsetCells.y;
            if (ox == p.Origin.X && oy == p.Origin.Y) { Select(p.Uid); return; }
            if (_host.Move(p.Uid, ox, oy, p.Rotation)) _s.Audio.Ui("sfx_slot_click"); else _s.Audio.Ui("ui_error");
            Refresh();
            OnSelect?.Invoke(_host.Board.Find(p.Uid));
        }
    }

    public sealed class PieceDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public BoardWidget Widget;
        public int Uid;
        public void OnBeginDrag(PointerEventData e) { Widget.BeginDrag(Uid, e); }
        public void OnDrag(PointerEventData e) { Widget.Drag(e); }
        public void OnEndDrag(PointerEventData e) { Widget.EndDrag(e); }
        public void OnPointerClick(PointerEventData e) { if (!e.dragging) Widget.Select(Uid); }
    }
}
