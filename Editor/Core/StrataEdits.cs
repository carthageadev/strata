using System;
using System.Collections.Generic;
using UnityEngine;

namespace Strata
{
    public enum ShapeProfile { Valley, Hill, Ramp, Smooth, Crater }

    public static class StrataEdits
    {
        public const float Sliver = 0.45f;

        static CellKind KindFor(StrataMap m, int c, int r, float height) =>
            height >= m.NaturalAtCell(c, r) - 1e-3f ? CellKind.Raised : CellKind.Sunken;

        static readonly int[] Dx = { 0, 1, 1, 0 }, Dz = { 0, 0, 1, 1 };

        static int CornerAt(int vc, int vr, int cc, int rr) => vc == cc ? (vr == rr ? 0 : 3) : (vr == rr ? 1 : 2);

        static bool Seal(StrataMap m, IEnumerable<int> changed, HashSet<int> edited, float meet)
        {
            bool any = false;
            var seen = new HashSet<long>();
            var mine = new float[4];
            foreach (int i in changed)
            {
                int c = i % m.w, r = i / m.w;
                for (int q = 0; q < 4; q++)
                {
                    int vc = c + Dx[q], vr = r + Dz[q];
                    if (!seen.Add((long)vr * (m.w + 1) + vc)) continue;
                    int n = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int cc = vc - 1 + (k & 1), rr = vr - 1 + (k >> 1);
                        if (!m.InGrid(cc, rr)) continue;
                        int j = rr * m.w + cc;
                        if (edited.Contains(j)) mine[n++] = m.corner[j * 4 + CornerAt(vc, vr, cc, rr)];
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        int cc = vc - 1 + (k & 1), rr = vr - 1 + (k >> 1);
                        if (!m.InGrid(cc, rr)) continue;
                        int j = rr * m.w + cc;
                        if (edited.Contains(j) || m.kind[j] == (byte)CellKind.Stair) continue;
                        int o = j * 4 + CornerAt(vc, vr, cc, rr);
                        float v = m.corner[o], best = v;
                        double near = double.MaxValue;
                        for (int e = 0; e < n; e++) { double d = Math.Abs((double)mine[e] - v); if (d < near) { near = d; best = mine[e]; } }
                        double reach = m.kind[j] == (byte)CellKind.Natural ? Math.Max(StrataRules.Eps, meet) : StrataRules.Eps;
                        if (near > 0 && near < reach) { m.corner[o] = best; any = true; }
                    }
                }
            }
            return any;
        }

        public static void Bounds(List<int> cells, StrataMap m, out int c0, out int r0, out int c1, out int r1)
        {
            c0 = r0 = int.MaxValue; c1 = r1 = int.MinValue;
            foreach (int i in cells)
            {
                int c = i % m.w, r = i / m.w;
                c0 = Math.Min(c0, c); r0 = Math.Min(r0, r); c1 = Math.Max(c1, c + 1); r1 = Math.Max(r1, r + 1);
            }
        }

        //? why Seal() need meet when the brush is round, i dont remember anymore
        public static bool SetLevel(StrataTerrain t, List<int> cells, float height, int wall, int rim, float meet)
        {
            var m = t.map;
            cells = cells.FindAll(i => m.kind[i] != (byte)CellKind.Stair);
            if (cells.Count == 0) return false;
            Bounds(cells, m, out int c0, out int r0, out int c1, out int r1);
            t.history.TouchCells(c0 - 1, r0 - 1, c1 + 1, r1 + 1);
            var changed = new List<int>();
            foreach (int i in cells)
            {
                int c = i % m.w, r = i / m.w, o = i * 4;
                var kind = (byte)KindFor(m, c, r, height);
                byte wv = wall >= 0 ? (byte)wall : m.wall[i], rv = rim >= 0 ? (byte)rim : m.rim[i];
                if (m.corner[o] == height && m.corner[o + 1] == height && m.corner[o + 2] == height && m.corner[o + 3] == height && m.kind[i] == kind && m.wall[i] == wv && m.rim[i] == rv) continue;
                m.corner[o] = m.corner[o + 1] = m.corner[o + 2] = m.corner[o + 3] = height;
                m.kind[i] = kind; m.wall[i] = wv; m.rim[i] = rv;
                changed.Add(i);
            }
            if (changed.Count == 0) return false;
            Seal(m, changed, new HashSet<int>(cells), meet);
            t.CellsChanged(c0 - 1, r0 - 1, c1 + 1, r1 + 1);
            return true;
        }

