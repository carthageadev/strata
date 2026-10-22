using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Strata
{
    public static class StrataIcons
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static Texture2D Tool => GetTool(EditorGUIUtility.isProSkin);

        public static Texture2D Level => Get("level", new[]
        {
            "................",
            "................",
            "..........+++++.",
            "..........#####.",
            "..........#...#.",
            "......+++++...#.",
            "......#####...#.",
            "......#.......#.",
            "..+++++.......#.",
            "..#####.......#.",
            "..#...........#.",
            "..#...........#.",
            "..#############.",
            "................",
            "................",
            "................",
        });

        public static Texture2D Paint => Get("paint", new[]
        {
            "................",
            "...........##...",
            "..........###...",
            ".........###....",
            "........###.....",
            ".......###......",
            "......##........",
            ".....#..........",
            "...+++..........",
            "..+++++.........",
            ".+++++++........",
            ".+++++++........",
            "..+++++.........",
            "...+++..........",
            "................",
            "................",
        });

        public static Texture2D Sides => Get("sides", new[]
        {
            "................",
            "................",
            ".++++++++++++++.",
            ".+++.++++.+++++.",
            ".##############.",
            ".#.....#......#.",
            ".##############.",
            ".#..#......#..#.",
            ".##############.",
            ".#.....#......#.",
            ".##############.",
            ".#..#......#..#.",
            ".##############.",
            "................",
            "................",
            "................",
        });

        public static Texture2D Stairs => Get("stairs", new[]
        {
            "................",
            "................",
            "...........++++.",
            "...........####.",
            "...........#..#.",
            "........++++..#.",
            "........####..#.",
            "........#.....#.",
            ".....++++.....#.",
            ".....####.....#.",
            ".....#........#.",
            "..++++........#.",
            "..####........#.",
            "..#############.",
            "................",
            "................",
        });

        public static Texture2D Shape => Get("shape", new[]
        {
            "................",
            "................",
            "+..............+",
            "#..............#",
            "#..............#",
            ".#............#.",
            ".#............#.",
            "..#..........#..",
            "...##......##...",
            ".....######.....",
            "................",
            "..+..+..+..+..+.",
            "................",
            "................",
            "................",
            "................",
        });

        public static Texture2D Revert => Get("revert", new[]
        {
            "................",
            "................",
            ".....#####......",
            "...##.....##....",
            "..#.........#...",
            ".#...........#..",
            ".#...........#..",
            ".#...........#..",
            ".#........+++++.",
            "..#........+++..",
            "...##.......+...",
            ".....####.......",
            "................",
            "................",
            "................",
            "................",
        });

        public static Texture2D Gear => Get("gear", new[]
        {
            "................",
            "......####......",
            "...##.####.##...",
            "..############..",
            "...##########...",
            "..####....####..",
            "#####......#####",
            "####........####",
            "####........####",
            "#####......#####",
            "..####....####..",
            "...##########...",
            "..############..",
            "...##.####.##...",
            "......####......",
            "................",
        });

        public static Texture2D Materials => Get("materials", new[]
        {
            "................",
            "................",
            ".+++++..#####...",
            ".+++++..#####...",
            ".+++++..#####...",
            ".+++++..#####...",
            ".+++++..#####...",
            "................",
            "................",
            ".#####..+++++...",
            ".#####..+++++...",
            ".#####..+++++...",
            ".#####..+++++...",
            ".#####..+++++...",
            "................",
            "................",
        });

        static Texture2D GetTool(bool pro)
        {
            string key = pro ? "tool-d" : "tool-l";
            if (cache.TryGetValue(key, out var cached) && cached) return cached;
            const int size = 64, padding = 8, block = 8;
            const float radius = 4f;
            Color ink = pro ? new Color(0.86f, 0.86f, 0.86f, 1f) : new Color(0.2f, 0.2f, 0.2f, 1f);
            Color accent = new Color(0.95f, 0.72f, 0.3f, 1f);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
                name = "Strata tool"
            };
            float half = (size - 2 * padding) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = size - y - 0.5f;
                    float dx = Mathf.Abs(px - size * 0.5f) - half + radius;
                    float dy = Mathf.Abs(py - size * 0.5f) - half + radius;
                    float distance = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                    float alpha = Mathf.Clamp01(0.5f - distance);
                    if (px >= padding + 5 * block && py < padding + block) alpha = 0f;
                    int stripe = Mathf.Clamp(Mathf.FloorToInt((py - padding) / block), 0, 5);
                    Color color = (stripe & 1) == 0 ? accent : ink;
                    color.a = alpha;
                    texture.SetPixel(x, y, color);
                }
            texture.Apply(false, true);
            cache[key] = texture;
            return texture;
        }

        static Texture2D Get(string key, string[] art)
        {
            bool pro = EditorGUIUtility.isProSkin;
            string k = key + (pro ? "-d" : "-l");
            if (cache.TryGetValue(k, out var t) && t) return t;
            Color ink = pro ? new Color(0.86f, 0.86f, 0.86f, 1f) : new Color(0.2f, 0.2f, 0.2f, 1f);
            Color accent = new Color(0.95f, 0.72f, 0.3f, 1f);
            t = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave, name = "Strata " + key };
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    char ch = art[15 - y][x];
                    t.SetPixel(x, y, ch == '#' ? ink : ch == '+' ? accent : new Color(0, 0, 0, 0));
                }
            t.Apply(false, true);
            cache[k] = t;
            return t;
        }

        public static Texture2D For(StrataBrush b) => b switch
        {
            StrataBrush.Level => Level,
            StrataBrush.Paint => Paint,
            StrataBrush.Sides => Sides,
            StrataBrush.Stairs => Stairs,
            StrataBrush.Shape => Shape,
            _ => Revert,
        };
    }
}
