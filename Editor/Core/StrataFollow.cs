using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Strata
{
    public sealed class StrataFollow
    {
        readonly StrataMap before;

        StrataFollow(StrataMap before) { this.before = before; }

        public static StrataFollow Begin(StrataTerrain t) => new StrataFollow(t.map.CloneHeights());

        public int Apply(StrataTerrain t, string undoName)
        {
            var now = t.map;
            if (now.w != before.w || now.h != before.h || !Changed(before, now, out var area)) return 0;
            float tolerance = StrataSettings.FollowTolerance, maxSize = StrataSettings.FollowMaxSize;
            var found = new List<(Transform tr, Bounds b)>();
            var skip = t.instance ? t.instance.transform : null;
            var near = new Rect(now.x0 + area.xMin - 1, -(now.z0 + area.yMax) - 1, area.width + 2, area.height + 2);
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var go in scene.GetRootGameObjects()) Collect(go.transform, skip, now, area, near, maxSize, found);
            }
            int moved = 0;
            foreach (var (tr, b) in found)
            {
                var p = tr.position;
                double g0 = before.Height(p.x, -p.z), g1 = now.Height(p.x, -p.z), d = g1 - g0;
                if (Math.Abs(d) < 1e-4 || !Standing(p, g0, tolerance, b)) continue;
                Undo.RecordObject(tr, undoName);
                tr.position = new Vector3(p.x, (float)(p.y + d), p.z);
                PrefabUtility.RecordPrefabInstancePropertyModifications(tr);
                moved++;
            }
            return moved;
        }

        static void Collect(Transform tr, Transform skip, StrataMap m, RectInt area, Rect near, float maxSize, List<(Transform, Bounds)> into)
        {
            var go = tr.gameObject;
            if (tr == skip || !go.activeInHierarchy || (go.hideFlags & (HideFlags.HideInHierarchy | HideFlags.DontSave | HideFlags.NotEditable)) != 0) return;
            if (go.GetComponent<Camera>()) return;
            bool unit = PrefabUtility.IsAnyPrefabInstanceRoot(go) || (!PrefabUtility.IsPartOfPrefabInstance(go) && go.GetComponents<Component>().Length > 1);
            if (unit)
            {
                bool inside = Inside(m, area, tr.position);
                if (!inside && tr.childCount == 0) return;
                if (!Extent(tr, out var b)) return;
                if (!inside && (b.max.x < near.xMin || b.min.x > near.xMax || b.max.z < near.yMin || b.min.z > near.yMax)) return;
                if (Width(tr, b) <= maxSize) { if (inside) into.Add((tr, b)); return; }
            }
            for (int k = 0; k < tr.childCount; k++) Collect(tr.GetChild(k), skip, m, area, near, maxSize, into);
        }

        static readonly List<Renderer> renderers = new List<Renderer>();
        static readonly List<Collider> colliders = new List<Collider>();

        static bool Extent(Transform tr, out Bounds b)
        {
            b = default;
            bool any = false;
            tr.GetComponentsInChildren(renderers);
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            return any;
        }

        static float Width(Transform tr, Bounds shape)
        {
            Bounds b = default;
            bool any = false;
            tr.GetComponentsInChildren(colliders);
            foreach (var c in colliders)
            {
                if (!c.enabled || c.isTrigger) continue;
                if (any) b.Encapsulate(c.bounds); else { b = c.bounds; any = true; }
            }
            if (!any) b = shape;
            return Math.Max(b.size.x, b.size.z);
        }

        static bool Inside(StrataMap m, RectInt area, Vector3 p)
        {
            int i = m.CellAt(p.x, -p.z);
            if (i < 0) return false;
            int c = i % m.w, r = i / m.w;
            return c >= area.xMin && c < area.xMax && r >= area.yMin && r < area.yMax;
        }

        static bool Standing(Vector3 pivot, double ground, float tolerance, Bounds b)
        {
            if (Math.Abs(pivot.y - ground) <= tolerance) return true;
            double bottom = b.min.y;
            return bottom <= ground + tolerance && bottom >= ground - Math.Max(tolerance, 0.5 * b.size.y);
        }

        static bool Changed(StrataMap a, StrataMap b, out RectInt area)
        {
            int c0 = int.MaxValue, r0 = int.MaxValue, c1 = int.MinValue, r1 = int.MinValue;
            void Add(int c, int r)
            {
                c = Math.Max(0, Math.Min(b.w - 1, c)); r = Math.Max(0, Math.Min(b.h - 1, r));
                c0 = Math.Min(c0, c); r0 = Math.Min(r0, r); c1 = Math.Max(c1, c); r1 = Math.Max(r1, r);
            }
            for (int i = 0; i < b.Cells; i++)
            {
                int o = i * 4;
                if (a.corner[o] != b.corner[o] || a.corner[o + 1] != b.corner[o + 1] || a.corner[o + 2] != b.corner[o + 2] || a.corner[o + 3] != b.corner[o + 3] || a.stairOf[i] != b.stairOf[i])
                    Add(i % b.w, i / b.w);
            }
            if (!SameFlights(a.flights, b.flights))
                foreach (var list in new[] { a.flights, b.flights })
                    foreach (var f in list) { Add(f.x0 - b.x0, f.z0 - b.z0); Add(f.x1 - 1 - b.x0, f.z1 - 1 - b.z0); }
            area = c1 < c0 ? default : new RectInt(c0, r0, c1 - c0 + 1, r1 - r0 + 1);
            return c1 >= c0;
        }

        static bool SameFlights(List<Flight> a, List<Flight> b)
        {
            if (a.Count != b.Count) return false;
            for (int k = 0; k < a.Count; k++)
            {
                Flight x = a[k], y = b[k];
                if (x.x0 != y.x0 || x.x1 != y.x1 || x.z0 != y.z0 || x.z1 != y.z1 || x.dir != y.dir || x.y0 != y.y0 || x.y1 != y.y1 || x.steps != y.steps) return false;
            }
            return true;
        }
    }
}
