using System;
using UnityEditor;
using UnityEngine;

namespace Strata
{
    static class StrataPreferences
    {
        public const string Path = "Preferences/Strata";

        [SettingsProvider]
        static SettingsProvider Create() => new SettingsProvider(Path, SettingsScope.User)
        {
            label = "Strata",
            keywords = new[] { "Strata", "terrain", "bake", "backup", "brush", "label", "follow", "ground", "experimental" },
            guiHandler = _ => Draw(),
        };

        static void Draw()
        {
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 230;
            EditorGUI.BeginChangeCheck();

            Header("Baking");
            Toggle("Bake before Play", null, () => StrataSessions.AutoBakeOnPlay, v => StrataSessions.AutoBakeOnPlay = v);
            Toggle("Bake when the scene is saved", null, () => StrataSessions.BakeOnSave, v => StrataSessions.BakeOnSave = v);

            Header("Backups");
            Int("Backups kept per terrain", "In StrataBackups/ beside the project", 1, 200, () => StrataSettings.BackupKeep, v => StrataSettings.BackupKeep = v);
            Int("Minutes between backups", "A bake backs the terrain up at most this often (0: every bake)", 0, 120, () => StrataSettings.BackupMinutes, v => StrataSettings.BackupMinutes = v);

            Header("Brushes");
            Int("Largest brush (cells)", null, 8, 1024, () => StrataSettings.MaxSize, v => StrataSettings.MaxSize = v);
            Toggle("Meet natural ground", "Natural ground beside a painted level comes level with it when it is within about half a level step (at most 0.45 m), so no thin wall is left",
                () => StrataSettings.MeetNatural, v => StrataSettings.MeetNatural = v);
            Toggle("Labels in the Scene view", null, () => StrataSettings.Labels, v => StrataSettings.Labels = v);

            Header("Experimental");
            Toggle("Objects follow the ground", "Props, trees, buildings and characters standing on ground a stroke raises or lowers move with it. The moves undo with the stroke.",
                () => StrataSettings.FollowGround, v => StrataSettings.FollowGround = v);
            using (new EditorGUI.DisabledScope(!StrataSettings.FollowGround))
            {
                EditorGUI.indentLevel++;
                Float("Standing within (m)", "How close an object's base must be to the ground to count as standing on it", 0.05f, 2f, () => StrataSettings.FollowTolerance, v => StrataSettings.FollowTolerance = v);
                Float("Largest object (m)", "Wider objects stay put (water, whole groups) and their parts are checked one by one. Width is taken from colliders where there are any.", 2f, 200f, () => StrataSettings.FollowMaxSize, v => StrataSettings.FollowMaxSize = v);
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(14);
            if (GUILayout.Button("Reset to defaults", GUILayout.Width(140))) StrataSettings.ResetPreferences();
            if (EditorGUI.EndChangeCheck()) { StrataSessions.Raise(); SceneView.RepaintAll(); }
            EditorGUIUtility.labelWidth = labelWidth;
        }

        static void Header(string text)
        {
            GUILayout.Space(10);
            GUILayout.Label(text, EditorStyles.boldLabel);
        }

        static void Toggle(string label, string tip, Func<bool> get, Action<bool> set)
        {
            bool v = get(), n = EditorGUILayout.Toggle(new GUIContent(label, tip), v);
            if (n != v) set(n);
        }

        static void Int(string label, string tip, int min, int max, Func<int> get, Action<int> set)
        {
            int v = get(), n = EditorGUILayout.IntSlider(new GUIContent(label, tip), v, min, max);
            if (n != v) set(n);
        }

        static void Float(string label, string tip, float min, float max, Func<float> get, Action<float> set)
        {
            float v = get(), n = EditorGUILayout.Slider(new GUIContent(label, tip), v, min, max);
            if (n != v) set(n);
        }
    }
}
