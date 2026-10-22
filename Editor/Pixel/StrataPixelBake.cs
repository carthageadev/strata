using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Strata
{
    public static class StrataPixelBake
    {
        //! 1024 because shelves.txt and all the old bakes use this size, change it = rebake everything
        public const int Page = 1024;

        sealed class Src
        {
            public Color32[] px; public int w, h;
            public Color32[] ramp;
            public StrataBakeProfile.Style style;
            public int course, blockMin, blockMax, slab;
        }

        sealed class Job
        {
            public MeshRenderer r; public MeshFilter f; public Mesh src;
            public Vector3[] P; public int[] triV; public int[] triSub; public Vector3[] tn, tc; public float[] area;
            public int[] wid; public Dictionary<long, List<int>> edges; public int[] chartOf;
            public Src[] srcs;
        }

        sealed class Chart
        {
            public Job job; public int sub;
            public List<int> tris = new List<int>();
            public Vector3 n, U, V; public float d;
            public int iu0, iv0, iu1, iv1, page, ax, ay;
            public int W => iu1 - iu0;
            public int H => iv1 - iv0;
        }

        [MenuItem("Tools/Strata/Pixel Bake Selection", priority = 40)]
        static void BakeSelection()
        {
            var rs = Selection.gameObjects.SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)).Distinct().ToList();
            if (rs.Count == 0) { EditorUtility.DisplayDialog("Pixel bake", "Select the props to bake in the scene.", "OK"); return; }
            var profile = ProfileFor(rs[0].gameObject);
            if (!profile) { EditorUtility.DisplayDialog("Pixel bake", "No pixel bake profile found for this scene (Create ▸ Strata ▸ Pixel Bake Profile, next to the map).", "OK"); return; }
            try { Debug.Log("Strata: " + Bake(rs, profile, (s, f) => EditorUtility.DisplayProgressBar("Pixel bake", s, f))); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        [MenuItem("Tools/Strata/Restore Pixel Bake Selection", priority = 41)]
        static void RestoreSelection()
        {
            var rs = Selection.gameObjects.SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)).Distinct().ToList();
            Debug.Log("Strata: " + Restore(rs));
        }

        public static StrataBakeProfile ProfileFor(GameObject go)
        {
            string near = go.scene.path;
            var all = AssetDatabase.FindAssets("t:StrataBakeProfile").Select(AssetDatabase.GUIDToAssetPath).ToList();
            string best = null; int score = -1;
            foreach (var p in all)
            {
                string dir = Path.GetDirectoryName(p).Replace('\\', '/');
                string root = dir.Contains("/") ? dir.Substring(0, dir.IndexOf('/', dir.IndexOf('/') + 1) < 0 ? dir.Length : dir.IndexOf('/', dir.IndexOf('/') + 1)) : dir;
                int s = !string.IsNullOrEmpty(near) && near.StartsWith(root + "/") ? root.Length : 0;
                if (s > score) { score = s; best = p; }
            }
            return best != null ? AssetDatabase.LoadAssetAtPath<StrataBakeProfile>(best) : null;
        }

        static string Folder(StrataBakeProfile p)
        {
            string f = !string.IsNullOrEmpty(p.outputFolder) ? p.outputFolder : Path.GetDirectoryName(AssetDatabase.GetAssetPath(p)).Replace('\\', '/') + "/Baked";
            StrataTerrain.EnsureFolder(f);
            return f;
        }

        static bool Bakeable(MeshRenderer r)
        {
            var f = r.GetComponent<MeshFilter>();
            if (!f || !f.sharedMesh || f.sharedMesh.vertexCount == 0) return false;
            foreach (var m in r.sharedMaterials)
            {
                if (!m) continue;
                string s = m.shader ? m.shader.name : "";
                if (s.Contains("Sprite") || s.Contains("Particle") || s.Contains("Water") || s.Contains("Fog") || s.Contains("Shaft") || s.Contains("Sky") || s.Contains("Backdrop")) return false;
                if (m.renderQueue >= 2450 && !s.Contains("Lit")) return false;
            }
            return true;
        }

        sealed class Pages
        {
            sealed class Shelf { public int page, y, h, x; }
            public string folder, state;
            public int count;
            readonly List<Shelf> shelves = new List<Shelf>();
            readonly List<int> top = new List<int>();
            public readonly Dictionary<int, (Color32[] col, float[] hgt)> open = new Dictionary<int, (Color32[], float[])>();
            public readonly HashSet<int> touched = new HashSet<int>();

            // TODO kif tfasakh page men Baked, shelves.txt yab9a y7seb fiha, lezem nsal7ouha
            public static Pages Load(string folder)
            {
                var p = new Pages { folder = folder, state = folder + "/Bake~/shelves.txt" };
                Directory.CreateDirectory(folder + "/Bake~");
                if (File.Exists(p.state))
                    foreach (var line in File.ReadAllLines(p.state))
                    {
                        var v = line.Split(',').Select(int.Parse).ToArray();
                        if (v.Length == 2) { while (p.top.Count <= v[0]) p.top.Add(0); p.top[v[0]] = v[1]; p.count = Math.Max(p.count, v[0] + 1); }
                        else if (v.Length == 4) p.shelves.Add(new Shelf { page = v[0], y = v[1], h = v[2], x = v[3] });
                    }
                return p;
            }

            public void Place(List<Chart> charts)
            {
                foreach (var ch in charts.OrderByDescending(c => c.H).ThenByDescending(c => c.W))
                {
                    int w = ch.W + 2, h = ch.H + 2;
                    Shelf best = null;
                    foreach (var sh in shelves)
                        if (sh.h >= h && sh.h <= h * 2 + 4 && sh.x + w <= Page && (best == null || sh.h < best.h)) best = sh;
                    if (best == null)
                    {
                        int pg = 0;
                        while (pg < top.Count && top[pg] + h > Page) pg++;
                        if (pg == top.Count) top.Add(0);
                        best = new Shelf { page = pg, y = top[pg], h = h, x = 0 };
                        top[pg] += h;
                        shelves.Add(best);
                    }
                    ch.page = best.page; ch.ax = best.x; ch.ay = best.y; best.x += w;
                    count = Math.Max(count, best.page + 1);
                    touched.Add(best.page);
                }
            }

            public string Albedo(int p) => $"{folder}/Pages/Page {p}_albedo.png";
            public string Normal(int p) => $"{folder}/Pages/Page {p}_normal.png";
            string Height(int p) => $"{folder}/Bake~/Page {p}_height.png";

            public (Color32[] col, float[] hgt) Canvas(int p)
            {
                if (open.TryGetValue(p, out var c)) return c;
                var col = new Color32[Page * Page]; var hgt = new float[Page * Page];
                string mask = folder + $"/Bake~/Page {p}_mask.png";
                if (File.Exists(mask))
                {
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    t.LoadImage(File.ReadAllBytes(mask)); if (t.width == Page) col = t.GetPixels32();
                    Object.DestroyImmediate(t);
                }
                if (File.Exists(Height(p)))
                {
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                    t.LoadImage(File.ReadAllBytes(Height(p)));
                    if (t.width == Page) { var h = t.GetPixels32(); for (int i = 0; i < h.Length; i++) hgt[i] = h[i].r / 200f; }
                    Object.DestroyImmediate(t);
                }
                open[p] = (col, hgt);
                return open[p];
            }

            public Material Save(int p)
            {
                StrataTerrain.EnsureFolder(folder + "/Pages");
                var (col, hgt) = Canvas(p);
                var outCol = (Color32[])col.Clone(); var outH = (float[])hgt.Clone();
                Dilate(outCol, outH, col.Select(c => c.a > 0).ToArray());
                for (int i = 0; i < outCol.Length; i++) outCol[i].a = 255;
                var ta = new Texture2D(Page, Page, TextureFormat.RGBA32, false); ta.SetPixels32(outCol); ta.Apply(false);
                File.WriteAllBytes(Albedo(p), ta.EncodeToPNG()); Object.DestroyImmediate(ta);
                var keep = new Texture2D(Page, Page, TextureFormat.RGBA32, false); keep.SetPixels32(col); keep.Apply(false);
                var raw = keep.EncodeToPNG(); Object.DestroyImmediate(keep);
                File.WriteAllBytes(folder + $"/Bake~/Page {p}_mask.png", raw);
                var th = new Texture2D(Page, Page, TextureFormat.RGBA32, false, true);
                th.SetPixels32(hgt.Select(h => { byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(h * 200f), 0, 255); return new Color32(b, b, b, 255); }).ToArray()); th.Apply(false);
                File.WriteAllBytes(Height(p), th.EncodeToPNG()); Object.DestroyImmediate(th);
                var tn = new Texture2D(Page, Page, TextureFormat.RGBA32, false, true); tn.SetPixels32(Normals(outH)); tn.Apply(false);
                File.WriteAllBytes(Normal(p), tn.EncodeToPNG()); Object.DestroyImmediate(tn);
                foreach (var (path, lin) in new[] { (Albedo(p), false), (Normal(p), true) })
                {
                    AssetDatabase.ImportAsset(path);
                    var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                    imp.textureType = lin ? TextureImporterType.NormalMap : TextureImporterType.Default; imp.sRGBTexture = !lin;
                    imp.filterMode = FilterMode.Point; imp.mipmapEnabled = false; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.wrapMode = TextureWrapMode.Clamp; imp.maxTextureSize = Page;
                    imp.SaveAndReimport();
                }
                return Mat(p);
            }

            public Material Mat(int p)
            {
                string path = $"{folder}/Pages/Page {p}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat) return mat;
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = $"Pixel Bake {p}" };
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Smoothness", 0.04f); mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_EnvironmentReflections", 0f); mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                mat.SetFloat("_SpecularHighlights", 0f); mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                mat.EnableKeyword("_NORMALMAP");
                StrataTerrain.EnsureFolder(folder + "/Pages");
                AssetDatabase.CreateAsset(mat, path);
                return mat;
            }

            public void Link(int p)
            {
                var mat = Mat(p);
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Albedo(p)));
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Normal(p)));
                EditorUtility.SetDirty(mat);
            }

            public void Close() => File.WriteAllLines(state, top.Select((t, i) => $"{i},{t}").Concat(shelves.Select(sh => $"{sh.page},{sh.y},{sh.h},{sh.x}")));
        }

        public static string Bake(List<MeshRenderer> renderers, StrataBakeProfile profile, Action<string, float> progress = null, float seconds = float.MaxValue)
        {
            var t0 = DateTime.Now;
            float D = profile.density;
            string folder = Folder(profile);
            var log = BakeLog.Load(folder);
            var pages = Pages.Load(folder);
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.type == LightType.Directional).OrderByDescending(l => l.intensity).FirstOrDefault();
            var L = sun ? -sun.transform.forward : new Vector3(-0.45f, 0.75f, -0.48f).normalized;
            var srcCache = new Dictionary<(Material, StrataBakeProfile.Rule), Src>();
            int skipped = 0, baked = 0, left = 0, faces = 0;
            var holder = new GameObject("Strata bake occluder") { hideFlags = HideFlags.HideAndDontSave };
            var occ = holder.AddComponent<MeshCollider>();
            StrataTerrain.EnsureFolder(folder + "/Meshes");
            var done = new List<(Job job, List<Chart> charts)>();
            long budget = seconds >= 1e6f ? long.MaxValue : (long)(seconds * 30000f), texels = 0;
            try
            {
                for (int ri = 0; ri < renderers.Count; ri++)
                {
                    var r = renderers[ri];
                    if (!r || !Bakeable(r)) { skipped++; continue; }
                    var f = r.GetComponent<MeshFilter>();
                    if (log.IsBaked(f.sharedMesh)) { skipped++; continue; }
                    if (done.Count > 0 && texels > budget) { left = renderers.Skip(ri).Count(x => x && Bakeable(x) && !log.IsBaked(x.GetComponent<MeshFilter>().sharedMesh)); break; }
                    var mats = r.sharedMaterials;
                    var srcs = new Src[f.sharedMesh.subMeshCount];
                    bool any = false;
                    for (int s = 0; s < srcs.Length; s++)
                    {
                        var m = s < mats.Length ? mats[s] : null;
                        var rule = profile.RuleFor(m);
                        if (rule == null && !profile.bakeUnmatched) continue;
                        if (rule != null && rule.style == StrataBakeProfile.Style.Keep) continue;
                        var key = (m, rule);
                        if (!srcCache.TryGetValue(key, out var src)) srcCache[key] = src = MakeSrc(m, rule, profile);
                        srcs[s] = src; if (src != null) any = true;
                    }
                    if (!any) { skipped++; continue; }
                    var job = Prepare(r, f, srcs);
                    var charts = Charts(job, D);
                    if (charts.Count == 0) { skipped++; continue; }
                    texels += charts.Sum(c => (long)c.W * c.H);
                    done.Add((job, charts));
                }
                pages.Place(done.SelectMany(d => d.charts).ToList());
                for (int k = 0; k < done.Count; k++)
                {
                    var (job, charts) = done[k];
                    progress?.Invoke($"Painting {job.r.name}", (float)k / done.Count);
                    holder.transform.SetPositionAndRotation(job.r.transform.position, job.r.transform.rotation);
                    holder.transform.localScale = job.r.transform.lossyScale;
                    occ.sharedMesh = job.src;
                    Physics.SyncTransforms();
                    foreach (var ch in charts)
                    {
                        var (col, hgt) = pages.Canvas(ch.page);
                        Paint(ch, D, L, occ, col, hgt);
                    }
                    faces += charts.Count;
                    baked++;
                }
            }
            finally { Object.DestroyImmediate(holder); }
            if (baked == 0) return $"nothing to bake ({skipped} skipped)";
            foreach (int p in pages.touched.OrderBy(x => x)) pages.Save(p);
            foreach (int p in pages.touched) pages.Link(p);
            foreach (var (j, charts) in done)
            {
                var used = charts.Select(c => c.page).Distinct().OrderBy(x => x).ToList();
                var mesh = BuildMesh(j, charts, used, D, out var kept);
                string mp = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Meshes/{Safe(j.r.name)}.asset");
                AssetDatabase.CreateAsset(mesh, mp);
                log.Add(mesh, j.src, j.r.sharedMaterials);
                Undo.RecordObject(j.f, "Pixel bake"); Undo.RecordObject(j.r, "Pixel bake");
                var col2 = j.r.GetComponent<MeshCollider>();
                if (col2 && col2.sharedMesh == j.src) { Undo.RecordObject(col2, "Pixel bake"); col2.sharedMesh = mesh; }
                var srcMats = j.r.sharedMaterials;
                j.f.sharedMesh = mesh;
                j.r.sharedMaterials = used.Select(p => pages.Mat(p)).Concat(kept.Select(s => s < srcMats.Length ? srcMats[s] : null)).ToArray();
                if (j.r.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(j.r.gameObject.scene);
                else if (PrefabUtility.IsPartOfPrefabAsset(j.r)) EditorUtility.SetDirty(j.r);
            }
            pages.Close();
            log.Save();
            AssetDatabase.SaveAssets();
            return $"baked {baked} ({faces} faces) onto {pages.count} page{(pages.count == 1 ? "" : "s")}, {skipped} skipped, {left} left";
        }

        //? restore still work if the prop got moved to other folder? the log keep the guid so maybe yes
        public static string Restore(List<MeshRenderer> renderers)
        {
            int n = 0;
            foreach (var r in renderers)
            {
                var f = r.GetComponent<MeshFilter>(); if (!f || !f.sharedMesh) continue;
                var folder = Path.GetDirectoryName(Path.GetDirectoryName(AssetDatabase.GetAssetPath(f.sharedMesh))).Replace('\\', '/');
                if (string.IsNullOrEmpty(folder)) continue;
                var log = BakeLog.Load(folder);
                if (!log.Original(f.sharedMesh, out var mesh, out var mats)) continue;
                Undo.RecordObject(f, "Restore"); Undo.RecordObject(r, "Restore");
                var c = r.GetComponent<MeshCollider>(); if (c && c.sharedMesh == f.sharedMesh) { Undo.RecordObject(c, "Restore"); c.sharedMesh = mesh; }
                f.sharedMesh = mesh; r.sharedMaterials = mats; n++;
                if (r.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
            }
            return $"restored {n} props";
        }

        static string Safe(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s; }

        static Src MakeSrc(Material m, StrataBakeProfile.Rule rule, StrataBakeProfile profile)
        {
            float D = profile.density;
            Texture2D tex = null; Color tint = m && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
            bool own = !(rule != null && rule.paint && rule.paint.albedo);
            if (!own) { tex = rule.paint.albedo; tint *= rule.paint.tint; }
            else if (m && m.HasProperty("_BaseMap")) tex = m.GetTexture("_BaseMap") as Texture2D;
            if (!tex) return null;
            var s = new Src { style = rule?.style ?? StrataBakeProfile.Style.Sample, course = rule?.course ?? 6, blockMin = rule?.blockMin ?? 9, blockMax = rule?.blockMax ?? 16, slab = rule?.slab ?? 9 };
            int step = own ? Mathf.Max(1, Mathf.RoundToInt(profile.sourceDensity / D)) : 1;
            var px = Read(tex);
            s.w = tex.width / step; s.h = tex.height / step;
            s.px = new Color32[s.w * s.h];
            for (int y = 0; y < s.h; y++) for (int x = 0; x < s.w; x++)
            {
                var c = px[(y * step) * tex.width + x * step];
                s.px[y * s.w + x] = new Color32((byte)Mathf.Clamp(c.r * tint.r, 0, 255), (byte)Mathf.Clamp(c.g * tint.g, 0, 255), (byte)Mathf.Clamp(c.b * tint.b, 0, 255), 255);
            }
            s.ramp = RampOf(s.px, 8);
            return s;
        }

        static Color32[] Read(Texture2D t)
        {
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(t, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var r = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
            r.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0); r.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var px = r.GetPixels32(); Object.DestroyImmediate(r);
            return px;
        }

        static long Key(Vector3 p) { long x = (long)Math.Round(p.x * 2000), y = (long)Math.Round(p.y * 2000), z = (long)Math.Round(p.z * 2000); return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L); }
        static long EK(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        static Job Prepare(MeshRenderer r, MeshFilter f, Src[] srcs)
        {
            var j = new Job { r = r, f = f, src = f.sharedMesh, srcs = srcs };
            var l2w = r.transform.localToWorldMatrix;
            var vl = j.src.vertices;
            j.P = vl.Select(v => l2w.MultiplyPoint3x4(v)).ToArray();
            var triV = new List<int>(); var triSub = new List<int>();
            for (int sm = 0; sm < j.src.subMeshCount; sm++)
            {
                var t = j.src.GetTriangles(sm);
                for (int i = 0; i < t.Length; i += 3) { triV.Add(t[i]); triV.Add(t[i + 1]); triV.Add(t[i + 2]); triSub.Add(sm); }
            }
            j.triV = triV.ToArray(); j.triSub = triSub.ToArray();
            int T = j.triSub.Length;
            var weld = new Dictionary<long, int>(); j.wid = new int[j.P.Length];
            for (int i = 0; i < j.P.Length; i++) { long k = Key(j.P[i]); if (!weld.TryGetValue(k, out int id)) { id = weld.Count; weld[k] = id; } j.wid[i] = id; }
            j.tn = new Vector3[T]; j.tc = new Vector3[T]; j.area = new float[T];
            for (int t = 0; t < T; t++)
            {
                Vector3 a = j.P[j.triV[3 * t]], b = j.P[j.triV[3 * t + 1]], c = j.P[j.triV[3 * t + 2]];
                var cr = Vector3.Cross(b - a, c - a);
                j.area[t] = cr.magnitude * 0.5f; j.tn[t] = cr.sqrMagnitude > 1e-12f ? cr.normalized : Vector3.up; j.tc[t] = (a + b + c) / 3f;
            }
            j.edges = new Dictionary<long, List<int>>();
            for (int t = 0; t < T; t++)
                for (int e = 0; e < 3; e++)
                {
                    long k = EK(j.wid[j.triV[3 * t + e]], j.wid[j.triV[3 * t + (e + 1) % 3]]);
                    if (!j.edges.TryGetValue(k, out var l)) j.edges[k] = l = new List<int>(2);
                    l.Add(t);
                }
            j.chartOf = Enumerable.Repeat(-1, T).ToArray();
            return j;
        }

        static List<Chart> Charts(Job j, float D)
        {
            var list = new List<Chart>();
            int T = j.triSub.Length;
            //! 32 degree, less than that and the church spire split in like 10 charts, dont touch
            float cosMax = Mathf.Cos(32f * Mathf.Deg2Rad);
            for (int t0 = 0; t0 < T; t0++)
            {
                if (j.chartOf[t0] >= 0 || j.area[t0] < 1e-9f || j.srcs[j.triSub[t0]] == null) continue;
                var ch = new Chart { job = j, sub = j.triSub[t0] };
                int id = list.Count;
                var q = new Queue<int>(); q.Enqueue(t0); j.chartOf[t0] = id;
                var seed = j.tn[t0];
                while (q.Count > 0)
                {
                    int t = q.Dequeue(); ch.tris.Add(t);
                    for (int e = 0; e < 3; e++)
                        foreach (int o in j.edges[EK(j.wid[j.triV[3 * t + e]], j.wid[j.triV[3 * t + (e + 1) % 3]])])
                            if (j.chartOf[o] < 0 && j.triSub[o] == ch.sub && j.area[o] > 1e-9f && Vector3.Dot(j.tn[o], seed) > cosMax) { j.chartOf[o] = id; q.Enqueue(o); }
                }
                list.Add(ch);
            }
            foreach (var ch in list)
            {
                Vector3 n = Vector3.zero; foreach (int t in ch.tris) n += j.tn[t] * j.area[t];
                n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
                ch.n = n;
                ch.U = Mathf.Abs(n.y) > 0.75f ? Vector3.right : new Vector3(-n.z, 0f, n.x).normalized;
                ch.V = Vector3.Cross(ch.U, n).normalized;
                float d = 0, ws = 0; foreach (int t in ch.tris) { d += Vector3.Dot(j.tc[t], n) * j.area[t]; ws += j.area[t]; }
                ch.d = d / Mathf.Max(1e-9f, ws);
                float mu = float.MaxValue, mv = float.MaxValue, xu = float.MinValue, xv = float.MinValue;
                foreach (int t in ch.tris) for (int e = 0; e < 3; e++) { var p = j.P[j.triV[3 * t + e]]; float u = Vector3.Dot(p, ch.U), v = Vector3.Dot(p, ch.V); mu = Mathf.Min(mu, u); mv = Mathf.Min(mv, v); xu = Mathf.Max(xu, u); xv = Mathf.Max(xv, v); }
                ch.iu0 = Mathf.FloorToInt(mu * D + 1e-3f); ch.iv0 = Mathf.FloorToInt(mv * D + 1e-3f);
                ch.iu1 = Mathf.Max(ch.iu0 + 1, Mathf.CeilToInt(xu * D - 1e-3f)); ch.iv1 = Mathf.Max(ch.iv0 + 1, Mathf.CeilToInt(xv * D - 1e-3f));
                if (ch.W + 2 > Page) ch.iu1 = ch.iu0 + Page - 2;
                if (ch.H + 2 > Page) ch.iv1 = ch.iv0 + Page - 2;
            }
            return list;
        }

        static readonly Vector3[] AoDirs = Enumerable.Range(0, 10).Select(i =>
        {
            float a = (i + 0.5f) / 10f * Mathf.PI * 2f, r = Mathf.Sqrt((i % 3 + 0.5f) / 3f);
            return new Vector3(Mathf.Cos(a) * r, Mathf.Sqrt(Mathf.Max(0f, 1f - r * r)), Mathf.Sin(a) * r);
        }).ToArray();

        static void Paint(Chart ch, float D, Vector3 L, MeshCollider occ, Color32[] col, float[] hgt)
        {
            var j = ch.job; var s = j.srcs[ch.sub];
            Vector2 Pr(int vi) { var p = j.P[vi]; return new Vector2(Vector3.Dot(p, ch.U) * D - ch.iu0, Vector3.Dot(p, ch.V) * D - ch.iv0); }
            var tri2 = ch.tris.Select(t => (Pr(j.triV[3 * t]), Pr(j.triV[3 * t + 1]), Pr(j.triV[3 * t + 2]))).ToList();
            var bound = new List<(Vector2 a, Vector2 b, Vector2 o, int type)>();
            int me = j.chartOf[ch.tris[0]];
            foreach (int t in ch.tris)
                for (int e = 0; e < 3; e++)
                {
                    int va = j.triV[3 * t + e], vb = j.triV[3 * t + (e + 1) % 3];
                    var near = j.edges[EK(j.wid[va], j.wid[vb])];
                    if (near.Any(o => o != t && j.chartOf[o] == me)) continue;
                    var other = near.FirstOrDefault(o => o != t);
                    Vector2 A = Pr(va), B = Pr(vb), C = Pr(j.triV[3 * t + (e + 2) % 3]);
                    Vector2 dir = (B - A).normalized, on = new Vector2(dir.y, -dir.x);
                    if (Vector2.Dot(on, C - A) > 0) on = -on;
                    int type = near.Count < 2 ? 0 : Vector3.Dot(ch.n, j.tc[other] - j.tc[t]) < -1e-4f ? 1 : 2;
                    bound.Add((A, B, on, type));
                }
            int W = ch.W, H = ch.H;
            var block = new int[W * H]; var bev = new int[W * H];
            bool flat = Mathf.Abs(ch.n.y) > 0.75f;
            Lay(s, ch, W, H, block, bev, flat);
            int mid = s.ramp.Length / 2;
            var basis = Quaternion.FromToRotation(Vector3.up, ch.n);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var q = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = false;
                    foreach (var tr in tri2) if (InTri(q, tr.Item1, tr.Item2, tr.Item3, 0.55f)) { inside = true; break; }
                    if (!inside) continue;
                    int gu = ch.iu0 + x, gv = ch.iv0 + y;
                    var tex = s.px[Mod(gv, s.h) * s.w + Mod(gu, s.w)];
                    int b = block[y * W + x];
                    Color32 c; float h;
                    if (s.style == StrataBakeProfile.Style.Sample || b == int.MinValue) { c = tex; h = Lum(tex); }
                    else if (b < 0) { c = s.ramp[Mathf.Clamp(mid - 3, 0, s.ramp.Length - 1)]; h = 0.05f; }
                    else
                    {
                        float r = Hash01(b * 7919 + 17);
                        int tone = mid + (r < 0.28f ? -1 : r > 0.78f ? 1 : 0);
                        if (s.style == StrataBakeProfile.Style.Planks) tone += Lum(tex) > 0.62f ? 1 : Lum(tex) < 0.32f ? -1 : 0;
                        float chip = Hash01(gu * 73856093 ^ gv * 19349663);
                        if (chip < 0.05f) tone -= 1; else if (chip > 0.965f) tone += 1;
                        c = s.ramp[Mathf.Clamp(tone, 0, s.ramp.Length - 1)];
                        h = 0.75f + bev[y * W + x] * 0.1f;
                    }
                    int delta = bev[y * W + x];
                    foreach (var e in bound)
                    {
                        float dist = SegDist(q, e.a, e.b);
                        if (dist > 1.6f) continue;
                        bool first = dist <= 0.85f;
                        bool top = e.o.y > 0.7f, bottom = e.o.y < -0.7f, left = e.o.x < -0.7f, right = e.o.x > 0.7f;
                        if (e.type == 1) { if (first) delta += flat ? (bottom || left ? 1 : top || right ? -1 : 0) : (top ? 2 : left ? 1 : right ? -1 : bottom ? -1 : 0); }
                        else if (e.type == 2) { if (first) delta -= 1; }
                        else if (!flat && bottom) delta += first ? -2 : -1;
                        else if (!flat && top && first) delta += 1;
                    }
                    var wp = ch.U * ((gu + 0.5f) / D) + ch.V * ((gv + 0.5f) / D) + ch.n * ch.d;
                    if (occ)
                    {
                        var org = wp + ch.n * 0.02f;
                        int hit = 0, rays = 0;
                        foreach (var dl in AoDirs)
                        {
                            var dir = basis * dl;
                            if (dir.y < -0.2f) continue;
                            rays++;
                            if (occ.Raycast(new Ray(org, dir), out _, 0.4f)) hit++;
                        }
                        float o = rays > 0 ? hit / (float)rays : 0f;
                        if (o > 0.85f) delta -= 2; else if (o > 0.55f) delta -= 1;
                        if (Vector3.Dot(ch.n, L) > 0.05f && occ.Raycast(new Ray(org, L), out _, 2.5f)) delta -= 1;
                    }
                    if (delta != 0) c = Shift(s.ramp, c, delta);
                    int i = (ch.ay + 1 + y) * Page + ch.ax + 1 + x;
                    c.a = 255; col[i] = c; hgt[i] = h + delta * 0.04f;
                }
        }

        static void Lay(Src s, Chart ch, int W, int H, int[] block, int[] bev, bool flat)
        {
            var st = s.style;
            if (st == StrataBakeProfile.Style.Sample) { for (int i = 0; i < block.Length; i++) block[i] = int.MinValue; return; }
            //! keep the mask, negative ids made every texel a joint and props got full of black lines
            int seed = (ch.iu0 * 92821 + ch.iv0 * 68917 + ch.sub * 7 + ch.job.r.GetInstanceID()) & 0x1fffffff;
            if (flat && st != StrataBakeProfile.Style.Planks) st = StrataBakeProfile.Style.Slabs;
            int id = 1;
            if (st == StrataBakeProfile.Style.Shingles || st == StrataBakeProfile.Style.Planks)
            {
                bool shingles = st == StrataBakeProfile.Style.Shingles;
                int rowH = Math.Max(3, s.course);
                for (int j0 = 0, row = 0; j0 < H; row++)
                {
                    int rh = Math.Min(rowH, H - j0);
                    int x = -(int)(Hash01(seed + row * 37) * (shingles ? s.blockMin : s.blockMax));
                    while (x < W)
                    {
                        int len = shingles ? s.blockMin + (int)(Hash01(seed + id * 101) * (s.blockMax - s.blockMin + 1)) : s.blockMax * 2 + (int)(Hash01(seed + id * 101) * s.blockMax * 2);
                        for (int y = j0; y < j0 + rh; y++)
                            for (int i = Math.Max(0, x); i < Math.Min(W, x + len); i++)
                            {
                                int k = y * W + i;
                                bool seam = shingles ? (y == j0 && j0 > 0) || i == x + len - 1 : y == j0 && j0 > 0 || (i == x + len - 1 && x + len < W);
                                block[k] = seam ? -1 : seed + id;
                                if (!seam)
                                {
                                    int b = 0;
                                    if (shingles) { if (y == j0 + 1) b -= 1; if (y == j0 + rh - 1) b += 1; }
                                    else { if (y == j0 + rh - 1) b += 1; if (y == j0 + 1 && j0 > 0) b -= 1; }
                                    bev[k] = b;
                                }
                            }
                        x += len; id++;
                    }
                    j0 += rh;
                }
                return;
            }
            bool slabs = st == StrataBakeProfile.Style.Slabs;
            int course = slabs ? Math.Max(4, s.slab) : s.course;
            for (int j0 = 0, row = 0; j0 < H; row++)
            {
                int ch_ = Math.Min(course, H - j0);
                if (H - (j0 + ch_) < course / 2 && H - (j0 + ch_) > 0) ch_ = H - j0;
                int x = 0;
                int first = slabs ? (int)(Hash01(seed + row * 13) * s.slab) : (row % 2 == 1 ? (s.blockMin + s.blockMax) / 4 : 0) + (int)(Hash01(seed + row * 31) * 3);
                while (x < W)
                {
                    int len = x == 0 && first > 2 ? first : slabs ? Math.Max(4, s.slab - 2 + (int)(Hash01(seed + id * 53) * 5)) : s.blockMin + (int)(Hash01(seed + id * 101) * (s.blockMax - s.blockMin + 1));
                    if (W - (x + len) < Math.Max(2, s.blockMin / 2)) len = W - x;
                    for (int y = j0; y < j0 + ch_; y++)
                        for (int i = x; i < Math.Min(W, x + len); i++)
                        {
                            int k = y * W + i;
                            bool joint = (y == j0 + ch_ - 1 && j0 + ch_ < H) || (i == x + len - 1 && x + len < W);
                            block[k] = joint ? -1 : seed + id;
                            if (!joint)
                            {
                                int b = 0;
                                if (y == j0 + ch_ - 2 || (y == H - 1 && y == j0 + ch_ - 1)) b += 1;
                                if (y == j0 && j0 > 0) b -= 1;
                                if (i == x + len - 2 && x + len < W) b -= 1;
                                bev[k] = Math.Max(-1, Math.Min(1, b));
                            }
                        }
                    x += len; id++;
                }
                j0 += ch_;
            }
        }

        static Mesh BuildMesh(Job j, List<Chart> mine, List<int> used, float D, out List<int> kept)
        {
            var w2l = j.r.transform.worldToLocalMatrix;
            var vl = j.src.vertices; var srcN = j.src.normals;
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nuv = new List<Vector2>(); var ntan = new List<Vector4>();
            var subs = used.Select(_ => new List<int>()).ToArray();
            foreach (var ch in mine)
            {
                var map = new Dictionary<int, int>();
                Vector3 Ul = w2l.MultiplyVector(ch.U).normalized, nl = w2l.MultiplyVector(ch.n).normalized, Vl = w2l.MultiplyVector(ch.V).normalized;
                float sgn = Vector3.Dot(Vector3.Cross(nl, Ul), Vl) < 0 ? -1f : 1f;
                var into = subs[used.IndexOf(ch.page)];
                foreach (int t in ch.tris)
                    for (int e = 0; e < 3; e++)
                    {
                        int vi = j.triV[3 * t + e];
                        if (!map.TryGetValue(vi, out int ni))
                        {
                            ni = nv.Count; map[vi] = ni;
                            nv.Add(vl[vi]); nn.Add(srcN.Length > 0 ? srcN[vi] : nl);
                            var p = j.P[vi];
                            float u = Vector3.Dot(p, ch.U) * D - ch.iu0, v = Vector3.Dot(p, ch.V) * D - ch.iv0;
                            nuv.Add(new Vector2((ch.ax + 1 + u) / Page, (ch.ay + 1 + v) / Page));
                            ntan.Add(new Vector4(Ul.x, Ul.y, Ul.z, sgn));
                        }
                        into.Add(ni);
                    }
            }
            kept = new List<int>();
            var keptTris = new List<List<int>>();
            var srcUv = j.src.uv; var srcTan = j.src.tangents;
            for (int sm = 0; sm < j.src.subMeshCount; sm++)
            {
                if (j.srcs[sm] != null) continue;
                var tris = j.src.GetTriangles(sm);
                if (tris.Length == 0) continue;
                var map = new Dictionary<int, int>(); var into = new List<int>();
                foreach (int vi in tris)
                {
                    if (!map.TryGetValue(vi, out int ni))
                    {
                        ni = nv.Count; map[vi] = ni;
                        nv.Add(vl[vi]); nn.Add(srcN.Length > 0 ? srcN[vi] : Vector3.up);
                        nuv.Add(srcUv.Length > 0 ? srcUv[vi] : Vector2.zero);
                        ntan.Add(srcTan.Length > 0 ? srcTan[vi] : new Vector4(1, 0, 0, 1));
                    }
                    into.Add(ni);
                }
                kept.Add(sm); keptTris.Add(into);
            }
            var mesh = new Mesh { name = j.src.name + " (pixel)", indexFormat = nv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(nv); mesh.SetNormals(nn); mesh.SetUVs(0, nuv); mesh.SetTangents(ntan);
            mesh.subMeshCount = subs.Length + keptTris.Count;
            for (int i = 0; i < subs.Length; i++) mesh.SetTriangles(subs[i], i);
            for (int i = 0; i < keptTris.Count; i++) mesh.SetTriangles(keptTris[i], subs.Length + i);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Dilate(Color32[] col, float[] hgt, bool[] filled)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var add = new List<int>();
                for (int y = 0; y < Page; y++) for (int x = 0; x < Page; x++)
                {
                    int i = y * Page + x; if (filled[i]) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= Page || ny >= Page) continue;
                        int j = ny * Page + nx;
                        if (filled[j]) { col[i] = col[j]; hgt[i] = hgt[j]; add.Add(i); break; }
                    }
                }
                foreach (int i in add) filled[i] = true;
            }
        }

        static Color32[] Normals(float[] hgt)
        {
            var o = new Color32[Page * Page];
            for (int y = 0; y < Page; y++) for (int x = 0; x < Page; x++)
            {
                float hl = hgt[y * Page + Math.Max(0, x - 1)], hr = hgt[y * Page + Math.Min(Page - 1, x + 1)];
                float hd = hgt[Math.Max(0, y - 1) * Page + x], hu = hgt[Math.Min(Page - 1, y + 1) * Page + x];
                var n = new Vector3((hl - hr) * 2.2f, (hd - hu) * 2.2f, 1f).normalized;
                o[y * Page + x] = new Color32((byte)(n.x * 127.5f + 127.5f), (byte)(n.y * 127.5f + 127.5f), (byte)(n.z * 127.5f + 127.5f), 255);
            }
            return o;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float pad)
        {
            float s1 = Cross(b - a, p - a), s2 = Cross(c - b, p - b), s3 = Cross(a - c, p - c);
            bool neg = s1 < 0 || s2 < 0 || s3 < 0, pos = s1 > 0 || s2 > 0 || s3 > 0;
            if (!(neg && pos)) return true;
            return SegDist(p, a, b) < pad || SegDist(p, b, c) < pad || SegDist(p, c, a) < pad;
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l = ab.sqrMagnitude;
            float t = l > 1e-9f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l) : 0f;
            return (p - (a + ab * t)).magnitude;
        }
        static int Mod(int a, int m) => ((a % m) + m) % m;
        static float Hash01(int x) { unchecked { uint h = (uint)x * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; return (h & 0xffffff) / 16777216f; } }
        static float Lum(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

        static Color32 Shift(Color32[] ramp, Color32 c, int delta)
        {
            float l = Lum(c); int best = 0; float bd = 9;
            for (int i = 0; i < ramp.Length; i++) { float d = Mathf.Abs(Lum(ramp[i]) - l); if (d < bd) { bd = d; best = i; } }
            int j = Mathf.Clamp(best + delta, 0, ramp.Length - 1);
            if (j == best) j = Mathf.Clamp(best + Math.Sign(delta), 0, ramp.Length - 1);
            float k = Lum(ramp[j]) / Mathf.Max(0.03f, Lum(ramp[best]));
            var own = new Color(c.r / 255f * k, c.g / 255f * k, c.b / 255f * k, 1f);
            var o = Color.Lerp(own, ramp[j], 0.3f);
            return new Color32((byte)Mathf.Clamp(o.r * 255f, 0, 255), (byte)Mathf.Clamp(o.g * 255f, 0, 255), (byte)Mathf.Clamp(o.b * 255f, 0, 255), 255);
        }

        static Color32[] RampOf(Color32[] px, int n)
        {
            var sorted = px.Where(p => p.a > 128).OrderBy(Lum).ToArray();
            if (sorted.Length == 0) sorted = new[] { new Color32(128, 128, 128, 255) };
            var ramp = new Color32[n];
            for (int k = 0; k < n; k++)
            {
                int a = sorted.Length * k / n, b = Math.Max(a + 1, sorted.Length * (k + 1) / n);
                b = Math.Min(b, sorted.Length); a = Math.Min(a, b - 1);
                float r = 0, g = 0, bl = 0; for (int i = a; i < b; i++) { r += sorted[i].r; g += sorted[i].g; bl += sorted[i].b; }
                int cnt = b - a; ramp[k] = new Color32((byte)(r / cnt), (byte)(g / cnt), (byte)(bl / cnt), 255);
            }
            float mr = 0, mg = 0, mb = 0; foreach (var p in sorted) { mr += p.r; mg += p.g; mb += p.b; }
            mr /= sorted.Length; mg /= sorted.Length; mb /= sorted.Length;
            float ml = (0.299f * mr + 0.587f * mg + 0.114f * mb) / 255f;
            for (int k = 0; k < n; k++)
            {
                float f = Lum(ramp[k]) / Mathf.Max(0.02f, ml);
                var warm = new Color32((byte)Mathf.Clamp(mr * f, 0, 255), (byte)Mathf.Clamp(mg * f, 0, 255), (byte)Mathf.Clamp(mb * f, 0, 255), 255);
                ramp[k] = Color32.Lerp(ramp[k], warm, k == 0 ? 0.15f : 0.4f);
            }
            ramp[0] = Color32.Lerp(ramp[0], new Color32(30, 24, 32, 255), 0.2f);
            ramp[n - 1] = Color32.Lerp(ramp[n - 1], new Color32(255, 246, 220, 255), 0.15f);
            return ramp;
        }

        sealed class BakeLog
        {
            string path; readonly Dictionary<string, (string mesh, string[] mats)> map = new Dictionary<string, (string, string[])>();
            public static BakeLog Load(string folder)
            {
                var l = new BakeLog { path = folder + "/bake-log.txt" };
                if (File.Exists(l.path))
                    foreach (var line in File.ReadAllLines(l.path))
                    {
                        var p = line.Split('\t'); if (p.Length < 2) continue;
                        l.map[p[0]] = (p[1], p.Skip(2).ToArray());
                    }
                return l;
            }
            static string G(Object o) => o ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o)) + ":" + (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out _, out long id) ? id : 0) : "";
            static T Get<T>(string key) where T : Object
            {
                var p = key.Split(':'); if (p.Length < 2) return null;
                var path = AssetDatabase.GUIDToAssetPath(p[0]); long id = long.Parse(p[1]);
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is T t && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out _, out long lid) && lid == id) return t;
                return null;
            }
            public bool IsBaked(Mesh m) => m && map.ContainsKey(G(m));
            public void Add(Mesh baked, Mesh original, Material[] mats) { map[G(baked)] = (G(original), mats.Select(G).ToArray()); }
            public bool Original(Mesh baked, out Mesh mesh, out Material[] mats)
            {
                mesh = null; mats = null;
                if (!map.TryGetValue(G(baked), out var e)) return false;
                mesh = Get<Mesh>(e.mesh); mats = e.mats.Select(Get<Material>).ToArray();
                return mesh;
            }
            public void Save()
            {
                File.WriteAllLines(path, map.Select(kv => kv.Key + "\t" + kv.Value.mesh + "\t" + string.Join("\t", kv.Value.mats)));
                AssetDatabase.ImportAsset(path);
            }
        }
    }
}
