using System;
using System.Collections.Generic;
using UnityEngine;

namespace Strata
{
    public struct StairLook
    {
        public Vector2 stone, tread, deck, timber;
        public static StairLook Default => new StairLook { stone = new Vector2(256, 256), tread = new Vector2(256, 256), deck = new Vector2(128, 128), timber = new Vector2(64, 128) };
        public static double Scale(Vector2 tex) => tex.x > 0 ? 16.0 / tex.x : 1.0 / 16;
    }

    public struct StairStyle
    {
        public bool timber;
        public int body, tread, cheek, plank, post;
        public StairLook look;
        public StairModel step, first, last;
        public bool stack;
        public int turn;
        public bool Models => step != null;

        public static StairStyle[] Classic(StairLook look) => new[]
        {
            new StairStyle { timber = false, body = 0, tread = 1, cheek = 2, plank = 3, post = 4, look = look },
            new StairStyle { timber = true, body = 0, tread = 1, cheek = 2, plank = 3, post = 4, look = look },
        };

        public static int Slots(StairStyle[] styles)
        {
            int n = StrataStairs.Submeshes;
            foreach (var s in styles)
            {
                n = Math.Max(n, Math.Max(Math.Max(s.body, s.tread), Math.Max(s.cheek, Math.Max(s.plank, s.post))) + 1);
                foreach (var m in new[] { s.step, s.first, s.last }) if (m != null) foreach (var p in m.parts) n = Math.Max(n, p.slot + 1);
            }
            return n;
        }
    }

    public sealed class StairModel
    {
        public readonly List<Vector3> pos = new List<Vector3>(), nor = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<(int slot, int[] tris)> parts = new List<(int, int[])>();
        public Bounds bounds;

