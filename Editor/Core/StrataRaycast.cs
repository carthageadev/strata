using System;
using UnityEngine;

namespace Strata
{
    public struct StrataHit
    {
        public double x, y, z;
        public int cell, c, r;
        public bool wall;
        public Vector3 World => new Vector3((float)x, (float)y, (float)-z);
    }

    public static class StrataRaycast
    {
        //? 2000 max distance is enough for big maps? never had problem but who knows
        public static bool Cast(StrataMap m, Ray worldRay, out StrataHit hit, float maxDistance = 2000f)
        {
            hit = default;
            double ox = worldRay.origin.x, oy = worldRay.origin.y, oz = -worldRay.origin.z;
            double dx = worldRay.direction.x, dy = worldRay.direction.y, dz = -worldRay.direction.z;
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 1e-9) return false;
            dx /= len; dy /= len; dz /= len;
            Range(m, out double lo, out double hi);
            double t0 = 0, t1 = maxDistance;
            if (!Clip(ox, dx, m.x0, m.x0 + m.w, ref t0, ref t1) || !Clip(oz, dz, m.z0, m.z0 + m.h, ref t0, ref t1) || !Clip(oy, dy, lo - 1, hi + 1, ref t0, ref t1)) return false;
            const double step = 0.05;
            double prevT = t0;
            bool prevAbove = Above(m, ox, oy, oz, dx, dy, dz, t0);
            if (!prevAbove) { Fill(m, ox, oy, oz, dx, dy, dz, t0, ref hit); return true; }
            for (double t = t0 + step; t <= t1 + step; t += step)
            {
                double tt = Math.Min(t, t1);
                if (!Above(m, ox, oy, oz, dx, dy, dz, tt))
                {
                    double a = prevT, b = tt;
                    for (int k = 0; k < 24; k++)
                    {
                        double mid = (a + b) / 2;
                        if (Above(m, ox, oy, oz, dx, dy, dz, mid)) a = mid; else b = mid;
                    }
                    Fill(m, ox, oy, oz, dx, dy, dz, b, ref hit);
                    double xa = ox + dx * a, za = oz + dz * a;
                    int ca = m.CellAt(xa, za);
                    hit.wall = ca >= 0 && ca != hit.cell && m.Height(hit.x, hit.z) - m.Height(xa, za) > StrataRules.Eps;
                    return true;
                }
                prevT = tt;
                if (tt >= t1) break;
            }
            return false;
        }

        static bool Above(StrataMap m, double ox, double oy, double oz, double dx, double dy, double dz, double t)
        {
            double x = ox + dx * t, z = oz + dz * t;
            int i = m.CellAt(x, z);
            if (i < 0) return true;
            return oy + dy * t > m.Height(x, z);
        }

        static void Fill(StrataMap m, double ox, double oy, double oz, double dx, double dy, double dz, double t, ref StrataHit hit)
        {
            hit.x = ox + dx * t; hit.z = oz + dz * t;
            hit.cell = m.CellAt(hit.x, hit.z);
            if (hit.cell < 0) hit.cell = m.CellAt(Math.Min(m.x0 + m.w - 1e-6, Math.Max(m.x0, hit.x)), Math.Min(m.z0 + m.h - 1e-6, Math.Max(m.z0, hit.z)));
            hit.c = hit.cell % m.w; hit.r = hit.cell / m.w;
            hit.y = m.Height(hit.x, hit.z);
        }

        static bool Clip(double o, double d, double min, double max, ref double t0, ref double t1)
        {
            if (Math.Abs(d) < 1e-12) return o >= min && o <= max;
            double a = (min - o) / d, b = (max - o) / d;
            if (a > b) (a, b) = (b, a);
            t0 = Math.Max(t0, a); t1 = Math.Min(t1, b);
            return t0 <= t1;
        }

        static long rangeRevision = -1;
        static StrataMap rangeMap;
        static double rangeLo, rangeHi;

        public static void Range(StrataMap m, out double lo, out double hi)
        {
            if (rangeMap != m || rangeRevision != m.revision)
            {
                rangeLo = double.MaxValue; rangeHi = double.MinValue;
                foreach (var v in m.corner) { if (v < rangeLo) rangeLo = v; if (v > rangeHi) rangeHi = v; }
                foreach (var f in m.flights) { rangeLo = Math.Min(rangeLo, Math.Min(f.y0, f.y1)); rangeHi = Math.Max(rangeHi, Math.Max(f.y0, f.y1)); }
                rangeMap = m; rangeRevision = m.revision;
            }
            lo = rangeLo; hi = rangeHi;
        }
    }
}
