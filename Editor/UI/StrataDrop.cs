using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;
using Role = Strata.StrataTileSet.Role;

namespace Strata
{
    public static class StrataDrop
    {
        public enum Map { None, Albedo, Normal, Metallic, Smoothness, Roughness, Occlusion, Packed, Height, Bump, Unused }

        public sealed class Group
        {
            public string key, name;
            public readonly Dictionary<Map, Texture2D> maps = new Dictionary<Map, Texture2D>();
            public readonly List<Texture2D> unused = new List<Texture2D>();
            public bool dx;
            public Material from;
            public StrataMaterial strata;
        }

        public sealed class Models
        {
            public string key, name;
            public GameObject step, first, last;
            public StrataStairType existing;
        }

        public sealed class Plan
        {
            public readonly List<Group> groups = new List<Group>();
            public readonly List<Models> stairs = new List<Models>();
            public readonly List<string> files = new List<string>();
            public readonly List<string> notes = new List<string>();
            public bool Empty => groups.Count == 0 && stairs.Count == 0 && files.Count == 0;

            public string Summary()
            {
                var names = groups.Select(g => g.name).Concat(stairs.Select(s => s.name)).ToList();
                foreach (var f in files)
                {
                    string n = Pretty(Parse(Path.GetFileNameWithoutExtension(f)).stem);
                    if (n.Length > 0 && !names.Contains(n)) names.Add(n);
                }
                if (names.Count == 0) return notes.FirstOrDefault() ?? "";
                return names.Count <= 3 ? And(names) : $"{names.Count} items";
            }
        }

        public sealed class Result
        {
            public readonly List<(Object item, Role role)> items = new List<(Object, Role)>();
            public readonly List<(Object item, Role role)> left = new List<(Object, Role)>();
            public readonly List<string> created = new List<string>();
            public readonly List<string> notes = new List<string>();
            public readonly List<string> configured = new List<string>();
            public readonly List<string> updated = new List<string>();
            public readonly List<string> already = new List<string>();
            public Object first => items.Count > 0 ? items[0].item : left.Count > 0 ? left[0].item : null;
            public Role role => items.Count > 0 ? items[0].role : left.Count > 0 ? left[0].role : Role.Top;
            public string status;
            public string full;
        }

        static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".exr", ".bmp", ".gif", ".hdr" };
        static readonly string[] ModelExt = { ".fbx", ".obj", ".dae", ".3ds", ".dxf", ".blend" };
        const int MaxItems = 400;

        static readonly Dictionary<string, Map> Words = new Dictionary<string, Map>
        {
            ["albedo"] = Map.Albedo, ["basecolor"] = Map.Albedo, ["basecolour"] = Map.Albedo, ["basemap"] = Map.Albedo, ["diffuse"] = Map.Albedo, ["diff"] = Map.Albedo,
            ["dif"] = Map.Albedo, ["color"] = Map.Albedo, ["colour"] = Map.Albedo, ["col"] = Map.Albedo, ["albedotransparency"] = Map.Albedo, ["maintex"] = Map.Albedo, ["d"] = Map.Albedo,
            ["normal"] = Map.Normal, ["normals"] = Map.Normal, ["normalmap"] = Map.Normal, ["normalgl"] = Map.Normal, ["normaldx"] = Map.Normal, ["nrm"] = Map.Normal,
            ["nor"] = Map.Normal, ["norm"] = Map.Normal, ["nml"] = Map.Normal, ["n"] = Map.Normal,
            ["metallic"] = Map.Metallic, ["metalness"] = Map.Metallic, ["metal"] = Map.Metallic, ["met"] = Map.Metallic, ["metallicsmoothness"] = Map.Metallic, ["ms"] = Map.Metallic,
            ["smoothness"] = Map.Smoothness, ["smooth"] = Map.Smoothness, ["gloss"] = Map.Smoothness, ["glossiness"] = Map.Smoothness,
            ["roughness"] = Map.Roughness, ["rough"] = Map.Roughness, ["rgh"] = Map.Roughness,
            ["ao"] = Map.Occlusion, ["occlusion"] = Map.Occlusion, ["ambientocclusion"] = Map.Occlusion, ["occ"] = Map.Occlusion,
            ["orm"] = Map.Packed, ["arm"] = Map.Packed,
            ["height"] = Map.Height, ["heightmap"] = Map.Height, ["disp"] = Map.Height, ["displacement"] = Map.Height, ["parallax"] = Map.Height,
            ["bump"] = Map.Bump, ["bumpmap"] = Map.Bump,
            ["emission"] = Map.Unused, ["emissive"] = Map.Unused, ["opacity"] = Map.Unused, ["alpha"] = Map.Unused, ["mask"] = Map.Unused, ["maskmap"] = Map.Unused,
            ["specular"] = Map.Unused, ["spec"] = Map.Unused, ["translucency"] = Map.Unused, ["cavity"] = Map.Unused, ["curvature"] = Map.Unused,
        };
        static readonly Regex Skip = new Regex(@"^(\d+k|\d+px|128|256|512|1024|2048|4096|8192|gl|dx|directx|opengl|ogl|png|jpe?g|tga|tiff?|exr|psd|bmp|map)$");
        static readonly string[] Prefixes = { "t", "tx", "tex", "texture", "m", "mat", "mtl" };

