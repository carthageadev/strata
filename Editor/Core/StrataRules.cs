using System;
using System.Collections.Generic;

namespace Strata
{
    public struct Wall
    {
        public int hi, lo;
        public int side;
        public int x0, z0, x1, z1;
        public double t0, t1, b0, b1;
        public int style, rim;
        public int order;
    }

    public static class StrataRules
    {
        public const double Eps = 0.1;

        const byte Natural = (byte)CellKind.Natural, Raised = (byte)CellKind.Raised, Sunken = (byte)CellKind.Sunken, Basin = (byte)CellKind.Basin, StairK = (byte)CellKind.Stair;
        const int Rock = 1, Masonry = 2, Turf = 1, Coping = 2;

        static double C(StrataMap m, int k) => m.corner[k];

        public static double CellTop(StrataMap m, int i)
        {
            int o = i * 4;
            return Math.Max(Math.Max(C(m, o), C(m, o + 1)), Math.Max(C(m, o + 2), C(m, o + 3)));
        }

        //? landing only check y0, ok for now but a landing between two flights with diffrent height??
        public static bool StairConnects(StrataMap m, int s, int n, int dx, int dz)
        {
            int fi = m.stairOf[s];
            if (fi < 0 || fi >= m.flights.Count) return false;
            var f = m.flights[fi];
            double top = CellTop(m, n);
            if (m.kind[n] == StairK) return true;
            if (f.dir == StairDir.Landing) return Math.Abs(f.y0 - top) < 0.15;
            var (ax, az) = Flight.Step(f.dir);
            if (ax == dx && az == dz) return Math.Abs(f.y1 - top) < 0.15;
            if (ax == -dx && az == -dz) return Math.Abs(f.y0 - top) < 0.15;
            return false;
        }

        public static bool Edge(StrataMap m, int i, int j, int side, double a0, double a1, double b0, double b1, int x0, int z0, int x1, int z1, int order, out Wall wall)
        {
            wall = default;
            double d0 = a0 - b0, d1 = a1 - b1;
            if (Math.Abs(d0) < Eps && Math.Abs(d1) < Eps) return false;
            byte ki = m.kind[i], kj = m.kind[j];
            bool iHigh = d0 + d1 > 0;
            int hi = iHigh ? i : j, lo = iHigh ? j : i;
            if (m.kind[hi] == StairK) return false;
            if (ki == StairK || kj == StairK)
            {
                int s = ki == StairK ? i : j, nb = s == i ? j : i;
                int dx = side == 1 ? 1 : 0, dz = side == 1 ? 0 : 1;
                if (s != i) { dx = -dx; dz = -dz; }
                if (StairConnects(m, s, nb, dx, dz)) return false;
            }
            int sideFromHi = iHigh ? side : (side + 2) % 4;
            double t0 = Math.Max(a0, b0), t1 = Math.Max(a1, b1), bb0 = Math.Min(a0, b0), bb1 = Math.Min(a1, b1);

            int style = m.wall[hi], rim = m.rim[hi];
            byte kh = m.kind[hi], kl = m.kind[lo];
            if (kh != Raised && (kl == Sunken || kl == Basin)) { style = m.wall[lo]; rim = m.rim[lo]; }
            else if (kh == Natural) { style = kl == StairK ? Masonry : Rock; rim = kl == StairK ? Coping : Turf; }
            if (style == 0) { style = Rock; rim = Turf; }
            if (kl == StairK)
            {
                int fi = m.stairOf[lo];
                if (fi >= 0 && fi < m.flights.Count)
                {
                    var f = m.flights[fi];
                    double low = Math.Min(Math.Min(bb0, bb1), Math.Min((double)f.y0, f.y1)) - 0.6;
                    bb0 = bb1 = low;
                }
            }
            wall = new Wall { hi = hi, lo = lo, side = sideFromHi, x0 = x0, z0 = z0, x1 = x1, z1 = z1, t0 = t0, t1 = t1, b0 = bb0, b1 = bb1, style = style, rim = rim, order = order };
            return true;
        }

        public static bool EastWall(StrataMap m, int c, int r, out Wall wall)
        {
            wall = default;
            if (c < 0 || r < 0 || c >= m.w - 1 || r >= m.h) return false;
            int i = r * m.w + c, j = i + 1, o = i * 4, p = j * 4;
            return Edge(m, i, j, 1, C(m, o + 1), C(m, o + 2), C(m, p), C(m, p + 3), m.x0 + c + 1, m.z0 + r, m.x0 + c + 1, m.z0 + r + 1, i * 2, out wall);
        }

