using System;
using UnityEditor;
using UnityEngine;

namespace Strata
{
    public enum StrataBrush { Level, Paint, Sides, Stairs, Shape, Revert }

    public enum SnapMode { Cell, Quarter, Half, Block, Custom }

    public static class StrataSettings
    {
        static int I(string k, int d) => EditorPrefs.GetInt("Strata." + k, d);
        static float F(string k, float d) => EditorPrefs.GetFloat("Strata." + k, d);
        static bool B(string k, bool d) => EditorPrefs.GetBool("Strata." + k, d);
        static void Set(string k, int v) => EditorPrefs.SetInt("Strata." + k, v);
        static void Set(string k, float v) => EditorPrefs.SetFloat("Strata." + k, v);
        static void Set(string k, bool v) => EditorPrefs.SetBool("Strata." + k, v);

        public static StrataBrush Brush
        {
            get { var b = (StrataBrush)Mathf.Clamp(I("Brush", 0), 0, 5); return b == StrataBrush.Sides ? StrataBrush.Level : b; }
            set => Set("Brush", (int)value);
        }

        public static bool ApplyHeight { get => B("ApplyHeight", true); set => Set("ApplyHeight", value); }
        public static bool ApplyTop { get => B("ApplyTop", false); set => Set("ApplyTop", value); }
        public static bool ApplySides { get => B("ApplySides", true); set => Set("ApplySides", value); }

        public static float Level { get => F("Level", 0f); set => Set("Level", Mathf.Clamp(value, -1000f, 1000f)); }
        public static float Step { get => Mathf.Max(0.05f, F("Step", 1f)); set => Set("Step", value); }
        public static int Size { get => Mathf.Clamp(I("Size", 3), 1, MaxSize); set => Set("Size", Mathf.Clamp(value, 1, MaxSize)); }
        public static bool Round { get => B("Round", false); set => Set("Round", value); }
        public static int Wall { get => I("Wall", 1); set => Set("Wall", value); }
        public static int Rim { get => I("Rim", 1); set => Set("Rim", value); }

        public static SnapMode Snap { get => (SnapMode)Mathf.Clamp(I("Snap", 0), 0, 4); set => Set("Snap", (int)value); }
        public static int SnapCustom { get => Mathf.Clamp(I("SnapCustom", 4), 1, 1024); set => Set("SnapCustom", Mathf.Clamp(value, 1, 1024)); }

        public static int Layer { get => I("Layer", 0); set => Set("Layer", value); }
        public static float Radius { get => Mathf.Clamp(F("Radius", 2f), 0.25f, 64f); set => Set("Radius", Mathf.Clamp(value, 0.25f, 64f)); }
        public static float Strength { get => F("Strength", 0.5f); set => Set("Strength", value); }
        public static float Hardness { get => F("Hardness", 0.5f); set => Set("Hardness", value); }

        public static bool Timber { get => B("Timber", false); set => Set("Timber", value); }
        public static int StairType { get => Mathf.Clamp(I("StairType", Timber ? 1 : 0), 0, 255); set => Set("StairType", value); }
        public static bool CheekWalls { get => B("CheekWalls", true); set => Set("CheekWalls", value); }
        public static bool StairCustom { get => B("StairCustom", false); set => Set("StairCustom", value); }
        public static float StairRise { get => Mathf.Clamp(F("StairRise", 1f), 0.25f, 100f); set => Set("StairRise", Mathf.Clamp(value, 0.25f, 100f)); }

        public static ShapeProfile Shape { get => (ShapeProfile)Mathf.Clamp(I("Shape", 0), 0, 4); set => Set("Shape", (int)value); }
        public static float ShapeAmount { get => F("ShapeAmount", 3f); set => Set("ShapeAmount", value); }

        public static bool RevertHeights { get => B("RevertHeights", true); set => Set("RevertHeights", value); }
        public static bool RevertPaint { get => B("RevertPaint", false); set => Set("RevertPaint", value); }

        public static int MaxSize { get => Mathf.Clamp(I("MaxSize", 256), 8, 1024); set => Set("MaxSize", Mathf.Clamp(value, 8, 1024)); }
        public static bool MeetNatural { get => B("MeetNatural", true); set => Set("MeetNatural", value); }
        public static bool Labels { get => B("Labels", true); set => Set("Labels", value); }
        public static int BackupKeep { get => Mathf.Clamp(I("BackupKeep", 30), 1, 500); set => Set("BackupKeep", value); }
        public static int BackupMinutes { get => Mathf.Clamp(I("BackupMinutes", 10), 0, 600); set => Set("BackupMinutes", value); }

        public static bool FollowGround { get => B("FollowGround", false); set => Set("FollowGround", value); }
        public static float FollowTolerance { get => Mathf.Clamp(F("FollowTolerance", 0.3f), 0.01f, 10f); set => Set("FollowTolerance", value); }
        public static float FollowMaxSize { get => Mathf.Clamp(F("FollowMaxSize", 24f), 1f, 1000f); set => Set("FollowMaxSize", value); }

        public static void ResetPreferences()
        {
            foreach (var k in new[] { "MaxSize", "MeetNatural", "Labels", "BackupKeep", "BackupMinutes", "FollowGround", "FollowTolerance", "FollowMaxSize", "AutoBakeOnPlay", "BakeOnSave" })
                EditorPrefs.DeleteKey("Strata." + k);
        }

        public static float StepLevel(int dir)
        {
            float s = Step;
            float l = Level;
            float k = Mathf.Round(l / s);
            if (Mathf.Abs(l - k * s) > 1e-3f) k = dir > 0 ? Mathf.Ceil(l / s) - 1 : Mathf.Floor(l / s) + 1;
            return (k + dir) * s;
        }

        public static int SnapCells(int size) => Snap switch
        {
            SnapMode.Quarter => Math.Max(1, (int)Math.Round(size / 4.0)),
            SnapMode.Half => Math.Max(1, (int)Math.Round(size / 2.0)),
            SnapMode.Block => Math.Max(1, size),
            SnapMode.Custom => SnapCustom,
            _ => 1,
        };

        public static float MeetDistance => MeetNatural ? Mathf.Min(StrataEdits.Sliver, 0.45f * Step) : 0f;
    }
}