        public static bool Restyle(StrataTerrain t, List<int> cells, int wall, int rim)
        {
            var m = t.map;
            cells = cells.FindAll(i => m.kind[i] != (byte)CellKind.Stair);
            if (cells.Count == 0) return false;
            Bounds(cells, m, out int c0, out int r0, out int c1, out int r1);
            t.history.TouchCells(c0, r0, c1, r1);
            bool any = false;
            foreach (int i in cells)
            {
                int c = i % m.w, r = i / m.w;
                byte kind = m.kind[i] == (byte)CellKind.Natural ? (byte)KindFor(m, c, r, m.Top(i)) : m.kind[i];
                if (m.wall[i] == wall && m.rim[i] == rim && m.kind[i] == kind) continue;
                m.wall[i] = (byte)wall; m.rim[i] = (byte)rim; m.kind[i] = kind;
                any = true;
            }
            if (any) t.CellsChanged(c0, r0, c1, r1);
            return any;
        }

        public static bool Revert(StrataTerrain t, List<int> cells)
        {
            var m = t.map; var o = t.Original;
            if (o == null) return false;
            cells = cells.FindAll(i => m.kind[i] != (byte)CellKind.Stair && o.kind[i] != (byte)CellKind.Stair);
            if (cells.Count == 0) return false;
            Bounds(cells, m, out int c0, out int r0, out int c1, out int r1);
            t.history.TouchCells(c0 - 1, r0 - 1, c1 + 1, r1 + 1);
            var changed = new List<int>();
            foreach (int i in cells)
            {
                bool same = m.kind[i] == o.kind[i] && m.wall[i] == o.wall[i] && m.rim[i] == o.rim[i];
                for (int q = 0; q < 4 && same; q++) same = m.corner[i * 4 + q] == o.corner[i * 4 + q];
                if (same) continue;
                Array.Copy(o.corner, i * 4, m.corner, i * 4, 4);
                m.kind[i] = o.kind[i]; m.wall[i] = o.wall[i]; m.rim[i] = o.rim[i];
                changed.Add(i);
            }
            if (changed.Count == 0) return false;
            var edited = new HashSet<int>(cells);
            Unpull(m, o, changed, edited);
            Seal(m, changed, edited, 0f);
            t.CellsChanged(c0 - 1, r0 - 1, c1 + 1, r1 + 1);
            return true;
        }

