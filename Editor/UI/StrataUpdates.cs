using System;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Networking;

namespace Strata
{
    [InitializeOnLoad]
    static class StrataUpdates
    {
        const string Repo = "carthageadev/strata";
        const string Off = "Strata.UpdatesOff", Asked = "Strata.UpdateAsked";

        public static bool Enabled { get => !EditorPrefs.GetBool(Off, false); set => EditorPrefs.SetBool(Off, !value); }

        [SettingsProvider]
        static SettingsProvider Page() => new SettingsProvider("Preferences/Strata/Updates", SettingsScope.User)
        {
            keywords = new[] { "Strata", "update", "version" },
            guiHandler = _ =>
            {
                GUILayout.Space(10);
                Enabled = EditorGUILayout.Toggle(new GUIContent("Check for updates on start", "Once per session, asks before updating"), Enabled);
                GUILayout.Space(6);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Version " + Version, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                    if (GUILayout.Button("Check now", EditorStyles.miniButton, GUILayout.Width(80))) Check(true);
                }
            },
        };

        static StrataUpdates()
        {
            if (Application.isBatchMode || !Enabled || SessionState.GetBool(Asked, false)) return;
            SessionState.SetBool(Asked, true);
            EditorApplication.delayCall += () => Check(false);
        }

        public static string Root => Path.GetDirectoryName(Path.GetDirectoryName(CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName("Strata.Editor"))).Replace('\\', '/');

        public static string Version
        {
            get
            {
                try { return JsonUtility.FromJson<Manifest>(File.ReadAllText(Root + "/package.json")).version; }
                catch { return "0.0.0"; }
            }
        }

        [Serializable] class Manifest { public string version; }
        [Serializable] class Release { public string tag_name, html_url; public Asset[] assets; }
        [Serializable] class Asset { public string name, browser_download_url; }

        public static void Check(bool byHand)
        {
            var req = UnityWebRequest.Get($"https://api.github.com/repos/{Repo}/releases/latest");
            req.SetRequestHeader("User-Agent", "Strata");
            req.timeout = 15;
            req.SendWebRequest().completed += _ =>
            {
                try
                {
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        if (byHand) EditorUtility.DisplayDialog("Strata", "Could not reach GitHub to check for updates.", "OK");
                        return;
                    }
                    var r = JsonUtility.FromJson<Release>(req.downloadHandler.text);
                    string latest = (r?.tag_name ?? "").TrimStart('v'), mine = Version;
                    if (Newer(latest, mine)) Offer(r, latest, mine);
                    else if (byHand) EditorUtility.DisplayDialog("Strata", $"Strata {mine} is the latest.", "OK");
                }
                catch (Exception ex) { if (byHand) Debug.LogWarning("Strata: update check failed: " + ex.Message); }
                finally { req.Dispose(); }
            };
        }

        static void Offer(Release r, string latest, string mine)
        {
            int pick = EditorUtility.DisplayDialogComplex("Strata update", $"Strata {latest} is out. You have {mine}.\n\nUpdate now?", "Update", "Not now", "Don't ask again");
            if (pick == 2) Enabled = false;
            if (pick != 0) return;

            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(Root + "/package.json");
            if (info != null && info.source == PackageSource.Git)
            {
                Client.Add($"https://github.com/{Repo}.git#v{latest}");
                return;
            }
            string url = null;
            foreach (var a in r.assets ?? new Asset[0]) if (a.name.EndsWith(".unitypackage")) url = a.browser_download_url;
            if (info != null || url == null) { Application.OpenURL(r.html_url); return; }

            string file = Path.Combine(Path.GetTempPath(), $"Strata-{latest}.unitypackage");
            var get = new UnityWebRequest(url) { downloadHandler = new DownloadHandlerFile(file) };
            get.SendWebRequest().completed += _ =>
            {
                bool ok = get.result == UnityWebRequest.Result.Success;
                get.Dispose();
                if (ok) AssetDatabase.ImportPackage(file, true);
                else Application.OpenURL(r.html_url);
            };
        }

        static bool Newer(string a, string b)
        {
            int[] Parse(string v)
            {
                var p = v.Split('-')[0].Split('.');
                var n = new int[3];
                for (int i = 0; i < 3 && i < p.Length; i++) int.TryParse(p[i], out n[i]);
                return n;
            }
            var x = Parse(a); var y = Parse(b);
            for (int i = 0; i < 3; i++) if (x[i] != y[i]) return x[i] > y[i];
            return false;
        }
    }
}