        static readonly string[] SideWords = { "wall", "walls", "brick", "bricks", "cliff", "cliffs", "rockface", "facade", "masonry", "ashlar", "plaster", "stucco", "bank", "crag", "rampart", "palisade", "siding", "cladding" };
        static readonly string[] EdgeWords = { "edge", "edges", "trim", "coping", "rim", "lip", "curb", "kerb", "border", "fringe", "overhang" };
        static readonly string[] StairWords = { "stair", "stairs", "staircase", "step", "steps", "tread", "treads", "riser" };
        static readonly string[] FirstWords = { "first", "bottom", "start", "begin", "lower" };
        static readonly string[] LastWords = { "last", "top", "end", "upper", "cap" };

        public static (string stem, Map map, bool dx) Parse(string name)
        {
            string spaced = Regex.Replace(name ?? "", "([a-z])([A-Z])", "$1 $2");
            var t = Regex.Split(spaced.ToLowerInvariant(), @"[_\-\s\.]+").Where(s => s.Length > 0).ToList();
            bool dx = t.Any(s => s == "dx" || s == "directx" || s == "normaldx");
            var number = new List<string>();
            while (t.Count > 1 && t[t.Count - 1].Length <= 2 && t[t.Count - 1].All(char.IsDigit)) { number.Insert(0, t[t.Count - 1]); t.RemoveAt(t.Count - 1); }
            var map = Map.None;
            int at = -1;
            for (int i = t.Count - 1; i >= 0; i--)
            {
                if (Skip.IsMatch(t[i])) continue;
                if (Words.TryGetValue(t[i], out var w) && (t[i].Length > 1 || i > 0)) { map = w; at = i; }
                break;
            }
            if (at < 0) { t.AddRange(number); number.Clear(); }
            var stem = at >= 0 ? t.Take(at).ToList() : t.ToList();
            if (at > 0 && stem.Count > 0)
            {
                string prev = stem[stem.Count - 1];
                if (map == Map.Albedo && prev == "base") stem.RemoveAt(stem.Count - 1);
                else if (map == Map.Occlusion && prev == "ambient") stem.RemoveAt(stem.Count - 1);
                else if (map == Map.Smoothness && (prev == "metallic" || prev == "metal")) { stem.RemoveAt(stem.Count - 1); map = Map.Metallic; }
            }
            while (stem.Count > 0 && Skip.IsMatch(stem[stem.Count - 1])) stem.RemoveAt(stem.Count - 1);
            while (stem.Count > 1 && Prefixes.Contains(stem[0])) stem.RemoveAt(0);
            stem.AddRange(number);
            return (string.Join(" ", stem), map, dx);
        }

        public static string Pretty(string stem) =>
            string.Join(" ", (stem ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));

