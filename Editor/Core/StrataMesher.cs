using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Strata
{
    public sealed class MeshData
    {
        public readonly List<Vector3> pos = new List<Vector3>();
        public readonly List<Vector3> nor = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Color> col = new List<Color>();
        public readonly List<Vector4> uv1 = new List<Vector4>();
        public readonly List<List<int>> tris = new List<List<int>>();

        public int VertexCount => pos.Count;

        public List<int> Sub(int k)
        {
            while (tris.Count <= k) tris.Add(new List<int>());
            return tris[k];
        }

        public void Clear()
        {
            pos.Clear(); nor.Clear(); uv.Clear(); col.Clear(); uv1.Clear();
            foreach (var t in tris) t.Clear();
        }

        public int Add(Vector3 p, Vector3 n, Vector2 t, Color c)
        {
            if (uv1.Count > 0) uv1.Add(default);
            pos.Add(p); nor.Add(n); uv.Add(t); col.Add(c);
            return pos.Count - 1;
        }

        public int Add(Vector3 p, Vector3 n, Vector2 t, Color c, Vector4 t1)
        {
            while (uv1.Count < pos.Count) uv1.Add(default);
            uv1.Add(t1);
            pos.Add(p); nor.Add(n); uv.Add(t); col.Add(c);
            return pos.Count - 1;
        }

        bool HasUv1 => uv1.Count > 0 && uv1.Count == pos.Count;

        public void ToMesh(Mesh mesh, bool weld, int submeshes = -1, bool tangents = false)
        {
            int subs = submeshes > 0 ? submeshes : Math.Max(1, tris.Count);
            while (tris.Count < subs) tris.Add(new List<int>());
            mesh.Clear();
            if (!weld)
            {
                mesh.indexFormat = pos.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(pos);
                mesh.SetNormals(nor);
                mesh.SetUVs(0, uv);
                if (HasUv1) mesh.SetUVs(1, uv1);
                mesh.SetColors(col);
                mesh.subMeshCount = subs;
                for (int s = 0; s < subs; s++) mesh.SetTriangles(tris[s], s, false);
            }
            else
            {
                var map = new Dictionary<VertexKey, int>(pos.Count);
                var remap = new int[pos.Count];
                var P = new List<Vector3>(pos.Count / 2); var N = new List<Vector3>(pos.Count / 2);
                var U = new List<Vector2>(pos.Count / 2); var C = new List<Color>(pos.Count / 2);
                bool has1 = HasUv1;
                var U1 = has1 ? new List<Vector4>(pos.Count / 2) : null;
                for (int i = 0; i < pos.Count; i++)
                {
                    var key = new VertexKey(pos[i], nor[i], uv[i], col[i], has1 ? uv1[i] : default);
                    if (!map.TryGetValue(key, out int k))
                    {
                        k = P.Count; map[key] = k;
                        P.Add(pos[i]); N.Add(nor[i]); U.Add(uv[i]); C.Add(col[i]);
                        if (has1) U1.Add(uv1[i]);
                    }
                    remap[i] = k;
                }
                mesh.indexFormat = P.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(P);
                mesh.SetNormals(N);
                mesh.SetUVs(0, U);
                if (has1) mesh.SetUVs(1, U1);
                mesh.SetColors(C);
                mesh.subMeshCount = subs;
                var t = new List<int>();
                for (int s = 0; s < subs; s++)
                {
                    t.Clear();
                    foreach (var v in tris[s]) t.Add(remap[v]);
                    mesh.SetTriangles(t, s, false);
                }
            }
            if (tangents && mesh.vertexCount > 0) mesh.RecalculateTangents();
            mesh.RecalculateBounds();
        }

        readonly struct VertexKey : IEquatable<VertexKey>
        {
            readonly Vector3 p, n; readonly Vector2 t; readonly Color c; readonly Vector4 t1;
            public VertexKey(Vector3 p, Vector3 n, Vector2 t, Color c, Vector4 t1) { this.p = p; this.n = n; this.t = t; this.c = c; this.t1 = t1; }
            public bool Equals(VertexKey o) =>
                p.x == o.p.x && p.y == o.p.y && p.z == o.p.z && n.x == o.n.x && n.y == o.n.y && n.z == o.n.z &&
                t.x == o.t.x && t.y == o.t.y && c.r == o.c.r && c.g == o.c.g && c.b == o.c.b && c.a == o.c.a &&
                t1.x == o.t1.x && t1.y == o.t1.y && t1.z == o.t1.z && t1.w == o.t1.w;
            public override bool Equals(object obj) => obj is VertexKey k && Equals(k);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = p.x.GetHashCode();
                    h = h * 397 ^ p.y.GetHashCode(); h = h * 397 ^ p.z.GetHashCode();
                    h = h * 397 ^ n.x.GetHashCode(); h = h * 397 ^ n.y.GetHashCode(); h = h * 397 ^ n.z.GetHashCode();
                    h = h * 397 ^ t.x.GetHashCode(); h = h * 397 ^ t.y.GetHashCode();
                    h = h * 397 ^ c.r.GetHashCode(); h = h * 397 ^ c.g.GetHashCode(); h = h * 397 ^ c.b.GetHashCode();
                    return h;
                }
            }
        }
    }

    public static class StrataMesher
    {
        static double JsRound(double v) => Math.Floor(v + 0.5);
        public static float P4(double v) { double d = (float)v; return Math.Abs(d) < 5e-5 ? 0f : (float)(JsRound(d * 1e4) / 1e4); }
        public static float N3(double v) { double d = (float)v; return (float)(JsRound(d * 1e3) / 1e3); }
        public static float U5(double v) { double d = (float)v; return (float)(JsRound(d * 1e5) / 1e5); }

        static readonly int[,] Around = { { -1, -1, 2 }, { 0, -1, 3 }, { -1, 0, 1 }, { 0, 0, 0 } };
        static readonly int[,] Pts = { { 0, 0 }, { 1, 0 }, { 1, 1 }, { 0, 1 } };
        static readonly Color White = new Color(1f, 1f, 1f, 1f);

        static void Plane(StrataMap m, int i, out double nx, out double ny, out double nz)
        {
            int o = i * 4;
            double h0 = m.corner[o], h1 = m.corner[o + 1], h2 = m.corner[o + 2], h3 = m.corner[o + 3];
            double sx = ((h1 - h0) + (h2 - h3)) / 2, sz = ((h3 - h0) + (h2 - h1)) / 2;
            double l = Math.Sqrt(sx * sx + 1 + sz * sz);
            nx = -sx / l; ny = 1 / l; nz = -sz / l;
        }

        static void VertexNormal(StrataMap m, int vc, int vr, double h, out double nx, out double ny, out double nz)
        {
            nx = ny = nz = 0;
            for (int k = 0; k < 4; k++)
            {
                int cc = vc + Around[k, 0], rr = vr + Around[k, 1];
                if (cc < 0 || rr < 0 || cc >= m.w || rr >= m.h) continue;
                int j = rr * m.w + cc;
                if (Math.Abs(m.corner[j * 4 + Around[k, 2]] - h) > 1e-3 || m.kind[j] == (byte)CellKind.Stair) continue;
                Plane(m, j, out double px, out double py, out double pz);
                nx += px; ny += py; nz += pz;
            }
            double l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (l == 0) l = 1;
            nx /= l; ny /= l; nz /= l;
        }

        public static void Ground(StrataMap m, int c0, int r0, int c1, int r1, double pivotX, double pivotZ, MeshData into)
        {
            var tris = into.Sub(0);
            var ids = new int[4];
            var h = new double[4];
            for (int r = Math.Max(0, r0); r < Math.Min(m.h, r1); r++)
                for (int c = Math.Max(0, c0); c < Math.Min(m.w, c1); c++)
                {
                    int i = r * m.w + c;
                    if (m.kind[i] == (byte)CellKind.Stair) continue;
                    int o = i * 4;
                    for (int q = 0; q < 4; q++) h[q] = m.corner[o + q];
                    bool flat = Math.Abs(h[0] - h[1]) < 1e-4 && Math.Abs(h[0] - h[2]) < 1e-4 && Math.Abs(h[0] - h[3]) < 1e-4;
                    int x0 = m.x0 + c, z0 = m.z0 + r;
                    for (int q = 0; q < 4; q++)
                    {
                        int dx = Pts[q, 0], dz = Pts[q, 1];
                        double nx = 0, ny = 1, nz = 0;
                        if (!flat) VertexNormal(m, c + dx, r + dz, h[q], out nx, out ny, out nz);
                        double X = x0 + dx, Z = z0 + dz;
                        ids[q] = into.Add(
                            new Vector3((float)(X - pivotX), P4(h[q]), (float)-(Z - pivotZ)),
                            new Vector3(N3(nx), N3(ny), -N3(nz)),
                            new Vector2(U5(X), U5(Z)), White);
                    }
                    if (Math.Abs(h[1] - h[3]) + 1e-4 < Math.Abs(h[0] - h[2])) { Tri(tris, ids[0], ids[3], ids[1]); Tri(tris, ids[1], ids[3], ids[2]); }
                    else { Tri(tris, ids[0], ids[3], ids[2]); Tri(tris, ids[0], ids[2], ids[1]); }
                }
        }

        static float Reach(List<Wall> walls, int j, int x, int z, bool top)
        {
            if (j < 0) return -10000f;
            var w = walls[j];
            bool first = w.x0 == x && w.z0 == z;
            return P4(top ? (first ? w.t0 : w.t1) : (first ? w.b0 : w.b1));
        }

        static void Tri(List<int> t, int a, int b, int c) { t.Add(a); t.Add(c); t.Add(b); }

        static readonly int[,] SideNormal = { { 0, 0, -1 }, { 1, 0, 0 }, { 0, 0, 1 }, { -1, 0, 0 } };

        public static void Walls(StrataMap m, int c0, int r0, int c1, int r1, double pivotX, double pivotZ, MeshData into, List<Wall> scratch = null, List<(int, int)> nbScratch = null)
        {
            var walls = scratch ?? new List<Wall>();
            var nb = nbScratch ?? new List<(int, int)>();
            var picks = new List<(int, int)>();
            walls.Clear();
            StrataRules.Walls(m, c0 - 2, r0 - 2, c1 + 2, r1 + 2, walls);
            StrataRules.Neighbours(m, walls, nb, picks);
            bool wide = m.WideCodes;
            int nbase = wide ? 16 : 4;
            var tris = into.Sub(0);
            for (int k = 0; k < walls.Count; k++)
            {
                var w = walls[k];
                int hc = w.hi % m.w, hr = w.hi / m.w;
                if (hc < c0 || hc >= c1 || hr < r0 || hr >= r1) continue;
                var (sL, sR) = nb[k];
                var n = new Vector3(SideNormal[w.side, 0], 0, -SideNormal[w.side, 2]);
                float codeA, codeB;
                if (wide) { codeA = (Math.Min(w.style, 15) + Math.Min(w.rim, 15) * 16) / 256f; codeB = (Math.Min(sL, 15) + Math.Min(sR, 15) * 16) / 256f; }
                else { codeA = (w.style + w.rim * 4) / 16f; codeB = (sL + sR * 4) / 16f; }
                Vector3 P(double x, double y, double z) => new Vector3((float)(x - pivotX), P4(y), (float)-(z - pivotZ));
                int a, b, c, d;
                if (wide)
                {
                    var (pl, pr) = picks[k];
                    var ext = new Vector4(Reach(walls, pl, w.x0, w.z0, true), Reach(walls, pl, w.x0, w.z0, false), Reach(walls, pr, w.x1, w.z1, true), Reach(walls, pr, w.x1, w.z1, false));
                    a = into.Add(P(w.x0, w.b0, w.z0), n, new Vector2(U5(w.t0), U5(w.b0)), new Color(codeA, codeB, 0f, 1f), ext);
                    b = into.Add(P(w.x1, w.b1, w.z1), n, new Vector2(U5(w.t1), U5(w.b1)), new Color(codeA, codeB, 1f, 1f), ext);
                    c = into.Add(P(w.x1, w.t1, w.z1), n, new Vector2(U5(w.t1), U5(w.b1)), new Color(codeA, codeB, 1f, 1f), ext);
                    d = into.Add(P(w.x0, w.t0, w.z0), n, new Vector2(U5(w.t0), U5(w.b0)), new Color(codeA, codeB, 0f, 1f), ext);
                }
                else
                {
                    a = into.Add(P(w.x0, w.b0, w.z0), n, new Vector2(U5(w.t0), U5(w.b0)), new Color(codeA, codeB, 0f, 1f));
                    b = into.Add(P(w.x1, w.b1, w.z1), n, new Vector2(U5(w.t1), U5(w.b1)), new Color(codeA, codeB, 1f, 1f));
                    c = into.Add(P(w.x1, w.t1, w.z1), n, new Vector2(U5(w.t1), U5(w.b1)), new Color(codeA, codeB, 1f, 1f));
                    d = into.Add(P(w.x0, w.t0, w.z0), n, new Vector2(U5(w.t0), U5(w.b0)), new Color(codeA, codeB, 0f, 1f));
                }
                int ux = w.x1 - w.x0, uz = w.z1 - w.z0;
                int facing = ux * SideNormal[w.side, 2] - uz * SideNormal[w.side, 0];
                if (facing > 0) { Tri(tris, a, b, c); Tri(tris, a, c, d); }
                else { Tri(tris, a, c, b); Tri(tris, a, d, c); }
            }
        }
    }
}
