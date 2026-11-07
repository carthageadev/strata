using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Strata.Formats
{
    public static class BasicProfile
    {
        public const string Id = "basic";

        public const string DefaultSetPath = "Assets/Strata Materials/Default Set.asset";
        const string BasicGuid = "c382d96aaf884864aa103e400cf99eba";

        public static StrataTileSet Shipped() => AssetDatabase.LoadAssetAtPath<StrataTileSet>(AssetDatabase.GUIDToAssetPath(BasicGuid));

        public static StrataTileSet DefaultTiles()
        {
            var d = AssetDatabase.LoadAssetAtPath<StrataTileSet>(DefaultSetPath);
            if (d) return d;
            var b = Shipped();
            if (b) return b;
            var any = AssetDatabase.FindAssets("t:StrataTileSet").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            return any != null ? AssetDatabase.LoadAssetAtPath<StrataTileSet>(any) : null;
        }

        public static StrataTileSet DefaultSet()
        {
            var d = AssetDatabase.LoadAssetAtPath<StrataTileSet>(DefaultSetPath);
            if (d) return d;
            var from = Shipped();
            d = ScriptableObject.CreateInstance<StrataTileSet>();
            if (from)
            {
                from.Upgrade();
                d.CopyItems(from);
                string home = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(from)).Replace('\\', '/') + "/";
                bool Own(UnityEngine.Object o) => o && AssetDatabase.GetAssetPath(o).StartsWith(home);
                var slope = from.slopeLayer >= 0 && from.slopeLayer < from.tops.Count ? from.tops[from.slopeLayer] : null;
                d.tops.RemoveAll(x => !Own(x)); d.sides.RemoveAll(x => !Own(x)); d.edges.RemoveAll(x => !Own(x)); d.stairs.RemoveAll(x => !Own(x));
                d.slopeLayer = Own(slope) ? d.tops.IndexOf(slope) : -1;
            }
            StrataTerrain.EnsureFolder(System.IO.Path.GetDirectoryName(DefaultSetPath).Replace('\\', '/'));
            AssetDatabase.CreateAsset(d, DefaultSetPath);
            d.Upgrade();
            d.Build();
            AssetDatabase.SaveAssets();
            return d;
        }

        public static StrataTileSet TilesOf(StrataMap m)
        {
            if (!string.IsNullOrEmpty(m.tileSet))
            {
                var t = AssetDatabase.LoadAssetAtPath<StrataTileSet>(AssetDatabase.GUIDToAssetPath(m.tileSet));
                if (t) return t;
            }
            return DefaultTiles();
        }

        static StrataTileSet Ready(StrataTileSet ts)
        {
            if (!ts) return null;
            ts.Upgrade();
            if (!ts.Built) ts.Build();
            return ts;
        }

        public static StrataPalette Palette(StrataTerrain t)
        {
            var ts = Ready(TilesOf(t.map));
            var p = new StrataPalette();
            if (!ts) return p;
            p.library = ts;
            p.stamp = ts.buildStamp * 397 ^ StrataIds.Hash(ts);
            int n = Math.Min(StrataTileSet.MaxTops, ts.tops.Count);
            p.layers = Enumerable.Range(0, n).Select(k => ts.tops[k] ? ts.tops[k].Title : null).ToArray();
            p.thumbs = StrataPalette.Thumbs(ts.groundAlbedo, n);
            p.walls = Enumerable.Range(0, Math.Min(StrataTileSet.MaxSides, ts.sides.Count)).Where(k => ts.sides[k]).Select(k => (k + 1, ts.sides[k].Title)).ToArray();
            p.rims = Enumerable.Range(0, Math.Min(StrataTileSet.MaxEdges, ts.edges.Count)).Where(k => ts.edges[k]).Select(k => (k + 1, ts.edges[k].Title)).Append((0, "None")).ToArray();
            p.stairs = Enumerable.Range(0, Math.Min(StrataTileSet.MaxStairs, ts.stairs.Count)).Where(k => ts.stairs[k]).Select(k => (k, ts.stairs[k].Title, ts.stairs[k].Timber)).ToArray();
            p.stairSides = Enumerable.Range(0, Math.Min(StrataTileSet.MaxStairs, ts.stairs.Count)).Select(k => ts.stairs[k] && ts.stairs[k].HasSides ? StrataStairType.SidesName(ts.stairs[k].sides) : null).ToArray();
            p.wallColors = new Color[16];
            for (int k = 0; k < 16; k++) p.wallColors[k] = Average(ts.wallAlbedo, Math.Min(k, ts.wallAlbedo ? ts.wallAlbedo.depth - 1 : 0));
            p.rimColors = new Color[16];
            for (int k = 1; k < 16; k++)
            {
                var e = k - 1 < ts.edges.Count ? ts.edges[k - 1] : null;
                p.rimColors[k] = !e ? Color.gray : e.EdgeTextured && ts.edgeAlbedo && k < ts.edgeAlbedo.depth ? Average(ts.edgeAlbedo, k) : e.edgeLight;
            }
            return p;
        }

        static Color Average(Texture2DArray arr, int slice)
        {
            if (!arr || !arr.isReadable || slice < 0 || slice >= arr.depth) return Color.gray;
            try
            {
                var px = arr.GetPixels32(slice, arr.mipmapCount - 1);
                float r = 0, g = 0, b = 0;
                foreach (var c in px) { r += c.r; g += c.g; b += c.b; }
                return new Color(r / px.Length / 255f, g / px.Length / 255f, b / px.Length / 255f);
            }
            catch (Exception) { return Color.gray; }
        }

        public static void Configure(StrataTerrain t, bool rebuildStairs = false)
        {
            var ts = Ready(TilesOf(t.map));
            if (!ts) return;
            var g = t.groundMaterial; var w = t.wallsMaterial;
            bool old = g && g.GetFloat("_StrataCodes") < 2;
            if (old || (g && g.GetTexture("_EdgeTable") != ts.edgeTable) || (w && w.GetTexture("_EdgeTable") != ts.edgeTable))
            {
                ts.Apply(g, w);
                if (g) AssetDatabase.SaveAssetIfDirty(g);
                if (w) AssetDatabase.SaveAssetIfDirty(w);
            }
            t.UseStairs(ts.StairStyles(), ts.StairMaterials(), rebuildStairs);
            if (old) t.RebuildAll();
        }

        public static void UpgradeOld()
        {
            foreach (var path in StrataSessions.FindTerrains())
            {
                if (StrataSessions.Active != null && StrataSessions.Active.dataPath == path) continue;
                string dir = Path.GetDirectoryName(path).Replace('\\', '/'), name = Path.GetFileNameWithoutExtension(path);
                var g = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/Materials/{name}_Ground.mat");
                if (!g || !g.shader || g.shader.name != "Strata/TileGround" || g.GetFloat("_StrataCodes") >= 2) continue;
                StrataTerrain t = null;
                try
                {
                    t = StrataTerrain.Open(path);
                    if (t.map.profile != Id) continue;
                    Configure(t);
                    Debug.Log($"Strata: brought {t.Name} up to date (meshes and mask rebuilt for the new tile sets; its data is unchanged).");
                }
                catch (Exception ex) { Debug.LogWarning($"Strata: could not bring {path} up to date: {ex.Message}"); }
                finally { t?.Dispose(); }
            }
        }

        public enum Slot { Top, Side, Edge, Stair }

        static bool Uses(StrataMap m, StrataTileSet ts, string guid, bool isDefault) =>
            m.profile == Id && (m.tileSet == guid || ((string.IsNullOrEmpty(m.tileSet) || string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(m.tileSet))) && isDefault));

        public static int RemoveFromSet(StrataTileSet ts, Slot what, int index, Action<string, float> progress = null)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(ts));
            bool isDefault = DefaultTiles() == ts;
            var users = new System.Collections.Generic.List<string>();
            foreach (var path in StrataSessions.FindTerrains())
            {
                try { if (Uses(StrataIO.Load(path), ts, guid, isDefault)) users.Add(path); }
                catch (Exception ex) { Debug.LogWarning($"Strata: could not read {path}: {ex.Message}"); }
            }
            if (what == Slot.Stair) ts.RemoveStairSlot(index);
            else ts.RemoveSlot(what == Slot.Top ? StrataTileSet.Role.Top : what == Slot.Side ? StrataTileSet.Role.Side : StrataTileSet.Role.Edge, index);
            int edgesLeft = ts.edges.Count;
            ts.Build();
            int n = 0;
            foreach (var path in users)
            {
                progress?.Invoke($"Updating {Path.GetFileNameWithoutExtension(path)}", (float)n / users.Count);
                var active = StrataSessions.Active != null && StrataSessions.Active.dataPath == path ? StrataSessions.Active : null;
                StrataTerrain t = active;
                try
                {
                    t ??= StrataTerrain.Open(path);
                    t.Backup(force: true);
                    Remap(t.map, what, index, edgesLeft);
                    t.map.revision++;
                    t.ResetHistory();
                    string orig = StrataTerrain.OriginalPath(path);
                    if (File.Exists(orig))
                    {
                        var om = StrataIO.Load(orig);
                        Remap(om, what, index, edgesLeft);
                        StrataIO.Save(om, orig);
                        t.ForgetOriginal();
                    }
                    ts.Apply(t.groundMaterial, t.wallsMaterial);
                    t.UseStairs(ts.StairStyles(), ts.StairMaterials(), rebuild: true);
                    t.RebuildAll();
                    n++;
                }
                catch (Exception ex) { Debug.LogError($"Strata: could not update {path} after taking an item out of {ts.name}: {ex.Message}"); }
                finally { if (active == null) t?.Dispose(); }
            }
            Refresh(ts);
            return n;
        }

        public static void Remap(StrataMap m, Slot what, int index, int edgesLeft)
        {
            switch (what)
            {
                case Slot.Top:
                {
                    var w = new int[StrataMap.Layers]; var o = new int[StrataMap.Layers];
                    for (int i = 0; i < m.splatW * m.splatH; i++)
                    {
                        for (int k = 0; k < StrataMap.Layers; k++) w[k] = m.Weight(i, k);
                        if (w[index] == 0 && LastNonZero(w) < index) continue;
                        for (int k = 0; k < StrataMap.Layers; k++) o[k] = 0;
                        for (int k = 0; k < StrataMap.Layers; k++) if (k != index) o[k < index ? k : k - 1] += w[k];
                        o[0] += w[index];
                        for (int k = 0; k < StrataMap.Layers; k++) m.SetWeight(i, k, (byte)Math.Min(255, o[k]));
                    }
                    break;
                }
                case Slot.Side:
                case Slot.Edge:
                {
                    var arr = what == Slot.Side ? m.wall : m.rim;
                    int gone = index + 1;
                    byte instead = (byte)(what == Slot.Edge && edgesLeft == 0 ? 0 : 1);
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (arr[i] == gone) arr[i] = instead;
                        else if (arr[i] > gone) arr[i]--;
                    }
                    break;
                }
                case Slot.Stair:
                    foreach (var s in m.stairs)
                    {
                        if (s.style == index) s.style = 0;
                        else if (s.style > index) s.style--;
                    }
                    break;
            }
        }

        static int LastNonZero(int[] w) { for (int k = w.Length - 1; k >= 0; k--) if (w[k] > 0) return k; return -1; }

        public static int Heal(StrataTileSet ts)
        {
            int n = 0;
            foreach (var (role, slot) in new[] { (StrataTileSet.Role.Top, Slot.Top), (StrataTileSet.Role.Side, Slot.Side), (StrataTileSet.Role.Edge, Slot.Edge) })
            {
                var list = ts.List(role);
                for (int k = list.Count - 1; k >= 0; k--)
                    if (!list[k] && !(list.Count(x => x) == 0 && role != StrataTileSet.Role.Edge)) { RemoveFromSet(ts, slot, k); n++; }
            }
            for (int k = ts.stairs.Count - 1; k >= 0; k--) if (!ts.stairs[k] && ts.stairs.Count > 1) { RemoveFromSet(ts, Slot.Stair, k); n++; }
            if (n > 0) Debug.Log($"Strata: {n} missing item(s) taken out of {ts.name}; what was painted with them shows each list's first.");
            return n;
        }

        public static void Refresh(StrataTileSet ts)
        {
            foreach (var path in StrataSessions.FindTerrains())
            {
                string dir = Path.GetDirectoryName(path).Replace('\\', '/'), name = Path.GetFileNameWithoutExtension(path);
                var g = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/Materials/{name}_Ground.mat");
                var w = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/Materials/{name}_Walls.mat");
                bool mine = g && g.shader && g.shader.name == "Strata/TileGround" && g.GetTexture("_GroundAlbedo") == ts.groundAlbedo;
                if (!mine) continue;
                ts.Apply(g, w && w.shader && w.shader.name == "Strata/TileWalls" ? w : null);
                AssetDatabase.SaveAssetIfDirty(g);
                if (w) AssetDatabase.SaveAssetIfDirty(w);
            }
            var a = StrataSessions.Active;
            if (a != null && a.map.profile == Id && TilesOf(a.map) == ts) Configure(a, rebuildStairs: true);
            StrataSessions.Raise();
            SceneView.RepaintAll();
        }

        public static string CreateTerrain(string name, string folder, int w, int h, StrataTileSet tiles, Action<string, float> progress = null)
        {
            if (!tiles) throw new InvalidOperationException("Pick a tile set for the new terrain.");
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidOperationException("Give the terrain a name that can be a file name.");
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (!folder.StartsWith("Assets/") && folder != "Assets") throw new InvalidOperationException("The terrain's folder must be inside Assets.");
            if (File.Exists($"{folder}/{name}.strata")) throw new InvalidOperationException($"There is already a terrain called {name} in {folder}.");
            Ready(tiles);
            int first = Math.Max(0, tiles.tops.FindIndex(x => x));

            var map = StrataMap.Create(name, w, h, -(w / 2), -(h / 2), 0f, (byte)first);
            map.profile = Id;
            map.tileSet = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tiles));
            map.bakeChunk = 64;
            for (int i = 0; i < map.wall.Length; i++) { map.wall[i] = 1; map.rim[i] = 1; }

            StrataTerrain.EnsureFolder(folder + "/Materials");
            var ground = new Material(Shader.Find("Strata/TileGround")) { name = name + "_Ground", enableInstancing = true };
            var walls = new Material(Shader.Find("Strata/TileWalls")) { name = name + "_Walls", enableInstancing = true };
            AssetDatabase.CreateAsset(ground, $"{folder}/Materials/{name}_Ground.mat");
            AssetDatabase.CreateAsset(walls, $"{folder}/Materials/{name}_Walls.mat");
            tiles.Apply(ground, walls);

            var t = StrataTerrain.Create(map, folder, ground, walls, tiles.StairMaterials(), progress, tiles.StairStyles());
            var scene = EditorSceneManager.GetActiveScene();
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(t.prefab, scene);
            Undo.RegisterCreatedObjectUndo(inst, "Strata: new terrain");
            Selection.activeGameObject = inst;
            EditorSceneManager.MarkSceneDirty(scene);
            string path = t.dataPath;
            t.Dispose();
            return path;
        }
    }
}
