using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace Strata
{
    [CreateAssetMenu(fileName = "Tiles", menuName = "Strata/Tile Set", order = 400)]
    public sealed class StrataTileSet : ScriptableObject
    {
        public enum Family { Soil = 0, Turf = 1, Paving = 2, Rock = 3 }
        public enum Role { Top, Side, Edge }
        // TODO lift the 12 tops / 15 sides / 15 edges some day, save which grounds r on each spot instead of 1 channel per ground
        public const int MaxTops = 12, MaxSides = 15, MaxEdges = 15, MaxStairs = 256;

        public List<StrataMaterial> tops = new List<StrataMaterial>();
        public List<StrataMaterial> sides = new List<StrataMaterial>();
        public List<StrataMaterial> edges = new List<StrataMaterial>();
        public List<StrataStairType> stairs = new List<StrataStairType>();
        [Tooltip("The top shown on steep slopes (-1: none).")]
        public int slopeLayer = -1;
        [Tooltip("Large, slow colour drift across the ground (needs a noise texture).")]
        [Range(0, 1)] public float macro = 0.5f;
        public Texture2D noise;
        [Tooltip("The pixel grid blends between materials follow, as the resolution of one 16 m tile (256: every texel; 32: pixels of 8 x 8 texels). 0: each material's own (found from its art, or set on it).")]
        public int pixelGrid = 0;

        [Serializable]
        public sealed class GroundLayer
        {
            public string name = "Ground";
            public Texture2D albedo, normal, orm;
            public float priority = 5;
            public Family family = Family.Turf;
            public float wobble = 0.4f, ragged = 0.12f;
        }

        [Serializable]
        public sealed class WallLayer
        {
            public string name = "Wall";
            public Texture2D albedo, normal, orm;
        }

        [SerializeField, HideInInspector] int format;
        [HideInInspector] public List<GroundLayer> ground = new List<GroundLayer>();
        [HideInInspector] public List<WallLayer> walls = new List<WallLayer>();
        [HideInInspector] public Color turf = new Color(0.34f, 0.47f, 0.17f), turfLip = new Color(0.46f, 0.6f, 0.26f);
        [HideInInspector] public Color copingDark = new Color(0.36f, 0.32f, 0.24f), copingLight = new Color(0.54f, 0.49f, 0.37f);
        [HideInInspector] public Color stonesDark = new Color(0.4f, 0.3f, 0.16f), stonesLight = new Color(0.6f, 0.48f, 0.28f);
        [HideInInspector] public Material stairStone, stairTread, stairCheek, stairPlank, stairPost;

        [HideInInspector] public Texture2DArray groundAlbedo, groundNormal, groundOrm, wallAlbedo, wallNormal, wallOrm, edgeAlbedo;
        [HideInInspector] public Texture2D edgeTable;
        [HideInInspector] public int buildStamp;

        public bool Current => format >= 2;
        public bool Built => Current && groundAlbedo && groundNormal && groundOrm && wallAlbedo && wallNormal && wallOrm && edgeAlbedo && edgeTable;

        public List<StrataMaterial> List(Role r) => r == Role.Top ? tops : r == Role.Side ? sides : edges;
        public void CopyItems(StrataTileSet from)
        {
            tops = new List<StrataMaterial>(from.tops); sides = new List<StrataMaterial>(from.sides);
            edges = new List<StrataMaterial>(from.edges); stairs = new List<StrataStairType>(from.stairs);
            slopeLayer = from.slopeLayer; macro = from.macro; noise = from.noise; pixelGrid = from.pixelGrid;
        }

        public static int Max(Role r) => r == Role.Top ? MaxTops : r == Role.Side ? MaxSides : MaxEdges;
        public string Folder => Path.GetDirectoryName(AssetDatabase.GetAssetPath(this)).Replace('\\', '/');

        public StrataStairType StairType(int style)
        {
            if (style >= 0 && style < stairs.Count && stairs[style]) return stairs[style];
            return stairs.FirstOrDefault(s => s);
        }

        public void Upgrade()
        {
            if (Current) return;
            if (ground.Count == 0 && walls.Count == 0)
            {
                format = 2;
                EditorUtility.SetDirty(this);
                return;
            }
            string mdir = Folder + "/Strata Materials", sdir = Folder + "/Stair Types";
            StrataTerrain.EnsureFolder(mdir);
            StrataTerrain.EnsureFolder(sdir);
            tops.Clear(); sides.Clear(); edges.Clear(); stairs.Clear();
            foreach (var l in ground)
                tops.Add(Make(mdir, l.name, m => { m.albedo = l.albedo; m.normal = l.normal; m.packed = l.orm; m.priority = l.priority; m.family = l.family; m.wobble = l.wobble; m.ragged = l.ragged; }));
            foreach (var w in walls)
                sides.Add(Make(mdir, w.name, m => { m.albedo = w.albedo; m.normal = w.normal; m.packed = w.orm; }));
            edges.Add(MakeEdge(mdir, "Turf", StrataMaterial.EdgePattern.Overhang, turf, turfLip));
            edges.Add(MakeEdge(mdir, "Coping", StrataMaterial.EdgePattern.Slabs, copingDark, copingLight));
            edges.Add(MakeEdge(mdir, "Field Stones", StrataMaterial.EdgePattern.Stones, stonesDark, stonesLight));
            var parts = new Dictionary<(Texture, Texture, float), StrataMaterial>();
            StrataMaterial Part(Material src)
            {
                if (!src) return null;
                var a = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.mainTexture;
                var n = src.HasProperty("_NormalMap") ? src.GetTexture("_NormalMap") : null;
                float rough = src.HasProperty("_Roughness") ? src.GetFloat("_Roughness") : 0.9f;
                if (parts.TryGetValue((a, n, rough), out var have)) return have;
                return parts[(a, n, rough)] = Make(mdir, src.name, m => { m.albedo = a as Texture2D; m.normal = n as Texture2D; m.smoothness = 1f - rough; if (src.HasProperty("_BaseColor")) m.tint = src.GetColor("_BaseColor"); });
            }
            if (stairStone || stairTread || stairCheek)
                stairs.Add(MakeStair(sdir, "Stone", StrataStairType.Build.Stone, t => { t.body = Part(stairStone); t.tread = Part(stairTread); t.cheek = Part(stairCheek); t.built = new[] { stairStone, stairTread, stairCheek }; }));
            if (stairPlank || stairPost)
                stairs.Add(MakeStair(sdir, "Timber", StrataStairType.Build.Timber, t => { t.planks = Part(stairPlank); t.posts = Part(stairPost); t.built = new[] { stairPlank, stairPost }; }));
            format = 2;
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        static StrataMaterial Make(string folder, string name, Action<StrataMaterial> fill)
        {
            var m = CreateInstance<StrataMaterial>();
            fill(m);
            AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{Safe(name)}.asset"));
            return m;
        }

        static StrataMaterial MakeEdge(string folder, string name, StrataMaterial.EdgePattern pattern, Color dark, Color light) =>
            Make(folder, name, m => { m.edgePattern = pattern; m.edgeDark = dark; m.edgeLight = light; m.edgeTexture = false; });

        static StrataStairType MakeStair(string folder, string name, StrataStairType.Build build, Action<StrataStairType> fill)
        {
            var t = CreateInstance<StrataStairType>();
            t.build = build;
            fill(t);
            AssetDatabase.CreateAsset(t, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{Safe(name)}.asset"));
            return t;
        }

        public static string Safe(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Material" : name.Trim();
        }

        const int Size = 256;
        static readonly Color32 FlatNormal = new Color32(128, 128, 255, 255), PlainOrm = new Color32(255, 230, 0, 255), Grey = new Color32(128, 128, 128, 255);
        enum Channel { Albedo, Normal, Orm }

        public string Build()
        {
            Upgrade();
            var sb = new StringBuilder();
            if (!tops.Any(t => t)) throw new InvalidOperationException("A tile set needs at least one top.");
            if (!sides.Any(s => s)) throw new InvalidOperationException("A tile set needs at least one side.");
            var firstSide = sides.First(s => s);
            var g = tops.Take(MaxTops).ToList();
            var w = new List<StrataMaterial> { sides[0] ? sides[0] : firstSide };
            int nw = Math.Max(3, Math.Min(MaxSides, sides.Count));
            for (int k = 0; k < nw; k++) w.Add(k < sides.Count ? (sides[k] ? sides[k] : firstSide) : sides[sides.Count - 1] ? sides[sides.Count - 1] : firstSide);
            var e = new List<StrataMaterial> { null };
            int lastTextured = -1;
            for (int k = 0; k < Math.Min(MaxEdges, edges.Count); k++) if (edges[k] && edges[k].EdgeTextured) lastTextured = k;
            for (int k = 0; k <= lastTextured; k++) e.Add(edges[k] && edges[k].EdgeTextured ? edges[k] : null);
            var cache = new Dictionary<(Texture, int), Color32[]>();
            groundAlbedo = Pack(g, Channel.Albedo, true, Grey, "Ground Albedo", groundAlbedo, sb, cache);
            groundNormal = Pack(g, Channel.Normal, false, FlatNormal, "Ground Normal", groundNormal, sb, cache);
            groundOrm = Pack(g, Channel.Orm, false, PlainOrm, "Ground ORM", groundOrm, sb, cache);
            wallAlbedo = Pack(w, Channel.Albedo, true, Grey, "Wall Albedo", wallAlbedo, sb, cache);
            wallNormal = Pack(w, Channel.Normal, false, FlatNormal, "Wall Normal", wallNormal, sb, cache);
            wallOrm = Pack(w, Channel.Orm, false, PlainOrm, "Wall ORM", wallOrm, sb, cache);
            edgeAlbedo = Pack(e, Channel.Albedo, true, Grey, "Edge Albedo", edgeAlbedo, sb, cache);
            edgeTable = Table(edgeTable, w, g);
            foreach (var st in stairs) if (st) BuildStair(st);
            buildStamp++;
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            sb.Insert(0, $"{name}: {g.Count(x => x)} tops, {sides.Count(x => x)} sides, {edges.Count(x => x)} edges, {stairs.Count(x => x)} stair types.\n");
            return sb.ToString().TrimEnd();
        }

        Texture2DArray Pack(List<StrataMaterial> slices, Channel ch, bool srgb, Color32 fallback, string arrayName, Texture2DArray existing, StringBuilder report, Dictionary<(Texture, int), Color32[]> cache)
        {
            var arr = new Texture2DArray(Size, Size, Math.Max(1, slices.Count), TextureFormat.RGBA32, true, !srgb)
            {
                name = arrayName, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4,
            };
            for (int k = 0; k < Math.Max(1, slices.Count); k++)
            {
                var px = (k < slices.Count ? Slice(slices[k], ch, report, cache) : null) ?? Fill(fallback);
                arr.SetPixels32(px, k, 0);
            }
            arr.Apply(true, false);
            string path = AssetDatabase.GetAssetPath(this);
            if (existing && AssetDatabase.GetAssetPath(existing) == path)
            {
                EditorUtility.CopySerialized(arr, existing);
                DestroyImmediate(arr);
                return existing;
            }
            AssetDatabase.AddObjectToAsset(arr, this);
            return arr;
        }

        static Color32[] Slice(StrataMaterial m, Channel ch, StringBuilder report, Dictionary<(Texture, int), Color32[]> cache)
        {
            if (!m) return null;
            switch (ch)
            {
                case Channel.Albedo:
                {
                    var px = Read(m.albedo, Kind.Colour, report, cache);
                    if (px == null || m.tint == Color.white) return px;
                    px = (Color32[])px.Clone();
                    Color32 t = m.tint;
                    for (int i = 0; i < px.Length; i++) px[i] = new Color32((byte)(px[i].r * t.r / 255), (byte)(px[i].g * t.g / 255), (byte)(px[i].b * t.b / 255), px[i].a);
                    return px;
                }
                case Channel.Normal:
                    return Read(m.normal, UnityNormal(m.normal) ? Kind.NormalMap : Kind.Data, report, cache);
                default:
                {
                    if (m.packed) return Read(m.packed, Kind.Data, report, cache);
                    var occ = Read(m.occlusion, Kind.Data, report, cache);
                    var met = Read(m.metallic, Kind.Data, report, cache);
                    var o = new Color32[Size * Size];
                    byte metal = (byte)Math.Round(255 * Mathf.Clamp01(m.metalness));
                    for (int i = 0; i < o.Length; i++)
                    {
                        float smooth = met != null ? met[i].a / 255f * m.smoothness : m.smoothness;
                        o[i] = new Color32(occ != null ? occ[i].g : (byte)255, (byte)Math.Round(255 * (1 - Mathf.Clamp01(smooth))), met != null ? met[i].r : metal, 255);
                    }
                    return o;
                }
            }
        }

        static Color32[] Fill(Color32 c) { var a = new Color32[Size * Size]; for (int i = 0; i < a.Length; i++) a[i] = c; return a; }

        public static bool UnityNormal(Texture tex) => tex && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) is TextureImporter imp && imp.textureType == TextureImporterType.NormalMap;

        enum Kind { Colour, Data, NormalMap }

        static Color32[] Read(Texture2D tex, Kind kind, StringBuilder report, Dictionary<(Texture, int), Color32[]> cache)
        {
            if (!tex) return null;
            if (cache.TryGetValue((tex, (int)kind), out var hit)) return hit;
            int w = tex.width, h = tex.height;
            bool repeat = w <= Size && h <= Size && Size % w == 0 && Size % h == 0;
            if (!repeat && (w < Size || h < Size)) report.AppendLine($"{tex.name}: {w} x {h} stretched to {Size} x {Size} (tiles of 256, 128, 64… keep sixteen texels a metre).");
            Color32[] o = null;
            if (tex.isReadable && kind != Kind.NormalMap)
            {
                try
                {
                    var src = tex.GetPixels32(0);
                    o = new Color32[Size * Size];
                    for (int y = 0; y < Size; y++)
                        for (int x = 0; x < Size; x++)
                        {
                            int sx = repeat ? x % w : x * w / Size, sy = repeat ? y % h : y * h / Size;
                            o[y * Size + x] = src[sy * w + sx];
                        }
                }
                catch (Exception) { o = null; }
            }
            if (o == null)
            {
                try { o = Gpu(tex, kind, repeat); }
                catch (Exception ex) { report.AppendLine($"{tex.name}: could not be read ({ex.Message}); left plain."); }
            }
            cache[(tex, (int)kind)] = o;
            return o;
        }

        static Material copy;

        static Color32[] Gpu(Texture2D tex, Kind kind, bool repeat)
        {
            if (!copy)
            {
                var shader = Shader.Find("Hidden/Strata/Copy");
                if (!shader) throw new InvalidOperationException("Strata's copy shader is missing");
                copy = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            bool srgbSource = GraphicsFormatUtility.IsSRGBFormat(tex.graphicsFormat);
            bool srgbOut = kind == Kind.Colour && srgbSource;
            copy.SetFloat("_Mode", kind == Kind.Colour ? 0 : kind == Kind.Data ? 1 : 2);
            copy.SetFloat("_SrgbData", kind == Kind.Data && srgbSource ? 1 : 0);
            copy.SetVector("_Tile", repeat ? new Vector4(Size / (float)tex.width, Size / (float)tex.height, 0, 0) : new Vector4(1, 1, 0, 0));
            copy.SetFloat("_Lod", !repeat && tex.width > Size && tex.mipmapCount > 1 ? Mathf.Log(tex.width / (float)Size, 2) : 0);
            var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(Size, Size, srgbOut ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm, 0));
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(tex, rt, copy);
                RenderTexture.active = rt;
                var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false, !srgbOut);
                t.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                t.Apply(false);
                var px = t.GetPixels32();
                DestroyImmediate(t);
                return px;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        Texture2D Table(Texture2D existing, List<StrataMaterial> wallSlots, List<StrataMaterial> topSlots)
        {
            var t = new Texture2D(16, 6, TextureFormat.RGBAFloat, false, true) { name = "Edge Table", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[16 * 6];
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Color Lin(Color c) => linear ? c.linear : c;
            for (int k = 0; k < 16; k++)
            {
                var m = k == 0 || k - 1 >= edges.Count ? null : edges[k - 1];
                px[k] = new Color(m ? (int)m.edgePattern / 255f : 0f, m && m.EdgeTextured ? 1f : 0f, 0f, 1f);
                px[16 + k] = m ? Lin(m.edgeDark) : Color.black;
                px[32 + k] = m ? Lin(m.edgeLight) : Color.black;
                px[48 + k] = new Color(k < wallSlots.Count ? Grid(wallSlots[k], wallAlbedo, k) : 16f, 0, 0, 1);
                px[64 + k] = new Color(k < topSlots.Count ? Grid(topSlots[k], groundAlbedo, k) : 16f, 0, 0, 1);
                px[80 + k] = Color.black;
            }
            t.SetPixels(px);
            t.Apply(false, false);
            if (existing && AssetDatabase.GetAssetPath(existing) == AssetDatabase.GetAssetPath(this))
            {
                EditorUtility.CopySerialized(t, existing);
                DestroyImmediate(t);
                return existing;
            }
            AssetDatabase.AddObjectToAsset(t, this);
            return t;
        }

        float Grid(StrataMaterial m, Texture2DArray arr, int slice)
        {
            if (pixelGrid > 0) return Mathf.Clamp(pixelGrid, 16, 1024) / 16f;
            if (m && m.pixelSize > 0) return 16f / Mathf.Clamp(m.pixelSize, 1, 16);
            return 16f / PixelSize(arr, slice);
        }

        public static int PixelSize(Texture2DArray arr, int slice)
        {
            if (!arr || !arr.isReadable || slice < 0 || slice >= arr.depth) return 1;
            Color32[] px;
            try { px = arr.GetPixels32(slice, 0); } catch (Exception) { return 1; }
            int n = arr.width;
            foreach (int k in new[] { 16, 8, 4, 2 })
            {
                int blocks = 0, plain = 0;
                for (int by = 0; by < n; by += k)
                    for (int bx = 0; bx < n; bx += k)
                    {
                        blocks++;
                        var c = px[by * n + bx];
                        bool same = true;
                        for (int y = by; y < by + k && same; y++)
                            for (int x = bx; x < bx + k; x++)
                            {
                                var d = px[y * n + x];
                                if (d.r != c.r || d.g != c.g || d.b != c.b) { same = false; break; }
                            }
                        if (same) plain++;
                    }
                if (plain >= blocks * 0.98) return k;
            }
            return 1;
        }

        public static void BuildStair(StrataStairType st)
        {
            if (st.Models)
            {
                st.built = ModelMaterials(st).ToArray();
                EditorUtility.SetDirty(st);
                return;
            }
            var shader = Shader.Find("Strata/TileLit");
            int n = st.Parts;
            if (st.built == null || st.built.Length != n)
            {
                var old = st.built ?? new Material[0];
                st.built = new Material[n];
                for (int k = 0; k < n && k < old.Length; k++) st.built[k] = old[k];
            }
            string path = AssetDatabase.GetAssetPath(st);
            for (int k = 0; k < n; k++)
            {
                var mat = st.built[k];
                if (!mat)
                {
                    mat = new Material(shader) { name = $"{st.Title} {st.PartName(k)}", enableInstancing = true };
                    if (!string.IsNullOrEmpty(path)) AssetDatabase.AddObjectToAsset(mat, st);
                    st.built[k] = mat;
                }
                var p = st.Part(k);
                if (mat.shader != shader && shader) mat.shader = shader;
                mat.SetTexture("_BaseMap", p ? p.albedo : null);
                mat.SetColor("_BaseColor", p ? p.tint : Color.white);
                mat.SetTexture("_NormalMap", p ? p.normal : null);
                mat.SetFloat("_NormalMode", p && UnityNormal(p.normal) ? 1 : 0);
                mat.SetFloat("_Roughness", p ? 1f - p.smoothness : 0.9f);
                mat.SetFloat("_Metallic", p ? p.metalness : 0f);
                EditorUtility.SetDirty(mat);
            }
            EditorUtility.SetDirty(st);
        }

        public Material[] StairMaterials()
        {
            var list = new List<Material>();
            foreach (var st in stairs)
            {
                if (!st) continue;
                if (st.Models) { list.AddRange(ModelMaterials(st)); continue; }
                if (st.built == null || st.built.Length != st.Parts) BuildStair(st);
                list.AddRange(st.built);
            }
            while (list.Count < StrataStairs.Submeshes) list.Add(null);
            return list.ToArray();
        }

        public StairStyle[] StairStyles()
        {
            var o = new StairStyle[Math.Max(1, stairs.Count)];
            int slot = 0;
            var first = default(StairStyle); bool any = false;
            for (int k = 0; k < stairs.Count; k++)
            {
                var st = stairs[k];
                if (!st) continue;
                var s = new StairStyle { timber = st.Timber, body = -1, tread = -1, cheek = -1, plank = -1, post = -1, look = Look(st) };
                if (st.Models)
                {
                    var mats = new List<Material>();
                    s.step = StairModel.From(st.step, mats, slot);
                    s.first = StairModel.From(st.firstStep, mats, slot);
                    s.last = StairModel.From(st.lastStep, mats, slot);
                    s.step ??= s.first ?? s.last;
                    s.stack = st.stack; s.turn = st.turn;
                    if (s.step == null) continue;
                    slot += mats.Count;
                }
                else if (st.Timber) { s.plank = slot; s.post = slot + 1; slot += st.Parts; }
                else { s.body = slot; s.tread = slot + 1; s.cheek = slot + 2; s.steps = st.steps; s.sides = st.sides; slot += st.Parts; }
                o[k] = s;
                if (!any) { first = s; any = true; }
            }
            for (int k = 0; k < o.Length; k++) if (k >= stairs.Count || !stairs[k] || (stairs[k].Models && o[k].step == null)) o[k] = any ? first : StairStyle.Classic(StairLook.Default)[0];
            return o;
        }

        public static List<Material> ModelMaterials(StrataStairType st)
        {
            var mats = new List<Material>();
            StairModel.From(st.step, mats, 0); StairModel.From(st.firstStep, mats, 0); StairModel.From(st.lastStep, mats, 0);
            return mats;
        }

        //! Material slot removal requires remapping terrain indices.
        public void RemoveSlot(Role role, int index)
        {
            var list = List(role);
            if (index < 0 || index >= list.Count) return;
            list.RemoveAt(index);
            if (role == Role.Top) { if (slopeLayer == index) slopeLayer = -1; else if (slopeLayer > index) slopeLayer--; }
            EditorUtility.SetDirty(this);
        }

        public void RemoveStairSlot(int index)
        {
            if (index < 0 || index >= stairs.Count) return;
            stairs.RemoveAt(index);
            EditorUtility.SetDirty(this);
        }

        static StairLook Look(StrataStairType st)
        {
            var look = StairLook.Default;
            Vector2 Size(StrataMaterial m, Vector2 fallback) => m && m.albedo ? new Vector2(Math.Min(m.albedo.width, StrataTileSet.Size), Math.Min(m.albedo.height, StrataTileSet.Size)) : fallback;
            if (st.Timber) { look.deck = Size(st.planks, look.deck); look.timber = Size(st.posts, look.timber); }
            else { look.stone = Size(st.body, look.stone); look.tread = Size(st.tread, look.tread); look.cheek = Size(st.cheek, look.stone); }
            return look;
        }

        public void Apply(Material groundMat, Material wallMat)
        {
            if (!Built) Build();
            if (groundMat)
            {
                groundMat.SetTexture("_GroundAlbedo", groundAlbedo);
                groundMat.SetTexture("_GroundNormal", groundNormal);
                groundMat.SetTexture("_GroundORM", groundOrm);
                groundMat.SetTexture("_EdgeAlbedo", edgeAlbedo);
                groundMat.SetTexture("_EdgeTable", edgeTable);
                if (noise) groundMat.SetTexture("_NoiseMap", noise);
                groundMat.SetFloat("_Macro", noise ? macro : 0f);
                groundMat.SetFloat("_SlopeLayer", slopeLayer);
                groundMat.SetFloat("_StrataCodes", 2);
                var prio = new float[12]; var cls = new float[12]; var wob = new float[12]; var rag = new float[12];
                for (int k = 0; k < Math.Min(12, tops.Count); k++)
                {
                    var t = tops[k];
                    if (!t) continue;
                    prio[k] = t.priority; cls[k] = (int)t.family; wob[k] = t.wobble; rag[k] = t.ragged;
                }
                for (int k = 0; k < 3; k++)
                {
                    groundMat.SetVector("_Prio" + k, new Vector4(prio[k * 4], prio[k * 4 + 1], prio[k * 4 + 2], prio[k * 4 + 3]));
                    groundMat.SetVector("_Class" + k, new Vector4(cls[k * 4], cls[k * 4 + 1], cls[k * 4 + 2], cls[k * 4 + 3]));
                    groundMat.SetVector("_Wob" + k, new Vector4(wob[k * 4], wob[k * 4 + 1], wob[k * 4 + 2], wob[k * 4 + 3]));
                    groundMat.SetVector("_Rag" + k, new Vector4(rag[k * 4], rag[k * 4 + 1], rag[k * 4 + 2], rag[k * 4 + 3]));
                }
                EditorUtility.SetDirty(groundMat);
            }
            if (wallMat)
            {
                wallMat.SetTexture("_WallAlbedo", wallAlbedo);
                wallMat.SetTexture("_WallNormal", wallNormal);
                wallMat.SetTexture("_WallORM", wallOrm);
                wallMat.SetTexture("_EdgeAlbedo", edgeAlbedo);
                wallMat.SetTexture("_EdgeTable", edgeTable);
                wallMat.SetFloat("_StrataCodes", 2);
                EditorUtility.SetDirty(wallMat);
            }
        }
    }

    [CustomEditor(typeof(StrataTileSet))]
    sealed class StrataTileSetEditor : Editor
    {
        string report = "";
        public override void OnInspectorGUI()
        {
            var ts = (StrataTileSet)target;
            if (GUILayout.Button("Open in the Materials manager", GUILayout.Height(26))) StrataWindow.ShowMaterials(ts);
            GUILayout.Space(6);
            DrawDefaultInspector();
            GUILayout.Space(8);
            if (GUILayout.Button("Build"))
            {
                try { report = ts.Build(); Formats.BasicProfile.Refresh(ts); }
                catch (Exception ex) { report = ex.Message; }
            }
            if (!string.IsNullOrEmpty(report)) EditorGUILayout.HelpBox(report, MessageType.Info);
        }
    }
}
