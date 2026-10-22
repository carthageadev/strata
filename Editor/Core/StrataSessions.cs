using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Strata
{
    [InitializeOnLoad]
    public static class StrataSessions
    {
        public static StrataTerrain Active { get; private set; }
        public static event Action Changed;
        public static string Notice { get; set; } = "";
        public static bool RecoveryOffered { get; private set; }

        const string ReloadKey = "Strata.ReloadPath";

        public static bool AutoBakeOnPlay { get => EditorPrefs.GetBool("Strata.AutoBakeOnPlay", true); set => EditorPrefs.SetBool("Strata.AutoBakeOnPlay", value); }
        public static bool BakeOnSave { get => EditorPrefs.GetBool("Strata.BakeOnSave", true); set => EditorPrefs.SetBool("Strata.BakeOnSave", value); }

        static StrataSessions()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.quitting += OnQuit;
            EditorSceneManager.sceneOpened += (s, m) => OnScenesChanged();
            EditorSceneManager.sceneClosed += s => OnScenesChanged();
            EditorApplication.delayCall += AfterReload;
            EditorApplication.delayCall += UpgradeOld;
        }

        static void UpgradeOld()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.delayCall += UpgradeOld; return; }
            try { Formats.BasicProfile.UpgradeOld(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        public static string[] FindTerrains()
        {
            var root = Application.dataPath;
            return Directory.GetFiles(root, "*.strata", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".original.strata", StringComparison.OrdinalIgnoreCase))
                .Select(f => "Assets" + f.Substring(root.Length).Replace('\\', '/'))
                .OrderBy(f => f).ToArray();
        }

        public static string[] TerrainsInScene()
        {
            var list = new List<string>();
            var instances = new HashSet<GameObject>();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) instances.Add(PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject));
            }
            foreach (var path in FindTerrains())
            {
                string name = Path.GetFileNameWithoutExtension(path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Path.GetDirectoryName(path).Replace('\\', '/')}/{name}.prefab");
                if (prefab && instances.Contains(prefab)) list.Add(path);
            }
            return list.ToArray();
        }

        public static StrataTerrain Open(string dataPath)
        {
            if (Active != null && Active.dataPath == dataPath) { Active.FindInstance(); return Active; }
            Close();
            try
            {
                Active = StrataTerrain.Open(dataPath);
                Notice = "";
                Formats.StrataProfiles.Configure(Active);
                if (Active.HasRecovery) { RecoveryOffered = true; Notice = "This terrain has edits that were not saved when Unity last closed."; }
            }
            catch (Exception ex)
            {
                Active = null;
                Notice = ex.Message;
                Debug.LogError(ex);
            }
            Changed?.Invoke();
            return Active;
        }

        public static StrataTerrain OpenForScene()
        {
            string selected = SelectedTerrain();
            if (selected != null && (Active == null || Active.dataPath != selected)) return Open(selected);
            if (Active != null) { if (!Active.Connected) Active.FindInstance(); if (Active.Connected) return Active; }
            var here = TerrainsInScene();
            return here.Length > 0 ? Open(here[0]) : Active;
        }

        public static string SelectedTerrain()
        {
            var go = Selection.activeGameObject;
            if (!go || EditorUtility.IsPersistent(go)) return null;
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            var source = root ? PrefabUtility.GetCorrespondingObjectFromSource(root) : null;
            if (!source) return null;
            string data = Path.ChangeExtension(AssetDatabase.GetAssetPath(source), ".strata").Replace('\\', '/');
            return File.Exists(data) ? data : null;
        }

        public static void Close()
        {
            if (Active == null) return;
            try { if (Active.history.Recording) Active.history.End(); if (Active.Unsaved || Active.Unbaked) Active.Bake(); }
            catch (Exception ex) { Debug.LogError(ex); }
            Active.Dispose();
            Active = null;
            RecoveryOffered = false;
            Changed?.Invoke();
        }

        public static void KeepRecovered()
        {
            if (Active == null) return;
            Active.Recover();
            RecoveryOffered = false;
            Notice = "Recovered the unsaved edits.";
            Changed?.Invoke();
        }

        public static void DiscardRecovered()
        {
            if (Active == null) return;
            Active.Save(backup: false);
            RecoveryOffered = false;
            Notice = "";
            Changed?.Invoke();
        }

        public static void Raise() => Changed?.Invoke();

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.ExitingEditMode || Active == null) return;
            try
            {
                if (Active.history.Recording) Active.history.End();
                if (AutoBakeOnPlay && (Active.Unbaked || Active.Unsaved)) Active.Bake();
            }
            catch (Exception ex) { Debug.LogError("Strata could not bake the terrain before Play: " + ex.Message); }
        }

        static void OnSceneSaving(Scene scene, string path)
        {
            if (Active == null || !BakeOnSave || !Active.instance || Active.instance.scene != scene) return;
            try { if (Active.Unbaked || Active.Unsaved) Active.Bake(); }
            catch (Exception ex) { Debug.LogError("Strata could not bake the terrain while saving: " + ex.Message); }
        }

        static void OnBeforeReload()
        {
            if (Active == null) return;
            try
            {
                if (Active.history.Recording) Active.history.End();
                Active.WriteRecovery();
                SessionState.SetString(ReloadKey, Active.dataPath);
            }
            catch (Exception ex) { Debug.LogError(ex); }
            Active.Dispose();
            Active = null;
        }

        static void AfterReload()
        {
            string path = SessionState.GetString(ReloadKey, "");
            if (string.IsNullOrEmpty(path)) return;
            SessionState.EraseString(ReloadKey);
            if (!File.Exists(path)) return;
            if (Active != null && Active.dataPath != path) return;
            try
            {
                if (Active == null) { Active = StrataTerrain.Open(path); Formats.StrataProfiles.Configure(Active); }
                if (Active.HasRecovery) { Active.AdoptRecovery(); RecoveryOffered = false; Notice = ""; }
            }
            catch (Exception ex) { Active?.Dispose(); Active = null; Notice = ex.Message; }
            Changed?.Invoke();
        }

        static void OnQuit()
        {
            if (Active == null) return;
            try { Active.WriteRecovery(); } catch (Exception) { }
        }

        static void OnScenesChanged()
        {
            if (Active == null) return;
            Active.FindInstance();
            Changed?.Invoke();
        }
    }
}
