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
        public static bool RecoveryOffered => Active != null && recovery.Contains(Active);
        public static IReadOnlyList<StrataTerrain> Opened => open;

        static readonly List<StrataTerrain> open = new List<StrataTerrain>();
        static readonly HashSet<StrataTerrain> recovery = new HashSet<StrataTerrain>();

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

        public static string[] TerrainsInScene(string[] found = null)
        {
            var list = new List<string>();
            foreach (var path in found ?? FindTerrains())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path.ChangeExtension(path, ".prefab"));
                if (prefab && PrefabUtility.FindAllInstancesOfPrefab(prefab).Length > 0) list.Add(path);
            }
            return list.ToArray();
        }

        public static StrataTerrain Find(string dataPath) => open.FirstOrDefault(t => t.dataPath == dataPath);

        public static StrataTerrain Open(string dataPath)
        {
            var have = Find(dataPath);
            if (have != null) { have.FindInstance(); Focus(have); return have; }
            if (!StrataPlug.Multi) { foreach (var o in open.ToArray()) Shut(o); Active = null; }
            var t = Load(dataPath);
            if (t != null)
            {
                Active = t;
                Notice = recovery.Contains(t) ? "This terrain has edits that were not saved when Unity last closed." : "";
                foreach (var p in StrataPlug.All) p.Joined(t);
            }
            Changed?.Invoke();
            return t;
        }

        static StrataTerrain Load(string dataPath)
        {
            try
            {
                var t = StrataTerrain.Open(dataPath);
                Formats.StrataProfiles.Configure(t);
                open.Add(t);
                if (t.HasRecovery) recovery.Add(t);
                return t;
            }
            catch (Exception ex)
            {
                Notice = ex.Message;
                Debug.LogError(ex);
                return null;
            }
        }

        public static void Focus(StrataTerrain t)
        {
            if (t == null || t == Active || !open.Contains(t)) return;
            Active = t;
            Changed?.Invoke();
        }

        public static StrataTerrain OpenForScene()
        {
            string selected = SelectedTerrain();
            if (!StrataPlug.Multi)
            {
                if (selected != null && (Active == null || Active.dataPath != selected)) return Open(selected);
                if (Active != null) { if (!Active.Connected) Active.FindInstance(); if (Active.Connected) return Active; }
                var here = TerrainsInScene();
                return here.Length > 0 ? Open(here[0]) : Active;
            }
            foreach (var path in TerrainsInScene())
                if (Find(path) == null)
                {
                    var t = Load(path);
                    if (t != null) foreach (var p in StrataPlug.All) p.Joined(t);
                }
            if (selected != null) return Open(selected);
            if (Active != null && !Active.Connected) Active.FindInstance();
            if (Active == null || !Active.Connected) Active = open.FirstOrDefault(t => t.Connected) ?? Active;
            Changed?.Invoke();
            return Active;
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
            var t = Active;
            if (t == null) return;
            Shut(t);
            Active = open.FirstOrDefault(o => o.Connected) ?? open.FirstOrDefault();
            Changed?.Invoke();
        }

        public static void CloseAll()
        {
            foreach (var t in open.ToArray()) Shut(t);
            Active = null;
            Changed?.Invoke();
        }

        static void Shut(StrataTerrain t)
        {
            try { if (t.history.Recording) t.history.End(); if (t.Unsaved || t.Unbaked) t.Bake(); }
            catch (Exception ex) { Debug.LogError(ex); }
            t.Dispose();
            open.Remove(t);
            recovery.Remove(t);
            Rearranged();
        }

        public static void FlushAll()
        {
            foreach (var t in open) t.Flush();
        }

        public static void KeepRecovered()
        {
            if (Active == null) return;
            Active.Recover();
            recovery.Remove(Active);
            Notice = "Recovered the unsaved edits.";
            Changed?.Invoke();
        }

        public static void DiscardRecovered()
        {
            if (Active == null) return;
            Active.Save(backup: false);
            recovery.Remove(Active);
            Notice = "";
            Changed?.Invoke();
        }

        public static void Raise() => Changed?.Invoke();

        static void Rearranged()
        {
            foreach (var p in StrataPlug.All) p.Rearranged();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.ExitingEditMode) return;
            foreach (var t in open)
                try
                {
                    if (t.history.Recording) t.history.End();
                    if (AutoBakeOnPlay && (t.Unbaked || t.Unsaved)) t.Bake();
                }
                catch (Exception ex) { Debug.LogError($"Strata could not bake {t.Name} before Play: " + ex.Message); }
        }

        static void OnSceneSaving(Scene scene, string path)
        {
            if (!BakeOnSave) return;
            foreach (var t in open)
            {
                if (!t.instance || t.instance.scene != scene) continue;
                try { if (t.Unbaked || t.Unsaved) t.Bake(); }
                catch (Exception ex) { Debug.LogError($"Strata could not bake {t.Name} while saving: " + ex.Message); }
            }
        }

        static void OnBeforeReload()
        {
            if (open.Count == 0) return;
            var paths = new List<string>();
            foreach (var t in open)
            {
                try
                {
                    if (t.history.Recording) t.history.End();
                    t.WriteRecovery();
                    paths.Add(t.dataPath);
                }
                catch (Exception ex) { Debug.LogError(ex); }
                t.Dispose();
            }
            if (Active != null) { paths.Remove(Active.dataPath); paths.Insert(0, Active.dataPath); }
            SessionState.SetString(ReloadKey, string.Join("\n", paths));
            open.Clear();
            recovery.Clear();
            Active = null;
        }

        static void AfterReload()
        {
            string list = SessionState.GetString(ReloadKey, "");
            if (string.IsNullOrEmpty(list)) return;
            SessionState.EraseString(ReloadKey);
            var paths = list.Split('\n').Where(p => p.Length > 0 && File.Exists(p)).ToArray();
            foreach (var path in paths)
            {
                if (Find(path) != null) continue;
                try
                {
                    var t = StrataTerrain.Open(path);
                    Formats.StrataProfiles.Configure(t);
                    if (t.HasRecovery) t.AdoptRecovery();
                    open.Add(t);
                }
                catch (Exception ex) { Notice = ex.Message; }
            }
            Active = paths.Length > 0 ? Find(paths[0]) ?? open.FirstOrDefault() : open.FirstOrDefault();
            Rearranged();
            Changed?.Invoke();
        }

        static void OnQuit()
        {
            foreach (var t in open)
                try { t.WriteRecovery(); } catch (Exception) { }
        }

        static void OnScenesChanged()
        {
            if (open.Count == 0) return;
            foreach (var t in open) t.FindInstance();
            Rearranged();
            Changed?.Invoke();
        }
    }
}
