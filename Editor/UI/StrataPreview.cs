using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Strata
{
    static class StrataPreview
    {
        static readonly Color Warm = new Color(1f, 0.82f, 0.42f), Cool = new Color(0.5f, 0.78f, 1f), Bad = new Color(1f, 0.35f, 0.3f), Leaf = new Color(0.62f, 1f, 0.6f), Violet = new Color(0.82f, 0.66f, 1f), Amber = new Color(1f, 0.62f, 0.25f);
        static GUIStyle label;

        static Vector3 W(double x, double y, double z) => new Vector3((float)x, (float)y, (float)-z);

        public static void Draw(StrataTerrain t, Event e)
        {
            var m = t.map;
            var b = StrataSettings.Brush;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            if (b == StrataBrush.Shape) { Shape(t); if (StrataTool.Selection.HasValue) return; }
            if (b == StrataBrush.Stairs && StrataTool.Dragging) { Stairs(t); return; }
            if (!StrataTool.HasHover) return;
            var h = StrataTool.Hover;
            bool revert = b == StrataBrush.Revert || e.control || e.command;
            switch (b)
            {
                case StrataBrush.Level when !revert:
                    if (StrataSettings.ApplyHeight) Level(t, StrataTool.BrushAt(m, h.c, h.r), StrataSettings.Level, LevelLabel(t));
                    else
                    {
                        Cells(m, StrataTool.BrushAt(m, h.c, h.r), StrataSettings.ApplySides ? StyleColor(t) : Leaf);
                        Label(h.World, LevelLabel(t));
                    }
                    break;
                case StrataBrush.Paint:
                    Disc(h, StrataSettings.Radius, StrataSettings.Hardness, revert ? Cool : Leaf);
                    var p = Palette(t);
                    Label(h.World, revert ? "Revert" : (StrataSettings.Layer < p.layers.Length ? p.layers[StrataSettings.Layer] : $"Layer {StrataSettings.Layer}"));
                    break;
                case StrataBrush.Stairs:
                    bool remove = m.kind[h.cell] == (byte)CellKind.Stair && (e.control || e.command);
                    Cells(m, RectMask(m, new RectInt(h.c, h.r, 1, 1)), remove ? Bad : Color.white);
                    if (remove) Label(h.World, "Remove");
                    break;
                case StrataBrush.Shape:
                    Cells(m, RectMask(m, StrataTool.ShapeRect(m, h.c, h.r, h.c, h.r)), Violet);
                    break;
                default:
                    Cells(m, StrataTool.BrushAt(m, h.c, h.r), Cool);
                    Label(h.World, t.Original == null ? "No original" : "Revert");
                    break;
            }
        }

        static StrataPalette palette; static StrataTerrain paletteOf;
        public static StrataPalette Palette(StrataTerrain t)
        {
            if (paletteOf != t || palette == null || palette.stamp != Formats.StrataProfiles.Stamp(t)) { palette?.Release(); palette = Formats.StrataProfiles.Palette(t); paletteOf = t; }
            return palette;
        }

        static string LevelLabel(StrataTerrain t)
        {
            var p = Palette(t);
            var parts = new List<string>();
            if (StrataSettings.ApplyHeight) parts.Add($"{StrataSettings.Level:0.##} m");
            if (StrataSettings.ApplyTop) parts.Add(StrataSettings.Layer < p.layers.Length && p.layers[StrataSettings.Layer] != null ? p.layers[StrataSettings.Layer] : "?");
            if (StrataSettings.ApplySides) parts.Add($"{p.WallName(StrataSettings.Wall)} / {p.RimName(StrataSettings.Rim)}");
            return string.Join(" · ", parts);
        }

        static Color StyleColor(StrataTerrain t)
        {
            var p = Palette(t);
            int w = StrataSettings.Wall;
            return w >= 0 && w < p.wallColors.Length ? Color.Lerp(p.wallColors[w], Color.white, 0.35f) : Warm;
        }

        static BrushMask RectMask(StrataMap m, RectInt r)
        {
            int c0 = Math.Max(0, r.xMin), r0 = Math.Max(0, r.yMin), c1 = Math.Min(m.w, r.xMax), r1 = Math.Min(m.h, r.yMax);
            var k = new BrushMask { c0 = c0, r0 = r0 };
            if (c1 <= c0 || r1 <= r0) return k;
            k.w = c1 - c0; k.h = r1 - r0; k.count = k.w * k.h;
            k.on = new bool[k.count];
            for (int i = 0; i < k.on.Length; i++) k.on[i] = true;
            return k;
        }

        static bool On(BrushMask k, int x, int y) => x >= 0 && y >= 0 && x < k.w && y < k.h && k.on[y * k.w + x];

        static void Level(StrataTerrain t, BrushMask k, float level, string text)
        {
            var m = t.map;
            if (k.count == 0) return;
            double sum = 0;
            for (int y = 0; y < k.h; y++)
                for (int x = 0; x < k.w; x++)
                    if (k.on[y * k.w + x]) sum += m.Top((k.r0 + y) * m.w + k.c0 + x);
            var col = level >= sum / k.count - 0.01 ? Warm : Cool;
            var fill = new Color(col.r, col.g, col.b, 0.18f);
            var quad = new Vector3[4];
            for (int y = 0; y < k.h; y++)
            {
                int r = k.r0 + y;
                bool Open(int x) => k.on[y * k.w + x] && m.kind[r * m.w + k.c0 + x] != (byte)CellKind.Stair;
                for (int x = 0; x < k.w;)
                {
                    if (!Open(x)) { x++; continue; }
                    int x1 = x;
                    while (x1 < k.w && Open(x1)) x1++;
                    double X0 = m.x0 + k.c0 + x, X1 = m.x0 + k.c0 + x1, Z = m.z0 + r;
                    quad[0] = W(X0, level, Z); quad[1] = W(X1, level, Z); quad[2] = W(X1, level, Z + 1); quad[3] = W(X0, level, Z + 1);
                    Handles.DrawSolidRectangleWithOutline(quad, fill, Color.clear);
                    x = x1;
                }
            }
            Handles.color = col;
            Outline(m, k, level);
            double gx0 = m.x0 + k.c0, gx1 = gx0 + k.w, gz0 = m.z0 + k.r0, gz1 = gz0 + k.h;
            Handles.color = new Color(col.r, col.g, col.b, 0.6f);
            foreach (var (gx, gz) in new[] { (gx0, gz0), (gx1, gz0), (gx1, gz1), (gx0, gz1) })
            {
                double ground = m.Height(Math.Min(gx, m.x0 + m.w - 1e-3), Math.Min(gz, m.z0 + m.h - 1e-3));
                Handles.DrawDottedLine(W(gx, ground, gz), W(gx, level, gz), 3f);
            }
            Label(W(gx1, level, gz0), text);
        }

        static void Outline(StrataMap m, BrushMask k, float y)
        {
            for (int yy = 0; yy <= k.h; yy++)
                for (int x = 0, start = -1; x <= k.w; x++)
                {
                    bool edge = x < k.w && On(k, x, yy - 1) != On(k, x, yy);
                    if (edge && start < 0) start = x;
                    else if (!edge && start >= 0) { Line(W(m.x0 + k.c0 + start, y, m.z0 + k.r0 + yy), W(m.x0 + k.c0 + x, y, m.z0 + k.r0 + yy)); start = -1; }
                }
            for (int xx = 0; xx <= k.w; xx++)
                for (int z = 0, start = -1; z <= k.h; z++)
                {
                    bool edge = z < k.h && On(k, xx - 1, z) != On(k, xx, z);
                    if (edge && start < 0) start = z;
                    else if (!edge && start >= 0) { Line(W(m.x0 + k.c0 + xx, y, m.z0 + k.r0 + start), W(m.x0 + k.c0 + xx, y, m.z0 + k.r0 + z)); start = -1; }
                }
        }

        static void Line(Vector3 a, Vector3 b) => Handles.DrawAAPolyLine(3f, a, b);

        static void Cells(StrataMap m, BrushMask k, Color col)
        {
            if (k.count == 0) return;
            Handles.color = col;
            var fill = new Color(col.r, col.g, col.b, 0.12f);
            bool filled = k.count <= 4096;
            var quad = new Vector3[4];
            var segs = new List<Vector3>();
            for (int y = 0; y < k.h; y++)
                for (int x = 0; x < k.w; x++)
                {
                    if (!k.on[y * k.w + x]) continue;
                    int c = k.c0 + x, r = k.r0 + y, i = r * m.w + c, o = i * 4;
                    double gx = m.x0 + c, gz = m.z0 + r;
                    var nw = W(gx, m.corner[o], gz); var ne = W(gx + 1, m.corner[o + 1], gz); var se = W(gx + 1, m.corner[o + 2], gz + 1); var sw = W(gx, m.corner[o + 3], gz + 1);
                    if (m.kind[i] == (byte)CellKind.Stair && m.FlightOf(i) is Flight f) { float top = Math.Max(f.y0, f.y1); nw.y = ne.y = se.y = sw.y = top; }
                    if (filled) { quad[0] = nw; quad[1] = ne; quad[2] = se; quad[3] = sw; Handles.DrawSolidRectangleWithOutline(quad, fill, Color.clear); }
                    if (!On(k, x, y - 1)) { segs.Add(nw); segs.Add(ne); }
                    if (!On(k, x, y + 1)) { segs.Add(sw); segs.Add(se); }
                    if (!On(k, x - 1, y)) { segs.Add(nw); segs.Add(sw); }
                    if (!On(k, x + 1, y)) { segs.Add(ne); segs.Add(se); }
                }
            if (segs.Count <= 2048) for (int s = 0; s < segs.Count; s += 2) Line(segs[s], segs[s + 1]);
            else Handles.DrawLines(segs.ToArray());
        }

        static void Disc(StrataHit h, float radius, float hardness, Color col)
        {
            Handles.color = col;
            Handles.DrawWireDisc(h.World, Vector3.up, radius, 2.5f);
            Handles.color = new Color(col.r, col.g, col.b, 0.5f);
            if (hardness > 0.02f && hardness < 0.98f) Handles.DrawWireDisc(h.World, Vector3.up, radius * hardness, 1.5f);
            Handles.color = new Color(col.r, col.g, col.b, 0.08f);
            Handles.DrawSolidDisc(h.World, Vector3.up, radius);
        }

        static void Stairs(StrataTerrain t)
        {
            var m = t.map;
            if (!StrataTool.HasHover) return;
            var h = StrataTool.Hover;
            bool ok = StrataTool.StairPlan(m, StrataTool.AnchorC, StrataTool.AnchorR, h.c, h.r, out var rect, out var dir, out float y0, out float y1, StrataTool.StairRise);
            var (cut, replaced) = ok ? StrataEdits.StairOverlaps(m, rect) : (0, 0);
            string over = (cut > 0 ? $" · cuts {cut}" : "") + (replaced > 0 ? $" · replaces {replaced}" : "");
            var col = !ok ? Bad : cut + replaced > 0 ? Amber : Color.white;
            var fill = new Color(col.r, col.g, col.b, 0.2f);
            double X0 = m.x0 + rect.xMin, X1 = m.x0 + rect.xMax, Z0 = m.z0 + rect.yMin, Z1 = m.z0 + rect.yMax;
            if (dir == StairDir.Landing)
            {
                Handles.DrawSolidRectangleWithOutline(new[] { W(X0, y0, Z0), W(X1, y0, Z0), W(X1, y0, Z1), W(X0, y0, Z1) }, fill, col);
                Label(W(X1, y0, Z0), ok ? $"Landing · {y0:0.##} m{over}" : "Blocked");
                return;
            }
            int steps = StrataEdits.StepsFor(y0, y1);
            var (dx, dz) = Flight.Step(dir);
            double len = dx != 0 ? X1 - X0 : Z1 - Z0;
            var cuts = StrataStairs.TexelSplits(len, steps);
            for (int k = 0; k < steps; k++)
            {
                double a = cuts[k], b = cuts[k + 1], top = y0 + (y1 - y0) * (k + 1) / steps;
                double px0, px1, pz0, pz1;
                if (dx > 0) { px0 = X0 + a; px1 = X0 + b; pz0 = Z0; pz1 = Z1; }
                else if (dx < 0) { px0 = X1 - b; px1 = X1 - a; pz0 = Z0; pz1 = Z1; }
                else if (dz > 0) { px0 = X0; px1 = X1; pz0 = Z0 + a; pz1 = Z0 + b; }
                else { px0 = X0; px1 = X1; pz0 = Z1 - b; pz1 = Z1 - a; }
                Handles.DrawSolidRectangleWithOutline(new[] { W(px0, top, pz0), W(px1, top, pz0), W(px1, top, pz1), W(px0, top, pz1) }, fill, col);
            }
            Label(W(X1, y1, Z0), ok ? $"{steps} steps · {y0:0.##} to {y1:0.##} m{over}" : "Blocked");
        }

        static void Shape(StrataTerrain t)
        {
            if (!StrataTool.Selection.HasValue) return;
            var m = t.map;
            var rect = StrataTool.Selection.Value;
            Cells(m, RectMask(m, rect), Violet);
            var r2 = rect;
            var hgt = StrataEdits.ShapeHeights(m, ref r2, StrataSettings.Shape, StrataSettings.ShapeAmount, r2.width >= r2.height);
            if (hgt != null)
            {
                int vw = r2.width + 1, vh = r2.height + 1;
                int every = Math.Max(1, Math.Max(vw, vh) / 64);
                Handles.color = new Color(1f, 1f, 1f, 0.7f);
                for (int b = 0; b < vh; b++)
                    for (int a = 0; a < vw; a++)
                    {
                        var p = W(m.x0 + r2.xMin + a, hgt[b * vw + a], m.z0 + r2.yMin + b);
                        if (a + 1 < vw && b % every == 0) Handles.DrawLine(p, W(m.x0 + r2.xMin + a + 1, hgt[b * vw + a + 1], m.z0 + r2.yMin + b));
                        if (b + 1 < vh && a % every == 0) Handles.DrawLine(p, W(m.x0 + r2.xMin + a, hgt[(b + 1) * vw + a], m.z0 + r2.yMin + b + 1));
                    }
            }
            int cx = Math.Max(0, Math.Min(m.w - 1, rect.xMax - 1)), cr = Math.Max(0, Math.Min(m.h - 1, rect.yMin));
            var top = W(m.x0 + cx + 1, m.Top(cr * m.w + cx), m.z0 + cr);
            Label(top, StrataTool.Dragging ? $"{rect.width} × {rect.height}" : $"{StrataSettings.Shape} · Enter");
        }

        public static List<int> RectCells(StrataMap m, RectInt r)
        {
            var list = new List<int>();
            for (int y = Math.Max(0, r.yMin); y < Math.Min(m.h, r.yMax); y++)
                for (int x = Math.Max(0, r.xMin); x < Math.Min(m.w, r.xMax); x++)
                    list.Add(y * m.w + x);
            return list;
        }

        static void Label(Vector3 world, string text)
        {
            if (!StrataSettings.Labels) return;
            if (label == null)
            {
                label = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 6, 2, 3) };
                label.normal.textColor = Color.white;
                var bg = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                bg.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.1f, 0.78f)); bg.Apply();
                label.normal.background = bg;
            }
            Handles.BeginGUI();
            var p = HandleUtility.WorldToGUIPoint(world);
            var size = label.CalcSize(new GUIContent(text));
            GUI.Label(new Rect(p.x + 14, p.y - size.y - 6, size.x, size.y), text, label);
            Handles.EndGUI();
        }
    }
}