        public static bool SouthWall(StrataMap m, int c, int r, out Wall wall)
        {
            wall = default;
            if (c < 0 || r < 0 || c >= m.w || r >= m.h - 1) return false;
            int i = r * m.w + c, j = i + m.w, o = i * 4, p = j * 4;
            return Edge(m, i, j, 2, C(m, o + 3), C(m, o + 2), C(m, p), C(m, p + 1), m.x0 + c, m.z0 + r + 1, m.x0 + c + 1, m.z0 + r + 1, i * 2 + 1, out wall);
        }

        public static void Walls(StrataMap m, int c0, int r0, int c1, int r1, List<Wall> into)
        {
            c0 = Math.Max(0, c0); r0 = Math.Max(0, r0); c1 = Math.Min(m.w, c1); r1 = Math.Min(m.h, r1);
            for (int r = r0; r < r1; r++)
                for (int c = c0; c < c1; c++)
                {
                    if (EastWall(m, c, r, out var e)) into.Add(e);
                    if (SouthWall(m, c, r, out var s)) into.Add(s);
                }
        }

        static readonly int[,] Diagonals = { { 1, -1, 1, 0 }, { 1, 1, 2, 3 }, { -1, 1, 3, 2 }, { -1, -1, 0, 1 } };

        public static UnityEngine.Color32 Mask(StrataMap m, int c, int r)
        {
            int i = r * m.w + c;
            int drop = 0, rise = 0, rims = 0;
            bool wide = m.WideCodes;
            void Take(in Wall wl)
            {
                if (wl.hi == i) { drop |= 1 << wl.side; rims |= wide ? Math.Min(wl.rim, 15) << (wl.side * 4) : wl.rim << (wl.side * 2); }
                if (wl.lo == i) rise |= 1 << ((wl.side + 2) % 4);
            }
            if (EastWall(m, c, r, out var a)) Take(a);
            if (EastWall(m, c - 1, r, out var b)) Take(b);
            if (SouthWall(m, c, r, out var s)) Take(s);
            if (SouthWall(m, c, r - 1, out var n)) Take(n);
            if (r >= 1 && r < m.h - 1 && c >= 1 && c < m.w - 1)
            {
                for (int k = 0; k < 4; k++)
                {
                    int j = (r + Diagonals[k, 1]) * m.w + (c + Diagonals[k, 0]);
                    double av = C(m, i * 4 + Diagonals[k, 2]), bv = C(m, j * 4 + Diagonals[k, 3]);
                    if (av - bv > Eps) drop |= 1 << (4 + k);
                    else if (bv - av > Eps) rise |= 1 << (4 + k);
                }
            }
            if (wide) return new UnityEngine.Color32((byte)drop, (byte)rise, (byte)(rims & 255), (byte)(rims >> 8));
            return new UnityEngine.Color32((byte)drop, (byte)rise, (byte)rims, m.kind[i] == StairK ? (byte)255 : (byte)0);
        }

        public static void Neighbours(StrataMap m, List<Wall> walls, List<(int left, int right)> into, List<(int left, int right)> picks = null)
        {
            var ends = new Dictionary<long, List<int>>();
            long Key(int x, int z) => (long)(z - m.z0) * (m.w + 1) + (x - m.x0);
            for (int k = 0; k < walls.Count; k++)
            {
                var w = walls[k];
                Add(Key(w.x0, w.z0), k);
                Add(Key(w.x1, w.z1), k);
            }
            void Add(long key, int k)
            {
                if (!ends.TryGetValue(key, out var list)) ends[key] = list = new List<int>(4);
                list.Add(k);
            }
            int Pick(int k, long key)
            {
                var w = walls[k];
                int best = -1;
                foreach (var j in ends[key])
                {
                    if (j == k) continue;
                    if (walls[j].side == w.side) { best = j; break; }
                    if (best < 0) best = j;
                }
                return best;
            }
            into.Clear();
            picks?.Clear();
            for (int k = 0; k < walls.Count; k++)
            {
                int l = Pick(k, Key(walls[k].x0, walls[k].z0)), r = Pick(k, Key(walls[k].x1, walls[k].z1));
                into.Add((l >= 0 ? walls[l].style : walls[k].style, r >= 0 ? walls[r].style : walls[k].style));
                picks?.Add((l, r));
            }
        }
    }
}
