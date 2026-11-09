using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Strata
{
    public sealed class BrushMask
    {
        public int c0, r0, w, h, count;
        public bool[] on = new bool[0];

        public bool Has(int c, int r) => c >= c0 && r >= r0 && c < c0 + w && r < r0 + h && on[(r - r0) * w + (c - c0)];

        public List<int> Cells(StrataMap m)
        {
            var list = new List<int>(count);
            for (int k = 0; k < on.Length; k++) if (on[k]) list.Add((r0 + k / w) * m.w + c0 + k % w);
            return list;
        }
    }

    [EditorTool("Strata Terrain")]
    public sealed class StrataTool : EditorTool
    {
        public override GUIContent toolbarIcon => new GUIContent(StrataIcons.Tool, "Strata Terrain");

        public static StrataHit Hover;
        public static bool HasHover;
        public static StrataTerrain HoverTerrain;
        public static RectInt? Selection;
        static bool stroking, dragging;
        static int lastC = int.MinValue, lastR, anchorC, anchorR;
        static double lastPx, lastPz;
        static StrataTerrain anchor;
        static string strokeName;
        static bool strokeMoves;
        static int undoGroup;
        static readonly Dictionary<StrataTerrain, bool> touched = new Dictionary<StrataTerrain, bool>();
        static readonly Dictionary<StrataTerrain, StrataFollow> follows = new Dictionary<StrataTerrain, StrataFollow>();
        static readonly Dictionary<StrataTerrain, Vector2Int> lastOrigins = new Dictionary<StrataTerrain, Vector2Int>();

        [MenuItem("Tools/Strata/Edit Terrain", priority = 1)]
        static void Activate()
        {
            StrataSessions.OpenForScene();
            ToolManager.SetActiveTool<StrataTool>();
        }

        public override void OnActivated()
        {
            StrataSessions.OpenForScene();
            SceneView.RepaintAll();
        }

        public override void OnWillBeDeactivated()
        {
            EndStroke();
            dragging = false;
            anchor = null;
            HasHover = false;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView)) return;
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var t = StrataSessions.Active;
            if (!StrataSessions.Opened.Any(o => o.Connected))
            {
                if (e.type == EventType.Repaint) Note(t == null ? "No terrain open · Tools > Strata > Terrain Editor" : "This terrain is not in the open scene");
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) { if (e.type == EventType.Repaint) Note("Paused in Play mode"); return; }
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            if (t == null || !t.Connected) t = StrataSessions.Opened.First(o => o.Connected);

            Keys(e, t);
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.MouseDown)
            {
                var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (dragging && anchor != null) { HasHover = StrataRaycast.Cast(anchor.map, ray, out Hover); HoverTerrain = anchor; }
                else
                {
                    HasHover = Cast(ray, out Hover, out var hit);
                    if (HasHover) { HoverTerrain = hit; StrataSessions.Focus(hit); }
                }
                if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            }
            if (HasHover && HoverTerrain != null && HoverTerrain.Connected) t = HoverTerrain;

            bool navigating = e.alt || e.button != 0;
            switch (e.type)
            {
                case EventType.MouseDown when !navigating && HasHover:
                    GUIUtility.hotControl = id;
                    Press(t, e);
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    Drag(e);
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    Release(e);
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                case EventType.Repaint:
                    StrataPreview.Draw(t, e);
                    DrawOthers(t, e);
                    break;
            }
        }

        static bool Cast(Ray ray, out StrataHit best, out StrataTerrain on)
        {
            best = default; on = null;
            double near = double.MaxValue;
            foreach (var o in StrataSessions.Opened)
            {
                if (!o.Connected || !StrataRaycast.Cast(o.map, ray, out var h)) continue;
                double d = (h.World - ray.origin).sqrMagnitude;
                if (d < near) { near = d; best = h; on = o; }
            }
            return on != null;
        }

        static IEnumerable<StrataTerrain> Under(double x0, double z0, double x1, double z1)
        {
            foreach (var o in StrataSessions.Opened)
            {
                var m = o.map;
                if (o.Connected && m.x0 <= x1 && x0 < m.x0 + m.w && m.z0 <= z1 && z0 < m.z0 + m.h) yield return o;
            }
        }

        static void DrawOthers(StrataTerrain t, Event e)
        {
            var b = StrataSettings.Brush;
            if (!HasHover || dragging || b == StrataBrush.Paint || b == StrataBrush.Stairs || b == StrataBrush.Shape) return;
            int wc = Hover.c + t.map.x0, wr = Hover.r + t.map.z0, pad = StrataSettings.Size + 1;
            var keep = Hover;
            StrataPreview.Quiet = true;
            try
            {
                foreach (var o in Under(wc - pad, wr - pad, wc + pad, wr + pad).ToList())
                {
                    if (o == t) continue;
                    var m = o.map;
                    int c = wc - m.x0, r = wr - m.z0;
                    Hover.c = c; Hover.r = r;
                    Hover.cell = Mathf.Clamp(r, 0, m.h - 1) * m.w + Mathf.Clamp(c, 0, m.w - 1);
                    StrataPreview.Draw(o, e);
                }
            }
            finally { Hover = keep; StrataPreview.Quiet = false; }
        }

        static void Note(string text)
        {
            Handles.BeginGUI();
            var style = new GUIStyle(EditorStyles.helpBox) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            var size = style.CalcSize(new GUIContent(text));
            GUI.Label(new Rect(10, 10, size.x + 20, 28), text, style);
            Handles.EndGUI();
        }

        static void Keys(Event e, StrataTerrain t)
        {
            if (e.type != EventType.KeyDown) return;
            var b = StrataSettings.Brush;
            bool used = true;
            switch (e.keyCode)
            {
                case KeyCode.LeftBracket:
                    if (b == StrataBrush.Paint) StrataSettings.Radius /= 1.25f;
                    else StrataSettings.Size = Smaller(StrataSettings.Size);
                    break;
                case KeyCode.RightBracket:
                    if (b == StrataBrush.Paint) StrataSettings.Radius *= 1.25f;
                    else StrataSettings.Size = Bigger(StrataSettings.Size);
                    break;
                case KeyCode.Minus: case KeyCode.KeypadMinus: case KeyCode.PageDown:
                    StrataSettings.Level = StrataSettings.StepLevel(-1);
                    break;
                case KeyCode.Equals: case KeyCode.KeypadPlus: case KeyCode.PageUp:
                    StrataSettings.Level = StrataSettings.StepLevel(1);
                    break;
                case KeyCode.Return: case KeyCode.KeypadEnter:
                    if (b == StrataBrush.Shape && Selection.HasValue) ApplyShape(); else used = false;
                    break;
                case KeyCode.Escape:
                    if (Selection.HasValue || dragging) { Selection = null; dragging = false; anchor = null; } else used = false;
                    break;
                default: used = false; break;
            }
            if (used) { e.Use(); StrataSessions.Raise(); SceneView.RepaintAll(); }
        }

        // TODO n7ebou el size 3al scroll mta3 el souris, el +/- y9ala9 barcha
        public static int Bigger(int size) => size < 8 ? size + 1 : Mathf.CeilToInt(size * 1.25f);
        public static int Smaller(int size) => size <= 8 ? size - 1 : Mathf.FloorToInt(size / 1.25f);

        static void Press(StrataTerrain t, Event e)
        {
            var b = StrataSettings.Brush;
            if (e.shift) { Pick(t, b); return; }
            if (b == StrataBrush.Stairs)
            {
                if (e.control || e.command)
                {
                    int f = t.map.stairOf[Hover.cell];
                    if (f >= 0) { Begin(t, "Remove stair", moves: true); if (StrataEdits.RemoveFlight(t, f)) touched[t] = true; EndStroke(); }
                    return;
                }
                dragging = true; anchor = t; anchorC = Hover.c; anchorR = Hover.r;
                return;
            }
            if (b == StrataBrush.Shape) { dragging = true; anchor = t; anchorC = Hover.c; anchorR = Hover.r; Selection = ShapeRect(t.map, anchorC, anchorR, anchorC, anchorR); return; }
            bool revert = b == StrataBrush.Revert || e.control || e.command;
            bool moves = b != StrataBrush.Paint && !(b == StrataBrush.Level && !revert && !StrataSettings.ApplyHeight) && !(b == StrataBrush.Revert && !StrataSettings.RevertHeights);
            string name = b == StrataBrush.Paint ? (revert ? "Revert paint" : "Paint ground")
                : revert ? "Revert terrain"
                : StrataSettings.ApplyHeight ? "Paint level" : StrataSettings.ApplySides ? "Restyle walls" : "Paint top";
            Begin(t, name, moves);
            lastC = int.MinValue;
            Apply(e);
        }

        static void Drag(Event e)
        {
            var b = StrataSettings.Brush;
            if (dragging)
            {
                if (HasHover && b == StrataBrush.Shape && anchor != null) Selection = ShapeRect(anchor.map, anchorC, anchorR, Hover.c, Hover.r);
                SceneView.RepaintAll();
                return;
            }
            if (stroking && HasHover) Apply(e);
        }

        static void Release(Event e)
        {
            var b = StrataSettings.Brush;
            if (dragging && b == StrataBrush.Stairs)
            {
                dragging = false;
                var t = anchor;
                if (t != null && HasHover && StairPlan(t.map, anchorC, anchorR, Hover.c, Hover.r, out var rect, out var dir, out float y0, out float y1, StairRise))
                {
                    var p = StrataPreview.Palette(t);
                    int style = p.stairs.Any(s => s.index == StrataSettings.StairType) ? StrataSettings.StairType : (p.stairs.Length > 0 ? p.stairs[0].index : 0);
                    Begin(t, "Add stair", moves: true);
                    if (StrataEdits.AddStair(t, rect, dir, y0, y1, style, p.StairTimber(style), StrataSettings.CheekWalls) >= 0) touched[t] = true;
                    EndStroke();
                }
                return;
            }
            if (dragging && b == StrataBrush.Shape) { dragging = false; return; }
            EndStroke();
        }

        static void Begin(StrataTerrain t, string name, bool moves)
        {
            if (!StrataSessions.RecoveryOffered) StrataSessions.Notice = "";
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            strokeName = name;
            strokeMoves = moves;
            stroking = true;
            touched.Clear(); follows.Clear(); lastOrigins.Clear();
            Touch(t);
        }

        static void Touch(StrataTerrain t)
        {
            if (touched.ContainsKey(t)) return;
            t.history.Begin(strokeName);
            touched[t] = false;
            if (strokeMoves && StrataSettings.FollowGround) follows[t] = StrataFollow.Begin(t);
        }

        static void EndStroke()
        {
            if (!stroking) return;
            stroking = false;
            int kept = 0;
            foreach (var kv in touched.ToList())
            {
                var t = kv.Key;
                follows.TryGetValue(t, out var f);
                if (kv.Value && t.history.Recording)
                {
                    string name = "Strata: " + t.history.RecordingName;
                    t.history.End(f == null ? (Action)null : () =>
                    {
                        int n = f.Apply(t, name);
                        StrataSessions.Notice = n > 0 ? $"{n} object{(n == 1 ? "" : "s")} moved with the ground" : "";
                    });
                    kept++;
                }
                else t.history.Cancel();
                t.Flush();
            }
            if (kept > 1) Undo.CollapseUndoOperations(undoGroup);
            touched.Clear(); follows.Clear();
            StrataSessions.FlushAll();
            StrataSessions.Raise();
        }

        static void Apply(Event e)
        {
            var here = HoverTerrain;
            if (here == null || !touched.Keys.Any(k => k.history.Recording)) { stroking = false; return; }
            var b = StrataSettings.Brush;
            bool revert = b == StrataBrush.Revert || e.control || e.command;
            if (b == StrataBrush.Paint)
            {
                float r = StrataSettings.Radius, spacing = Mathf.Max(0.25f, r * 0.25f);
                double px = Hover.x, pz = Hover.z;
                int n = lastC == int.MinValue ? 1 : Mathf.Max(1, Mathf.CeilToInt((float)(Math.Sqrt((px - lastPx) * (px - lastPx) + (pz - lastPz) * (pz - lastPz)) / spacing)));
                if (lastC != int.MinValue && n == 1 && Math.Abs(px - lastPx) + Math.Abs(pz - lastPz) < spacing * 0.5) return;
                for (int k = 1; k <= n; k++)
                {
                    double x = lastC == int.MinValue ? px : lastPx + (px - lastPx) * k / n, z = lastC == int.MinValue ? pz : lastPz + (pz - lastPz) * k / n;
                    foreach (var t in Under(x - r, z - r, x + r, z + r).ToList())
                    {
                        Touch(t);
                        bool ch = revert ? StrataEdits.RevertPaint(t, x, z, r, StrataSettings.Strength, StrataSettings.Hardness)
                                         : StrataEdits.Paint(t, x, z, r, StrataSettings.Strength * 0.6f, StrataSettings.Hardness, StrataSettings.Layer);
                        if (ch) touched[t] = true;
                    }
                }
                lastPx = px; lastPz = pz; lastC = 0;
                foreach (var t in touched.Keys.ToList()) t.Flush();
                return;
            }
            int c1 = Hover.c + here.map.x0, r1 = Hover.r + here.map.z0, size = StrataSettings.Size;
            bool first = lastC == int.MinValue;
            int c0 = first ? c1 : lastC, r0 = first ? r1 : lastR;
            if (!first && c0 == c1 && r0 == r1) return;
            var points = new List<Vector2Int>();
            int steps = Math.Max(Math.Abs(c1 - c0), Math.Abs(r1 - r0));
            for (int k = first ? 0 : 1; k <= steps; k++)
                points.Add(steps == 0 ? new Vector2Int(c1, r1) : new Vector2Int((int)Math.Round(c0 + (c1 - c0) * (double)k / steps), (int)Math.Round(r0 + (r1 - r0) * (double)k / steps)));
            lastC = c1; lastR = r1;
            if (points.Count == 0) return;
            int pad = size + 1;
            int minX = points.Min(p => p.x) - pad, maxX = points.Max(p => p.x) + pad, minZ = points.Min(p => p.y) - pad, maxZ = points.Max(p => p.y) + pad;
            foreach (var t in Under(minX, minZ, maxX, maxZ).ToList())
            {
                var m = t.map;
                var seen = new HashSet<Vector2Int>();
                if (!first && lastOrigins.TryGetValue(t, out var lo)) seen.Add(lo);
                var origins = new List<Vector2Int>();
                foreach (var p in points) { var o = BrushOrigin(m, p.x - m.x0, p.y - m.z0, size); if (seen.Add(o)) origins.Add(o); }
                lastOrigins[t] = BrushOrigin(m, c1 - m.x0, r1 - m.z0, size);
                if (origins.Count == 0) continue;
                var mask = Brush(m, origins, size);
                if (mask.count == 0) continue;
                var list = mask.Cells(m);
                Touch(t);
                bool changed = false;
                bool level = b == StrataBrush.Level;
                if (revert)
                {
                    if (level ? StrataSettings.ApplyHeight || StrataSettings.ApplySides : StrataSettings.RevertHeights) changed |= StrataEdits.Revert(t, list);
                    if (level ? StrataSettings.ApplyTop : StrataSettings.RevertPaint) changed |= StrataEdits.RevertPaintCells(t, list);
                }
                else
                {
                    bool sides = StrataSettings.ApplySides;
                    if (StrataSettings.ApplyHeight) changed |= StrataEdits.SetLevel(t, list, StrataSettings.Level, sides ? StrataSettings.Wall : -1, sides ? StrataSettings.Rim : -1, StrataSettings.MeetDistance);
                    else if (sides) changed |= StrataEdits.Restyle(t, list, StrataSettings.Wall, StrataSettings.Rim);
                    if (StrataSettings.ApplyTop) changed |= StrataEdits.PaintTop(t, list, StrataSettings.Layer);
                }
                if (changed) { touched[t] = true; t.Flush(); }
            }
        }

        //? shift click pick only the top layer, should it pick the wall style also
        static void Pick(StrataTerrain t, StrataBrush b)
        {
            var m = t.map;
            int i = Hover.cell;
            int sx = (int)Math.Floor((Hover.x - m.x0) * m.splatRes), sy = (int)Math.Floor((Hover.z - m.z0) * m.splatRes);
            bool onPaint = sx >= 0 && sy >= 0 && sx < m.splatW && sy < m.splatH;
            if (b == StrataBrush.Paint)
            {
                if (onPaint) StrataSettings.Layer = m.TopLayer(sy * m.splatW + sx);
            }
            else
            {
                StrataSettings.Level = (float)Math.Round(m.Top(i), 3);
                if (m.kind[i] != (byte)CellKind.Natural && m.kind[i] != (byte)CellKind.Stair && m.wall[i] != 0) { StrataSettings.Wall = m.wall[i]; StrataSettings.Rim = m.rim[i]; }
                if (StrataSettings.ApplyTop && onPaint) StrataSettings.Layer = m.TopLayer(sy * m.splatW + sx);
            }
            StrataSessions.Raise();
            SceneView.RepaintAll();
        }

        public static void ApplyShape()
        {
            var t = anchor ?? StrataSessions.Active;
            if (t == null || !Selection.HasValue) return;
            var rect = Selection.Value;
            Begin(t, "Shape terrain", moves: true);
            if (StrataEdits.Shape(t, rect, StrataSettings.Shape, StrataSettings.ShapeAmount, rect.width >= rect.height)) touched[t] = true;
            EndStroke();
            Selection = null;
            anchor = null;
            SceneView.RepaintAll();
        }

        public static RectInt Rect(int c0, int r0, int c1, int r1) => new RectInt(Math.Min(c0, c1), Math.Min(r0, r1), Math.Abs(c1 - c0) + 1, Math.Abs(r1 - r0) + 1);

        public static Vector2Int BrushOrigin(StrataMap m, int c, int r, int size)
        {
            int s = StrataSettings.SnapCells(size);
            double ic = c + 0.5 - size / 2.0, ir = r + 0.5 - size / 2.0;
            return new Vector2Int((int)(Math.Floor((m.x0 + ic) / s + 0.5) * s) - m.x0, (int)(Math.Floor((m.z0 + ir) / s + 0.5) * s) - m.z0);
        }

        public static BrushMask Brush(StrataMap m, List<Vector2Int> origins, int size)
        {
            int a0 = int.MaxValue, b0 = int.MaxValue, a1 = int.MinValue, b1 = int.MinValue;
            foreach (var o in origins) { a0 = Math.Min(a0, o.x); b0 = Math.Min(b0, o.y); a1 = Math.Max(a1, o.x + size); b1 = Math.Max(b1, o.y + size); }
            a0 = Math.Max(0, a0); b0 = Math.Max(0, b0); a1 = Math.Min(m.w, a1); b1 = Math.Min(m.h, b1);
            var mask = new BrushMask { c0 = a0, r0 = b0 };
            if (a1 <= a0 || b1 <= b0) return mask;
            mask.w = a1 - a0; mask.h = b1 - b0;
            mask.on = new bool[mask.w * mask.h];
            bool round = StrataSettings.Round && size > 2;
            float rad = size / 2f;
            foreach (var o in origins)
                for (int rr = Math.Max(b0, o.y); rr < Math.Min(b1, o.y + size); rr++)
                    for (int cc = Math.Max(a0, o.x); cc < Math.Min(a1, o.x + size); cc++)
                    {
                        if (round) { float fx = cc + 0.5f - o.x - rad, fz = rr + 0.5f - o.y - rad; if (fx * fx + fz * fz > rad * rad) continue; }
                        int k = (rr - b0) * mask.w + (cc - a0);
                        if (!mask.on[k]) { mask.on[k] = true; mask.count++; }
                    }
            return mask;
        }

        public static BrushMask BrushAt(StrataMap m, int c, int r)
        {
            int size = StrataSettings.Size;
            return Brush(m, new List<Vector2Int> { BrushOrigin(m, c, r, size) }, size);
        }

        public static List<int> BrushCells(StrataMap m, int c, int r) => BrushAt(m, c, r).Cells(m);

        public static RectInt ShapeRect(StrataMap m, int c0, int r0, int c1, int r1)
        {
            int s = StrataSettings.Snap == SnapMode.Custom ? StrataSettings.SnapCustom : 1;
            int a0 = Math.Min(c0, c1), a1 = Math.Max(c0, c1) + 1, b0 = Math.Min(r0, r1), b1 = Math.Max(r0, r1) + 1;
            if (s > 1)
            {
                a0 = (int)Math.Floor((m.x0 + a0) / (double)s) * s - m.x0; a1 = (int)Math.Ceiling((m.x0 + a1) / (double)s) * s - m.x0;
                b0 = (int)Math.Floor((m.z0 + b0) / (double)s) * s - m.z0; b1 = (int)Math.Ceiling((m.z0 + b1) / (double)s) * s - m.z0;
            }
            return new RectInt(a0, b0, a1 - a0, b1 - b0);
        }

        public static float StairRise => StrataSettings.StairCustom ? StrataSettings.StairRise : 0f;

        public static bool StairPlan(StrataMap m, int c0, int r0, int c1, int r1, out RectInt rect, out StairDir dir, out float y0, out float y1, float rise = 0f)
        {
            rect = Rect(c0, r0, c1, r1);
            dir = StairDir.Landing; y0 = y1 = 0;
            int dx = c1 - c0, dz = r1 - r0;
            if (dx == 0 && dz == 0) return false;
            bool alongX = Math.Abs(dx) >= Math.Abs(dz);
            int sx = alongX ? Math.Sign(dx) : 0, sz = alongX ? 0 : Math.Sign(dz);
            float Beyond(int c, int r, int ox, int oz)
            {
                int cc = c + ox, rr = r + oz;
                if (!m.InGrid(cc, rr)) return m.Top(r * m.w + c);
                int i = rr * m.w + cc;
                return m.kind[i] == (byte)CellKind.Stair && m.FlightOf(i) is Flight f ? Math.Max(f.y0, f.y1) : m.Top(i);
            }
            float a = Beyond(c0, r0, -sx, -sz), b = Beyond(c1, r1, sx, sz);
            if (rise > 0)
            {
                y0 = a; y1 = a + rise;
                dir = sx > 0 ? StairDir.E : sx < 0 ? StairDir.W : sz > 0 ? StairDir.S : StairDir.N;
            }
            else if (Math.Abs(a - b) < 0.01f) { y0 = y1 = Math.Min(a, b); dir = StairDir.Landing; }
            else
            {
                bool up = b > a;
                y0 = up ? a : b; y1 = up ? b : a;
                int ax = up ? sx : -sx, az = up ? sz : -sz;
                dir = ax > 0 ? StairDir.E : ax < 0 ? StairDir.W : az > 0 ? StairDir.S : StairDir.N;
            }
            return StrataEdits.CanPlaceStair(m, rect);
        }

        public static bool Dragging => dragging;
        public static int AnchorC => anchorC;
        public static int AnchorR => anchorR;
    }
}
