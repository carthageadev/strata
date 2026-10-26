using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Strata.Formats;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Role = Strata.StrataTileSet.Role;
using Slot = Strata.Formats.BasicProfile.Slot;

namespace Strata
{
    sealed class StrataMaterialsView : IDisposable
    {
        public const string MyFolder = "Assets/Strata Materials";
        StrataTileSet set;
        bool all;

        public Action<string> full;

        public StrataTileSet library
        {
            get => set;
            set { if (value && value != set) all = false; set = value; }
        }
        Object selected;
        double buildAt = -1;
        string status = "";
        readonly Action repaint;
        SerializedObject so;
        Role selRole;
        GUIStyle tileName, tileTag, plus, sectionTitle, count, slotLabel, panel, panelTitle;
        bool stylesPro;
        Vector2 listScroll, settingsScroll;
        float viewH;
        bool locked, selLocked;
        readonly HashSet<Object> flash = new HashSet<Object>();
        double flashUntil;
        Object scrollTo;
        float settingsH;
        string renaming;
        bool healing;
        object hover;
        string hoverText, after;

        List<StrataPack> packs = new List<StrataPack>();
        List<StrataTileSet> sets = new List<StrataTileSet>();
        List<(StrataMaterial item, string pack)> materials = new List<(StrataMaterial, string)>();
        List<(StrataStairType item, string pack)> stairTypes = new List<(StrataStairType, string)>();
        Dictionary<Object, string> packOf = new Dictionary<Object, string>();
        Dictionary<Role, List<StrataMaterial>> kinds = new Dictionary<Role, List<StrataMaterial>>();
        List<StrataStairType> stairList = new List<StrataStairType>();
        List<StrataTileSet> pickSets;
        GUIContent[] pickNames;
        StrataTileSet pickedFor;
        readonly GUIContent tileLabel = new GUIContent();
        bool stale = true;

        static readonly Color Accent = new Color(0.95f, 0.72f, 0.3f);
        const float TileW = 64, TileH = 80;
        const string Mine = "My materials";

        public StrataMaterialsView(Action repaint)
        {
            this.repaint = repaint;
            EditorApplication.update += Tick;
            EditorApplication.projectChanged += MarkStale;
            Undo.undoRedoPerformed += OnUndo;
        }

        public void Dispose()
        {
            EditorApplication.update -= Tick;
            EditorApplication.projectChanged -= MarkStale;
            Undo.undoRedoPerformed -= OnUndo;
            if (buildAt > 0) BuildNow();
        }

        public void Select(Object item, Role role = Role.Top)
        {
            selected = item; selRole = role; so = null; renaming = null; settingsScroll = Vector2.zero;
            selLocked = Locked(item);
            if (panelH.TryGetValue(PanelOf(item), out float h)) settingsH = h;
            else if (item) settingsH = float.MaxValue;
        }

        readonly Dictionary<Type, float> panelH = new Dictionary<Type, float>();
        static Type PanelOf(Object item) => item ? item.GetType() : typeof(StrataTileSet);
        void MarkStale() { stale = true; repaint(); }

        //! cached, dont call AssetDatabase in OnGUI again it was lagging like crazy
        void Gather()
        {
            if (!stale) return;
            stale = false;
            sets = AssetDatabase.FindAssets("t:StrataTileSet").Select(g => AssetDatabase.LoadAssetAtPath<StrataTileSet>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x).OrderBy(x => x.name).ToList();
            packs = AssetDatabase.FindAssets("t:StrataPack").Select(g => AssetDatabase.LoadAssetAtPath<StrataPack>(AssetDatabase.GUIDToAssetPath(g))).Where(p => p).OrderBy(p => p.Title).ToList();
            string PackOf(Object o) => packs.FirstOrDefault(p => o is StrataMaterial m ? p.materials.Contains(m) : p.stairs.Contains((StrataStairType)o))?.Title ?? Mine;
            materials = AssetDatabase.FindAssets("t:StrataMaterial").Select(g => AssetDatabase.LoadAssetAtPath<StrataMaterial>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x).Select(x => (x, PackOf(x))).OrderBy(x => x.Item2 == Mine).ThenBy(x => x.Item2).ThenBy(x => x.Item1.Title).ToList();
            stairTypes = AssetDatabase.FindAssets("t:StrataStairType").Select(g => AssetDatabase.LoadAssetAtPath<StrataStairType>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x).Select(x => (x, PackOf(x))).OrderBy(x => x.Item2 == Mine).ThenBy(x => x.Item2).ThenBy(x => x.Item1.Title).ToList();
            packOf = materials.Select(x => ((Object)x.item, x.pack)).Concat(stairTypes.Select(x => ((Object)x.item, x.pack))).GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.First().Item2);
            kinds = new[] { Role.Top, Role.Side, Role.Edge }.ToDictionary(r => r, r => materials.Where(x => (x.item.meantAs & UseOf(r)) != 0).Select(x => x.item).ToList());
            stairList = stairTypes.Select(x => x.item).ToList();
            pickNames = null;
        }

        void Tick()
        {
            if (buildAt > 0 && EditorApplication.timeSinceStartup >= buildAt) { buildAt = -1; BuildNow(); }
            if (flashUntil <= 0) return;
            if (EditorApplication.timeSinceStartup > flashUntil) { flashUntil = 0; flash.Clear(); }
            repaint();
        }