        public static StairModel From(GameObject go, List<Material> materials, int slotBase)
        {
            if (!go) return null;
            var m = new StairModel();
            var root = go.transform.worldToLocalMatrix;
            bool any = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (!mesh || !mesh.isReadable && mesh.vertexCount == 0) continue;
                Vector3[] v; Vector3[] n; Vector2[] t;
                try { v = mesh.vertices; n = mesh.normals; t = mesh.uv; } catch (Exception) { continue; }
                if (v.Length == 0) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                var mats = mr ? mr.sharedMaterials : new Material[0];
                var to = root * mf.transform.localToWorldMatrix;
                var tn = to.inverse.transpose;
                int at = m.pos.Count;
                for (int i = 0; i < v.Length; i++)
                {
                    var p = to.MultiplyPoint3x4(v[i]);
                    m.pos.Add(p);
                    m.nor.Add(n.Length == v.Length ? tn.MultiplyVector(n[i]).normalized : Vector3.up);
                    m.uv.Add(t.Length == v.Length ? t[i] : Vector2.zero);
                    if (!any) { m.bounds = new Bounds(p, Vector3.zero); any = true; } else m.bounds.Encapsulate(p);
                }
                bool flip = to.determinant < 0;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = mats.Length > 0 ? mats[Math.Min(s, mats.Length - 1)] : null;
                    int k = materials.IndexOf(mat);
                    if (k < 0) { materials.Add(mat); k = materials.Count - 1; }
                    var tri = mesh.GetTriangles(s);
                    var o = new int[tri.Length];
                    for (int i = 0; i + 2 < tri.Length; i += 3)
                    {
                        o[i] = tri[i] + at;
                        o[i + 1] = (flip ? tri[i + 2] : tri[i + 1]) + at;
                        o[i + 2] = (flip ? tri[i + 1] : tri[i + 2]) + at;
                    }
                    m.parts.Add((slotBase + k, o));
                }
            }
            return any ? m : null;
        }
    }

    public static class StrataStairs
    {
        public const int Submeshes = 5;
        const double Texel = 1.0 / 16.0, TT = 0.125;

        sealed class Builder
        {
            public readonly MeshData data;
            readonly double px, pz;
            public Builder(MeshData data, double px, double pz) { this.data = data; this.px = px; this.pz = pz; }

            public void Tri(int sub, Vector3d a, Vector3d b, Vector3d c, Vector3d n, Vector2d ua, Vector2d ub, Vector2d uc, double ca, double cb, double cc)
            {
                if (sub < 0) return;
                var t = data.Sub(sub);
                int ia = V(a, n, ua, ca), ib = V(b, n, ub, cb), ic = V(c, n, uc, cc);
                t.Add(ia); t.Add(ic); t.Add(ib);
            }

            int V(Vector3d p, Vector3d n, Vector2d uv, double col)
            {
                float c = StrataMesher.N3(col);
                return data.Add(
                    new Vector3((float)(StrataMesher.P4(p.x) - px), StrataMesher.P4(p.y), -(float)(StrataMesher.P4(p.z) - pz)),
                    new Vector3(StrataMesher.N3(n.x), StrataMesher.N3(n.y), -StrataMesher.N3(n.z)),
                    new Vector2(StrataMesher.U5(uv.x), StrataMesher.U5(uv.y)),
                    new Color(c, c, c, 1f));
            }
        }

        public struct Vector3d
        {
            public double x, y, z;
            public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
            public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.x + b.x, a.y + b.y, a.z + b.z);
            public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.x - b.x, a.y - b.y, a.z - b.z);
            public static Vector3d operator *(Vector3d a, double k) => new Vector3d(a.x * k, a.y * k, a.z * k);
            public static Vector3d Cross(Vector3d a, Vector3d b) => new Vector3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
            public Vector3d Normalized { get { double l = Math.Sqrt(x * x + y * y + z * z); return l > 0 ? new Vector3d(x / l, y / l, z / l) : this; } }
        }

        public struct Vector2d
        {
            public double x, y;
            public Vector2d(double x, double y) { this.x = x; this.y = y; }
        }

        public static double[] TexelSplits(double len, int n)
        {
            double T = Math.Floor(len / Texel + 0.5);
            var o = new double[n + 1];
            for (int k = 1; k <= n; k++) o[k] = Math.Floor(k * T / n + 0.5) * Texel;
            return o;
        }

        struct Piece { public double x0, x1, z0, z1, top; }

        static List<Piece> Pieces(Flight f)
        {
            var list = new List<Piece>();
            if (f.dir == StairDir.Landing) { list.Add(new Piece { x0 = f.x0, x1 = f.x1, z0 = f.z0, z1 = f.z1, top = f.y0 }); return list; }
            var (dx, dz) = Flight.Step(f.dir);
            double len = dx != 0 ? f.x1 - f.x0 : f.z1 - f.z0;
            var cuts = TexelSplits(len, f.steps);
            for (int k = 0; k < f.steps; k++)
            {
                double a = cuts[k], b = cuts[k + 1];
                double top = f.y0 + ((double)f.y1 - f.y0) * (k + 1) / f.steps;
                if (dx > 0) list.Add(new Piece { x0 = f.x0 + a, x1 = f.x0 + b, z0 = f.z0, z1 = f.z1, top = top });
                else if (dx < 0) list.Add(new Piece { x0 = f.x1 - b, x1 = f.x1 - a, z0 = f.z0, z1 = f.z1, top = top });
                else if (dz > 0) list.Add(new Piece { x0 = f.x0, x1 = f.x1, z0 = f.z0 + a, z1 = f.z0 + b, top = top });
                else list.Add(new Piece { x0 = f.x0, x1 = f.x1, z0 = f.z1 - b, z1 = f.z1 - a, top = top });
            }
            return list;
        }

        public static void Build(StrataMap m, int stairIndex, StairLook look, double px, double pz, MeshData into) =>
            Build(m, stairIndex, StairStyle.Classic(look), px, pz, into);

        public static void Build(StrataMap m, int stairIndex, StairStyle[] styles, double px, double pz, MeshData into)
        {
            var b = new Builder(into, px, pz);
            for (int s = 0; s < StairStyle.Slots(styles); s++) into.Sub(s);
            var def = m.stairs[stairIndex];
            var st = styles[def.style < styles.Length ? def.style : 0];
            var look = st.look;
            bool stone = !st.timber;
            for (int fi = 0; fi < m.flights.Count; fi++)
            {
                var f = m.flights[fi];
                if (f.stair != stairIndex) continue;
                if (st.Models) { ModelFlight(into, f, st, px, pz); continue; }
                double low = f.low;
                var pieces = Pieces(f);
                if (stone) StoneBody(b, f, low, (float)StairLook.Scale(look.stone), st.body);
                for (int k = 0; k < pieces.Count; k++)
                {
                    var p = pieces[k];
                    double cx = (p.x0 + p.x1) / 2, cz = (p.z0 + p.z1) / 2, pw = p.x1 - p.x0, pd = p.z1 - p.z0;
                    if (stone)
                    {
                        double nose = f.dir != StairDir.Landing ? 1.0 / 16 : 0;
                        var (ndx, ndz) = Flight.Step(f.dir);
                        BevelBox(b, st.tread, pw + Math.Abs(ndx) * nose, TT, pd + Math.Abs(ndz) * nose, 1.0 / 16, look.tread, 0, 0, new Vector3d(cx - ndx * nose / 2, p.top - TT / 2, cz - ndz * nose / 2));
                    }
                    else
                    {
                        Box(b, st.plank, pw - 0.02, 0.12, pd - 0.02, look.deck, true, new Vector3d(cx, p.top - 0.06, cz), false);
                        double g = f.ground != null && k < f.ground.Length ? f.ground[k] : GroundUnder(m, cx, cz);
                        bool ew = f.dir == StairDir.E || f.dir == StairDir.W;
                        for (int sx = -1; sx <= 1; sx += 2)
                        {
                            double ppx = ew ? cx : cx + sx * (pw / 2 - 0.12), ppz = ew ? cz + sx * (pd / 2 - 0.12) : cz;
                            Box(b, st.post, 0.14, p.top - g + 0.2, 0.14, look.timber, true, new Vector3d(ppx, (p.top + g - 0.2) / 2 - 0.06, ppz), false);
                        }
                    }
                }
                if (def.walls && st.cheek >= 0) CheekWalls(b, m, f, low, look, st);
            }
        }

        static void ModelFlight(MeshData into, Flight f, StairStyle st, double px, double pz)
        {
            var pieces = Pieces(f);
            var (ax, az) = Flight.Step(f.dir == StairDir.Landing ? StairDir.S : f.dir);
            var fwd = new Vector3(ax, 0, -az);
            var right = Vector3.Cross(Vector3.up, fwd);
            double rise = f.dir == StairDir.Landing || f.steps <= 0 ? StrataMap.Rise : Math.Abs((double)f.y1 - f.y0) / f.steps;
            double foot = Math.Min(f.y0, f.y1);
            for (int k = 0; k < pieces.Count; k++)
            {
                var p = pieces[k];
                var model = k == 0 && st.first != null ? st.first : k == pieces.Count - 1 && st.last != null ? st.last : st.step;
                double across = ax != 0 ? p.z1 - p.z0 : p.x1 - p.x0, along = ax != 0 ? p.x1 - p.x0 : p.z1 - p.z0;
                var centre = new Vector3((float)((p.x0 + p.x1) / 2 - px), 0, (float)-((p.z0 + p.z1) / 2 - pz));
                int copies = st.stack ? Math.Max(1, (int)Math.Round((p.top - foot) / rise)) : 1;
                for (int c = 0; c < copies; c++)
                {
                    double top = p.top - c * rise;
                    var part = c == 0 ? model : st.step;
                    Place(into, part, centre + Vector3.up * (float)(top - rise / 2), right, fwd, (float)across, (float)rise, (float)along, st.turn);
                }
            }
        }

        static void Place(MeshData into, StairModel m, Vector3 centre, Vector3 right, Vector3 fwd, float sx, float sy, float sz, int turn)
        {
            if (m == null) return;
            var b = m.bounds;
            var size = new Vector3(Mathf.Max(b.size.x, 1e-4f), Mathf.Max(b.size.y, 1e-4f), Mathf.Max(b.size.z, 1e-4f));
            Vector3 Turn(Vector3 v) { for (int t = 0; t < (turn & 3); t++) v = new Vector3(v.z, v.y, -v.x); return v; }
            var tsize = Turn(size); tsize = new Vector3(Mathf.Abs(tsize.x), Mathf.Abs(tsize.y), Mathf.Abs(tsize.z));
            var k = new Vector3(sx / tsize.x, sy / tsize.y, sz / tsize.z);
            int at = into.VertexCount;
            for (int i = 0; i < m.pos.Count; i++)
            {
                var l = Turn(m.pos[i] - b.center);
                var w = centre + right * (l.x * k.x) + Vector3.up * (l.y * k.y) + fwd * (l.z * k.z);
                var nl = Turn(m.nor[i]);
                var n = (right * (nl.x / k.x) + Vector3.up * (nl.y / k.y) + fwd * (nl.z / k.z)).normalized;
                into.Add(new Vector3(StrataMesher.P4(w.x), StrataMesher.P4(w.y), StrataMesher.P4(w.z)), n, m.uv[i], Color.white);
            }
            foreach (var (slot, tris) in m.parts)
            {
                var t = into.Sub(slot);
                foreach (int v in tris) t.Add(v + at);
            }
        }

        static double GroundUnder(StrataMap m, double x, double z)
        {
            int i = m.CellAt(x, z);
            return i >= 0 ? m.Bottom(i) : 0;
        }

        static void StoneBody(Builder b, Flight f, double low, float scale, int sub)
        {
            int w = f.x1 - f.x0, d = f.z1 - f.z0;
            var prof = new List<Vector2d>();
            double depth;
            if (f.dir == StairDir.Landing)
            {
                double L = d;
                prof.Add(new Vector2d(0, low)); prof.Add(new Vector2d(L, low)); prof.Add(new Vector2d(L, f.y0 - TT)); prof.Add(new Vector2d(0, f.y0 - TT));
                depth = w;
            }
            else
            {
                var (dx, _) = Flight.Step(f.dir);
                double L = dx != 0 ? w : d;
                var cuts = TexelSplits(L, f.steps);
                prof.Add(new Vector2d(0, low)); prof.Add(new Vector2d(L, low));
                for (int k = f.steps - 1; k >= 0; k--)
                {
                    double top = f.y0 + ((double)f.y1 - f.y0) * (k + 1) / f.steps - TT;
                    prof.Add(new Vector2d(cuts[k + 1], top)); prof.Add(new Vector2d(cuts[k], top));
                }
                depth = dx != 0 ? d : w;
            }
            for (int i = prof.Count - 1; i > 0; i--) if (Math.Abs(prof[i].x - prof[i - 1].x) < 1e-9 && Math.Abs(prof[i].y - prof[i - 1].y) < 1e-9) prof.RemoveAt(i);
            if (prof.Count > 1 && Math.Abs(prof[0].x - prof[prof.Count - 1].x) < 1e-9 && Math.Abs(prof[0].y - prof[prof.Count - 1].y) < 1e-9) prof.RemoveAt(prof.Count - 1);
            if (SignedArea(prof) < 0) prof.Reverse();

            var dir = f.dir == StairDir.Landing ? StairDir.S : f.dir;
            double rot = dir == StairDir.E ? 0 : dir == StairDir.W ? Math.PI : dir == StairDir.S ? -Math.PI / 2 : Math.PI / 2;
            double ox = dir == StairDir.E || dir == StairDir.N ? f.x0 : f.x1, oz = dir == StairDir.E || dir == StairDir.S ? f.z0 : f.z1;
            double cs = Math.Cos(rot), sn = Math.Sin(rot);
            Vector3d P(double u, double v, double e) => new Vector3d(ox + u * cs + e * sn, v, oz - u * sn + e * cs);
            Vector3d Dir(double u, double v, double e) => new Vector3d(u * cs + e * sn, v, -u * sn + e * cs);

            var tris = Triangulate(prof);
            var n0 = Dir(0, 0, -1).Normalized; var n1 = Dir(0, 0, 1).Normalized;
            for (int t = 0; t < tris.Count; t += 3)
            {
                var A = prof[tris[t]]; var B = prof[tris[t + 1]]; var C = prof[tris[t + 2]];
                Face(b, sub, P(A.x, A.y, 0), P(C.x, C.y, 0), P(B.x, B.y, 0), n0, scale, low);
                Face(b, sub, P(A.x, A.y, depth), P(B.x, B.y, depth), P(C.x, C.y, depth), n1, scale, low);
            }
            for (int i = 0; i < prof.Count; i++)
            {
                var p0 = prof[i]; var p1 = prof[(i + 1) % prof.Count];
                double ex = p1.x - p0.x, ey = p1.y - p0.y;
                var n = Dir(ey, -ex, 0).Normalized;
                var a = P(p0.x, p0.y, 0); var bb = P(p1.x, p1.y, 0); var c = P(p1.x, p1.y, depth); var dd = P(p0.x, p0.y, depth);
                if (Dot(Vector3d.Cross(bb - a, dd - a), n) > 0) { Face(b, sub, a, bb, c, n, scale, low); Face(b, sub, a, c, dd, n, scale, low); }
                else { Face(b, sub, a, c, bb, n, scale, low); Face(b, sub, a, dd, c, n, scale, low); }
            }
        }

        static double Dot(Vector3d a, Vector3d b) => a.x * b.x + a.y * b.y + a.z * b.z;

        static void Face(Builder b, int sub, Vector3d A, Vector3d B, Vector3d C, Vector3d n, float scale, double low)
        {
            var uv = WorldUV(A, B, C, scale);
            b.Tri(sub, A, B, C, n, uv[0], uv[1], uv[2], AO(A.y, low), AO(B.y, low), AO(C.y, low));
        }

        static double AO(double y, double low)
        {
            double t = Math.Min(1, Math.Max(0, (y - (low + 0.6)) / 1.4));
            return 0.75 + 0.25 * Math.Sqrt(t);
        }

        static Vector2d[] WorldUV(Vector3d A, Vector3d B, Vector3d C, double scale)
        {
            var u = B - A; var v = C - A;
            double nx = Math.Abs(u.y * v.z - u.z * v.y), ny = Math.Abs(u.z * v.x - u.x * v.z), nz = Math.Abs(u.x * v.y - u.y * v.x);
            Vector2d Map(Vector3d p) => ny >= nx && ny >= nz ? new Vector2d(p.x * scale, -p.z * scale) : nx >= nz ? new Vector2d(p.z * scale, p.y * scale) : new Vector2d(p.x * scale, p.y * scale);
            return new[] { Map(A), Map(B), Map(C) };
        }

        static double SignedArea(List<Vector2d> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++) { var q = p[i]; var r = p[(i + 1) % p.Count]; a += q.x * r.y - r.x * q.y; }
            return a / 2;
        }

        static List<int> Triangulate(List<Vector2d> p)
        {
            var idx = new List<int>(); for (int i = 0; i < p.Count; i++) idx.Add(i);
            var tris = new List<int>();
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool cut = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ia = idx[(k + idx.Count - 1) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                    var a = p[ia]; var bb = p[ib]; var c = p[ic];
                    double cross = (bb.x - a.x) * (c.y - a.y) - (bb.y - a.y) * (c.x - a.x);
                    if (cross <= 1e-12) continue;
                    bool inside = false;
                    foreach (int j in idx)
                    {
                        if (j == ia || j == ib || j == ic) continue;
                        if (InTriangle(p[j], a, bb, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    tris.Add(ia); tris.Add(ib); tris.Add(ic);
                    idx.RemoveAt(k);
                    cut = true;
                    break;
                }
                if (!cut)
                {
                    idx.RemoveAt(1);
                }
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            return tris;
        }

        static bool InTriangle(Vector2d p, Vector2d a, Vector2d b, Vector2d c)
        {
            double d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            double d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            double d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < -1e-12 || d2 < -1e-12 || d3 < -1e-12, pos = d1 > 1e-12 || d2 > 1e-12 || d3 > 1e-12;
            return !(neg && pos);
        }

        static void CheekWalls(Builder b, StrataMap m, Flight f, double low, StairLook look, StairStyle st)
        {
            var (ax, az) = Flight.Step(f.dir);
            bool flight = f.dir != StairDir.Landing;
            var edges = new (bool runZ, double at, double from, double to, int outward)[]
            {
                (true, f.x0, f.z0, f.z1, -1), (true, f.x1, f.z0, f.z1, 1),
                (false, f.z0, f.x0, f.x1, -1), (false, f.z1, f.x0, f.x1, 1),
            };
            const double T = 0.375;
            foreach (var e in edges)
            {
                if (flight && ((e.runZ && az == 0) || (!e.runZ && ax == 0))) continue;
                var runs = new List<(double u0, double u1, double top)>();
                for (double u = e.from; u < e.to - 1e-6; u += 0.25)
                {
                    double u1 = Math.Min(e.to, u + 0.25), um = (u + u1) / 2;
                    double px = e.runZ ? e.at + e.outward * 0.5 : um, pz = e.runZ ? um : e.at + e.outward * 0.5;
                    double? top = null;
                    int ci = m.CellAt(px, pz);
                    if (ci >= 0 && m.kind[ci] != (byte)CellKind.Stair)
                    {
                        double ix = e.runZ ? e.at - e.outward * 0.25 : um, iz = e.runZ ? um : e.at - e.outward * 0.25;
                        double tread = m.Height(ix, iz);
                        if (m.Height(px, pz) < tread - 0.4) top = tread + 0.75;
                    }
                    if (top.HasValue && runs.Count > 0 && Math.Abs(runs[runs.Count - 1].top - top.Value) < 1e-3 && Math.Abs(runs[runs.Count - 1].u1 - u) < 1e-6)
                    {
                        var last = runs[runs.Count - 1]; runs[runs.Count - 1] = (last.u0, u1, last.top);
                    }
                    else if (top.HasValue) runs.Add((u, u1, top.Value));
                }
                foreach (var r in runs)
                {
                    double um = (r.u0 + r.u1) / 2, len = r.u1 - r.u0;
                    double wx = e.runZ ? e.at + e.outward * T / 2 : um, wz = e.runZ ? um : e.at + e.outward * T / 2;
                    double hair = e.runZ ? 1.0 / 128 : 3.0 / 128;
                    double sw = e.runZ ? T + hair * 2 : len, sd = e.runZ ? len : T + hair * 2;
                    Box(b, st.cheek, sw, r.top - low + hair, sd, look.stone, false, new Vector3d(wx, (r.top + low + hair) / 2, wz), true);
                    BevelBox(b, st.tread, sw + (e.runZ ? 0.125 : 0), 0.1875, sd + (e.runZ ? 0 : 0.125), 1.0 / 16, look.tread, r.u0 * StairLook.Scale(look.tread), 0, new Vector3d(wx, r.top + hair + 0.09, wz));
                }
            }
        }

        static void Box(Builder b, int sub, double w, double h, double d, Vector2 tex, bool grain, Vector3d c, bool worldUV)
        {
            double uv = StairLook.Scale(tex);
            double hw = w / 2, hh = h / 2, hd = d / 2;
            var faces = new (Vector3d n, Vector3d o, Vector3d ua, double ul, Vector3d va, double vl)[]
            {
                (new Vector3d(1, 0, 0), new Vector3d(hw, -hh, hd), new Vector3d(0, 0, -1), d, new Vector3d(0, 1, 0), h),
                (new Vector3d(-1, 0, 0), new Vector3d(-hw, -hh, -hd), new Vector3d(0, 0, 1), d, new Vector3d(0, 1, 0), h),
                (new Vector3d(0, 0, 1), new Vector3d(-hw, -hh, hd), new Vector3d(1, 0, 0), w, new Vector3d(0, 1, 0), h),
                (new Vector3d(0, 0, -1), new Vector3d(hw, -hh, -hd), new Vector3d(-1, 0, 0), w, new Vector3d(0, 1, 0), h),
                (new Vector3d(0, 1, 0), new Vector3d(-hw, hh, hd), new Vector3d(1, 0, 0), w, new Vector3d(0, 0, -1), d),
                (new Vector3d(0, -1, 0), new Vector3d(-hw, -hh, -hd), new Vector3d(1, 0, 0), w, new Vector3d(0, 0, 1), d),
            };
            foreach (var f in faces)
            {
                var q = new Vector3d[4]; var t = new Vector2d[4];
                bool swap = grain && f.ul > f.vl;
                int k = 0;
                foreach (var (a, bb) in new[] { (0, 0), (1, 0), (1, 1), (0, 1) })
                {
                    q[k] = c + f.o + f.ua * (f.ul * a) + f.va * (f.vl * bb);
                    double U = a * f.ul * uv, V = bb * f.vl * uv;
                    t[k] = swap ? new Vector2d(V, U) : new Vector2d(U, V);
                    k++;
                }
                if (!worldUV) Snap(q, t, tex);
                Quad(b, sub, q, t, f.n, worldUV ? uv : 0);
            }
        }

        static void BevelBox(Builder b, int sub, double w, double h, double d, double bevel, Vector2 tex, double offU, double offV, Vector3d c)
        {
            double uv = StairLook.Scale(tex);
            double bv = Math.Max(0, Math.Min(Math.Min(bevel, w / 2 - 1e-3), Math.Min(d / 2 - 1e-3, h - 1e-3)));
            double hw = w / 2, hh = h / 2, hd = d / 2, yB = -hh, yC = hh - bv, yT = hh;
            double s2 = Math.Sqrt(0.5);
            double hs = yC - yB, cv = hs + bv * Math.Sqrt(2);
            void F(double[] pts, Vector3d n, double[] uvq)
            {
                var q = new Vector3d[4]; var t = new Vector2d[4];
                for (int k = 0; k < 4; k++) { q[k] = c + new Vector3d(pts[k * 3], pts[k * 3 + 1], pts[k * 3 + 2]); t[k] = new Vector2d(uvq[k * 2] * uv + offU, uvq[k * 2 + 1] * uv + offV); }
                Snap(q, t, tex);
                Quad(b, sub, q, t, n, 0);
            }
            F(new[] { -hw, yB, hd, hw, yB, hd, hw, yC, hd, -hw, yC, hd }, new Vector3d(0, 0, 1), new[] { 0, 0, w, 0, w, hs, 0, hs });
            F(new[] { hw, yB, -hd, -hw, yB, -hd, -hw, yC, -hd, hw, yC, -hd }, new Vector3d(0, 0, -1), new[] { 0, 0, w, 0, w, hs, 0, hs });
            F(new[] { hw, yB, hd, hw, yB, -hd, hw, yC, -hd, hw, yC, hd }, new Vector3d(1, 0, 0), new[] { 0, 0, d, 0, d, hs, 0, hs });
            F(new[] { -hw, yB, -hd, -hw, yB, hd, -hw, yC, hd, -hw, yC, -hd }, new Vector3d(-1, 0, 0), new[] { 0, 0, d, 0, d, hs, 0, hs });
            F(new[] { -hw, yC, hd, hw, yC, hd, hw - bv, yT, hd - bv, -hw + bv, yT, hd - bv }, new Vector3d(0, s2, s2), new[] { 0, hs, w, hs, w - bv, cv, bv, cv });
            F(new[] { hw, yC, -hd, -hw, yC, -hd, -hw + bv, yT, -hd + bv, hw - bv, yT, -hd + bv }, new Vector3d(0, s2, -s2), new[] { 0, hs, w, hs, w - bv, cv, bv, cv });
            F(new[] { hw, yC, hd, hw, yC, -hd, hw - bv, yT, -hd + bv, hw - bv, yT, hd - bv }, new Vector3d(s2, s2, 0), new[] { 0, hs, d, hs, d - bv, cv, bv, cv });
            F(new[] { -hw, yC, -hd, -hw, yC, hd, -hw + bv, yT, hd - bv, -hw + bv, yT, -hd + bv }, new Vector3d(-s2, s2, 0), new[] { 0, hs, d, hs, d - bv, cv, bv, cv });
            F(new[] { -hw + bv, yT, hd - bv, hw - bv, yT, hd - bv, hw - bv, yT, -hd + bv, -hw + bv, yT, -hd + bv }, new Vector3d(0, 1, 0), new[] { bv, bv, w - bv, bv, w - bv, d - bv, bv, d - bv });
            F(new[] { -hw, yB, -hd, hw, yB, -hd, hw, yB, hd, -hw, yB, hd }, new Vector3d(0, -1, 0), new[] { 0, 0, w, 0, w, d, 0, d });
        }

        static void Snap(Vector3d[] q, Vector2d[] t, Vector2 tex)
        {
            if (tex.x <= 0 || tex.y <= 0) return;
            double su = 0, sv = 0; int cnt = 0;
            foreach (var (a, b, c) in new[] { (0, 1, 2), (0, 2, 3) })
            {
                var e1 = q[b] - q[a]; var e2 = q[c] - q[a];
                double f1u = t[b].x - t[a].x, f1v = t[b].y - t[a].y, f2u = t[c].x - t[a].x, f2v = t[c].y - t[a].y;
                double det = f1u * f2v - f2u * f1v;
                if (Math.Abs(det) < 1e-10) continue;
                var T = (e1 * f2v - e2 * f1v) * (1 / det); var B = (e2 * f1u - e1 * f2u) * (1 / det);
                double lt = Math.Sqrt(T.x * T.x + T.y * T.y + T.z * T.z), lb = Math.Sqrt(B.x * B.x + B.y * B.y + B.z * B.z);
                if (!(lt > 1e-9) || !(lb > 1e-9)) continue;
                su += 16 * lt / tex.x; sv += 16 * lb / tex.y; cnt++;
            }
            if (cnt == 0) return;
            double ku = su / cnt, kv = sv / cnt;
            if (Math.Abs(ku - 1) > 0.03 || Math.Abs(kv - 1) > 0.03)
                for (int k = 0; k < 4; k++) t[k] = new Vector2d(t[k].x * ku, t[k].y * kv);
        }

        static void Quad(Builder b, int sub, Vector3d[] q, Vector2d[] t, Vector3d n, double worldScale)
        {
            if (worldScale > 0)
            {
                var u0 = WorldUV(q[0], q[1], q[2], worldScale); var u1 = WorldUV(q[0], q[2], q[3], worldScale);
                b.Tri(sub, q[0], q[1], q[2], n, u0[0], u0[1], u0[2], 1, 1, 1);
                b.Tri(sub, q[0], q[2], q[3], n, u1[0], u1[1], u1[2], 1, 1, 1);
            }
            else
            {
                b.Tri(sub, q[0], q[1], q[2], n, t[0], t[1], t[2], 1, 1, 1);
                b.Tri(sub, q[0], q[2], q[3], n, t[0], t[2], t[3], 1, 1, 1);
            }
        }
    }
}
