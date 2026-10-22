using System;
using System.IO;
using System.Linq;
using Strata.Formats;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Strata
{
    public sealed class StrataWindow : EditorWindow
    {
        Vector2 scroll;
        string message = "";
        //! keep these NonSerialized!! unity keeps the window fields after recompile so the old styles came back
        //! and the title was black on the dark skin
        [NonSerialized] GUIStyle small, box, dropTitle, dropText;
        [NonSerialized] bool stylesPro;
        [SerializeField] int tab;
        [SerializeField] StrataTileSet materialsLibrary;
        StrataMaterialsView materials;

        [MenuItem("Tools/Strata/Terrain Editor", priority = 0)]
        public static void ShowWindow()
        {
            var w = GetWindow<StrataWindow>();
            w.minSize = new Vector2(330, 300);
            w.Show();
        }

        [MenuItem("Tools/Strata/Materials", priority = 3)]
        static void ShowMaterialsMenu() => ShowMaterials(null);

        public static void ShowMaterials(StrataTileSet ts)
        {
            var w = GetWindow<StrataWindow>();
            w.minSize = new Vector2(330, 300);
            w.tab = 1;
            w.materials.library = ts ? ts : StrataProfiles.Library(StrataSessions.Active) ?? BasicProfile.DefaultTiles();
            w.materials.Select(null);
            w.Show();
            w.Focus();
        }

        void OnEnable()
        {
            titleContent = new GUIContent("Strata", StrataIcons.Tool);
            StrataSessions.Changed += Repaint;
            materials = new StrataMaterialsView(Repaint) { library = materialsLibrary };
        }

        void OnDisable()
        {
            StrataSessions.Changed -= Repaint;
            materialsLibrary = materials.library;
            materials.Dispose();
        }

        void Styles()
        {
            if (small != null && stylesPro == EditorGUIUtility.isProSkin) return;
            stylesPro = EditorGUIUtility.isProSkin;
            small = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { fontSize = 10 };
            box = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 10) };
            dropTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            dropText = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
        }

        void OnGUI()
        {
            Styles();
            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                tab = GUILayout.Toolbar(tab, new[] { "Terrain", "Materials" });
                if (GUILayout.Button(new GUIContent(StrataIcons.Gear, "Preferences"), EditorStyles.iconButton, GUILayout.Width(20), GUILayout.Height(20))) SettingsService.OpenUserPreferences(StrataPreferences.Path);
            }
            GUILayout.Space(6);

            if (tab == 1)
            {
                if (!materials.library) materials.library = StrataProfiles.Library(StrataSessions.Active) ?? BasicProfile.DefaultTiles();
                materials.Draw(position.width, position.height);
                materialsLibrary = materials.library;
            }
            else TerrainTab();
            Dropping();
        }

        void Dropping()
        {
            var e = Event.current;
            if (e.type == EventType.DragExited) { materials.EndHover(); return; }
            if (e.type == EventType.Repaint)
            {
                if (materials.HoverText != null && !materials.OverSection) Overlay(materials.HoverText);
                return;
            }
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            var plan = StrataDrop.Peek();
            if (plan.Empty)
            {
                if (plan.notes.Count == 0) { materials.EndHover(); return; }
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                materials.Hover("window", plan.notes[0]);
                e.Use();
                return;
            }
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                materials.EndHover();
                tab = 1;
                if (!materials.library) materials.library = StrataProfiles.Library(StrataSessions.Active) ?? BasicProfile.DefaultTiles();
                materials.Take(DragAndDrop.objectReferences, DragAndDrop.paths, null, false);
            }
            else materials.Hover("window", plan.Summary());
            e.Use();
        }

        void Overlay(string text)
        {
            var all = new Rect(0, 0, position.width, position.height);
            EditorGUI.DrawRect(all, new Color(0f, 0f, 0f, 0.35f));
            var r = new Rect(16, position.height / 2 - 36, position.width - 32, 72);
            var accent = new Color(0.95f, 0.72f, 0.3f);
            EditorGUI.DrawRect(r, EditorGUIUtility.isProSkin ? new Color(0.16f, 0.16f, 0.16f, 1f) : new Color(0.86f, 0.86f, 0.86f, 1f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 2), accent);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2, r.width, 2), accent);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 2, r.height), accent);
            EditorGUI.DrawRect(new Rect(r.xMax - 2, r.y, 2, r.height), accent);
            GUI.Label(new Rect(r.x, r.y + 12, r.width, 22), "Drop to add to Strata", dropTitle);
            GUI.Label(new Rect(r.x + 8, r.y + 40, r.width - 16, 18), text, dropText);
        }

        void TerrainTab()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            if (!string.IsNullOrEmpty(StrataSessions.Notice) && !StrataSessions.RecoveryOffered) EditorGUILayout.HelpBox(StrataSessions.Notice, MessageType.Info);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.None);

            var t = StrataSessions.Active;
            if (t != null) OpenTerrain(t); else Terrains();

            if (VillageV2Profile.MapPresent && !VillageV2Profile.Converted) Village V2();
            if (VillageV2Profile.MapPresent && !VillageV2Profile.PackMade && GUILayout.Button(new GUIContent("Make Village V2's Strata pack", "Village V2's ground, walls, edges and stairs as Strata materials, kept with the map, so Strata terrains can paint with them"), GUILayout.Height(24)))
                Run(() => message = VillageV2Profile.MakePack());
            if (GUILayout.Button(new GUIContent("New terrain…", "Also GameObject > Strata > Terrain, or right-click in the Hierarchy"), GUILayout.Height(24))) StrataNewTerrain.Open();
            Keys();
            EditorGUILayout.EndScrollView();
        }

        void OpenTerrain(StrataTerrain t)
        {
            using (new EditorGUILayout.VerticalScope(box))
            {
                GUILayout.Label(t.Name, EditorStyles.boldLabel);
                GUILayout.Label(new GUIContent($"{StrataProfiles.DisplayName(t)} · {t.map.w} × {t.map.h} m · {t.map.stairs.Count} stairs", t.dataPath), small);
                if (StrataSessions.RecoveryOffered)
                {
                    EditorGUILayout.HelpBox(StrataSessions.Notice, MessageType.Warning);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Restore those edits")) Run(() => StrataSessions.KeepRecovered());
                        if (GUILayout.Button("Discard them")) Run(() => StrataSessions.DiscardRecovered());
                    }
                }
                if (!t.Connected) EditorGUILayout.HelpBox("Not in the open scene: drag its prefab in, or open its scene.", MessageType.Warning);
                bool unsaved = t.Unsaved || t.Unbaked;
                GUILayout.Label(unsaved ? "Unsaved edits" : "Baked and saved", small);
                GUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool editing = ToolManager.activeToolType == typeof(StrataTool);
                    GUI.enabled = t.Connected;
                    if (GUILayout.Button(editing ? "Editing" : "Edit", GUILayout.Height(28))) { ToolManager.SetActiveTool<StrataTool>(); SceneView.lastActiveSceneView?.Focus(); }
                    GUI.enabled = unsaved;
                    if (GUILayout.Button(new GUIContent("Bake", "Weld, refresh colliders and save. Also happens before Play and when the scene is saved."), GUILayout.Height(28), GUILayout.Width(70))) Run(() => message = t.Bake());
                    GUI.enabled = true;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Check", "Compare the meshes, mask and paint with the data"))) Run(() => message = t.Check());
                    if (GUILayout.Button(new GUIContent("Rebuild all", "Rebuild every chunk from the data"))) Run(() => { t.RebuildAll(); message = "Rebuilt every chunk."; });
                    if (GUILayout.Button("Close")) Run(() => StrataSessions.Close());
                }
                TileSet(t);
                Backups(t);
            }
        }

        void TileSet(StrataTerrain t)
        {
            if (t.map.profile != BasicProfile.Id) return;
            var lib = StrataProfiles.Library(t);
            GUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                var pick = (StrataTileSet)EditorGUILayout.ObjectField(new GUIContent("Tile set", "What this terrain paints with"), lib, typeof(StrataTileSet), false);
                if (pick && pick != lib && EditorUtility.DisplayDialog("Use another tile set?", $"Draw {t.Name} with {pick.name}? Cells keep their places in the lists, so they show that tile set's materials in those places.", "Use it", "Cancel"))
                    Run(() =>
                    {
                        t.map.tileSet = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(pick));
                        t.map.revision++;
                        BasicProfile.Configure(t, rebuildStairs: true);
                        pick.Apply(t.groundMaterial, t.wallsMaterial);
                        message = $"{t.Name} now paints from {pick.name}.";
                    });
                if (GUILayout.Button(new GUIContent("Edit", "Its materials, in the Materials tab"), EditorStyles.miniButton, GUILayout.Width(40))) { tab = 1; materials.library = lib; materials.Select(null); }
            }
        }

        void Backups(StrataTerrain t)
        {
            var list = StrataTerrain.Backups(t.Name);
            GUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent($"Backups: {list.Length}", $"StrataBackups/{t.Name} beside the project"), small);
                if (GUILayout.Button("Back up now", EditorStyles.miniButton, GUILayout.Width(90))) Run(() => message = "Backed up to " + t.Backup(force: true));
                if (list.Length > 0 && GUILayout.Button("Restore…", EditorStyles.miniButton, GUILayout.Width(70)))
                {
                    var menu = new GenericMenu();
                    foreach (var f in list.Take(30))
                    {
                        string file = f;
                        menu.AddItem(new GUIContent(Path.GetFileNameWithoutExtension(f)), false, () =>
                        {
                            if (EditorUtility.DisplayDialog("Restore a backup?", $"Replace {t.Name} with {Path.GetFileName(file)}? The terrain as it is now is backed up first.", "Restore", "Cancel"))
                                Run(() => { t.Restore(file); message = "Restored " + Path.GetFileName(file); });
                        });
                    }
                    menu.ShowAsContext();
                }
            }
        }

        void Terrains()
        {
            using (new EditorGUILayout.VerticalScope(box))
            {
                GUILayout.Label("Terrains", EditorStyles.boldLabel);
                var here = StrataSessions.TerrainsInScene();
                var all = StrataSessions.FindTerrains();
                if (all.Length == 0) GUILayout.Label("None yet.", small);
                foreach (var path in all)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool inScene = here.Contains(path);
                        GUILayout.Label(new GUIContent(Path.GetFileNameWithoutExtension(path) + (inScene ? "   (in this scene)" : ""), path));
                        if (GUILayout.Button("Open", GUILayout.Width(60))) Run(() => { if (StrataSessions.Open(path) != null && inScene) ToolManager.SetActiveTool<StrataTool>(); });
                    }
                }
            }
        }

        void Village V2()
        {
            using (new EditorGUILayout.VerticalScope(box))
            {
                GUILayout.Label("Village V2", EditorStyles.boldLabel);
                GUILayout.Label("Rebuilds the exported terrain as an editable one, checks it matches, and swaps it in. The scene is backed up first.", small);
                if (!VillageV2Import.Available) EditorGUILayout.HelpBox("Generated/Data/terrainGrid.bin is missing: re-export the map from the web version.", MessageType.Warning);
                GUI.enabled = VillageV2Import.Available;
                if (GUILayout.Button("Make Village V2's terrain editable", GUILayout.Height(26)))
                    Run(() =>
                    {
                        message = VillageV2Profile.Convert((s, f) => EditorUtility.DisplayProgressBar("Strata", s, f));
                        StrataSessions.Open(VillageV2Profile.DataPath);
                    });
                GUI.enabled = true;
            }
        }

        void Keys()
        {
            using (new EditorGUILayout.VerticalScope(box))
            {
                GUILayout.Label("Keys", EditorStyles.boldLabel);
                GUILayout.Label(
                    "Drag: paint     Shift + click: pick     Ctrl + drag: revert\n" +
                    "[ ]: brush size     - =: level down / up\n" +
                    "Enter / Esc: apply / drop a shape     Ctrl + Z / Y: undo / redo", small);
            }
        }

        void Run(Action a)
        {
            try { a(); }
            catch (Exception ex) { message = ex.Message; Debug.LogException(ex); }
            finally { EditorUtility.ClearProgressBar(); }
            StrataSessions.Raise();
            Repaint();
        }
    }
}
