using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Strata
{
    public sealed class StrataHistory : IDisposable
    {
        const int Block = 32;
        const long Budget = 384L * 1024 * 1024;

        sealed class CellBlock
        {
            public int bx, by, c0, r0, c1, r1;
            public float[] corner; public byte[] kind, wall, rim; public short[] stairOf;
        }

        sealed class PaintBlock
        {
            public int bx, by, x0, y0, x1, y1;
            public Color32[][] planes;
        }

        sealed class Step
        {
            public string name;
            public readonly Dictionary<long, CellBlock> cellsBefore = new Dictionary<long, CellBlock>(), cellsAfter = new Dictionary<long, CellBlock>();
            public readonly Dictionary<long, PaintBlock> paintBefore = new Dictionary<long, PaintBlock>(), paintAfter = new Dictionary<long, PaintBlock>();
            public List<Flight> flightsBefore, flightsAfter;
            public List<Stair> stairsBefore, stairsAfter;
            public long bytes;
        }

        public sealed class Token : ScriptableObject { public int step; }

        readonly StrataMap map;
        readonly Action<RectInt, RectInt, bool> changed;
        readonly List<Step> steps = new List<Step>();
        int applied;
        int baseStep;
        Step open;
        Token token;
        long bytes;

        public bool Recording => open != null;
        public string RecordingName => open?.name;

        public StrataHistory(StrataMap map, Action<RectInt, RectInt, bool> changed)
        {
            this.map = map;
            this.changed = changed;
            token = ScriptableObject.CreateInstance<Token>();
            token.hideFlags = HideFlags.HideAndDontSave;
            token.name = "Strata terrain edit";
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        public void Dispose()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (token) { Undo.ClearUndo(token); UnityEngine.Object.DestroyImmediate(token); }
            steps.Clear();
        }

        public void Begin(string name)
        {
            if (open != null) End();
            open = new Step { name = name };
        }

        //! Capture cell, paint and stair snapshots before mutating a stroke.
        public void TouchCells(int c0, int r0, int c1, int r1)
        {
            if (open == null) return;
            c0 = Math.Max(0, c0); r0 = Math.Max(0, r0); c1 = Math.Min(map.w, c1); r1 = Math.Min(map.h, r1);
            for (int by = r0 / Block; by <= (r1 - 1) / Block; by++)
                for (int bx = c0 / Block; bx <= (c1 - 1) / Block; bx++)
                {
                    long key = ((long)by << 32) | (uint)bx;
                    if (!open.cellsBefore.ContainsKey(key)) open.cellsBefore[key] = CopyCells(bx, by);
                }
        }

        public void TouchPaint(int x0, int y0, int x1, int y1)
        {
            if (open == null) return;
            int B = Block * map.splatRes;
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(map.splatW, x1); y1 = Math.Min(map.splatH, y1);
            for (int by = y0 / B; by <= (y1 - 1) / B; by++)
                for (int bx = x0 / B; bx <= (x1 - 1) / B; bx++)
                {
                    long key = ((long)by << 32) | (uint)bx;
                    if (!open.paintBefore.ContainsKey(key)) open.paintBefore[key] = CopyPaint(bx, by);
                }
        }

        public void TouchStairs()
        {
            if (open == null || open.flightsBefore != null) return;
            open.flightsBefore = CloneFlights(map.flights);
            open.stairsBefore = CloneStairs(map.stairs);
        }

        public void Cancel() { open = null; }

        public void End(Action alongside = null)
        {
            var s = open;
            open = null;
            if (s == null) return;
            if (s.cellsBefore.Count == 0 && s.paintBefore.Count == 0 && s.flightsBefore == null) return;
            foreach (var kv in s.cellsBefore) { var b = kv.Value; s.cellsAfter[kv.Key] = CopyCells(b.bx, b.by); }
            foreach (var kv in s.paintBefore) { var b = kv.Value; s.paintAfter[kv.Key] = CopyPaint(b.bx, b.by); }
            if (s.flightsBefore != null) { s.flightsAfter = CloneFlights(map.flights); s.stairsAfter = CloneStairs(map.stairs); }
            s.bytes = (s.cellsBefore.Count * 2L) * Block * Block * 21 + (s.paintBefore.Count * 2L) * Block * Block * 48;

            for (int k = steps.Count - 1; k >= applied; k--) { bytes -= steps[k].bytes; steps.RemoveAt(k); }
            steps.Add(s);
            applied = steps.Count;
            bytes += s.bytes;
            while (bytes > Budget && steps.Count > 1) { bytes -= steps[0].bytes; steps.RemoveAt(0); applied--; baseStep++; }

            Undo.IncrementCurrentGroup();
            Undo.RecordObject(token, "Strata: " + s.name);
            token.step = baseStep + applied;
            if (alongside != null)
            {
                try { alongside(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
            Undo.SetCurrentGroupName("Strata: " + s.name);
            Undo.IncrementCurrentGroup();
        }

        void OnUndoRedo()
        {
            if (!token) return;
            int want = token.step - baseStep;
            if (want == applied) return;
            if (open != null) { var s = open; open = null; Apply(s, before: true); }
            want = Math.Max(0, Math.Min(steps.Count, want));
            while (applied > want) { Apply(steps[applied - 1], before: true); applied--; }
            while (applied < want) { Apply(steps[applied], before: false); applied++; }
        }

        void Apply(Step s, bool before)
        {
            var cells = before ? s.cellsBefore : s.cellsAfter;
            var paint = before ? s.paintBefore : s.paintAfter;
            RectInt cr = default, pr = default;
            bool any = false, anyP = false;
            foreach (var b in cells.Values) { PasteCells(b); cr = any ? Union(cr, new RectInt(b.c0, b.r0, b.c1 - b.c0, b.r1 - b.r0)) : new RectInt(b.c0, b.r0, b.c1 - b.c0, b.r1 - b.r0); any = true; }
            foreach (var b in paint.Values) { PastePaint(b); pr = anyP ? Union(pr, new RectInt(b.x0, b.y0, b.x1 - b.x0, b.y1 - b.y0)) : new RectInt(b.x0, b.y0, b.x1 - b.x0, b.y1 - b.y0); anyP = true; }
            bool stairs = s.flightsBefore != null;
            if (stairs)
            {
                map.flights = CloneFlights(before ? s.flightsBefore : s.flightsAfter);
                map.stairs = CloneStairs(before ? s.stairsBefore : s.stairsAfter);
            }
            map.revision++;
            changed?.Invoke(any ? cr : new RectInt(0, 0, 0, 0), anyP ? pr : new RectInt(0, 0, 0, 0), stairs);
        }

        static RectInt Union(RectInt a, RectInt b)
        {
            int x0 = Math.Min(a.xMin, b.xMin), y0 = Math.Min(a.yMin, b.yMin), x1 = Math.Max(a.xMax, b.xMax), y1 = Math.Max(a.yMax, b.yMax);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        CellBlock CopyCells(int bx, int by)
        {
            int c0 = bx * Block, r0 = by * Block, c1 = Math.Min(map.w, c0 + Block), r1 = Math.Min(map.h, r0 + Block);
            int n = (c1 - c0) * (r1 - r0);
            var b = new CellBlock { bx = bx, by = by, c0 = c0, r0 = r0, c1 = c1, r1 = r1, corner = new float[n * 4], kind = new byte[n], wall = new byte[n], rim = new byte[n], stairOf = new short[n] };
            int k = 0;
            for (int r = r0; r < r1; r++)
                for (int c = c0; c < c1; c++, k++)
                {
                    int i = r * map.w + c;
                    Array.Copy(map.corner, i * 4, b.corner, k * 4, 4);
                    b.kind[k] = map.kind[i]; b.wall[k] = map.wall[i]; b.rim[k] = map.rim[i]; b.stairOf[k] = map.stairOf[i];
                }
            return b;
        }

        void PasteCells(CellBlock b)
        {
            int k = 0;
            for (int r = b.r0; r < b.r1; r++)
                for (int c = b.c0; c < b.c1; c++, k++)
                {
                    int i = r * map.w + c;
                    Array.Copy(b.corner, k * 4, map.corner, i * 4, 4);
                    map.kind[i] = b.kind[k]; map.wall[i] = b.wall[k]; map.rim[i] = b.rim[k]; map.stairOf[i] = b.stairOf[k];
                }
        }

        PaintBlock CopyPaint(int bx, int by)
        {
            int B = Block * map.splatRes;
            int x0 = bx * B, y0 = by * B, x1 = Math.Min(map.splatW, x0 + B), y1 = Math.Min(map.splatH, y0 + B);
            int n = (x1 - x0) * (y1 - y0);
            var b = new PaintBlock { bx = bx, by = by, x0 = x0, y0 = y0, x1 = x1, y1 = y1, planes = new Color32[3][] };
            for (int p = 0; p < 3; p++)
            {
                var a = b.planes[p] = new Color32[n];
                int k = 0;
                for (int y = y0; y < y1; y++) { Array.Copy(map.splat[p], y * map.splatW + x0, a, k, x1 - x0); k += x1 - x0; }
            }
            return b;
        }

        void PastePaint(PaintBlock b)
        {
            for (int p = 0; p < 3; p++)
            {
                int k = 0, w = b.x1 - b.x0;
                for (int y = b.y0; y < b.y1; y++) { Array.Copy(b.planes[p], k, map.splat[p], y * map.splatW + b.x0, w); k += w; }
            }
        }

        static List<Flight> CloneFlights(List<Flight> f) => new List<Flight>(f);
        static List<Stair> CloneStairs(List<Stair> s) { var o = new List<Stair>(s.Count); foreach (var x in s) o.Add(x.Clone()); return o; }
    }
}
