using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Strata
{
    public sealed class StrataTerrain : IDisposable
    {
        public readonly string dataPath;
        public string Folder => Path.GetDirectoryName(dataPath).Replace('\\', '/');
        public string Name => map.name;
        public StrataMap map;
        public StrataHistory history;
        public GameObject prefab;
        public GameObject instance;
        public StairLook stairLook = StairLook.Default;
        public StairStyle[] stairStyles = StairStyle.Classic(StairLook.Default);
        public Material groundMaterial, wallsMaterial;
        public string lastMessage = "";
        long savedRevision;
        StrataMap original;
        DateTime lastBackup = DateTime.MinValue;

        sealed class Chunk
        {
            public int cx, cr, c0, r0, c1, r1;
            public double px, pz;
            public Mesh ground, walls, stairs;
            public bool meshDirty, stairsDirty, unbaked;
        }

        Chunk[] chunks;
        int chunksX, chunksY;
        Texture2D mask;
        readonly Texture2D[] splat = new Texture2D[3];
        RectInt maskDirty, paintDirty;
        bool texturesUnsaved;
        bool replaying;
        readonly MeshData scratch = new MeshData();
        readonly List<Wall> wallScratch = new List<Wall>();
        readonly List<(int, int)> nbScratch = new List<(int, int)>();

        public bool Unsaved => map.revision != savedRevision || texturesUnsaved;
        public bool Unbaked => map.revision != map.bakedRevision || (chunks != null && chunks.Any(c => c.unbaked || c.meshDirty || c.stairsDirty));
        public bool Connected => instance;

        public static string OriginalPath(string dataPath) => dataPath.Substring(0, dataPath.Length - ".strata".Length) + ".original.strata";
        public string PrefabPath => $"{Folder}/{Name}.prefab";
        string MeshPath(int cx, int cr) => $"{Folder}/Meshes/{Name}_{cx:00}_{cr:00}.asset";
        string MaskPath => $"{Folder}/Textures/{Name}_Mask.png";
        string SplatPath(int k) => $"{Folder}/Textures/{Name}_Splat{k}.png";
        public static string BackupRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "StrataBackups"));
        string RecoveryPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "Strata", Name + ".recovery.strata"));

        StrataTerrain(string dataPath, StrataMap map)
        {
            this.dataPath = dataPath;
            this.map = map;
            savedRevision = map.revision;
            history = new StrataHistory(map, OnHistory);
        }

        public void Dispose()
        {
            history?.Dispose();
            history = null;
        }

        public static StrataTerrain Open(string dataPath)
        {
            var map = StrataIO.Load(dataPath);
            var t = new StrataTerrain(dataPath, map);
            try { t.Bind(); }
            catch { t.Dispose(); throw; }
            return t;
        }

        void Bind()
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) throw new InvalidOperationException($"Strata: the terrain's prefab {PrefabPath} is missing. Rebuild it from the Strata window.");
            Layout();
            for (int k = 0; k < chunks.Length; k++)
            {
                var ch = chunks[k];
                string path = MeshPath(ch.cx, ch.cr);
                var all = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
                ch.ground = AssetDatabase.LoadMainAssetAtPath(path) as Mesh;
                ch.walls = all.FirstOrDefault(m => m.name.EndsWith("_Walls"));
                ch.stairs = all.FirstOrDefault(m => m.name.EndsWith("_Stairs"));
                if (!ch.ground || !ch.walls || !ch.stairs) throw new InvalidOperationException($"Strata: chunk {ch.cx}, {ch.cr} of {Name} is missing its meshes. Rebuild the terrain from the Strata window.");
            }
            var any = prefab.GetComponentsInChildren<MeshRenderer>(true);
            groundMaterial = any.FirstOrDefault(r => r.name == "Ground")?.sharedMaterial;
            wallsMaterial = any.FirstOrDefault(r => r.name == "Walls")?.sharedMaterial;
            var stairsR = any.FirstOrDefault(r => r.name == "Stairs");
            if (stairsR) stairLook = LookOf(stairsR.sharedMaterials);
            stairStyles = StairStyle.Classic(stairLook);
            if (!groundMaterial) throw new InvalidOperationException("Strata: the terrain's ground material is missing.");
            mask = groundMaterial.GetTexture("_Mask") as Texture2D;
            for (int k = 0; k < 3; k++) splat[k] = groundMaterial.GetTexture("_Splat" + k) as Texture2D;
            if (!mask || splat.Any(s => !s)) throw new InvalidOperationException("Strata: the terrain's ground material has lost its mask or paint textures.");
            foreach (var tex in splat.Append(mask)) MakeReadable(tex);
            FindInstance();
        }

        public void FindInstance()
        {
            instance = null;
            for (int s = 0; s < EditorSceneManager.sceneCount; s++)
            {
                var scene = EditorSceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == prefab) { instance = t.gameObject; return; }
            }
        }

        static void MakeReadable(Texture2D tex)
        {
            if (!tex || tex.isReadable) return;
            var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
            if (!imp) return;
            imp.isReadable = true;
            imp.SaveAndReimport();
        }

        static StairLook LookOf(Material[] mats)
        {
            var look = StairLook.Default;
            Vector2 Size(int k, Vector2 fallback)
            {
                if (mats == null || k >= mats.Length || !mats[k]) return fallback;
                var t = mats[k].HasProperty("_BaseMap") ? mats[k].GetTexture("_BaseMap") : mats[k].mainTexture;
                return t ? new Vector2(t.width, t.height) : fallback;
            }
            look.stone = Size(0, look.stone); look.tread = Size(1, look.tread); look.deck = Size(3, look.deck); look.timber = Size(4, look.timber);
            return look;
        }

        void Layout()
        {
            int S = map.bakeChunk;
            chunksX = (map.w + S - 1) / S; chunksY = (map.h + S - 1) / S;
            chunks = new Chunk[chunksX * chunksY];
            for (int cr = 0; cr < chunksY; cr++)
                for (int cx = 0; cx < chunksX; cx++)
                {
                    int c0 = cx * S, r0 = cr * S, c1 = Math.Min(map.w, c0 + S), r1 = Math.Min(map.h, r0 + S);
                    chunks[cr * chunksX + cx] = new Chunk { cx = cx, cr = cr, c0 = c0, r0 = r0, c1 = c1, r1 = r1, px = map.x0 + c0 + (c1 - c0) / 2.0, pz = map.z0 + r0 + (r1 - r0) / 2.0 };
                }
        }

        Chunk ChunkOfCell(int c, int r) => chunks[Math.Min(chunksY - 1, r / map.bakeChunk) * chunksX + Math.Min(chunksX - 1, c / map.bakeChunk)];

        Chunk ChunkOfStair(int s)
        {
            foreach (var f in map.flights)
            {
                if (f.stair != s) continue;
                int c = Math.Max(0, Math.Min(map.w - 1, (int)Math.Floor((f.x0 + f.x1) / 2.0) - map.x0)), r = Math.Max(0, Math.Min(map.h - 1, (int)Math.Floor((f.z0 + f.z1) / 2.0) - map.z0));
                return ChunkOfCell(c, r);
            }
            return null;
        }

        public static StrataTerrain Create(StrataMap map, string folder, Material ground, Material walls, Material[] stairMaterials, Action<string, float> progress = null, StairStyle[] stairStyles = null)
        {
            map.Validate();
            EnsureFolder(folder); EnsureFolder(folder + "/Meshes"); EnsureFolder(folder + "/Textures");
            string dataPath = $"{folder}/{map.name}.strata";
            map.revision = Math.Max(1, map.revision);
            map.bakedRevision = map.revision;
            StrataIO.Save(map, dataPath);
            StrataIO.Save(map, OriginalPath(dataPath));
            var t = new StrataTerrain(dataPath, map) { groundMaterial = ground, wallsMaterial = walls };
            t.Layout();
            if (stairMaterials != null) t.stairLook = LookOf(stairMaterials);
            t.stairStyles = stairStyles ?? StairStyle.Classic(t.stairLook);
            int slots = StairStyle.Slots(t.stairStyles);
            var stairMats = new Material[slots];
            for (int k = 0; k < slots && stairMaterials != null && k < stairMaterials.Length; k++) stairMats[k] = stairMaterials[k];

            progress?.Invoke("Painting textures", 0.05f);
            t.WriteTextures(create: true);
            ground.SetTexture("_Mask", t.mask);
            for (int k = 0; k < 3; k++) ground.SetTexture("_Splat" + k, t.splat[k]);
            ground.SetVector("_World", new Vector4(map.x0, map.z0, map.w, map.h));
            ground.SetVector("_Grid", new Vector4(map.x0, map.z0, map.w, map.h));
            EditorUtility.SetDirty(ground);

            for (int k = 0; k < t.chunks.Length; k++)
            {
                var ch = t.chunks[k];
                progress?.Invoke($"Building chunk {k + 1} of {t.chunks.Length}", 0.1f + 0.7f * k / t.chunks.Length);
                string path = t.MeshPath(ch.cx, ch.cr);
                AssetDatabase.DeleteAsset(path);
                ch.ground = new Mesh { name = $"{map.name}_{ch.cx:00}_{ch.cr:00}_Ground" };
                ch.walls = new Mesh { name = $"{map.name}_{ch.cx:00}_{ch.cr:00}_Walls" };
                ch.stairs = new Mesh { name = $"{map.name}_{ch.cx:00}_{ch.cr:00}_Stairs" };
                t.BuildChunk(ch, weld: true);
                t.BuildStairs(ch, weld: true);
                AssetDatabase.CreateAsset(ch.ground, path);
                AssetDatabase.AddObjectToAsset(ch.walls, path);
                AssetDatabase.AddObjectToAsset(ch.stairs, path);
            }
            AssetDatabase.SaveAssets();

            progress?.Invoke("Making the prefab", 0.85f);
            var root = new GameObject(map.name);
            try
            {
                foreach (var ch in t.chunks)
                {
                    var go = new GameObject($"Chunk_{ch.cx:00}_{ch.cr:00}");
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = new Vector3((float)ch.px, 0f, (float)-ch.pz);
                    Part(go.transform, "Ground", ch.ground, new[] { ground });
                    Part(go.transform, "Walls", ch.walls, new[] { walls });
                    Part(go.transform, "Stairs", ch.stairs, stairMats);
                }
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                t.prefab = PrefabUtility.SaveAsPrefabAsset(root, t.PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
            t.savedRevision = map.revision;
            t.FindInstance();
            progress?.Invoke("Done", 1f);
            return t;
        }

        static void Part(Transform parent, string name, Mesh mesh, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh.vertexCount > 0 ? mesh : null;
        }

        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        void BuildChunk(Chunk ch, bool weld)
        {
            scratch.Clear();
            StrataMesher.Ground(map, ch.c0, ch.r0, ch.c1, ch.r1, ch.px, ch.pz, scratch);
            scratch.ToMesh(ch.ground, weld);
            scratch.Clear();
            StrataMesher.Walls(map, ch.c0, ch.r0, ch.c1, ch.r1, ch.px, ch.pz, scratch, wallScratch, nbScratch);
            scratch.ToMesh(ch.walls, weld);
            ch.meshDirty = false;
            ch.unbaked = !weld;
        }

        void BuildStairs(Chunk ch, bool weld)
        {
            scratch.Clear();
            for (int s = 0; s < map.stairs.Count; s++)
                if (ChunkOfStair(s) == ch) StrataStairs.Build(map, s, stairStyles, ch.px, ch.pz, scratch);
            scratch.ToMesh(ch.stairs, weld, StairStyle.Slots(stairStyles), tangents: true);
            ch.stairsDirty = false;
            ch.unbaked |= !weld;
        }

        public void CellsChanged(int c0, int r0, int c1, int r1)
        {
            if (c1 <= c0 || r1 <= r0) return;
            map.revision++;
            int a0 = Math.Max(0, c0 - 2), b0 = Math.Max(0, r0 - 2), a1 = Math.Min(map.w, c1 + 2), b1 = Math.Min(map.h, r1 + 2);
            for (int cr = b0 / map.bakeChunk; cr <= Math.Min(chunksY - 1, (b1 - 1) / map.bakeChunk); cr++)
                for (int cx = a0 / map.bakeChunk; cx <= Math.Min(chunksX - 1, (a1 - 1) / map.bakeChunk); cx++)
                    chunks[cr * chunksX + cx].meshDirty = true;
            maskDirty = Grow(maskDirty, new RectInt(Math.Max(0, c0 - 1), Math.Max(0, r0 - 1), Math.Min(map.w, c1 + 1) - Math.Max(0, c0 - 1), Math.Min(map.h, r1 + 1) - Math.Max(0, r0 - 1)));
            for (int s = 0; s < map.stairs.Count; s++)
            {
                bool near = false;
                for (int k = 0; k < map.flights.Count && !near; k++)
                {
                    var f = map.flights[k];
                    if (f.stair != s) continue;
                    int fc0 = f.x0 - map.x0 - 2, fc1 = f.x1 - map.x0 + 2, fr0 = f.z0 - map.z0 - 2, fr1 = f.z1 - map.z0 + 2;
                    near = fc0 < c1 && c0 < fc1 && fr0 < r1 && r0 < fr1;
                }
                if (!near) continue;
                if (!replaying) RefootStair(s);
                var ch = ChunkOfStair(s);
                if (ch != null) ch.stairsDirty = true;
            }
        }

        public void UseStairs(StairStyle[] styles, Material[] materials, bool rebuild)
        {
            stairStyles = styles;
            int slots = StairStyle.Slots(styles);
            var mats = new Material[slots];
            for (int k = 0; k < slots && materials != null && k < materials.Length; k++) mats[k] = materials[k];
            bool prefabChanged = false;
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name != "Stairs" || r.sharedMaterials.SequenceEqual(mats)) continue;
                r.sharedMaterials = mats;
                prefabChanged = true;
            }
            if (prefabChanged) PrefabUtility.SavePrefabAsset(prefab);
            bool structural = chunks.Any(ch => ch.stairs && ch.stairs.subMeshCount != slots);
            if (rebuild || structural) foreach (var ch in chunks) ch.stairsDirty = true;
            Flush();
        }

        public void PaintChanged(int x0, int y0, int x1, int y1)
        {
            if (x1 <= x0 || y1 <= y0) return;
            map.revision++;
            paintDirty = Grow(paintDirty, new RectInt(x0, y0, x1 - x0, y1 - y0));
        }

        public void StairsChanged()
        {
            map.revision++;
            foreach (var ch in chunks) ch.stairsDirty = true;
        }

        void OnHistory(RectInt cells, RectInt paint, bool stairs)
        {
            replaying = true;
            try
            {
                if (cells.width > 0) CellsChanged(cells.xMin, cells.yMin, cells.xMax, cells.yMax);
                if (paint.width > 0) PaintChanged(paint.xMin, paint.yMin, paint.xMax, paint.yMax);
                if (stairs) StairsChanged();
            }
            finally { replaying = false; }
            Flush();
            SceneView.RepaintAll();
        }

        void RefootStair(int s)
        {
            for (int k = 0; k < map.flights.Count; k++)
            {
                var f = map.flights[k];
                if (f.stair != s) continue;
                double low = double.MaxValue;
                for (int z = f.z0 - 1; z <= f.z1; z++)
                    for (int x = f.x0 - 1; x <= f.x1; x++)
                    {
                        int i = map.CellAt(x + 0.5, z + 0.5);
                        if (i < 0 || map.kind[i] == (byte)CellKind.Stair) continue;
                        low = Math.Min(low, map.Bottom(i));
                    }
                low = Math.Min(low == double.MaxValue ? f.y0 : low, Math.Min(f.y0, f.y1)) - 0.6;
                if (Math.Abs(low - f.low) > 1e-4) { history?.TouchStairs(); f.low = (float)low; map.flights[k] = f; }
            }
        }

        static RectInt Grow(RectInt a, RectInt b)
        {
            if (a.width <= 0 || a.height <= 0) return b;
            int x0 = Math.Min(a.xMin, b.xMin), y0 = Math.Min(a.yMin, b.yMin), x1 = Math.Max(a.xMax, b.xMax), y1 = Math.Max(a.yMax, b.yMax);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        public void Flush()
        {
            if (chunks == null) return;
            foreach (var ch in chunks)
            {
                if (ch.meshDirty) { BuildChunk(ch, weld: false); EditorUtility.SetDirty(ch.ground); EditorUtility.SetDirty(ch.walls); }
                if (ch.stairsDirty) { BuildStairs(ch, weld: false); EditorUtility.SetDirty(ch.stairs); }
            }
            if (maskDirty.width > 0 && maskDirty.height > 0)
            {
                var r = maskDirty;
                var px = new Color32[r.width * r.height];
                for (int y = 0; y < r.height; y++)
                    for (int x = 0; x < r.width; x++)
                        px[y * r.width + x] = StrataRules.Mask(map, r.x + x, r.y + y);
                mask.SetPixels32(r.x, r.y, r.width, r.height, px);
                mask.Apply(false, false);
                maskDirty = default;
                texturesUnsaved = true;
            }
            if (paintDirty.width > 0 && paintDirty.height > 0)
            {
                var r = paintDirty;
                var px = new Color32[r.width * r.height];
                for (int k = 0; k < 3; k++)
                {
                    for (int y = 0; y < r.height; y++) Array.Copy(map.splat[k], (r.y + y) * map.splatW + r.x, px, y * r.width, r.width);
                    splat[k].SetPixels32(r.x, r.y, r.width, r.height, px);
                    splat[k].Apply(false, false);
                }
                paintDirty = default;
                texturesUnsaved = true;
            }
        }

        // TODO bake the chunks near the camera first, big maps take forever
        public string Bake(bool backup = true)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Flush();
            int rebuilt = 0;
            foreach (var ch in chunks)
            {
                if (!ch.unbaked) continue;
                BuildChunk(ch, weld: true);
                BuildStairs(ch, weld: true);
                EditorUtility.SetDirty(ch.ground); EditorUtility.SetDirty(ch.walls); EditorUtility.SetDirty(ch.stairs);
                rebuilt++;
            }
            RefreshColliders();
            map.bakedRevision = map.revision;
            Save(backup);
            lastMessage = $"Baked {rebuilt} chunk{(rebuilt == 1 ? "" : "s")} in {sw.ElapsedMilliseconds} ms";
            return lastMessage;
        }

        //! set sharedMesh to null first, unity dont refresh the collider if you give it the same mesh
        void RefreshColliders()
        {
            var roots = new List<GameObject>();
            if (instance) roots.Add(instance);
            bool prefabChanged = false;
            foreach (var mc in prefab.GetComponentsInChildren<MeshCollider>(true))
            {
                var mf = mc.GetComponent<MeshFilter>();
                var want = mf && mf.sharedMesh && mf.sharedMesh.vertexCount > 0 ? mf.sharedMesh : null;
                if (mc.sharedMesh != want) { mc.sharedMesh = want; prefabChanged = true; }
            }
            if (prefabChanged) PrefabUtility.SavePrefabAsset(prefab);
            foreach (var root in roots)
                foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
                {
                    var m = mc.sharedMesh;
                    if (!m) continue;
                    mc.sharedMesh = null; mc.sharedMesh = m;
                }
        }

        // TODO save write all 3 splat textures every time, only write the ones that changed
        public void Save(bool backup = true)
        {
            if (backup) Backup(force: false);
            StrataIO.Save(map, dataPath);
            savedRevision = map.revision;
            if (texturesUnsaved) WriteTextures(create: false);
            foreach (var ch in chunks)
            {
                AssetDatabase.SaveAssetIfDirty(ch.ground);
                AssetDatabase.SaveAssetIfDirty(ch.walls);
                AssetDatabase.SaveAssetIfDirty(ch.stairs);
            }
            DeleteRecovery();
        }

        void WriteTextures(bool create)
        {
            var all = new List<(string path, Color32[] px, int w, int h, bool point)>
            {
                (MaskPath, MaskPixels(), map.w, map.h, true),
            };
            for (int k = 0; k < 3; k++) all.Add((SplatPath(k), map.splat[k], map.splatW, map.splatH, false));
            foreach (var (path, px, w, h, point) in all)
            {
                var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                tmp.SetPixels32(px);
                tmp.Apply(false);
                File.WriteAllBytes(path, tmp.EncodeToPNG());
                Object.DestroyImmediate(tmp);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                if (AssetImporter.GetAtPath(path) is TextureImporter imp && (create || !imp.isReadable))
                {
                    imp.textureType = TextureImporterType.Default;
                    imp.sRGBTexture = false;
                    imp.alphaIsTransparency = false;
                    imp.mipmapEnabled = false;
                    imp.isReadable = true;
                    imp.npotScale = TextureImporterNPOTScale.None;
                    imp.maxTextureSize = 8192;
                    imp.textureCompression = TextureImporterCompression.Uncompressed;
                    imp.filterMode = point ? FilterMode.Point : FilterMode.Bilinear;
                    imp.wrapMode = TextureWrapMode.Clamp;
                    imp.SaveAndReimport();
                }
            }
            mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
            for (int k = 0; k < 3; k++) splat[k] = AssetDatabase.LoadAssetAtPath<Texture2D>(SplatPath(k));
            texturesUnsaved = false;
        }

        Color32[] MaskPixels()
        {
            var px = new Color32[map.w * map.h];
            for (int r = 0; r < map.h; r++)
                for (int c = 0; c < map.w; c++)
                    px[r * map.w + c] = StrataRules.Mask(map, c, r);
            return px;
        }

        //! slow on purpose, only call it when the tile set change
        public void RebuildAll()
        {
            foreach (var ch in chunks) { ch.meshDirty = true; ch.stairsDirty = true; }
            maskDirty = new RectInt(0, 0, map.w, map.h);
            paintDirty = new RectInt(0, 0, map.splatW, map.splatH);
            map.revision++;
            Bake();
        }

        public string Check()
        {
            Flush();
            int differ = 0, tris = 0;
            var data = new MeshData();
            foreach (var ch in chunks)
            {
                var want = new Dictionary<long, int>(); var have = new Dictionary<long, int>();
                var at = Matrix4x4.Translate(new Vector3((float)ch.px, 0f, (float)-ch.pz));
                data.Clear();
                StrataMesher.Ground(map, ch.c0, ch.r0, ch.c1, ch.r1, ch.px, ch.pz, data);
                tris += StrataVerify.AddTris(want, data, at);
                data.Clear();
                StrataMesher.Walls(map, ch.c0, ch.r0, ch.c1, ch.r1, ch.px, ch.pz, data);
                tris += StrataVerify.AddTris(want, data, at);
                data.Clear();
                for (int s = 0; s < map.stairs.Count; s++) if (ChunkOfStair(s) == ch) StrataStairs.Build(map, s, stairStyles, ch.px, ch.pz, data);
                tris += StrataVerify.AddTris(want, data, at);
                StrataVerify.AddTris(have, ch.ground, at); StrataVerify.AddTris(have, ch.walls, at); StrataVerify.AddTris(have, ch.stairs, at);
                var (missing, extra) = StrataVerify.Diff(want, have);
                if (missing + extra > 0) differ++;
            }
            int maskBad = 0;
            var mpx = mask.GetPixels32();
            for (int r = 0; r < map.h; r++)
                for (int c = 0; c < map.w; c++)
                {
                    var a = StrataRules.Mask(map, c, r); var b = mpx[r * map.w + c];
                    if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a) maskBad++;
                }
            int paintBad = 0;
            for (int k = 0; k < 3; k++)
            {
                var px = splat[k].GetPixels32();
                for (int i = 0; i < px.Length; i++) { var a = map.splat[k][i]; var b = px[i]; if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a) paintBad++; }
            }
            return differ == 0 && maskBad == 0 && paintBad == 0
                ? $"{Name}: all {chunks.Length} chunks ({tris} triangles), the ledge mask and the ground paint match the data."
                : $"{Name}: {differ} chunk(s), {maskBad} mask cell(s) and {paintBad} paint texel(s) differ from the data. Rebuild all puts them right.";
        }

        public string Backup(bool force)
        {
            if (!force && (DateTime.Now - lastBackup).TotalMinutes < StrataSettings.BackupMinutes) return null;
            if (!File.Exists(dataPath)) return null;
            string dir = Path.Combine(BackupRoot, Name);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"{Name}-{DateTime.Now:yyyyMMdd-HHmmss}.strata");
            File.Copy(dataPath, file, true);
            lastBackup = DateTime.Now;
            var old = Directory.GetFiles(dir, "*.strata").OrderByDescending(f => f).Skip(StrataSettings.BackupKeep).ToList();
            foreach (var f in old) File.Delete(f);
            return file;
        }

        public static string[] Backups(string name)
        {
            string dir = Path.Combine(BackupRoot, name);
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.strata").OrderByDescending(f => f).ToArray() : new string[0];
        }

        public void Restore(string backupFile)
        {
            Backup(force: true);
            var m = StrataIO.Load(backupFile);
            if (m.w != map.w || m.h != map.h || m.bakeChunk != map.bakeChunk) throw new InvalidOperationException("Strata: that backup has a different grid; restore it by copying it over the .strata file and rebuilding.");
            m.name = map.name;
            map = m;
            history.Dispose();
            history = new StrataHistory(map, OnHistory);
            RebuildAll();
        }

        public void ResetHistory()
        {
            history?.Dispose();
            history = new StrataHistory(map, OnHistory);
        }

        public void ForgetOriginal() { original = null; }

        public void WriteRecovery()
        {
            if (!Unsaved) return;
            Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath));
            StrataIO.Save(map, RecoveryPath);
        }

        public bool HasRecovery => File.Exists(RecoveryPath) && File.GetLastWriteTimeUtc(RecoveryPath) > File.GetLastWriteTimeUtc(dataPath);

        public void Recover()
        {
            var m = StrataIO.Load(RecoveryPath);
            if (m.w != map.w || m.h != map.h) throw new InvalidOperationException("Strata: the recovered terrain does not match this one.");
            m.name = map.name;
            map = m;
            history.Dispose();
            history = new StrataHistory(map, OnHistory);
            RebuildAll();
        }

        public void AdoptRecovery()
        {
            var m = StrataIO.Load(RecoveryPath);
            if (m.w != map.w || m.h != map.h) return;
            m.name = map.name;
            map = m;
            history.Dispose();
            history = new StrataHistory(map, OnHistory);
            savedRevision = -1;
            foreach (var ch in chunks) ch.unbaked = true;
        }

        void DeleteRecovery() { try { if (File.Exists(RecoveryPath)) File.Delete(RecoveryPath); } catch (IOException) { } }

        public StrataMap Original
        {
            get
            {
                if (original == null && File.Exists(OriginalPath(dataPath)))
                {
                    var o = StrataIO.Load(OriginalPath(dataPath));
                    if (o.w == map.w && o.h == map.h) original = o;
                }
                return original;
            }
        }
    }
}
