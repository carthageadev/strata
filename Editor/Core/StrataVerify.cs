using System;
using System.Collections.Generic;
using UnityEngine;

namespace Strata
{
    public static class StrataVerify
    {
        public static long VKey(Vector3 p, Vector3 n, Vector2 uv, Color col, Vector4 uv1 = default)
        {
            unchecked
            {
                long h = 1469598103934665603L;
                void Mix(long v) { h ^= v; h *= 1099511628211L; }
                Mix((long)Math.Round(p.x * 1000)); Mix((long)Math.Round(p.y * 1000)); Mix((long)Math.Round(p.z * 1000));
                Mix((long)Math.Round(n.x * 100)); Mix((long)Math.Round(n.y * 100)); Mix((long)Math.Round(n.z * 100));
                Mix((long)Math.Round(uv.x * 1000)); Mix((long)Math.Round(uv.y * 1000));
                Mix((long)Math.Round(col.r * 1000)); Mix((long)Math.Round(col.g * 1000)); Mix((long)Math.Round(col.b * 1000));
                if (uv1 != default) { Mix((long)Math.Round(uv1.x * 1000)); Mix((long)Math.Round(uv1.y * 1000)); Mix((long)Math.Round(uv1.z * 1000)); Mix((long)Math.Round(uv1.w * 1000)); }
                return h;
            }
        }

        public static long TriKey(long a, long b, long c)
        {
            if (b < a && b < c) (a, b, c) = (b, c, a);
            else if (c < a && c < b) (a, b, c) = (c, a, b);
            unchecked { return a * 1000003L ^ b * 999983L ^ c * 9973L ^ (b << 21) ^ (c << 42); }
        }

        public static void Bump(Dictionary<long, int> d, long key, int by) { d.TryGetValue(key, out int v); d[key] = v + by; }

        public static int AddTris(Dictionary<long, int> into, Mesh mesh, Matrix4x4 toWorld)
        {
            if (!mesh) return 0;
            var v = mesh.vertices; var n = mesh.normals; var u = mesh.uv; var c = mesh.colors;
            var u1 = new List<Vector4>(); mesh.GetUVs(1, u1);
            Vector4 U1(int i) => u1.Count > 0 ? u1[i] : default;
            var nm = toWorld.inverse.transpose;
            int count = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    long h0 = VKey(toWorld.MultiplyPoint3x4(v[t[k]]), nm.MultiplyVector(n[t[k]]), u.Length > 0 ? u[t[k]] : Vector2.zero, c.Length > 0 ? c[t[k]] : Color.white, U1(t[k]));
                    long h1 = VKey(toWorld.MultiplyPoint3x4(v[t[k + 1]]), nm.MultiplyVector(n[t[k + 1]]), u.Length > 0 ? u[t[k + 1]] : Vector2.zero, c.Length > 0 ? c[t[k + 1]] : Color.white, U1(t[k + 1]));
                    long h2 = VKey(toWorld.MultiplyPoint3x4(v[t[k + 2]]), nm.MultiplyVector(n[t[k + 2]]), u.Length > 0 ? u[t[k + 2]] : Vector2.zero, c.Length > 0 ? c[t[k + 2]] : Color.white, U1(t[k + 2]));
                    Bump(into, TriKey(h0, h1, h2), 1);
                    count++;
                }
            }
            return count;
        }

        public static int AddTris(Dictionary<long, int> into, MeshData d, Matrix4x4 toWorld)
        {
            var nm = toWorld.inverse.transpose;
            int count = 0;
            bool has1 = d.uv1.Count > 0 && d.uv1.Count == d.pos.Count;
            Vector4 U1(int i) => has1 ? d.uv1[i] : default;
            foreach (var t in d.tris)
                for (int k = 0; k + 2 < t.Count; k += 3)
                {
                    long h0 = VKey(toWorld.MultiplyPoint3x4(d.pos[t[k]]), nm.MultiplyVector(d.nor[t[k]]), d.uv[t[k]], d.col[t[k]], U1(t[k]));
                    long h1 = VKey(toWorld.MultiplyPoint3x4(d.pos[t[k + 1]]), nm.MultiplyVector(d.nor[t[k + 1]]), d.uv[t[k + 1]], d.col[t[k + 1]], U1(t[k + 1]));
                    long h2 = VKey(toWorld.MultiplyPoint3x4(d.pos[t[k + 2]]), nm.MultiplyVector(d.nor[t[k + 2]]), d.uv[t[k + 2]], d.col[t[k + 2]], U1(t[k + 2]));
                    Bump(into, TriKey(h0, h1, h2), 1);
                    count++;
                }
            return count;
        }

        public static (int missing, int extra) Diff(Dictionary<long, int> want, Dictionary<long, int> have)
        {
            int missing = 0, extra = 0;
            foreach (var kv in want) { have.TryGetValue(kv.Key, out int h); if (kv.Value > h) missing += kv.Value - h; }
            foreach (var kv in have) { want.TryGetValue(kv.Key, out int w); if (kv.Value > w) extra += kv.Value - w; }
            return (missing, extra);
        }
    }
}