        static string And(List<string> names) => names.Count == 0 ? "" : names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];

        static bool Has(string name, string[] words)
        {
            foreach (var token in (name ?? "").ToLowerInvariant().Split(' '))
                foreach (var w in words)
                    if (token == w || (w.Length >= 4 && token.Contains(w))) return true;
            return false;
        }

        public static Role RoleFor(string name) => Has(name, EdgeWords) ? Role.Edge : Has(name, SideWords) ? Role.Side : Role.Top;
        public static bool IsStair(string name) => Has(name, StairWords);

        static (StrataTileSet.Family family, float priority) Ground(string name)
        {
            if (Has(name, new[] { "cobble", "cobbles", "cobblestone", "flagstone", "flagstones", "flag", "flags", "paving", "pavement", "tile", "tiles", "floor", "marble", "plank", "planks", "boards", "deck", "road", "street", "plaza", "brick", "bricks", "mosaic", "terrazzo" }))
                return (StrataTileSet.Family.Paving, 8);
            if (Has(name, new[] { "sand", "beach" })) return (StrataTileSet.Family.Soil, 2);
            if (Has(name, new[] { "gravel", "pebble", "pebbles" })) return (StrataTileSet.Family.Soil, 5);
            if (Has(name, new[] { "dirt", "soil", "mud", "earth", "farm", "field", "path", "ground", "clay", "snow", "ash", "wheat" })) return (StrataTileSet.Family.Soil, 3);
            if (Has(name, new[] { "rock", "rocks", "stone", "stones", "boulder", "cliff", "crag", "granite", "slate", "basalt" })) return (StrataTileSet.Family.Rock, 5);
            if (Has(name, new[] { "grass", "lawn", "moss", "turf", "meadow", "clover", "leaves", "foliage", "forest" })) return (StrataTileSet.Family.Turf, 5);
            return (StrataTileSet.Family.Turf, 5);
        }

        static StrataMaterial.EdgePattern EdgeKind(string name) =>
            Has(name, new[] { "coping", "slab", "slabs", "trim", "curb", "kerb" }) ? StrataMaterial.EdgePattern.Slabs
            : Has(name, new[] { "stones", "stone", "pebble", "pebbles", "cobble", "cobbles", "field" }) ? StrataMaterial.EdgePattern.Stones
            : StrataMaterial.EdgePattern.Overhang;

        static StrataMaterial.Use UseOf(Role r) => r == Role.Top ? StrataMaterial.Use.Top : r == Role.Side ? StrataMaterial.Use.Side : StrataMaterial.Use.Edge;
        static Role RoleOf(StrataMaterial.Use u) => (u & StrataMaterial.Use.Top) != 0 ? Role.Top : (u & StrataMaterial.Use.Side) != 0 ? Role.Side : (u & StrataMaterial.Use.Edge) != 0 ? Role.Edge : Role.Top;

        static string ProjectPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            p = p.Replace('\\', '/');
            if (p.StartsWith("Assets/") || p.StartsWith("Packages/")) return p;
            string data = Application.dataPath.Replace('\\', '/');
            if (p.StartsWith(data + "/", StringComparison.OrdinalIgnoreCase)) return "Assets" + p.Substring(data.Length);
            return null;
        }

        static List<Object> Resolve(Object[] objs, string[] paths, List<string> files, List<string> notes)
        {
            var list = new List<Object>();
            var seen = new HashSet<int>();
            void Add(Object o)
            {
                if (!o || list.Count >= MaxItems || !seen.Add(o.GetInstanceID())) return;
                string path = AssetDatabase.GetAssetPath(o);
                if (o is DefaultAsset && AssetDatabase.IsValidFolder(path))
                {
                    foreach (var p in AssetDatabase.FindAssets("", new[] { path }).Select(AssetDatabase.GUIDToAssetPath).Distinct())
                    {
                        if (AssetDatabase.IsValidFolder(p)) continue;
                        var a = AssetDatabase.LoadMainAssetAtPath(p);
                        if (a is Texture2D || a is Material || a is GameObject || a is StrataMaterial || a is StrataStairType) Add(a);
                    }
                    return;
                }
                if (o is Sprite s) { Add(s.texture); return; }
                if (o is GameObject scene && !EditorUtility.IsPersistent(scene)) { notes.Add($"{o.name}: drag its prefab or model from the Project"); return; }
                if (o is Mesh || o is GameObject)
                {
                    var main = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
                    if (!main) { notes.Add($"{o.name}: drag the model or prefab it is in"); return; }
                    if (main != o) { Add(main); return; }
                }
                if (o is Texture2D || o is Material || o is GameObject || o is StrataMaterial || o is StrataStairType) list.Add(o);
            }
            void External(string f)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if ((ImageExt.Contains(ext) || ModelExt.Contains(ext)) && !files.Contains(f) && files.Count < MaxItems) files.Add(f);
            }
            foreach (var o in objs ?? new Object[0]) Add(o);
            foreach (var p in paths ?? new string[0])
            {
                string rel = ProjectPath(p);
                if (rel != null) { Add(AssetDatabase.LoadMainAssetAtPath(rel)); continue; }
                try
                {
                    if (Directory.Exists(p)) foreach (var f in Directory.GetFiles(p, "*", SearchOption.AllDirectories)) External(f);
                    else if (File.Exists(p)) External(p);
                }
                catch (Exception ex) { notes.Add($"{Path.GetFileName(p)}: {ex.Message}"); }
            }
            if (list.Count + files.Count >= MaxItems) notes.Add($"Only the first {MaxItems} items were taken.");
            return list;
        }

        static Texture2D Tex(Material m, params string[] props)
        {
            foreach (var p in props) if (m.HasProperty(p) && m.GetTexture(p) is Texture2D t) return t;
            return null;
        }

        static TextureImporter Importer(Texture t) => t ? AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter : null;

        static Map Settle(Texture2D t, Map map, bool probe)
        {
            var imp = Importer(t);
            if (map == Map.None)
            {
                if (imp && imp.textureType == TextureImporterType.NormalMap) return Map.Normal;
                return probe && Look(t).normal ? Map.Normal : Map.Albedo;
            }
            if (map == Map.Bump)
            {
                if (imp && imp.textureType == TextureImporterType.NormalMap) return Map.Normal;
                return !probe || Look(t).normal ? Map.Normal : Map.Height;
            }
            return map;
        }

        static Plan Read(List<Object> objects, bool probe, List<string> notes)
        {
            var plan = new Plan();
            plan.notes.AddRange(notes);
            var taken = new HashSet<Texture2D>();
            foreach (var m in objects.OfType<Material>())
            {
                string pretty = Pretty(Parse(m.name).stem);
                var g = new Group { key = "mat " + m.GetInstanceID(), name = pretty.Length > 0 ? pretty : m.name, from = m };
                void Put(Map k, Texture2D t) { if (t) g.maps[k] = t; }
                Put(Map.Albedo, Tex(m, "_BaseMap", "_MainTex", "_BaseColorMap", "_BaseColorTexture"));
                Put(Map.Normal, Tex(m, "_BumpMap", "_NormalMap"));
                Put(Map.Metallic, Tex(m, "_MetallicGlossMap"));
                Put(Map.Occlusion, Tex(m, "_OcclusionMap"));
                if (!g.maps.ContainsKey(Map.Albedo)) { plan.notes.Add($"{m.name}: no colour texture"); continue; }
                foreach (var t in g.maps.Values) taken.Add(t);
                plan.groups.Add(g);
            }
            var byStem = new Dictionary<string, Group>();
            foreach (var t in objects.OfType<Texture2D>())
            {
                if (taken.Contains(t)) continue;
                var (stem, map, dx) = Parse(t.name);
                if (stem.Length == 0) stem = Parse(Path.GetFileName(Path.GetDirectoryName(AssetDatabase.GetAssetPath(t)))).stem;
                if (stem.Length == 0) stem = t.name.ToLowerInvariant();
                map = Settle(t, map, probe);
                string key = stem;
                if (map == Map.Albedo) for (int k = 2; byStem.TryGetValue(key, out var same) && same.maps.ContainsKey(Map.Albedo); k++) key = $"{stem} {k}";
                if (!byStem.TryGetValue(key, out var g))
                {
                    g = new Group { key = key, name = Pretty(key) };
                    byStem[key] = g;
                    plan.groups.Add(g);
                }
                if (map == Map.Height || map == Map.Unused || g.maps.ContainsKey(map)) { g.unused.Add(t); continue; }
                g.maps[map] = t;
                if (map == Map.Normal) g.dx = dx;
            }
            foreach (var g in plan.groups.Where(g => g.maps.Count == 0 && !g.strata).ToList())
            {
                plan.notes.Add($"{g.name}: nothing Strata uses ({string.Join(", ", g.unused.Select(x => x.name))})");
                plan.groups.Remove(g);
            }
            foreach (var sm in objects.OfType<StrataMaterial>()) plan.groups.Add(new Group { key = "strata " + sm.GetInstanceID(), name = sm.Title, strata = sm });

            var models = new Dictionary<string, Models>();
            foreach (var go in objects.OfType<GameObject>())
            {
                var words = Parse(go.name).stem.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                bool first = words.Any(w => FirstWords.Contains(w)), last = !first && words.Any(w => LastWords.Contains(w));
                var stemWords = words.Where(w => !FirstWords.Contains(w) && !LastWords.Contains(w)).ToList();
                string key = string.Join(" ", stemWords);
                if (!models.TryGetValue(key, out var s) || (!first && !last && s.step))
                {
                    if (s != null) key += " " + go.GetInstanceID();
                    var bare = stemWords.Where(w => !StairWords.Contains(w)).ToList();
                    s = new Models { key = key, name = (bare.Count > 0 ? Pretty(string.Join(" ", bare)) : Pretty(string.Join(" ", stemWords))) + " Stairs" };
                    if (s.name == " Stairs") s.name = go.name + " Stairs";
                    models[key] = s;
                    plan.stairs.Add(s);
                }
                if (first) s.first = go; else if (last) s.last = go; else s.step = go;
            }
            foreach (var st in objects.OfType<StrataStairType>()) plan.stairs.Add(new Models { key = "strata " + st.GetInstanceID(), name = st.Title, existing = st });
            return plan;
        }

        static string peekKey;
        static Plan peek;

        public static Plan Peek()
        {
            var objs = DragAndDrop.objectReferences ?? new Object[0];
            var paths = DragAndDrop.paths ?? new string[0];
            string key = string.Join(",", objs.Select(o => o ? o.GetInstanceID() : 0)) + "|" + string.Join(",", paths);
            if (key == peekKey && peek != null) return peek;
            var notes = new List<string>();
            var files = new List<string>();
            var objects = Resolve(objs, paths, files, notes);
            peek = Read(objects, false, notes);
            peek.files.AddRange(files);
            peekKey = key;
            return peek;
        }

        sealed class Job
        {
            public Group g;
            public Role role;
            public bool stair;
            public StrataMaterial owner;
            public bool reuse;
        }

        static string Dir(Object o) => Path.GetDirectoryName(AssetDatabase.GetAssetPath(o)).Replace('\\', '/');

        //! all the imports first then make the assets, and set every field BEFORE CreateAsset
        //! if a reimport hapens after it reloads the asset from disk and ur fields are gone (albedo kept going null bc of this)
        public static Result Apply(Object[] objs, string[] paths, StrataTileSet library, Role? section, bool stairsOnly, StrataMaterial selected)
        {
            peekKey = null;
            var r = new Result();
            var files = new List<string>();
            var objects = Resolve(objs, paths, files, r.notes);
            if (files.Count > 0) objects.AddRange(Import(files, r));
            var plan = Read(objects, true, new List<string>());
            r.notes.AddRange(plan.notes);
            var all = AssetDatabase.FindAssets("t:StrataMaterial").Select(g => AssetDatabase.LoadAssetAtPath<StrataMaterial>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x).ToList();
            bool lone = plan.groups.Count == 1 && plan.stairs.Count == 0;
            var jobs = new List<Job>();
            foreach (var g in plan.groups)
            {
                try { var job = Prepare(g, section, stairsOnly, lone ? selected : null, all, r); if (job != null) jobs.Add(job); }
                catch (Exception ex) { r.notes.Add($"{g.name}: {ex.Message}"); Debug.LogException(ex); }
            }
            foreach (var job in jobs)
            {
                try { Commit(job, library, all, r); }
                catch (Exception ex) { r.notes.Add($"{job.g.name}: {ex.Message}"); Debug.LogException(ex); }
            }
            foreach (var s in plan.stairs)
            {
                try { TakeModels(s, library, r); }
                catch (Exception ex) { r.notes.Add($"{s.name}: {ex.Message}"); Debug.LogException(ex); }
            }
            AssetDatabase.SaveAssets();
            r.status = Status(r, library);
            if (r.notes.Count > 1 || r.configured.Count > 0)
                Debug.Log("Strata: " + r.status + (r.configured.Count > 0 ? "\nSet up: " + string.Join(", ", r.configured) : "") + (r.notes.Count > 0 ? "\n" + string.Join("\n", r.notes) : ""));
            return r;
        }

        static string Status(Result r, StrataTileSet library)
        {
            var updatedTitles = r.updated.Select(u => u.Substring(0, u.IndexOf(" (", StringComparison.Ordinal) < 0 ? u.Length : u.IndexOf(" (", StringComparison.Ordinal))).ToList();
            var added = r.items.Select(x => x.item).Distinct().Where(x => !(x is StrataMaterial m && updatedTitles.Contains(m.Title)) && !r.already.Contains(x is StrataMaterial mm ? mm.Title : x is StrataStairType ss ? ss.Title : x.name)).ToList();
            var names = added.Select(x => x is StrataMaterial m ? m.Title : x is StrataStairType st ? st.Title : x.name).ToList();
            int mats = added.OfType<StrataMaterial>().Count(), stairs = added.OfType<StrataStairType>().Count();
            string what = names.Count <= 3 ? And(names) : $"{mats} material{(mats == 1 ? "" : "s")}" + (stairs > 0 ? $" and {stairs} stair type{(stairs == 1 ? "" : "s")}" : "");
            string s0 = names.Count == 0 ? "" : (library ? "Added " : "Made ") + what + ".";
            if (r.updated.Count > 0) s0 += (s0.Length > 0 ? " " : "") + "Updated " + And(r.updated) + ".";
            var already = r.already.Distinct().ToList();
            if (already.Count > 0) s0 += (s0.Length > 0 ? " " : "") + And(already) + (already.Count == 1 ? " is" : " are") + " already " + (library ? "in " + library.name : "in the project") + ".";
            if (r.notes.Count > 0) s0 += (s0.Length > 0 ? " " : "") + r.notes[0] + (r.notes.Count > 1 ? $" (+{r.notes.Count - 1} more in the Console)" : "");
            return s0.Length > 0 ? s0 : "Nothing to add.";
        }

        static Job Prepare(Group g, Role? section, bool stairsOnly, StrataMaterial selected, List<StrataMaterial> all, Result r)
        {
            if (g.strata) return new Job { g = g, role = section ?? RoleOf(g.strata.meantAs), stair = stairsOnly };
            var job = new Job { g = g, role = section ?? RoleFor(g.name), stair = stairsOnly || (section == null && IsStair(g.name)) };
            if (!g.maps.ContainsKey(Map.Albedo))
            {
                string dir = Dir(g.maps.Values.First());
                job.owner = all.FirstOrDefault(x => x.albedo && Parse(x.albedo.name).stem == g.key && Dir(x.albedo) == dir) ?? selected;
                if (!job.owner)
                {
                    var albedo = Sibling(g);
                    if (!albedo) { r.notes.Add($"{g.name}: no colour map to go with {string.Join(", ", g.maps.Values.Select(x => x.name))}"); return null; }
                    g.maps[Map.Albedo] = albedo;
                }
            }
            if (!job.owner) Siblings(g);
            job.reuse = !job.owner && all.Any(x => x.albedo == g.maps[Map.Albedo]);
            if (job.reuse) return job;
            Configure(g, r);
            if (!g.maps.ContainsKey(Map.Packed) && (g.maps.ContainsKey(Map.Roughness) || g.maps.ContainsKey(Map.Smoothness)))
            {
                var size = g.maps.TryGetValue(Map.Albedo, out var a) ? a : job.owner && job.owner.albedo ? job.owner.albedo : g.maps.Values.First();
                var made = Pack(g, size, r);
                if (made) g.maps[Map.Packed] = made;
            }
            return job;
        }

        static void Commit(Job job, StrataTileSet library, List<StrataMaterial> all, Result r)
        {
            var g = job.g;
            StrataMaterial m;
            if (g.strata) m = g.strata;
            else if (job.owner)
            {
                Undo.RecordObject(job.owner, "Strata: drop maps");
                Assign(job.owner, g);
                Save(job.owner);
                r.updated.Add($"{job.owner.Title} ({string.Join(", ", g.maps.Keys.Select(k => k.ToString().ToLower()))})");
                r.items.Add((job.owner, RoleOf(job.owner.meantAs)));
                return;
            }
            else
            {
                m = all.FirstOrDefault(x => x.albedo == g.maps[Map.Albedo]);
                if (m) { if (Fill(m, g)) Save(m); }
                else { m = Create(g, job.role, job.stair, r); all.Add(m); }
            }
            if (job.stair)
            {
                var st = StoneStair(m, g, r);
                if (st) (Add(library, st, r) ? r.items : r.left).Add((st, job.role));
                return;
            }
            (Add(library, job.role, m, r) ? r.items : r.left).Add((m, job.role));
        }

        static void Save(Object o)
        {
            EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssetIfDirty(o);
        }

        static Texture2D Sibling(Group g)
        {
            var any = g.maps.Values.First();
            string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(any)).Replace('\\', '/');
            foreach (var p in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (Path.GetDirectoryName(p).Replace('\\', '/') != dir) continue;
                var (stem, map, _) = Parse(Path.GetFileNameWithoutExtension(p));
                if (stem != g.key || (map != Map.Albedo && map != Map.None)) continue;
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (t && !g.maps.ContainsValue(t) && Settle(t, map, true) == Map.Albedo) return t;
            }
            return null;
        }

        static void Siblings(Group g)
        {
            if (g.from) return;
            string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(g.maps[Map.Albedo])).Replace('\\', '/');
            foreach (var p in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (Path.GetDirectoryName(p).Replace('\\', '/') != dir) continue;
                var (stem, map, dx) = Parse(Path.GetFileNameWithoutExtension(p));
                if (stem != g.key || map == Map.None || map == Map.Albedo || map == Map.Height || map == Map.Unused) continue;
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (!t || g.maps.ContainsValue(t)) continue;
                map = Settle(t, map, true);
                if (map == Map.Height || g.maps.ContainsKey(map)) continue;
                g.maps[map] = t;
                if (map == Map.Normal) g.dx = dx;
            }
        }

        static void Configure(Group g, Result r)
        {
            foreach (var kv in g.maps.ToList())
            {
                var t = kv.Value;
                var imp = Importer(t);
                if (!imp || (imp.textureType != TextureImporterType.Default && imp.textureType != TextureImporterType.NormalMap)) continue;
                bool changed = false;
                if (kv.Key == Map.Normal)
                {
                    if (imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; changed = true; }
                    if (g.dx && !imp.flipGreenChannel) { imp.flipGreenChannel = true; changed = true; }
                }
                else if (kv.Key != Map.Albedo)
                {
                    if (imp.textureType == TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.Default; changed = true; }
                    if (imp.sRGBTexture) { imp.sRGBTexture = false; changed = true; }
                }
                if (Math.Max(t.width, t.height) <= 512)
                {
                    if (imp.filterMode != FilterMode.Point) { imp.filterMode = FilterMode.Point; changed = true; }
                    if (imp.textureCompression != TextureImporterCompression.Uncompressed) { imp.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
                }
                if (!changed) continue;
                imp.SaveAndReimport();
                r.configured.Add(t.name);
                g.maps[kv.Key] = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(t));
            }
        }

        static StrataMaterial Create(Group g, Role role, bool stair, Result r)
        {
            var m = ScriptableObject.CreateInstance<StrataMaterial>();
            m.meantAs = stair ? StrataMaterial.Use.None : UseOf(role);
            (m.family, m.priority) = Ground(g.name);
            if (role == Role.Edge && !stair)
            {
                m.edgePattern = EdgeKind(g.name);
                var avg = Look(g.maps[Map.Albedo]).average;
                m.edgeDark = new Color(avg.r * 0.72f, avg.g * 0.72f, avg.b * 0.72f, 1f);
                m.edgeLight = new Color(Mathf.Min(1f, avg.r * 1.12f), Mathf.Min(1f, avg.g * 1.12f), Mathf.Min(1f, avg.b * 1.12f), 1f);
            }
            if (g.from)
            {
                var src = g.from;
                if (src.HasProperty("_BaseColor")) m.tint = src.GetColor("_BaseColor"); else if (src.HasProperty("_Color")) m.tint = src.GetColor("_Color");
                m.tint.a = 1f;
                if (src.HasProperty("_Smoothness")) m.smoothness = src.GetFloat("_Smoothness"); else if (src.HasProperty("_Glossiness")) m.smoothness = src.GetFloat("_Glossiness");
                if (src.HasProperty("_Metallic")) m.metalness = src.GetFloat("_Metallic");
            }
            Assign(m, g);
            string dir = StrataMaterialsView.MyFolder;
            StrataTerrain.EnsureFolder(dir);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{StrataTileSet.Safe(g.name)}.asset");
            AssetDatabase.CreateAsset(m, path);
            Save(m);
            r.created.Add(path);
            return m;
        }

        static bool Fill(StrataMaterial m, Group g)
        {
            Texture2D Want(Map k, Texture2D have) => !have && g.maps.TryGetValue(k, out var t) ? t : null;
            Texture2D a = Want(Map.Albedo, m.albedo), n = Want(Map.Normal, m.normal), mt = Want(Map.Metallic, m.metallic), o = Want(Map.Occlusion, m.occlusion), p = Want(Map.Packed, m.packed);
            if (!a && !n && !mt && !o && !p) return false;
            Undo.RecordObject(m, "Strata: drop");
            if (a) m.albedo = a;
            if (n) m.normal = n;
            if (mt) m.metallic = mt;
            if (o) m.occlusion = o;
            if (p) m.packed = p;
            return true;
        }

        static void Assign(StrataMaterial m, Group g)
        {
            if (g.maps.TryGetValue(Map.Albedo, out var a) && !m.albedo) m.albedo = a;
            if (g.maps.TryGetValue(Map.Normal, out var n)) m.normal = n;
            if (g.maps.TryGetValue(Map.Metallic, out var mt)) m.metallic = mt;
            if (g.maps.TryGetValue(Map.Occlusion, out var o)) m.occlusion = o;
            if (g.maps.TryGetValue(Map.Packed, out var p)) m.packed = p;
        }

        static Texture2D Pack(Group g, Texture2D size, Result r)
        {
            int w = size.width, h = size.height;
            if (Math.Max(w, h) > 2048) { float k = 2048f / Math.Max(w, h); w = Math.Max(1, (int)(w * k)); h = Math.Max(1, (int)(h * k)); }
            Color32[] Get(Map k) => g.maps.TryGetValue(k, out var t) ? Pixels(t, w, h) : null;
            var ao = Get(Map.Occlusion);
            var rough = Get(Map.Roughness);
            var smooth = Get(Map.Smoothness);
            var metal = Get(Map.Metallic);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++)
            {
                byte rg = rough != null ? rough[i].r : smooth != null ? (byte)(255 - smooth[i].r) : metal != null ? (byte)(255 - metal[i].a) : (byte)230;
                px[i] = new Color32(ao != null ? ao[i].r : (byte)255, rg, metal != null ? metal[i].r : (byte)0, 255);
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            tex.Apply(false);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            string from = AssetDatabase.GetAssetPath(size);
            string dir = from.StartsWith("Assets/") ? Path.GetDirectoryName(from).Replace('\\', '/') : StrataMaterialsView.MyFolder + "/Textures";
            StrataTerrain.EnsureFolder(dir);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{StrataTileSet.Safe(g.name)}_ORM.png");
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.sRGBTexture = false;
                if (Math.Max(w, h) <= 512) { imp.filterMode = FilterMode.Point; imp.textureCompression = TextureImporterCompression.Uncompressed; }
                imp.SaveAndReimport();
            }
            r.created.Add(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Color32[] Pixels(Texture2D t, int w, int h)
        {
            bool srgb = GraphicsFormatUtility.IsSRGBFormat(t.graphicsFormat);
            var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(w, h, srgb ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm, 0));
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(t, rt);
                RenderTexture.active = rt;
                var read = new Texture2D(w, h, TextureFormat.RGBA32, false, !srgb);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                read.Apply(false);
                var px = read.GetPixels32();
                Object.DestroyImmediate(read);
                return px;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static (Color average, bool normal) Look(Texture2D t)
        {
            try
            {
                var px = Pixels(t, 16, 16);
                float r = 0, g = 0, b = 0;
                int blue = 0;
                foreach (var c in px)
                {
                    r += c.r; g += c.g; b += c.b;
                    if (c.b >= 150 && c.b >= c.r && c.b >= c.g) blue++;
                }
                float n = px.Length * 255f;
                var avg = new Color(r / n, g / n, b / n, 1f);
                bool normal = avg.b > 0.7f && avg.r > 0.32f && avg.r < 0.68f && avg.g > 0.32f && avg.g < 0.68f && blue >= px.Length * 0.85f;
                return (avg, normal);
            }
            catch (Exception) { return (Color.grey, false); }
        }

        static List<Object> Import(List<string> files, Result r)
        {
            var o = new List<Object>();
            string Kind(string f) => ImageExt.Contains(Path.GetExtension(f).ToLowerInvariant()) ? "Textures" : "Models";
            string Stem(string f)
            {
                var words = Parse(Path.GetFileNameWithoutExtension(f)).stem.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => !FirstWords.Contains(w) && !LastWords.Contains(w));
                string n = StrataTileSet.Safe(Pretty(string.Join(" ", words)));
                return n.Length > 0 ? n : StrataTileSet.Safe(Path.GetFileNameWithoutExtension(f));
            }
            foreach (var batch in files.GroupBy(f => $"{Kind(f)}/{Stem(f)}"))
            {
                string dir = $"{StrataMaterialsView.MyFolder}/{batch.Key}";
                if (batch.Any(f => File.Exists($"{dir}/{Path.GetFileName(f)}") && !Same(f, $"{dir}/{Path.GetFileName(f)}"))) dir = AssetDatabase.GenerateUniqueAssetPath(dir);
                StrataTerrain.EnsureFolder(dir);
                foreach (var f in batch)
                {
                    try
                    {
                        string dest = $"{dir}/{Path.GetFileName(f)}";
                        if (!File.Exists(dest))
                        {
                            File.Copy(f, dest);
                            AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceSynchronousImport);
                            r.created.Add(dest);
                        }
                        var a = AssetDatabase.LoadMainAssetAtPath(dest);
                        if (a) o.Add(a); else r.notes.Add($"{Path.GetFileName(f)}: Unity could not import it");
                    }
                    catch (Exception ex) { r.notes.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
                }
            }
            return o;
        }

        static bool Same(string a, string b)
        {
            try { return new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)); }
            catch (Exception) { return false; }
        }

        static bool Was(Object o, string title, Result r)
        {
            r.already.Add(title);
            return true;
        }

        static bool Add(StrataTileSet lib, Role role, StrataMaterial m, Result r)
        {
            if (!lib) return r.created.Contains(AssetDatabase.GetAssetPath(m)) || Was(m, m.Title, r);
            var list = lib.List(role);
            if (list.Contains(m)) return Was(m, m.Title, r);
            int max = StrataTileSet.Max(role);
            if (list.Count >= max) { r.full = role.ToString().ToLower() + "s"; r.notes.Add($"{m.Title}: the {r.full} are full ({max})"); return false; }
            Undo.RecordObject(lib, "Strata: drop");
            list.Add(m);
            EditorUtility.SetDirty(lib);
            return true;
        }

        static bool Add(StrataTileSet lib, StrataStairType st, Result r)
        {
            if (!lib) return r.created.Contains(AssetDatabase.GetAssetPath(st)) || Was(st, st.Title, r);
            if (lib.stairs.Contains(st)) return Was(st, st.Title, r);
            if (lib.stairs.Count >= StrataTileSet.MaxStairs) { r.full = "stairs"; r.notes.Add($"{st.Title}: the stair types are full ({StrataTileSet.MaxStairs})"); return false; }
            Undo.RecordObject(lib, "Strata: drop");
            lib.stairs.Add(st);
            EditorUtility.SetDirty(lib);
            return true;
        }

        static IEnumerable<StrataStairType> Stairs() =>
            AssetDatabase.FindAssets("t:StrataStairType").Select(g => AssetDatabase.LoadAssetAtPath<StrataStairType>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x);

        static StrataStairType NewStair(string name, Result r, Action<StrataStairType> init)
        {
            var st = ScriptableObject.CreateInstance<StrataStairType>();
            init(st);
            string dir = StrataMaterialsView.MyFolder + "/Stair Types";
            StrataTerrain.EnsureFolder(dir);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{StrataTileSet.Safe(name)}.asset");
            AssetDatabase.CreateAsset(st, path);
            Save(st);
            r.created.Add(path);
            return st;
        }

        static StrataStairType StoneStair(StrataMaterial m, Group g, Result r)
        {
            var st = Stairs().FirstOrDefault(x => x.build == StrataStairType.Build.Stone && x.body == m && x.tread == m && x.cheek == m);
            return st ? st : NewStair(IsStair(g.name) ? g.name : g.name + " Stairs", r, x => { x.build = StrataStairType.Build.Stone; x.body = x.tread = x.cheek = m; });
        }

        static void TakeModels(Models s, StrataTileSet library, Result r)
        {
            var st = s.existing;
            if (!st)
            {
                var step = s.step ? s.step : s.first ? s.first : s.last;
                st = Stairs().FirstOrDefault(x => x.build == StrataStairType.Build.Models && x.step == step);
                if (!st) st = NewStair(s.name, r, x =>
                {
                    x.build = StrataStairType.Build.Models;
                    x.step = step;
                    x.firstStep = s.first != step ? s.first : null;
                    x.lastStep = s.last != step ? s.last : null;
                });
            }
            (Add(library, st, r) ? r.items : r.left).Add((st, Role.Top));
        }
    }
}
