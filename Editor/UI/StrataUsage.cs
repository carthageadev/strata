using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Strata.Formats;
using UnityEditor;
using UnityEngine;

namespace Strata
{
    sealed class StrataUsage
    {
        const string OpenKey = "Strata.UsageOpen";
        static readonly Color Orange = new Color(1f, 0.62f, 0.2f), Red = new Color(1f, 0.38f, 0.32f), Accent = new Color(0.95f, 0.72f, 0.3f);

        readonly Action repaint;
        readonly bool[] tops = new bool[StrataMap.Layers], sides = new bool[256], edges = new bool[256], stairs = new bool[256];
        StrataTerrain counted;
        long countedRev = -1;
        double countedAt, flashUntil;
        string full;
        GUIStyle names, note;
        bool stylesPro;

        public StrataUsage(Action repaint) => this.repaint = repaint;

        static bool Open { get => EditorPrefs.GetBool(OpenKey, false); set => EditorPrefs.SetBool(OpenKey, value); }

        public bool Flashing => EditorApplication.timeSinceStartup < flashUntil;

        public bool Pending
        {
            get
            {
                var t = StrataSessions.Active;
                return Open && t != null && (t != counted || t.map.revision != countedRev) && !t.history.Recording;
            }
        }

        public void Focus(string kind)
        {
            Open = true;
            full = kind;
            flashUntil = EditorApplication.timeSinceStartup + 2.5;
            repaint();
        }

        void Count(StrataTerrain t)
        {
            var m = t.map;
            Array.Clear(tops, 0, tops.Length); Array.Clear(sides, 0, sides.Length); Array.Clear(edges, 0, edges.Length); Array.Clear(stairs, 0, stairs.Length);
            for (int k = 0; k < 3; k++)
            {
                if (m.splat[k] == null) continue;
                var px = MemoryMarshal.Cast<Color32, uint>(m.splat[k]);
                uint any = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    any |= px[i];
                    if ((i & 4095) == 0 && (any & 0xFF) != 0 && (any & 0xFF00) != 0 && (any & 0xFF0000) != 0 && (any & 0xFF000000) != 0) break;
                }
                for (int c = 0; c < 4; c++) tops[k * 4 + c] = ((any >> (8 * c)) & 0xFF) != 0;
            }
            foreach (var v in m.wall) if (v > 0) sides[v - 1] = true;
            foreach (var v in m.rim) if (v > 0) edges[v - 1] = true;
            foreach (var s in m.stairs) stairs[s.style] = true;
            sides[0] = edges[0] = true;
            if (m.stairs.Count > 0) sides[1] = edges[1] = true;
            counted = t; countedRev = m.revision; countedAt = EditorApplication.timeSinceStartup;
        }

        void Styles()
        {
            if (names != null && stylesPro == EditorGUIUtility.isProSkin) return;
            stylesPro = EditorGUIUtility.isProSkin;
            names = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { richText = true };
            note = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { richText = true };
        }

        public void Draw()
        {
            var t = StrataSessions.Active;
            var ts = StrataProfiles.Library(t);
            if (t == null || !ts) return;
            Styles();
            bool open = Open;
            if (open && Pending && EditorApplication.timeSinceStartup - countedAt > 1.0) Count(t);

            int nt = ts.tops.Count, ns = ts.sides.Count, ne = ts.edges.Count, nst = ts.stairs.Count;
            int left = Math.Min(StrataTileSet.MaxTops - nt, Math.Min(StrataTileSet.MaxSides - ns, StrataTileSet.MaxEdges - ne));
            Color? tint = left <= 0 ? Red : left <= 2 ? Orange : (Color?)null;

            using (var box = new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var keep = GUI.contentColor;
                    if (tint is Color c) GUI.contentColor = c;
                    bool now = EditorGUILayout.Foldout(open, new GUIContent("On this terrain", $"{t.Name} paints from {ts.name}"), true);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"{nt}/{StrataTileSet.MaxTops} tops  {ns}/{StrataTileSet.MaxSides} sides  {ne}/{StrataTileSet.MaxEdges} edges", EditorStyles.miniLabel);
                    GUI.contentColor = keep;
                    if (now != open) { Open = open = now; if (!now) full = null; }
                }
                if (open)
                {
                    bool known = counted == t;
                    Row("tops", ts.tops, tops, StrataTileSet.MaxTops, known);
                    Row("sides", ts.sides, sides, StrataTileSet.MaxSides, known);
                    Row("edges", ts.edges, edges, StrataTileSet.MaxEdges, known);
                    Row("stairs", ts.stairs, stairs, 0, known);
                    GUILayout.Space(2);
                    string say = full != null || left <= 0
                        ? $"this terrain is full on {full ?? Which(nt, ns, ne)}! remove one u dont use (grey ones arent painted yet, right click it in Materials > Remove) and u got room again. sorry, its how the paint gets saved for now, maybe ill lift it later"
                        : "heads up, one terrain can use 12 tops, 15 sides and 15 edges max, its how the paint gets saved for now. stairs got no limit. grey ones arent painted yet";
                    GUILayout.Label(tint is Color nc && (full != null || left <= 0) ? $"<color=#{ColorUtility.ToHtmlStringRGB(nc)}>{say}</color>" : say, note);
                }
                if (Flashing && Event.current.type == EventType.Repaint)
                {
                    var r = box.rect;
                    var a = new Color(Accent.r, Accent.g, Accent.b, Mathf.PingPong((float)EditorApplication.timeSinceStartup * 4f, 1f));
                    EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 2), a);
                    EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2, r.width, 2), a);
                    EditorGUI.DrawRect(new Rect(r.x, r.y, 2, r.height), a);
                    EditorGUI.DrawRect(new Rect(r.xMax - 2, r.y, 2, r.height), a);
                }
            }
        }

        static string Which(int nt, int ns, int ne) =>
            string.Join(" and ", new[] { nt >= StrataTileSet.MaxTops ? "tops" : null, ns >= StrataTileSet.MaxSides ? "sides" : null, ne >= StrataTileSet.MaxEdges ? "edges" : null }.Where(x => x != null));

        void Row<T>(string kind, List<T> items, bool[] painted, int max, bool known) where T : UnityEngine.Object
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(max > 0 ? $"{kind} {items.Count}/{max}" : $"{kind} {items.Count}", EditorStyles.miniBoldLabel, GUILayout.Width(74));
                var sb = new StringBuilder();
                for (int i = 0; i < items.Count; i++)
                {
                    if (!items[i]) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    string title = items[i] is StrataMaterial m ? m.Title : items[i] is StrataStairType s ? s.Title : items[i].name;
                    bool used = !known || i < painted.Length && painted[i];
                    sb.Append(used ? title : $"<color=#808080>{title}</color>");
                }
                GUILayout.Label(sb.Length > 0 ? sb.ToString() : "<color=#808080>none</color>", names);
            }
        }
    }
}