        static void Unpull(StrataMap m, StrataMap o, List<int> changed, HashSet<int> edited)
        {
            var seen = new HashSet<long>();
            foreach (int i in changed)
            {
                int c = i % m.w, r = i / m.w;
                for (int q = 0; q < 4; q++)
                {
                    int vc = c + Dx[q], vr = r + Dz[q];
                    if (!seen.Add((long)vr * (m.w + 1) + vc)) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        int cc = vc - 1 + (k & 1), rr = vr - 1 + (k >> 1);
                        if (!m.InGrid(cc, rr)) continue;
                        int j = rr * m.w + cc;
                        if (edited.Contains(j) || m.kind[j] != (byte)CellKind.Natural) continue;
                        int cj = j * 4 + CornerAt(vc, vr, cc, rr);
                        float now = m.corner[cj], was = o.corner[cj];
                        if (now == was || Math.Abs(now - was) > Sliver + 1e-3f) continue;
                        bool ranOn = false, held = false;
                        for (int e = 0; e < 4; e++)
                        {
                            int ec = vc - 1 + (e & 1), er = vr - 1 + (e >> 1);
                            if (!m.InGrid(ec, er)) continue;
                            int x = er * m.w + ec, cx = x * 4 + CornerAt(vc, vr, ec, er);
                            if (edited.Contains(x)) ranOn |= Math.Abs((double)o.corner[cx] - was) < StrataRules.Eps;
                            else if (x != j && m.kind[x] != (byte)CellKind.Natural && m.kind[x] != (byte)CellKind.Stair) held |= m.corner[cx] == now;
                        }
                        if (ranOn && !held) m.corner[cj] = was;
                    }
                }
            }
        }

        //? hardness 1 still give soft pixels on the border, is it the falloff or the splat res
        static float Falloff(float d, float radius, float hardness)
        {
            float x = d / Math.Max(1e-4f, radius);
            if (x >= 1f) return 0f;
            if (x <= hardness) return 1f;
            float k = (x - hardness) / Math.Max(1e-4f, 1f - hardness);
            return 1f - k * k * (3f - 2f * k);
        }

        // TODO soft paint go over the wall top and bleed on the cliff face, clamp it to the cell
        public static bool Paint(StrataTerrain t, double x, double z, float radius, float strength, float hardness, int layer)
        {
            var m = t.map;
            float res = m.splatRes;
            double fx = (x - m.x0) * res, fy = (z - m.z0) * res;
            float R = radius * res;
            int x0 = Math.Max(0, (int)Math.Floor(fx - R)), x1 = Math.Min(m.splatW, (int)Math.Ceiling(fx + R) + 1);
            int y0 = Math.Max(0, (int)Math.Floor(fy - R)), y1 = Math.Min(m.splatH, (int)Math.Ceiling(fy + R) + 1);
            if (x1 <= x0 || y1 <= y0) return false;
            t.history.TouchPaint(x0, y0, x1, y1);
            var w = new float[StrataMap.Layers];
            bool any = false;
            for (int y = y0; y < y1; y++)
                for (int xx = x0; xx < x1; xx++)
                {
                    float d = (float)Math.Sqrt((xx + 0.5 - fx) * (xx + 0.5 - fx) + (y + 0.5 - fy) * (y + 0.5 - fy));
                    float a = strength * Falloff(d, R, hardness);
                    if (a <= 0f) continue;
                    int i = y * m.splatW + xx;
                    for (int k = 0; k < StrataMap.Layers; k++) w[k] = m.Weight(i, k) * (1f - a);
                    w[layer] += 255f * a;
                    if (Store(m, i, w)) any = true;
                }
            if (any) t.PaintChanged(x0, y0, x1, y1);
            return any;
        }

        public static bool PaintTop(StrataTerrain t, List<int> cells, int layer)
        {
            var m = t.map;
            if (cells.Count == 0 || layer < 0 || layer >= StrataMap.Layers) return false;
            Bounds(cells, m, out int c0, out int r0, out int c1, out int r1);
            int res = m.splatRes, plane = layer >> 2, ch = layer & 3;
            t.history.TouchPaint(c0 * res, r0 * res, c1 * res, r1 * res);
            var full = new Color32(0, 0, 0, 0);
            StrataMap.Set(ref full, ch, 255);
            var none = new Color32(0, 0, 0, 0);
            bool any = false;
            foreach (int i in cells)
            {
                int c = i % m.w, r = i / m.w;
                for (int y = r * res; y < (r + 1) * res; y++)
                    for (int x = c * res; x < (c + 1) * res; x++)
                    {
                        int k = y * m.splatW + x;
                        for (int p = 0; p < 3; p++)
                        {
                            var want = p == plane ? full : none;
                            var have = m.splat[p][k];
                            if (have.r != want.r || have.g != want.g || have.b != want.b || have.a != want.a) { m.splat[p][k] = want; any = true; }
                        }
                    }
            }
            if (any) t.PaintChanged(c0 * res, r0 * res, c1 * res, r1 * res);
            return any;
        }

        public static bool RevertPaint(StrataTerrain t, double x, double z, float radius, float strength, float hardness)
        {
            var m = t.map; var o = t.Original;
            if (o == null) return false;
            float res = m.splatRes;
            double fx = (x - m.x0) * res, fy = (z - m.z0) * res;
            float R = radius * res;
            int x0 = Math.Max(0, (int)Math.Floor(fx - R)), x1 = Math.Min(m.splatW, (int)Math.Ceiling(fx + R) + 1);
            int y0 = Math.Max(0, (int)Math.Floor(fy - R)), y1 = Math.Min(m.splatH, (int)Math.Ceiling(fy + R) + 1);
            if (x1 <= x0 || y1 <= y0) return false;
            t.history.TouchPaint(x0, y0, x1, y1);
            var w = new float[StrataMap.Layers];
            bool any = false;
            for (int y = y0; y < y1; y++)
                for (int xx = x0; xx < x1; xx++)
                {
                    float d = (float)Math.Sqrt((xx + 0.5 - fx) * (xx + 0.5 - fx) + (y + 0.5 - fy) * (y + 0.5 - fy));
                    float a = strength * Falloff(d, R, hardness);
                    if (a <= 0f) continue;
                    int i = y * m.splatW + xx;
                    for (int k = 0; k < StrataMap.Layers; k++) w[k] = m.Weight(i, k) + (o.Weight(i, k) - m.Weight(i, k)) * a;
                    if (Store(m, i, w)) any = true;
                }
            if (any) t.PaintChanged(x0, y0, x1, y1);
            return any;
        }

        public static bool RevertPaintCells(StrataTerrain t, List<int> cells)
        {
            var m = t.map; var o = t.Original;
            if (o == null || cells.Count == 0) return false;
            Bounds(cells, m, out int c0, out int r0, out int c1, out int r1);
            int res = m.splatRes;
            t.history.TouchPaint(c0 * res, r0 * res, c1 * res, r1 * res);
            bool any = false;
            foreach (int i in cells)
            {
                int c = i % m.w, r = i / m.w;
                for (int y = r * res; y < (r + 1) * res; y++)
                    for (int x = c * res; x < (c + 1) * res; x++)
                    {
                        int k = y * m.splatW + x;
                        for (int p = 0; p < 3; p++)
                        {
                            var a = m.splat[p][k]; var b = o.splat[p][k];
                            if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a) { m.splat[p][k] = b; any = true; }
                        }
                    }
            }
            if (any) t.PaintChanged(c0 * res, r0 * res, c1 * res, r1 * res);
            return any;
        }

        static readonly int[] order = new int[StrataMap.Layers];
        static bool Store(StrataMap m, int texel, float[] w)
        {
            float sum = 0f;
            for (int k = 0; k < w.Length; k++) sum += Math.Max(0f, w[k]);
            if (sum <= 0f) return false;
            var b = new int[w.Length]; var frac = new float[w.Length];
            int total = 0;
            for (int k = 0; k < w.Length; k++)
            {
                float v = Math.Max(0f, w[k]) * 255f / sum;
                b[k] = (int)Math.Floor(v); frac[k] = v - b[k]; total += b[k];
                order[k] = k;
            }
            Array.Sort(order, (a, c) => frac[c].CompareTo(frac[a]));
            for (int k = 0; total < 255 && k < w.Length; k++, total++) b[order[k]]++;
            bool changed = false;
            for (int k = 0; k < w.Length; k++) if (m.Weight(texel, k) != b[k]) { m.SetWeight(texel, k, (byte)b[k]); changed = true; }
            return changed;
        }

        public static bool CanPlaceStair(StrataMap m, RectInt cells) =>
            cells.width > 0 && cells.height > 0 && cells.xMin >= 0 && cells.yMin >= 0 && cells.xMax <= m.w && cells.yMax <= m.h;

        public static List<int> FlightsIn(StrataMap m, RectInt cells)
        {
            var set = new SortedSet<int>();
            for (int r = Math.Max(0, cells.yMin); r < Math.Min(m.h, cells.yMax); r++)
                for (int c = Math.Max(0, cells.xMin); c < Math.Min(m.w, cells.xMax); c++)
                {
                    int f = m.stairOf[r * m.w + c];
                    if (f >= 0) set.Add(f);
                }
            return new List<int>(set);
        }

        public static (int cut, int replaced) StairOverlaps(StrataMap m, RectInt cells)
        {
            int cut = 0, replaced = 0;
            foreach (int fi in FlightsIn(m, cells)) { if (Trimmed(m, m.flights[fi], cells, out _)) cut++; else replaced++; }
            return (cut, replaced);
        }

        static bool Trimmed(StrataMap m, Flight f, RectInt cut, out Flight o)
        {
            o = f;
            int cx0 = cut.xMin + m.x0, cx1 = cut.xMax + m.x0, cz0 = cut.yMin + m.z0, cz1 = cut.yMax + m.z0;
            int ox0 = Math.Max(f.x0, cx0), ox1 = Math.Min(f.x1, cx1), oz0 = Math.Max(f.z0, cz0), oz1 = Math.Min(f.z1, cz1);
            if (ox1 <= ox0 || oz1 <= oz0) return true;
            bool fullX = ox0 <= f.x0 && ox1 >= f.x1, fullZ = oz0 <= f.z0 && oz1 >= f.z1;
            if (fullX && fullZ) return false;
            if (fullX)
            {
                if (oz0 <= f.z0) o.z0 = oz1; else if (oz1 >= f.z1) o.z1 = oz0; else return false;
            }
            else if (fullZ)
            {
                if (ox0 <= f.x0) o.x0 = ox1; else if (ox1 >= f.x1) o.x1 = ox0; else return false;
            }
            else return false;
            if (f.dir == StairDir.Landing) return true;
            var (dx, dz) = Flight.Step(f.dir);
            bool alongRun = dx != 0 ? o.x0 != f.x0 || o.x1 != f.x1 : o.z0 != f.z0 || o.z1 != f.z1;
            if (!alongRun) return true;
            double len = dx != 0 ? f.x1 - f.x0 : f.z1 - f.z0;
            double a0 = dx > 0 ? o.x0 - f.x0 : dx < 0 ? f.x1 - o.x1 : dz > 0 ? o.z0 - f.z0 : f.z1 - o.z1;
            double a1 = a0 + (dx != 0 ? o.x1 - o.x0 : o.z1 - o.z0);
            double rise = f.y1 - (double)f.y0;
            o.y0 = (float)(f.y0 + rise * a0 / len);
            o.y1 = (float)(f.y0 + rise * a1 / len);
            if (Math.Abs(o.y1 - o.y0) < StrataMap.Rise * 0.5f) { o.dir = StairDir.Landing; o.y1 = o.y0; o.steps = 0; }
            else o.steps = StepsFor(o.y0, o.y1);
            if (f.ground != null)
            {
                int pieces = o.dir == StairDir.Landing ? 1 : o.steps;
                o.ground = new float[pieces];
                for (int k = 0; k < pieces; k++) o.ground[k] = Math.Min(o.y0, o.y1);
            }
            return true;
        }

        static void ClearForStair(StrataTerrain t, RectInt cells)
        {
            var m = t.map;
            var list = FlightsIn(m, cells);
            for (int k = list.Count - 1; k >= 0; k--)
            {
                int fi = list[k];
                var f = m.flights[fi];
                if (!Trimmed(m, f, cells, out var o)) { RemoveFlight(t, fi); continue; }
                int c0 = f.x0 - m.x0, c1 = f.x1 - m.x0, r0 = f.z0 - m.z0, r1 = f.z1 - m.z0;
                t.history.TouchStairs();
                t.history.TouchCells(c0 - 1, r0 - 1, c1 + 1, r1 + 1);
                for (int r = Math.Max(0, r0); r < Math.Min(m.h, r1); r++)
                    for (int c = Math.Max(0, c0); c < Math.Min(m.w, c1); c++)
                    {
                        int x = m.x0 + c, z = m.z0 + r, i = r * m.w + c;
                        if (x >= o.x0 && x < o.x1 && z >= o.z0 && z < o.z1) continue;
                        if (m.stairOf[i] == fi) { m.stairOf[i] = -1; m.kind[i] = (byte)KindFor(m, c, r, m.Bottom(i)); }
                    }
                m.flights[fi] = o;
                t.StairsChanged();
                t.CellsChanged(c0 - 1, r0 - 1, c1 + 1, r1 + 1);
            }
        }

        // TODO el drouj el twal lezemhom landing fil wost wa7dou, tawa n7otouh bel yed kol mara
        public static int StepsFor(float y0, float y1) => Math.Max(1, (int)Math.Round(Math.Abs(y1 - y0) / StrataMap.Rise));

        public static int AddStair(StrataTerrain t, RectInt cells, StairDir dir, float y0, float y1, int style, bool timber, bool walls)
        {
            var m = t.map;
            if (!CanPlaceStair(m, cells)) return -1;
            if (dir == StairDir.Landing) y1 = y0;
            t.history.TouchStairs();
            t.history.TouchCells(cells.xMin - 1, cells.yMin - 1, cells.xMax + 1, cells.yMax + 1);
            ClearForStair(t, cells);
            int n = 1;
            string id;
            do id = $"stair{m.stairs.Count + n++}"; while (m.stairs.Exists(s => s.id == id));
            m.stairs.Add(new Stair { id = id, style = (byte)Math.Max(0, Math.Min(255, style)), walls = walls && !timber });
            var f = new Flight
            {
                x0 = m.x0 + cells.xMin, x1 = m.x0 + cells.xMax, z0 = m.z0 + cells.yMin, z1 = m.z0 + cells.yMax,
                dir = dir, y0 = y0, y1 = y1, steps = dir == StairDir.Landing ? 0 : StepsFor(y0, y1), stair = m.stairs.Count - 1,
            };
            double low = Math.Min(y0, y1);
            for (int r = cells.yMin - 1; r <= cells.yMax; r++)
                for (int c = cells.xMin - 1; c <= cells.xMax; c++)
                    if (m.InGrid(c, r)) low = Math.Min(low, m.Bottom(r * m.w + c));
            f.low = (float)(low - 0.6);
            if (timber)
            {
                int pieces = dir == StairDir.Landing ? 1 : f.steps;
                f.ground = new float[pieces];
                for (int k = 0; k < pieces; k++) f.ground[k] = GroundUnder(m, cells, dir, k, pieces, Math.Min(y0, y1));
            }
            m.flights.Add(f);
            short fi = (short)(m.flights.Count - 1);
            float bottom = Math.Min(y0, y1);
            for (int r = cells.yMin; r < cells.yMax; r++)
                for (int c = cells.xMin; c < cells.xMax; c++)
                {
                    int i = r * m.w + c, o = i * 4;
                    m.kind[i] = (byte)CellKind.Stair; m.stairOf[i] = fi;
                    m.corner[o] = m.corner[o + 1] = m.corner[o + 2] = m.corner[o + 3] = bottom;
                    m.wall[i] = 2; m.rim[i] = 2;
                }
            t.StairsChanged();
            t.CellsChanged(cells.xMin - 1, cells.yMin - 1, cells.xMax + 1, cells.yMax + 1);
            return fi;
        }

        //? when we remove a flight the stairOf of the flights after it get shifted right? i think yes but check
        static float GroundUnder(StrataMap m, RectInt cells, StairDir dir, int k, int pieces, float foot)
        {
            var (dx, dz) = Flight.Step(dir);
            int len = dx != 0 ? cells.width : cells.height, wid = dx != 0 ? cells.height : cells.width;
            int at = Math.Min(len - 1, (int)((k + 0.5) * len / pieces));
            float g = foot;
            for (int a = 0; a < len; a++)
            {
                if (dir != StairDir.Landing && a != at) continue;
                for (int e = 0; e < wid; e++)
                {
                    int c = dx > 0 ? cells.xMin + a : dx < 0 ? cells.xMax - 1 - a : cells.xMin + e;
                    int r = dz > 0 ? cells.yMin + a : dz < 0 ? cells.yMax - 1 - a : dx != 0 ? cells.yMin + e : cells.yMin + a;
                    if (m.InGrid(c, r) && m.kind[r * m.w + c] != (byte)CellKind.Stair) g = Math.Min(g, m.Bottom(r * m.w + c));
                }
            }
            return g;
        }

        public static bool RemoveFlight(StrataTerrain t, int flight)
        {
            var m = t.map;
            if (flight < 0 || flight >= m.flights.Count) return false;
            var f = m.flights[flight];
            int c0 = f.x0 - m.x0, c1 = f.x1 - m.x0, r0 = f.z0 - m.z0, r1 = f.z1 - m.z0;
            t.history.TouchStairs();
            t.history.TouchCells(c0 - 1, r0 - 1, c1 + 1, r1 + 1);
            for (int k = flight + 1; k < m.flights.Count; k++)
            {
                var g = m.flights[k];
                t.history.TouchCells(g.x0 - m.x0, g.z0 - m.z0, g.x1 - m.x0, g.z1 - m.z0);
            }
            float foot = Math.Min(f.y0, f.y1);
            for (int r = Math.Max(0, r0); r < Math.Min(m.h, r1); r++)
                for (int c = Math.Max(0, c0); c < Math.Min(m.w, c1); c++)
                {
                    int i = r * m.w + c, o = i * 4;
                    if (m.stairOf[i] != flight) continue;
                    m.stairOf[i] = -1;
                    m.kind[i] = (byte)KindFor(m, c, r, foot);
                    m.corner[o] = m.corner[o + 1] = m.corner[o + 2] = m.corner[o + 3] = foot;
                }
            m.flights.RemoveAt(flight);
            for (int i = 0; i < m.stairOf.Length; i++) if (m.stairOf[i] > flight) m.stairOf[i]--;
            int s = f.stair;
            if (!m.flights.Exists(x => x.stair == s))
            {
                m.stairs.RemoveAt(s);
                for (int k = 0; k < m.flights.Count; k++) if (m.flights[k].stair > s) { var x = m.flights[k]; x.stair--; m.flights[k] = x; }
            }
            t.StairsChanged();
            t.CellsChanged(c0 - 1, r0 - 1, c1 + 1, r1 + 1);
            return true;
        }

        public static bool Shape(StrataTerrain t, RectInt cells, ShapeProfile profile, float amount, bool alongX)
        {
            var m = t.map;
            var hgt = ShapeHeights(m, ref cells, profile, amount, alongX);
            if (hgt == null) return false;
            int vw = cells.width + 1;
            t.history.TouchCells(cells.xMin - 1, cells.yMin - 1, cells.xMax + 1, cells.yMax + 1);
            var edited = new HashSet<int>();
            for (int r = cells.yMin; r < cells.yMax; r++)
                for (int c = cells.xMin; c < cells.xMax; c++)
                {
                    int i = r * m.w + c;
                    if (m.kind[i] == (byte)CellKind.Stair) continue;
                    int a = c - cells.xMin, b = r - cells.yMin;
                    m.corner[i * 4] = (float)hgt[b * vw + a];
                    m.corner[i * 4 + 1] = (float)hgt[b * vw + a + 1];
                    m.corner[i * 4 + 2] = (float)hgt[(b + 1) * vw + a + 1];
                    m.corner[i * 4 + 3] = (float)hgt[(b + 1) * vw + a];
                    m.kind[i] = (byte)CellKind.Natural;
                    edited.Add(i);
                }
            Seal(m, edited, edited, 0f);
            t.CellsChanged(cells.xMin - 1, cells.yMin - 1, cells.xMax + 1, cells.yMax + 1);
            return true;
        }

        public static double[] ShapeHeights(StrataMap m, ref RectInt cells, ShapeProfile profile, float amount, bool alongX)
        {
            cells.xMin = Math.Max(0, cells.xMin); cells.yMin = Math.Max(0, cells.yMin);
            cells.xMax = Math.Min(m.w, cells.xMax); cells.yMax = Math.Min(m.h, cells.yMax);
            if (cells.width < 2 || cells.height < 2) return null;
            int vw = cells.width + 1, vh = cells.height + 1;
            var hgt = Vertices(m, cells);
            var o = (double[])hgt.Clone();
            double At(int a, int b) => o[b * vw + a];
            if (profile == ShapeProfile.Smooth)
            {
                int iters = Math.Max(1, (int)Math.Round(Math.Abs(amount) * 4));
                for (int it = 0; it < iters; it++)
                {
                    var src = (double[])hgt.Clone();
                    for (int b = 1; b < vh - 1; b++)
                        for (int a = 1; a < vw - 1; a++)
                            hgt[b * vw + a] = (src[b * vw + a - 1] + src[b * vw + a + 1] + src[(b - 1) * vw + a] + src[(b + 1) * vw + a]) / 4;
                }
            }
            else
            {
                for (int b = 1; b < vh - 1; b++)
                    for (int a = 1; a < vw - 1; a++)
                    {
                        double ua = 2.0 * a / (vw - 1) - 1, vb = 2.0 * b / (vh - 1) - 1;
                        double u = alongX ? vb : ua, v = alongX ? ua : vb;
                        double edge = 1 - Math.Pow(Math.Abs(v), 6);
                        if (profile == ShapeProfile.Valley) hgt[b * vw + a] = o[b * vw + a] - amount * (1 - u * u) * edge;
                        else if (profile == ShapeProfile.Hill) hgt[b * vw + a] = o[b * vw + a] + amount * (1 - u * u) * (1 - v * v);
                        else if (profile == ShapeProfile.Crater)
                        {
                            double d = Math.Sqrt(u * u + v * v), wall = Smooth(0.35, 0.72, d);
                            hgt[b * vw + a] = o[b * vw + a] - amount * (1 - wall) + amount * 0.25 * wall * (1 - Smooth(0.72, 1, d));
                        }
                        else
                        {
                            double s = (v + 1) / 2;
                            double h0 = alongX ? At(0, b) : At(a, 0), h1 = alongX ? At(vw - 1, b) : At(a, vh - 1);
                            hgt[b * vw + a] = h0 + (h1 - h0) * s;
                        }
                    }
            }
            return hgt;
        }

        static double Smooth(double e0, double e1, double x)
        {
            double s = Math.Max(0, Math.Min(1, (x - e0) / (e1 - e0)));
            return s * s * (3 - 2 * s);
        }

        public static double[] Vertices(StrataMap m, RectInt cells)
        {
            int vw = cells.width + 1, vh = cells.height + 1;
            var hgt = new double[vw * vh];
            for (int k = 0; k < hgt.Length; k++) hgt[k] = double.MinValue;
            int[] dx = { 0, 1, 1, 0 }, dz = { 0, 0, 1, 1 };
            for (int r = cells.yMin; r < cells.yMax; r++)
                for (int c = cells.xMin; c < cells.xMax; c++)
                {
                    int i = r * m.w + c;
                    for (int q = 0; q < 4; q++)
                    {
                        int k = (r - cells.yMin + dz[q]) * vw + (c - cells.xMin + dx[q]);
                        hgt[k] = Math.Max(hgt[k], m.corner[i * 4 + q]);
                    }
                }
            return hgt;
        }
    }
}
