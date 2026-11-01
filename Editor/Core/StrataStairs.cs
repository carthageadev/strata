using System;
using System.Collections.Generic;
using UnityEngine;

namespace Strata
{
    public struct StairLook
    {
        public Vector2 stone, tread, deck, timber, cheek;
        public static StairLook Default => new StairLook { stone = new Vector2(256, 256), tread = new Vector2(256, 256), deck = new Vector2(128, 128), timber = new Vector2(64, 128), cheek = new Vector2(256, 256) };
        public static double Scale(Vector2 tex) => tex.x > 0 ? 16.0 / tex.x : 1.0 / 16;
    }

    public struct StairStyle
    {
        public bool timber;
        public int body, tread, cheek, plank, post;
        public StairSteps steps;
        public StairSides sides;
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
                if (stone && st.steps != StairSteps.Slabs)
                {
                    bool logs = st.steps == StairSteps.Logs;
                    StoneBody(b, f, low, (float)StairLook.Scale(look.stone), st.body, logs ? 0 : TT);
                    if (logs) Logs(b, f, look, st); else Flagstones(b, m, f, look, st);
                    Sides(b, m, f, low, look, st, def);
                    continue;
                }
                var pieces = Pieces(f);
                if (stone) StoneBody(b, f, low, (float)StairLook.Scale(look.stone), st.body, TT);
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
                        bool along = f.dir == StairDir.Landing && Math.Max(pw, pd) > 2, alongX = pw > pd;
                        int n = along ? (int)Math.Ceiling((Math.Max(pw, pd) - 0.24) / 1.5) : 0;
                        for (int i = 0; i <= n; i++)
                        {
                            double a = along ? -Math.Max(pw, pd) / 2 + 0.12 + (Math.Max(pw, pd) - 0.24) * i / n : 0;
                            for (int sx = -1; sx <= 1; sx += 2)
                            {
                                double ppx = along ? (alongX ? cx + a : cx + sx * (pw / 2 - 0.12)) : ew ? cx : cx + sx * (pw / 2 - 0.12);
                                double ppz = along ? (alongX ? cz + sx * (pd / 2 - 0.12) : cz + a) : ew ? cz + sx * (pd / 2 - 0.12) : cz;
                                Box(b, st.post, 0.14, p.top - g + 0.2, 0.14, look.timber, true, new Vector3d(ppx, (p.top + g - 0.2) / 2 - 0.06, ppz), false);
                            }
                        }
                    }
                }
                Sides(b, m, f, low, look, st, def);
            }
        }

        static void Sides(Builder b, StrataMap m, Flight f, double low, StairLook look, StairStyle st, Stair def)
        {
            if (!def.walls || st.cheek < 0 || st.sides == StairSides.None) return;
            if (st.sides == StairSides.CheekWalls) { CheekWalls(b, m, f, low, look, st); return; }
            var runs = new List<Run>();
            if (f.dir == StairDir.Landing) LandingRuns(m, f, runs); else FlightRuns(m, f, runs);
            var wall = look.cheek.x > 0 ? look.cheek : look.stone;
            foreach (var r in runs)
                if (st.sides == StairSides.Balustrade) Balustrade(b, r, low, wall, look.tread, st, f.dir == StairDir.Landing);
                else WingWall(b, r, low, wall, look.tread, st, f.dir == StairDir.Landing);
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

        static void StoneBody(Builder b, Flight f, double low, float scale, int sub, double sink)
        {
            int w = f.x1 - f.x0, d = f.z1 - f.z0;
            var prof = new List<Vector2d>();
            double depth;
            if (f.dir == StairDir.Landing)
            {
                double L = d;
                prof.Add(new Vector2d(0, low)); prof.Add(new Vector2d(L, low)); prof.Add(new Vector2d(L, f.y0 - sink)); prof.Add(new Vector2d(0, f.y0 - sink));
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
                    double top = f.y0 + ((double)f.y1 - f.y0) * (k + 1) / f.steps - sink;
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

        struct Frame
        {
            public double ox, oz, ux, uz, ex, ez, len, wid;
            public Vector3d P(double u, double y, double e) => new Vector3d(ox + u * ux + e * ex, y, oz + u * uz + e * ez);
            public Vector3d D(double u, double y, double e) => new Vector3d(u * ux + e * ex, y, u * uz + e * ez);
            public double U(double x, double z) => (x - ox) * ux + (z - oz) * uz;
            public bool AlongX => ux != 0;
        }

        static Frame FrameOf(Flight f)
        {
            var (dx, dz) = Flight.Step(f.dir == StairDir.Landing ? StairDir.S : f.dir);
            if (f.y1 < f.y0) { dx = -dx; dz = -dz; }
            return new Frame
            {
                ux = dx, uz = dz, ex = dz != 0 ? 1 : 0, ez = dx != 0 ? 1 : 0,
                ox = dx < 0 ? f.x1 : f.x0, oz = dz < 0 ? f.z1 : f.z0,
                len = dx != 0 ? f.x1 - f.x0 : f.z1 - f.z0, wid = dx != 0 ? f.z1 - f.z0 : f.x1 - f.x0,
            };
        }

        struct Step { public double u0, u1, top, below; }

        static List<Step> Steps(Flight f, Frame fr)
        {
            var list = new List<Step>();
            foreach (var p in Pieces(f))
            {
                double a = fr.U(p.x0, p.z0), c = fr.U(p.x1, p.z1);
                list.Add(new Step { u0 = Math.Min(a, c), u1 = Math.Max(a, c), top = p.top });
            }
            list.Sort((x, y) => x.u0.CompareTo(y.u0));
            double foot = Math.Min(f.y0, f.y1);
            for (int k = 0; k < list.Count; k++) { var s = list[k]; s.below = k == 0 ? foot : list[k - 1].top; list[k] = s; }
            return list;
        }

        static int Seed(Flight f)
        {
            unchecked { return (f.x0 * 73856093 ^ f.z0 * 19349663 ^ f.x1 * 83492791 ^ f.z1 * 2654435 ^ (int)Math.Round(f.y0 * 4) * 40503 ^ (int)Math.Round(f.y1 * 4) * 9973) & 0x7fffffff; }
        }

        static void Logs(Builder b, Flight f, StairLook look, StairStyle st)
        {
            if (f.dir == StairDir.Landing || st.tread < 0) return;
            var fr = FrameOf(f);
            double rise = Math.Abs((double)f.y1 - f.y0) / Math.Max(1, f.steps);
            foreach (var s in Steps(f, fr))
            {
                if (s.top - s.below < rise * 0.5) continue;
                double u0 = s.u0 - Texel, u1 = s.u0 + 2 * Texel, e0 = Texel, e1 = fr.wid - Texel;
                var c = fr.P((u0 + u1) / 2, 0, (e0 + e1) / 2);
                double y0 = s.below - Texel, y1 = s.top + Texel;
                double lu = u1 - u0, le = e1 - e0;
                Box(b, st.tread, fr.AlongX ? lu : le, y1 - y0, fr.AlongX ? le : lu, look.tread, true, new Vector3d(c.x, (y0 + y1) / 2, c.z), false);
            }
        }

        static void Flagstones(Builder b, StrataMap m, Flight f, StairLook look, StairStyle st)
        {
            if (st.tread < 0) return;
            var fr = FrameOf(f);
            var rng = new System.Random(Seed(f));
            bool landing = f.dir == StairDir.Landing;
            double gap = Texel, ku = 16.0 / Math.Max(1, look.tread.x);
            bool Open(double u, double e, double under)
            {
                var p = fr.P(u, 0, e);
                int ci = m.CellAt(p.x, p.z);
                return ci < 0 || (m.kind[ci] != (byte)CellKind.Stair && m.Height(p.x, p.z) < under - 1e-3);
            }
            var steps = Steps(f, fr);
            for (int si = 0; si < steps.Count; si++)
            {
                var s = steps[si];
                double mid = (s.u0 + s.u1) / 2, under = s.top - 4 * Texel;
                double edge0 = Open(mid, -0.5, under) ? -Texel : Texel, edge1 = Open(mid, fr.wid + 0.5, under) ? fr.wid + Texel : fr.wid - Texel;
                var rows = new List<(double a, double z)>();
                if (!landing) rows.Add((s.u0, s.u1));
                else for (double u = s.u0; u < s.u1 - 1e-6;)
                {
                    double d = Math.Min(s.u1 - u, 0.625 + rng.Next(3) * 0.125);
                    if (s.u1 - (u + d) < 0.375) d = s.u1 - u;
                    rows.Add((u, u + d)); u += d;
                }
                for (int row = 0; row < rows.Count; row++)
                {
                    var (ra, rz) = rows[row];
                    for (double e = 0; e < fr.wid - 1e-6;)
                    {
                        double w = 0.5 + rng.Next(7) * 0.125;
                        if (fr.wid - (e + w) < 0.375) w = fr.wid - e;
                        double e0 = e > 1e-6 ? e + gap / 2 : edge0, e1 = e + w < fr.wid - 1e-6 ? e + w - gap / 2 : edge1;
                        double th = rng.Next(2) == 0 ? 2 * Texel : 3 * Texel;
                        double nose = landing ? 0 : (1 + rng.Next(3)) * Texel;
                        double back = landing && row < rows.Count - 1 ? gap : rng.Next(3) == 0 ? Texel : 0;
                        double top = s.top - (rng.Next(5) == 0 ? Texel : 0);
                        if (si == steps.Count - 1 && row == rows.Count - 1 && back < 1e-9) { th = 2 * Texel; top = s.top; }
                        double u0 = ra - nose, u1 = rz - back;
                        var c = fr.P((u0 + u1) / 2, top - th / 2, (e0 + e1) / 2);
                        double lu = u1 - u0, le = e1 - e0;
                        BevelBox(b, st.tread, fr.AlongX ? lu : le, th, fr.AlongX ? le : lu, Texel, look.tread, rng.Next(8) * 0.125 * ku, rng.Next(8) * 0.125 * ku, c);
                        e += w;
                    }
                }
            }
        }

        struct Run { public Vector3d a, b, side; public double ya, yb; public bool foot, head; }

        static double Len(Run r) => Math.Sqrt((r.b.x - r.a.x) * (r.b.x - r.a.x) + (r.b.z - r.a.z) * (r.b.z - r.a.z));

        static Vector3d RunPoint(Run r, double u, double o, double y)
        {
            double L = Math.Max(1e-9, Len(r));
            return new Vector3d(r.a.x + (r.b.x - r.a.x) * u / L + r.side.x * o, y, r.a.z + (r.b.z - r.a.z) * u / L + r.side.z * o);
        }

        static void FlightRuns(StrataMap m, Flight f, List<Run> runs)
        {
            var fr = FrameOf(f);
            var steps = Steps(f, fr);
            if (steps.Count == 0) return;
            var first = steps[0]; var last = steps[steps.Count - 1];
            double slope = steps.Count > 1 ? (last.top - first.top) / Math.Max(1e-6, last.u0 - first.u0) : 0;
            double y0 = first.top - slope * first.u0;
            bool StairAt(double u)
            {
                var p = fr.P(u, 0, fr.wid / 2);
                int ci = m.CellAt(p.x, p.z);
                return ci >= 0 && m.kind[ci] == (byte)CellKind.Stair;
            }
            bool foot = !StairAt(-0.5), head = !StairAt(fr.len + 0.5);
            for (int side = 0; side < 2; side++)
            {
                double e = side == 0 ? 0 : fr.wid, outward = side == 0 ? -1 : 1;
                int free = 0, n = 0;
                for (double u = 0.25; u < fr.len; u += 0.5)
                {
                    var p = fr.P(u, 0, e + outward * 0.5);
                    int ci = m.CellAt(p.x, p.z);
                    n++;
                    if (ci < 0) free++;
                    else if (m.kind[ci] != (byte)CellKind.Stair && m.Height(p.x, p.z) <= y0 + slope * u + 0.25) free++;
                }
                if (n == 0 || free * 4 < n * 3) continue;
                runs.Add(new Run { a = fr.P(0, 0, e), b = fr.P(fr.len, 0, e), side = fr.D(0, 0, outward), ya = y0, yb = y0 + slope * fr.len, foot = foot, head = head });
            }
        }

        static void LandingRuns(StrataMap m, Flight f, List<Run> runs)
        {
            var edges = new (bool runZ, double at, double from, double to, int outward)[]
            {
                (true, f.x0, f.z0, f.z1, -1), (true, f.x1, f.z0, f.z1, 1),
                (false, f.z0, f.x0, f.x1, -1), (false, f.z1, f.x0, f.x1, 1),
            };
            foreach (var e in edges)
            {
                double start = double.NaN;
                for (double u = e.from; u <= e.to + 1e-6; u += 0.25)
                {
                    bool open = false;
                    if (u < e.to - 1e-6)
                    {
                        double um = u + 0.125, px = e.runZ ? e.at + e.outward * 0.5 : um, pz = e.runZ ? um : e.at + e.outward * 0.5;
                        int ci = m.CellAt(px, pz);
                        open = ci >= 0 && m.kind[ci] != (byte)CellKind.Stair && m.Height(px, pz) < f.y0 - 0.4;
                    }
                    if (open && double.IsNaN(start)) start = u;
                    if (!open && !double.IsNaN(start))
                    {
                        double u0 = start;
                        Vector3d P(double t) => e.runZ ? new Vector3d(e.at, 0, t) : new Vector3d(t, 0, e.at);
                        bool StairBeyond(double t)
                        {
                            double inX = e.runZ ? e.at - e.outward * 0.5 : t, inZ = e.runZ ? t : e.at - e.outward * 0.5;
                            int ci = m.CellAt(inX, inZ);
                            return ci >= 0 && m.kind[ci] == (byte)CellKind.Stair && m.stairOf[ci] >= 0 && m.flights[m.stairOf[ci]].dir != StairDir.Landing;
                        }
                        runs.Add(new Run { a = P(u0), b = P(u), side = e.runZ ? new Vector3d(e.outward, 0, 0) : new Vector3d(0, 0, e.outward), ya = f.y0, yb = f.y0, foot = !StairBeyond(u0 - 0.5), head = !StairBeyond(u + 0.5) });
                        start = double.NaN;
                    }
                }
            }
        }

        const double Wall = 0.375;

        static void Balustrade(Builder b, Run r, double low, Vector2 stone, Vector2 rail, StairStyle st, bool flat)
        {
            double L = Len(r);
            if (L < 0.5) return;
            double Y(double u) => r.ya + (r.yb - r.ya) * u / L;
            bool foot = !flat || r.foot, head = (!flat || r.head) && L >= 1;
            const double h = Wall / 2;
            if (foot) Newel(b, st, r, h, low, Y(h) + 0.8125, stone, rail);
            if (head) Newel(b, st, r, L - h, low, Y(L - h) + 0.8125, stone, rail);
            double ua = foot ? Wall : 0, ub = head ? L - Wall : L;
            if (ub - ua < 0.25) return;
            Prism(b, st.cheek, r, ua, ub, 0, Wall, low, low, Y(ua) + 0.125, Y(ub) + 0.125, stone, cap0: !foot, cap1: !head);
            int n = Math.Max(0, (int)Math.Floor((ub - ua - 0.25) / 0.375));
            double first = ua + (ub - ua - n * 0.375) / 2;
            for (int k = 0; k <= n; k++)
            {
                double u = first + k * 0.375;
                var c = RunPoint(r, u, h, 0);
                Block(b, st.cheek, c.x, c.z, 2 * Texel, 2 * Texel, Y(u) + 0.125 - Texel, Y(u) + 0.625 + Texel, stone);
            }
            Prism(b, st.tread, r, ua, ub, Texel, Wall - Texel, Y(ua) + 0.625, Y(ub) + 0.625, Y(ua) + 0.75, Y(ub) + 0.75, rail, cap0: !foot, cap1: !head, bottom: true);
        }

        static void Newel(Builder b, StairStyle st, Run r, double u, double low, double top, Vector2 stone, Vector2 cap)
        {
            var c = RunPoint(r, u, Wall / 2, 0);
            Block(b, st.cheek, c.x, c.z, Wall, Wall, low, top, stone);
            BevelBox(b, st.tread, Wall + 2 * Texel, 2 * Texel, Wall + 2 * Texel, Texel, cap, 0, 0, new Vector3d(c.x, top + Texel, c.z));
            BevelBox(b, st.tread, Wall - 2 * Texel, 2 * Texel, Wall - 2 * Texel, Texel, cap, 0, 0, new Vector3d(c.x, top + 3 * Texel, c.z));
        }

        static void WingWall(Builder b, Run r, double low, Vector2 brick, Vector2 cap, StairStyle st, bool flat)
        {
            double L = Len(r);
            if (L < 0.5) return;
            double Y(double u) => r.ya + (r.yb - r.ya) * u / L + 0.5;
            bool foot = !flat || r.foot, head = flat ? r.head && L >= 2 * Wall + 0.25 : !r.head;
            if (foot) Pier(b, st, r, Wall / 2, low, Math.Max(Y(0), Y(Wall)) + 0.25, brick, cap);
            if (head) Pier(b, st, r, L - Wall / 2, low, Math.Max(Y(L), Y(L - Wall)) + 0.25, brick, cap);
            double ua = foot ? Wall : 0, ub = head ? L - Wall : L;
            if (ub - ua < 0.125) return;
            Prism(b, st.cheek, r, ua, ub, 0, Wall, low, low, Y(ua), Y(ub), brick, cap0: !foot, cap1: !head);
            Prism(b, st.tread, r, ua, ub, -Texel, Wall + Texel, Y(ua), Y(ub), Y(ua) + 0.125, Y(ub) + 0.125, cap, cap0: !foot, cap1: !head, bottom: true);
        }

        static void Pier(Builder b, StairStyle st, Run r, double u, double low, double top, Vector2 brick, Vector2 cap)
        {
            var c = RunPoint(r, u, Wall / 2, 0);
            Block(b, st.cheek, c.x, c.z, Wall, Wall, low, top, brick);
            BevelBox(b, st.tread, Wall + 2 * Texel, 2 * Texel, Wall + 2 * Texel, Texel, cap, 0, 0, new Vector3d(c.x, top + Texel, c.z));
        }

        static void Prism(Builder b, int sub, Run r, double u0, double u1, double o0, double o1, double bot0, double bot1, double top0, double top1, Vector2 tex, bool cap0 = true, bool cap1 = true, bool bottom = false)
        {
            if (sub < 0) return;
            var A0 = RunPoint(r, u0, o0, bot0); var A1 = RunPoint(r, u1, o0, bot1); var A2 = RunPoint(r, u1, o0, top1); var A3 = RunPoint(r, u0, o0, top0);
            var B0 = RunPoint(r, u0, o1, bot0); var B1 = RunPoint(r, u1, o1, bot1); var B2 = RunPoint(r, u1, o1, top1); var B3 = RunPoint(r, u0, o1, top0);
            var s = r.side; var l = (A1 - A0); l = new Vector3d(l.x, 0, l.z).Normalized;
            Facet(b, sub, new[] { A0, A1, A2, A3 }, s * -1, tex);
            Facet(b, sub, new[] { B0, B1, B2, B3 }, s, tex);
            var up = Vector3d.Cross(A2 - A3, B3 - A3).Normalized; if (up.y < 0) up = up * -1;
            Facet(b, sub, new[] { A3, A2, B2, B3 }, up, tex);
            if (bottom) { var dn = up * -1; Facet(b, sub, new[] { A0, A1, B1, B0 }, dn, tex); }
            if (cap0) Facet(b, sub, new[] { A0, B0, B3, A3 }, l * -1, tex);
            if (cap1) Facet(b, sub, new[] { A1, B1, B2, A2 }, l, tex);
        }

        static void Block(Builder b, int sub, double cx, double cz, double w, double d, double y0, double y1, Vector2 tex)
        {
            if (sub < 0) return;
            double x0 = cx - w / 2, x1 = cx + w / 2, z0 = cz - d / 2, z1 = cz + d / 2;
            Vector3d V(double x, double y, double z) => new Vector3d(x, y, z);
            Facet(b, sub, new[] { V(x1, y0, z0), V(x1, y0, z1), V(x1, y1, z1), V(x1, y1, z0) }, new Vector3d(1, 0, 0), tex);
            Facet(b, sub, new[] { V(x0, y0, z1), V(x0, y0, z0), V(x0, y1, z0), V(x0, y1, z1) }, new Vector3d(-1, 0, 0), tex);
            Facet(b, sub, new[] { V(x0, y0, z1), V(x1, y0, z1), V(x1, y1, z1), V(x0, y1, z1) }, new Vector3d(0, 0, 1), tex);
            Facet(b, sub, new[] { V(x1, y0, z0), V(x0, y0, z0), V(x0, y1, z0), V(x1, y1, z0) }, new Vector3d(0, 0, -1), tex);
            Facet(b, sub, new[] { V(x0, y1, z0), V(x1, y1, z0), V(x1, y1, z1), V(x0, y1, z1) }, new Vector3d(0, 1, 0), tex);
        }

        static Vector2d WUV(Vector3d p, Vector3d n, Vector2 tex)
        {
            double su = 16.0 / Math.Max(1, tex.x), sv = 16.0 / Math.Max(1, tex.y);
            double ax = Math.Abs(n.x), ay = Math.Abs(n.y), az = Math.Abs(n.z);
            if (ay >= ax && ay >= az) return new Vector2d(p.x * su, -p.z * sv);
            return ax >= az ? new Vector2d(p.z * su, p.y * sv) : new Vector2d(p.x * su, p.y * sv);
        }

        static void Facet(Builder b, int sub, Vector3d[] q, Vector3d n, Vector2 tex)
        {
            var t = new Vector2d[q.Length];
            for (int i = 0; i < q.Length; i++) t[i] = WUV(q[i], n, tex);
            FacetUV(b, sub, q, t, n);
        }

        static void FacetUV(Builder b, int sub, Vector3d[] q, Vector2d[] t, Vector3d n)
        {
            for (int i = 1; i + 1 < q.Length; i++)
            {
                int ib = i, ic = i + 1;
                if (Dot(Vector3d.Cross(q[ib] - q[0], q[ic] - q[0]), n) < 0) { ib = i + 1; ic = i; }
                b.Tri(sub, q[0], q[ib], q[ic], n, t[0], t[ib], t[ic], 1, 1, 1);
            }
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
