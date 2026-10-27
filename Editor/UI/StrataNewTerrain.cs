using System;
using System.IO;
using Strata.Formats;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Strata
{
    public sealed class StrataNewTerrain : EditorWindow
    {
        string terrainName = "Terrain";
        int sizeX = 64, sizeZ = 64;
        StrataTileSet tiles;
        string folder = "Assets/Terrains";
        string error = "";
        static readonly int[] Sizes = { 32, 64, 128, 256 };

        [MenuItem("GameObject/Strata/Terrain", false, 10)]
        static void FromHierarchy(MenuCommand _) => Open();

        [MenuItem("Tools/Strata/New Terrain…", priority = 2)]
        public static void Open()
        {
            var w = CreateInstance<StrataNewTerrain>();
            w.titleContent = new GUIContent("New Strata Terrain", StrataIcons.Tool);
            w.tiles = BasicProfile.DefaultSet();
            w.terrainName = w.FreeName("Terrain");
            var size = new Vector2(340, 186);
            w.minSize = w.maxSize = size;
            w.ShowUtility();
            var main = EditorGUIUtility.GetMainWindowPosition();
            w.position = new Rect(main.x + (main.width - size.x) / 2, main.y + (main.height - size.y) / 3, size.x, size.y);
        }

        string FreeName(string baseName)
        {
            string n = baseName;
            for (int k = 2; File.Exists($"{folder}/{n}/{n}.strata"); k++) n = $"{baseName} {k}";
            return n;
        }

        void OnGUI()
        {
            GUILayout.Space(8);
            EditorGUIUtility.labelWidth = 80;
            terrainName = EditorGUILayout.TextField("Name", terrainName);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Size", "Cells (metres) east-west and north-south"));
                foreach (int s in Sizes)
                    if (GUILayout.Toggle(sizeX == s && sizeZ == s, s.ToString(), EditorStyles.miniButtonMid, GUILayout.Width(40))) { sizeX = sizeZ = s; }
                GUILayout.Space(6);
                sizeX = Mathf.Clamp(EditorGUILayout.IntField(sizeX, GUILayout.Width(40)), 8, 2048);
                GUILayout.Label("×", GUILayout.Width(10));
                sizeZ = Mathf.Clamp(EditorGUILayout.IntField(sizeZ, GUILayout.Width(40)), 8, 2048);
            }
            tiles = (StrataTileSet)EditorGUILayout.ObjectField(new GUIContent("Tile set", "What it can be painted with"), tiles, typeof(StrataTileSet), false);
            using (new EditorGUILayout.HorizontalScope())
            {
                folder = EditorGUILayout.TextField(new GUIContent("Folder", "The terrain gets its own folder in here, with its data, meshes, textures and prefab"), folder);
                if (GUILayout.Button("…", GUILayout.Width(24)))
                {
                    string abs = EditorUtility.SaveFolderPanel("Folder for the terrain", Application.dataPath, "");
                    if (!string.IsNullOrEmpty(abs) && abs.Replace('\\', '/').StartsWith(Application.dataPath)) folder = "Assets" + abs.Replace('\\', '/').Substring(Application.dataPath.Length);
                }
            }
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(80))) Close();
                GUI.enabled = tiles && !string.IsNullOrWhiteSpace(terrainName);
                if (GUILayout.Button("Create", GUILayout.Width(100))) Create();
                GUI.enabled = true;
            }
            GUILayout.Space(8);
        }

        void Create()
        {
            try
            {
                string n = terrainName.Trim();
                string path = BasicProfile.CreateTerrain(n, $"{folder.TrimEnd('/')}/{n}", sizeX, sizeZ, tiles, (s, f) => EditorUtility.DisplayProgressBar("Strata", s, f));
                StrataSessions.Open(path);
                ToolManager.SetActiveTool<StrataTool>();
                SceneView.lastActiveSceneView?.Focus();
                Close();
            }
            catch (Exception ex) { error = ex.Message; }
            finally { EditorUtility.ClearProgressBar(); }
        }
    }
}