        void Flash(IEnumerable<Object> items, Object first)
        {
            flash.Clear();
            foreach (var x in items) if (x) flash.Add(x);
            flashUntil = flash.Count > 0 ? EditorApplication.timeSinceStartup + 1.6 : 0;
            scrollTo = first;
        }

        void ScrollTo(Object item, Rect cell)
        {
            if (!ReferenceEquals(item, scrollTo) || Event.current.type != EventType.Repaint) return;
            scrollTo = null;
            listScroll.y = Mathf.Max(0, cell.y - 40);
            repaint();
        }
        void OnUndo() { if (library) Schedule(); }
        void Schedule() { buildAt = EditorApplication.timeSinceStartup + 0.6; status = "Updating…"; }

        void BuildNow()
        {
            if (!library) return;
            try { library.Build(); BasicProfile.Refresh(library); status = after ?? "Up to date"; }
            catch (Exception ex) { status = ex.Message; Debug.LogException(ex); }
            after = null;
            repaint();
        }

        void Later(Action a)
        {
            EditorApplication.delayCall += () =>
            {
                try { a(); }
                catch (Exception ex) { status = ex.Message; Debug.LogException(ex); }
                finally { EditorUtility.ClearProgressBar(); stale = true; }
                repaint();
            };
        }

        void Styles()
        {
            if (tileName != null && stylesPro == EditorGUIUtility.isProSkin) return;
            stylesPro = EditorGUIUtility.isProSkin;
            tileName = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, clipping = TextClipping.Clip, fontSize = 10 };
            tileTag = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, fontSize = 8, clipping = TextClipping.Clip };
            tileTag.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
            sectionTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            count = new GUIStyle(EditorStyles.miniLabel);
            count.normal.textColor = new Color(0.55f, 0.55f, 0.55f);
            plus = new GUIStyle(tileName) { fontSize = 16 };
            slotLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter };
            panel = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(8, 8, 6, 8), margin = new RectOffset(4, 4, 4, 4) };
            panelTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, clipping = TextClipping.Clip };
        }

        public void Draw(float width, float height)
        {
            Styles();
            Gather();
            viewH = height;
            if (!selected && !ReferenceEquals(selected, null)) Select(null);
            Picker();
            var active = StrataSessions.Active;
            if (StrataProfiles.Find(active) is IStrataFormat format)
                EditorGUILayout.HelpBox($"{format.Name}'s terrain draws with its own materials; tile sets are for Strata terrains.", MessageType.None);
            if (!library && !all) { GUILayout.Label("Pick a tile set.", EditorStyles.miniLabel); return; }
            locked = !all && Locked(library);
            if (locked)
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Read only, it came with the package", EditorStyles.miniLabel);
                    if (GUILayout.Button(new GUIContent("Make a copy", "An editable tile set in Assets with the same items"), EditorStyles.miniButton, GUILayout.Width(90))) Later(NewLibrary);
                }
            if (!all && !locked && !library.Current) { library.Upgrade(); Schedule(); }
            if (!all && !locked && !healing && (Holes(library.tops) || Holes(library.sides) || Holes(library.edges) || Holes(library.stairs)))
            {
                healing = true;
                Later(() => { BasicProfile.Heal(library); healing = false; Select(null); });
            }

            if (selected || !all) Settings(width, height * 0.45f);
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            float listW = width - 16;
            Kind(Role.Top, "Tops", listW);
            Kind(Role.Side, "Sides", listW);
            Kind(Role.Edge, "Edges", listW);
            Stairs(listW);
            GUILayout.Space(8);
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Share…", "Export as a .unitypackage, with textures and models"), EditorStyles.miniButton, GUILayout.Width(60))) Share();
                if (GUILayout.Button(new GUIContent("Import…", "Import a .unitypackage of materials"), EditorStyles.miniButton, GUILayout.Width(60))) Later(Import);
                GUILayout.FlexibleSpace();
                GUILayout.Label(new GUIContent(hoverText ?? (buildAt > 0 ? "Updating…" : status), hoverText ?? status), count);
            }
            GUILayout.Space(2);
        }

        void Settings(float width, float maxH)
        {
            string name = selected is StrataMaterial sm ? sm.Title : selected is StrataStairType ss ? ss.Title : library.name;
            using (new EditorGUILayout.VerticalScope(panel))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent($"Settings for {name}", name), panelTitle);
                    GUILayout.FlexibleSpace();
                    if (selected && GUILayout.Button(new GUIContent("×", "Close"), EditorStyles.miniButton, GUILayout.Width(22)))
                    {
                        Select(null);
                        GUIUtility.keyboardControl = 0;
                    }
                }
                if (Event.current.type == EventType.Repaint)
                {
                    var line = GUILayoutUtility.GetLastRect();
                    EditorGUI.DrawRect(new Rect(line.x, line.yMax + 2, line.width, 1), new Color(0.5f, 0.5f, 0.5f, 0.35f));
                }
                GUILayout.Space(6);
                bool scrolls = settingsH > maxH;
                settingsScroll = EditorGUILayout.BeginScrollView(settingsScroll, GUIStyle.none, scrolls ? GUI.skin.verticalScrollbar : GUIStyle.none, GUILayout.Height(Mathf.Min(settingsH, maxH)));
                using (new EditorGUI.DisabledScope(selected ? selLocked : locked))
                using (new EditorGUILayout.VerticalScope())
                {
                    float inner = width - (scrolls ? 44 : 28);
                    if (selected is StrataMaterial m) EditMaterial(m, inner);
                    else if (selected is StrataStairType st) EditStair(st);
                    else EditSet();
                }
                if (Event.current.type == EventType.Repaint)
                {
                    float got = GUILayoutUtility.GetLastRect().height;
                    panelH[PanelOf(selected)] = got;
                    if (Mathf.Abs(got - settingsH) > 0.5f) { settingsH = got; repaint(); }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        void EditSet() => PixelGrid();

        static readonly GUIContent GridLabel = new GUIContent("Pixel grid", "The pixel grid blends follow. Each material's own: found from its art (or set on it). Or force one resolution for a 16 m tile: 256 is every texel, 32 is pixels of 8 x 8 texels.");
        static readonly GUIContent[] GridNames = { new GUIContent("Each material's own"), new GUIContent("512"), new GUIContent("256 (every texel)"), new GUIContent("128"), new GUIContent("64"), new GUIContent("32"), new GUIContent("16") };
        static readonly int[] GridValues = { 0, 512, 256, 128, 64, 32, 16 };

        void PixelGrid()
        {
            int grid = library.pixelGrid;
            int pick = EditorGUILayout.IntPopup(GridLabel, grid, GridNames, GridValues);
            if (pick != grid) { Undo.RecordObject(library, "Strata: pixel grid"); library.pixelGrid = pick; EditorUtility.SetDirty(library); Schedule(); }
        }

        void Picker()
        {
            if (pickNames == null || pickedFor != library)
            {
                pickedFor = library;
                pickSets = sets.ToList();
                if (library && !pickSets.Contains(library)) pickSets.Insert(0, library);
                pickNames = pickSets.Select(x => new GUIContent(x.name, AssetDatabase.GetAssetPath(x)))
                    .Append(new GUIContent("All materials", "Every Strata material and stair type in the project"))
                    .Append(new GUIContent("New tile set…", "A new tile set using the same items as this one")).ToArray();
            }
            var list = pickSets;
            int at = all ? list.Count : Mathf.Max(0, list.IndexOf(library));
            int pick = EditorGUILayout.Popup(at, pickNames, GUILayout.Height(20));
            if (pick == at) return;
            if (pick == list.Count) { all = true; Select(null); }
            else if (pick == list.Count + 1) Later(NewLibrary);
            else { set = list[pick]; all = false; Select(null); }
        }

        static StrataMaterial.Use UseOf(Role role) => role == Role.Top ? StrataMaterial.Use.Top : role == Role.Side ? StrataMaterial.Use.Side : StrataMaterial.Use.Edge;
        static Slot SlotOf(Role role) => role == Role.Top ? Slot.Top : role == Role.Side ? Slot.Side : Slot.Edge;

        void Kind(Role role, string title, float width)
        {
            var shown = all ? kinds[role] : library.List(role);
            bool room = all || !locked && shown.Count < StrataTileSet.Max(role);
            bool isFull = !all && !locked && !room;
            Header(title, all ? $"{shown.Count}" : $"{shown.Count} / {StrataTileSet.Max(role)}");
            var area = Grid(shown.Count + (room || isFull ? 1 : 0), width, out int perRow);
            for (int i = 0; i < shown.Count; i++)
            {
                var cell = Cell(area, i, perRow);
                ScrollTo(shown[i], cell);
                if (!Seen(cell)) continue;
                var item = shown[i];
                if (!item) continue;
                string tag = all ? PackName(item) : role == Role.Top ? (library.slopeLayer == i ? "slopes" : PackName(item)) : i == 0 ? "natural" : i == 1 ? "by stairs" : PackName(item);
                bool textured = role != Role.Edge || item.EdgeTextured;
                Tile(cell, item, role, textured ? item.albedo : null, role == Role.Edge ? item.edgeDark : (Color?)null, role == Role.Edge ? item.edgeLight : (Color?)null, item.Title, tag);
            }
            if (room && AddTile(Cell(area, shown.Count, perRow), $"Add a {title.ToLower().TrimEnd('s')} (or drop textures here)"))
            {
                if (all) Later(() => NewMaterial(role));
                else AddMenu(role);
            }
            else if (isFull && FullTile(Cell(area, shown.Count, perRow), $"{library.name} has no room for more {title.ToLower()}")) Full(title.ToLower());
            Drop(area, role, role, false);
        }

        void Stairs(float width)
        {
            var shown = all ? stairList : library.stairs;
            bool room = all || !locked && shown.Count < StrataTileSet.MaxStairs;
            Header("Stairs", $"{shown.Count}");
            var area = Grid(shown.Count + (room ? 1 : 0), width, out int perRow);
            for (int i = 0; i < shown.Count; i++)
            {
                ScrollTo(shown[i], Cell(area, i, perRow));
                if (Seen(Cell(area, i, perRow)) && shown[i]) Tile(Cell(area, i, perRow), shown[i], Role.Top, shown[i].Preview, null, null, shown[i].Title, PackName(shown[i]));
            }
            if (room && AddTile(Cell(area, shown.Count, perRow), "Add a stair type (or drop a step model here)"))
            {
                if (all) Later(NewStair);
                else AddMenu(null);
            }
            Drop(area, "stairs", null, true);
        }

        void AddMenu(Role? role) => AddMenuFor(role).ShowAsContext();

        GenericMenu AddMenuFor(Role? role)
        {
            var menu = new GenericMenu();
            if (role is Role r)
            {
                menu.AddItem(new GUIContent($"New {r.ToString().ToLower()}"), false, () => Later(() => NewMaterial(r)));
                var used = library.List(r);
                var others = materials.Where(x => (x.item.meantAs & UseOf(r)) != 0 && !used.Contains(x.item)).ToList();
                if (others.Count > 0) menu.AddSeparator("");
                foreach (var (m, pack) in others) menu.AddItem(new GUIContent($"{pack}/{m.Title}"), false, () => { if (AddTo(r, m)) Select(m, r); repaint(); });
            }
            else
            {
                menu.AddItem(new GUIContent("New stair type"), false, () => Later(NewStair));
                var others = stairTypes.Where(x => !library.stairs.Contains(x.item)).ToList();
                if (others.Count > 0) menu.AddSeparator("");
                foreach (var (st, pack) in others) menu.AddItem(new GUIContent($"{pack}/{st.Title}"), false, () => { AddStair(st); Select(st); repaint(); });
            }
            return menu;
        }

        void Header(string title, string n)
        {
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(title, sectionTitle, GUILayout.ExpandWidth(false));
                GUILayout.Label(new GUIContent(n, "In use / most this tile set can hold"), count);
            }
        }

        string PackName(Object o) => o && packOf.TryGetValue(o, out var p) ? p : null;

        bool Seen(Rect r) => r.yMax >= listScroll.y && r.y <= listScroll.y + viewH;

        static bool Locked(Object o)
        {
            string path = o ? AssetDatabase.GetAssetPath(o) : null;
            if (path == null || !path.StartsWith("Packages/")) return false;
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            return info != null && info.source != UnityEditor.PackageManager.PackageSource.Embedded && info.source != UnityEditor.PackageManager.PackageSource.Local;
        }

        static bool Holes<T>(List<T> list) where T : Object
        {
            foreach (var x in list) if (!x) return true;
            return false;
        }

        Rect Grid(int n, float width, out int perRow)
        {
            perRow = Mathf.Max(1, Mathf.FloorToInt((width - 8) / (TileW + 4)));
            int rows = Mathf.Max(1, Mathf.CeilToInt(n / (float)perRow));
            return GUILayoutUtility.GetRect(width - 8, rows * (TileH + 4));
        }

        static Rect Cell(Rect area, int k, int perRow) => new Rect(area.x + (k % perRow) * (TileW + 4), area.y + (k / perRow) * (TileH + 4), TileW, TileH);

        void Tile(Rect r, Object item, Role role, Texture tex, Color? a, Color? b, string name, string tag)
        {
            var e = Event.current;
            bool on = selected == item && (!(item is StrataMaterial) || selRole == role);
            if (e.type == EventType.Repaint)
            {
                var bg = on ? new Color(Accent.r, Accent.g, Accent.b, 0.35f) : new Color(0.5f, 0.5f, 0.5f, 0.1f);
                EditorGUI.DrawRect(r, bg);
                var img = new Rect(r.x + 6, r.y + 4, TileW - 12, TileW - 12);
                if (tex) GUI.DrawTexture(img, tex, ScaleMode.ScaleAndCrop);
                else if (a.HasValue)
                {
                    EditorGUI.DrawRect(new Rect(img.x, img.y, img.width, img.height / 2), b ?? a.Value);
                    EditorGUI.DrawRect(new Rect(img.x, img.y + img.height / 2, img.width, img.height / 2), a.Value);
                }
                else EditorGUI.DrawRect(img, new Color(0.3f, 0.3f, 0.3f));
                if (on) EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2, r.width, 2), Accent);
                if (flash.Contains(item)) Outline(r, Mathf.PingPong((float)EditorApplication.timeSinceStartup * 4f, 1f));
            }
            tileLabel.text = tileLabel.tooltip = name;
            tileName.alignment = tileName.CalcSize(tileLabel).x > r.width - 2 ? TextAnchor.UpperLeft : TextAnchor.UpperCenter;
            GUI.Label(new Rect(r.x + 1, r.y + TileW - 8, r.width - 2, 14), tileLabel, tileName);
            tileName.alignment = TextAnchor.UpperCenter;
            if (tag != null) GUI.Label(new Rect(r.x, r.y + TileW + 4, r.width, 12), tag, tileTag);
            if (r.Contains(e.mousePosition) && e.type == EventType.MouseDown)
            {
                Select(item, role);
                GUIUtility.keyboardControl = 0;
                if (e.button == 1) Context(item, role);
                e.Use();
            }
        }

        bool FullTile(Rect r, string tip)
        {
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(r, new Color(1f, 0.38f, 0.32f, 0.1f));
            GUI.Label(new Rect(r.x, r.y + TileW / 2 - 6, r.width, 14), "full", tileName);
            return GUI.Button(r, new GUIContent("", tip), GUIStyle.none);
        }

        void Full(string kind)
        {
            if (library == StrataProfiles.Library(StrataSessions.Active)) full?.Invoke(kind);
            else status = $"{library.name}: the {kind} are full";
        }

        bool AddTile(Rect r, string tip)
        {
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.06f));
            GUI.Label(new Rect(r.x, r.y + TileW / 2 - 16, r.width, 20), "+", plus);
            GUI.Label(new Rect(r.x, r.y + TileW / 2 + 4, r.width, 14), "add", tileName);
            return GUI.Button(r, new GUIContent("", tip), GUIStyle.none);
        }

        bool InSet(Object item, Role role) => item is StrataMaterial m ? library.List(role).Contains(m) : library.stairs.Contains(item as StrataStairType);

        void AddOrRemove(Object item, Role role)
        {
            if (item is StrataMaterial m)
            {
                int k = library.List(role).IndexOf(m);
                if (k >= 0) Later(() => AskRemove(SlotOf(role), k, m.Title)); else AddTo(role, m);
            }
            else if (item is StrataStairType st)
            {
                int k = library.stairs.IndexOf(st);
                if (k >= 0) Later(() => AskRemove(Slot.Stair, k, st.Title)); else AddStair(st);
            }
            repaint();
        }

        void Context(Object item, Role role)
        {
            var menu = new GenericMenu();
            if (!all) { menu.AddItem(new GUIContent($"Remove from {library.name}"), false, () => AddOrRemove(item, role)); menu.AddSeparator(""); }
            menu.AddItem(new GUIContent("Find in the Project"), false, () => EditorGUIUtility.PingObject(item));
            menu.AddItem(new GUIContent("Delete…"), false, () => Later(() => Delete(item)));
            menu.ShowAsContext();
        }

        void Drop(Rect area, object key, Role? role, bool stairs)
        {
            var e = Event.current;
            if (e.type == EventType.Repaint && Equals(hover, key)) Outline(area, 1f);
            if (!area.Contains(e.mousePosition) || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)) return;
            var plan = StrataDrop.Peek();
            if (plan.Empty)
            {
                if (plan.notes.Count == 0) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                Hover(key, plan.notes[0]);
                e.Use();
                return;
            }
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                EndHover();
                Take(DragAndDrop.objectReferences, DragAndDrop.paths, role, stairs);
            }
            else Hover(key, $"Drop: {plan.Summary()}" + (stairs ? " as stairs" : role is Role r ? $" as {r.ToString().ToLower()}s" : ""));
            e.Use();
        }

        static void Outline(Rect r, float alpha)
        {
            var c = new Color(Accent.r, Accent.g, Accent.b, alpha);
            EditorGUI.DrawRect(new Rect(r.x - 2, r.y - 2, r.width + 4, 2), c);
            EditorGUI.DrawRect(new Rect(r.x - 2, r.yMax, r.width + 4, 2), c);
            EditorGUI.DrawRect(new Rect(r.x - 2, r.y, 2, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax, r.y, 2, r.height), c);
        }

        public string HoverText => hoverText;
        public bool OverSection => hover != null && !Equals(hover, "window");

        public void Hover(object key, string text)
        {
            if (Equals(hover, key) && hoverText == text) return;
            hover = key;
            hoverText = text;
            repaint();
        }

        public void EndHover()
        {
            if (hover == null && hoverText == null) return;
            hover = null;
            hoverText = null;
            repaint();
        }

        public void Take(Object[] objects, string[] paths, Role? role, bool stairs)
        {
            var o = (objects ?? new Object[0]).ToArray();
            var p = (paths ?? new string[0]).ToArray();
            Later(() =>
            {
                bool into = !all && library && !locked;
                var target = into ? library : BasicProfile.DefaultSet();
                if (locked) library = target;
                var r = StrataDrop.Apply(o, p, target, role, stairs, selLocked ? null : selected as StrataMaterial);
                if (r.first) Select(r.first, r.role);
                Flash(r.items.Concat(r.left).Select(x => x.item), r.first);
                if (r.full != null && target && target == StrataProfiles.Library(StrataSessions.Active)) full?.Invoke(r.full);
                if (target == library) { Schedule(); after = r.status; status = "Updating…"; }
                else
                {
                    if (target && r.items.Count > 0) { target.Build(); BasicProfile.Refresh(target); }
                    status = target ? $"{r.status} In {target.name}." : r.status;
                }
            });
        }

        static string FolderFor(StrataPack pack, bool stairs)
        {
            string root = pack ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(pack)).Replace('\\', '/') : MyFolder;
            string dir = stairs ? root + "/Stair Types" : (pack ? root + "/Materials" : root);
            StrataTerrain.EnsureFolder(dir);
            return dir;
        }

        void NewMaterial(Role role)
        {
            var m = ScriptableObject.CreateInstance<StrataMaterial>();
            var like = library ? library.List(role).FirstOrDefault(x => x) : null;
            if (like) { m.albedo = like.albedo; m.normal = like.normal; m.metallic = like.metallic; m.occlusion = like.occlusion; m.packed = like.packed; m.edgePattern = like.edgePattern; m.edgeDark = like.edgeDark; m.edgeLight = like.edgeLight; m.edgeTexture = like.edgeTexture; m.family = like.family; }
            m.meantAs = UseOf(role);
            AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath($"{FolderFor(null, false)}/New Material.asset"));
            if (!all) AddTo(role, m);
            Select(m, role);
        }

        StrataStairType MakeStair()
        {
            var like = library ? library.stairs.FirstOrDefault(x => x && !x.Models) : null;
            var st = ScriptableObject.CreateInstance<StrataStairType>();
            if (like) { st.build = like.build; st.body = like.body; st.tread = like.tread; st.cheek = like.cheek; st.planks = like.planks; st.posts = like.posts; }
            AssetDatabase.CreateAsset(st, AssetDatabase.GenerateUniqueAssetPath($"{FolderFor(null, true)}/New Stair Type.asset"));
            return st;
        }

        void NewStair() { var st = MakeStair(); if (!all) AddStair(st); Select(st); }

        bool AddTo(Role role, StrataMaterial m)
        {
            var list = library.List(role);
            if (list.Contains(m)) return true;
            int max = StrataTileSet.Max(role);
            if (list.Count >= max) { status = $"No room: {role.ToString().ToLower()}s go up to {max}."; return false; }
            Undo.RecordObject(library, "Strata: add material");
            list.Add(m);
            EditorUtility.SetDirty(library);
            Schedule();
            return true;
        }

        void AddStair(StrataStairType st)
        {
            if (library.stairs.Contains(st)) return;
            if (library.stairs.Count >= StrataTileSet.MaxStairs) { status = $"No room: stair types go up to {StrataTileSet.MaxStairs}."; return; }
            Undo.RecordObject(library, "Strata: add stair type");
            library.stairs.Add(st);
            EditorUtility.SetDirty(library);
            Schedule();
        }

        void AskRemove(Slot slot, int index, string name)
        {
            var list = slot == Slot.Stair ? null : library.List(slot == Slot.Top ? Role.Top : slot == Slot.Side ? Role.Side : Role.Edge);
            int count = slot == Slot.Stair ? library.stairs.Count : list.Count;
            if (index < 0 || index >= count) return;
            if (count == 1 && slot != Slot.Edge) { status = $"A tile set needs at least one {(slot == Slot.Stair ? "stair type" : slot.ToString().ToLower())}."; return; }
            string first = slot == Slot.Stair ? library.stairs.Where((x, k) => k != index && x).Select(x => x.Title).FirstOrDefault()
                : list.Where((x, k) => k != index && x).Select(x => x.Title).FirstOrDefault();
            string instead = first != null ? $"show {first} instead" : "lose their edge";
            if (!EditorUtility.DisplayDialog("Take it out?", $"Stop using {name} in {library.name}? Terrains painted with it {instead}; everything else keeps its look. Each terrain is backed up first. This can't be undone.", "Take it out", "Cancel")) return;
            int n = BasicProfile.RemoveFromSet(library, slot, index, (s, f) => EditorUtility.DisplayProgressBar("Strata", s, f));
            status = n > 0 ? $"{name} taken out; {n} terrain{(n == 1 ? "" : "s")} updated." : $"{name} taken out.";
        }

        void Delete(Object item)
        {
            string path = AssetDatabase.GetAssetPath(item);
            var sets = AssetDatabase.FindAssets("t:StrataTileSet").Select(g => AssetDatabase.LoadAssetAtPath<StrataTileSet>(AssetDatabase.GUIDToAssetPath(g))).Where(s => s).ToList();
            var users = sets.Where(s => item is StrataMaterial m ? s.tops.Contains(m) || s.sides.Contains(m) || s.edges.Contains(m) : s.stairs.Contains((StrataStairType)item)).ToList();
            foreach (var s in users)
            {
                bool lastOne = item is StrataMaterial mm ? (s.tops.Contains(mm) && s.tops.Count == 1) || (s.sides.Contains(mm) && s.sides.Count == 1) : s.stairs.Count == 1;
                if (lastOne) { status = $"{item.name} is the last of its kind in {s.name}: add another first."; return; }
            }
            string where = users.Count > 0 ? $" It is used by {string.Join(", ", users.Select(s => s.name))}: terrains painted with it show the first of its kind instead (each backed up first)." : "";
            if (!EditorUtility.DisplayDialog("Delete?", $"Delete {item.name} from the project?{where} This can't be undone.", "Delete", "Cancel")) return;
            foreach (var s in users)
            {
                if (item is StrataMaterial m)
                {
                    foreach (var (role, slot) in new[] { (Role.Top, Slot.Top), (Role.Side, Slot.Side), (Role.Edge, Slot.Edge) })
                    {
                        int k = s.List(role).IndexOf(m);
                        if (k >= 0) BasicProfile.RemoveFromSet(s, slot, k, (t, f) => EditorUtility.DisplayProgressBar("Strata", t, f));
                    }
                }
                else BasicProfile.RemoveFromSet(s, Slot.Stair, s.stairs.IndexOf((StrataStairType)item), (t, f) => EditorUtility.DisplayProgressBar("Strata", t, f));
            }
            MoveTo(item, null, remove: true);
            AssetDatabase.DeleteAsset(path);
            status = $"Deleted {Path.GetFileNameWithoutExtension(path)}.";
            Select(null);
        }

        static void MoveTo(Object item, StrataPack pack, bool remove = false)
        {
            foreach (var p in AssetDatabase.FindAssets("t:StrataPack").Select(g => AssetDatabase.LoadAssetAtPath<StrataPack>(AssetDatabase.GUIDToAssetPath(g))).Where(p => p))
            {
                bool changed = item is StrataMaterial m ? p.materials.RemoveAll(x => x == m) > 0 : p.stairs.RemoveAll(x => x == (StrataStairType)item) > 0;
                if (changed) EditorUtility.SetDirty(p);
            }
            if (!remove && pack)
            {
                if (item is StrataMaterial m) pack.materials.Add(m); else if (item is StrataStairType s) pack.stairs.Add(s);
                EditorUtility.SetDirty(pack);
            }
            AssetDatabase.SaveAssets();
        }

        void Head(Object item)
        {
            if (!all && InSet(item, selRole))
            {
                if (GUILayout.Button(new GUIContent($"Remove from {library.name}", "Stop using it in this tile set (it stays in the project)"), GUILayout.Height(22))) AddOrRemove(item, selRole);
                GUILayout.Space(4);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                renaming ??= item.name;
                renaming = EditorGUILayout.DelayedTextField("Name", renaming);
                if (renaming != item.name) { Rename(item, renaming); renaming = item.name; }
                if (GUILayout.Button(new GUIContent("Find", "Show it in the Project window"), EditorStyles.miniButton, GUILayout.Width(40))) EditorGUIUtility.PingObject(item);
                if (GUILayout.Button(new GUIContent("Delete", "Delete it from the project"), EditorStyles.miniButton, GUILayout.Width(50))) { var it = item; Later(() => Delete(it)); }
            }
            var now = packs.FirstOrDefault(p => item is StrataMaterial m ? p.materials.Contains(m) : p.stairs.Contains((StrataStairType)item));
            var names = new List<string> { Mine }; names.AddRange(packs.Select(p => p.Title)); names.Add("New pack…");
            int at = now ? packs.IndexOf(now) + 1 : 0;
            int pick = EditorGUILayout.Popup(new GUIContent("Pack", "Which pack it belongs to (optional)"), at, names.ToArray());
            if (pick != at)
            {
                var it = item;
                var list = packs.ToList();
                if (pick == names.Count - 1) Later(() => { var g = ScriptableObject.CreateInstance<StrataPack>(); StrataTerrain.EnsureFolder(MyFolder); AssetDatabase.CreateAsset(g, AssetDatabase.GenerateUniqueAssetPath($"{MyFolder}/New Pack.asset")); MoveTo(it, g); EditorGUIUtility.PingObject(g); });
                else Later(() => MoveTo(it, pick == 0 ? null : list[pick - 1]));
            }
        }

        static readonly (string, string)[] Slots = { ("albedo", "Albedo"), ("normal", "Normal"), ("metallic", "Metallic"), ("occlusion", "Occlusion"), ("packed", "Packed") };
        static readonly GUIContent[] SizeNames = { new GUIContent("Found from the art"), new GUIContent("1 texel"), new GUIContent("2 texels"), new GUIContent("4 texels"), new GUIContent("8 texels"), new GUIContent("16 texels") };
        static readonly int[] SizeValues = { 0, 1, 2, 4, 8, 16 };

        void EditMaterial(StrataMaterial m, float width)
        {
            if (so == null || so.targetObject != m) so = new SerializedObject(m);
            so.Update();
            Head(m);
            GUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            var slots = Slots;
            var rect = GUILayoutUtility.GetRect(width, 82);
            float slotW = Mathf.Min(64, (rect.width - 4 * 6) / slots.Length);
            for (int k = 0; k < slots.Length; k++)
            {
                var (prop, label) = slots[k];
                var r = new Rect(rect.x + k * (slotW + 6), rect.y, slotW, slotW);
                var p = so.FindProperty(prop);
                p.objectReferenceValue = EditorGUI.ObjectField(r, p.objectReferenceValue, typeof(Texture2D), false);
                GUI.Label(new Rect(r.x - 4, r.yMax + 1, r.width + 8, 14), new GUIContent(label, p.tooltip), slotLabel);
            }
            EditorGUILayout.PropertyField(so.FindProperty("meantAs"), new GUIContent("Kind", "Where it is listed: as a top, a side, an edge (any mix)"));
            EditorGUILayout.PropertyField(so.FindProperty("tint"));
            var ps = so.FindProperty("pixelSize");
            ps.intValue = EditorGUILayout.IntPopup(new GUIContent("Pixel size", ps.tooltip), ps.intValue, SizeNames, SizeValues);
            EditorGUILayout.PropertyField(so.FindProperty("smoothness"));
            EditorGUILayout.PropertyField(so.FindProperty("metalness"));
            if ((m.meantAs & StrataMaterial.Use.Top) != 0 || (!all && library.tops.Contains(m)))
            {
                GUILayout.Label("As a top", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(so.FindProperty("priority"));
                EditorGUILayout.PropertyField(so.FindProperty("family"));
                EditorGUILayout.PropertyField(so.FindProperty("wobble"));
                EditorGUILayout.PropertyField(so.FindProperty("ragged"));
                int k = all ? -1 : library.tops.IndexOf(m);
                if (k >= 0)
                {
                    bool slope = library.slopeLayer == k;
                    bool now = EditorGUILayout.Toggle(new GUIContent("On steep slopes", "Shown wherever natural ground is too steep for the paint (one top at most)"), slope);
                    if (now != slope) { Undo.RecordObject(library, "Strata: slope top"); library.slopeLayer = now ? k : -1; EditorUtility.SetDirty(library); Schedule(); }
                }
            }
            if ((m.meantAs & StrataMaterial.Use.Edge) != 0 || (!all && library.edges.Contains(m)))
            {
                GUILayout.Label("As an edge", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(so.FindProperty("edgePattern"), new GUIContent("Pattern"));
                EditorGUILayout.PropertyField(so.FindProperty("edgeTexture"), new GUIContent("Use the albedo"));
                EditorGUILayout.PropertyField(so.FindProperty("edgeDark"), new GUIContent("Dark"));
                EditorGUILayout.PropertyField(so.FindProperty("edgeLight"), new GUIContent("Light"));
            }
            if (EditorGUI.EndChangeCheck()) { so.ApplyModifiedProperties(); stale = true; Schedule(); }
        }

        void EditStair(StrataStairType st)
        {
            if (so == null || so.targetObject != st) so = new SerializedObject(st);
            so.Update();
            Head(st);
            GUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(so.FindProperty("build"));
            var build = (StrataStairType.Build)so.FindProperty("build").enumValueIndex;
            if (build == StrataStairType.Build.Models)
            {
                EditorGUILayout.PropertyField(so.FindProperty("step"), new GUIContent("Step", "One step: repeated up the flight and fitted to each step (+Z climbs, +Y up)"));
                EditorGUILayout.PropertyField(so.FindProperty("firstStep"), new GUIContent("Bottom step", "Optional: the first step"));
                EditorGUILayout.PropertyField(so.FindProperty("lastStep"), new GUIContent("Top step", "Optional: the last step"));
                EditorGUILayout.PropertyField(so.FindProperty("stack"), new GUIContent("Solid", "Stack steps under each step, down to the foot"));
                var turn = so.FindProperty("turn");
                turn.intValue = EditorGUILayout.IntPopup(new GUIContent("Turn", "If the models face another way"), turn.intValue, new[] { new GUIContent("None"), new GUIContent("90°"), new GUIContent("180°"), new GUIContent("270°") }, new[] { 0, 1, 2, 3 });
            }
            else
            {
                bool timber = build == StrataStairType.Build.Timber;
                if (!timber)
                {
                    EditorGUILayout.PropertyField(so.FindProperty("steps"));
                    EditorGUILayout.PropertyField(so.FindProperty("sides"));
                }
                var steps = (StairSteps)so.FindProperty("steps").enumValueIndex;
                var sides = (StairSides)so.FindProperty("sides").enumValueIndex;
                var parts = timber ? new[] { "planks", "posts" } : sides == StairSides.None ? new[] { "body", "tread" } : new[] { "body", "tread", "cheek" };
                for (int k = 0; k < parts.Length; k++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var p = so.FindProperty(parts[k]);
                        EditorGUILayout.PropertyField(p, new GUIContent(StrataStairType.PartName(build, steps, sides, k), p.tooltip));
                        var mat = p.objectReferenceValue as StrataMaterial;
                        var r = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18));
                        if (mat && mat.albedo && Event.current.type == EventType.Repaint) GUI.DrawTexture(r, mat.albedo, ScaleMode.ScaleAndCrop);
                    }
                }
            }
            if (EditorGUI.EndChangeCheck()) { so.ApplyModifiedProperties(); Schedule(); }
        }

        static void Rename(Object asset, string name)
        {
            name = StrataTileSet.Safe(name);
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path) || name == asset.name) return;
            string err = AssetDatabase.RenameAsset(path, name);
            if (!string.IsNullOrEmpty(err)) Debug.LogWarning("Strata: " + err);
        }

        void NewLibrary()
        {
            string path = EditorUtility.SaveFilePanelInProject("New tile set", library ? library.name + " Copy" : "Tiles", "asset", "A tile set using the same items, to change on its own", AssetDatabase.IsValidFolder(MyFolder) ? MyFolder : "Assets");
            if (string.IsNullOrEmpty(path)) return;
            var ts = ScriptableObject.CreateInstance<StrataTileSet>();
            if (library)
            {
                library.Upgrade();
                ts.CopyItems(library);
            }
            AssetDatabase.CreateAsset(ts, path);
            ts.Upgrade();
            library = ts;
            stale = true;
            Select(null);
            BuildNow();
        }

        void Share()
        {
            var menu = new GenericMenu();
            if (selected) menu.AddItem(new GUIContent(selected.name), false, () => Export(selected));
            else menu.AddDisabledItem(new GUIContent("The selected item (none)"));
            if (!all) menu.AddItem(new GUIContent($"The tile set ({library.name})"), false, () => Export(library));
            foreach (var p in packs)
            {
                var pack = p;
                if (pack) menu.AddItem(new GUIContent($"The pack {pack.Title}"), false, () => Export(pack));
            }
            menu.ShowAsContext();
        }

        static void Export(Object what)
        {
            string file = EditorUtility.SaveFilePanel("Share Strata materials", "", what.name + ".unitypackage", "unitypackage");
            if (string.IsNullOrEmpty(file)) return;
            var flags = ExportPackageOptions.IncludeDependencies | ExportPackageOptions.Interactive;
#if UNITY_6000_7_OR_NEWER
            UnityEditor.AssetPackage.Package.Export(new UnityEditor.AssetPackage.ExportPackageParameters(AssetDatabase.GetAssetPath(what), file, flags: flags));
#else
            AssetDatabase.ExportPackage(AssetDatabase.GetAssetPath(what), file, flags);
#endif
        }

        static void Import()
        {
            string file = EditorUtility.OpenFilePanel("Import Strata materials", "", "unitypackage");
            if (string.IsNullOrEmpty(file)) return;
#if UNITY_6000_7_OR_NEWER
            UnityEditor.AssetPackage.Package.Import(file, true);
#else
            AssetDatabase.ImportPackage(file, true);
#endif
        }
    }
}
