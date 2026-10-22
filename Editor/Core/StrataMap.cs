using System;
using System.Collections.Generic;
using UnityEngine;

namespace Strata
{
    public enum CellKind : byte
    {
        Natural = 0,
        Raised = 1,
        Sunken = 2,
        Basin = 3,
        Stair = 4,
    }

    public enum StairDir : byte { Landing = 0, N = 1, E = 2, S = 3, W = 4 }

    [Serializable]
    public struct Flight
    {
        public int x0, x1, z0, z1;
        public StairDir dir;
        public float y0, y1;
        public int steps;
        public int stair;
        public float low;
        public float[] ground;

        public int Width => x1 - x0;
        public int Depth => z1 - z0;

        public static (int dx, int dz) Step(StairDir d) => d switch
        {
            StairDir.N => (0, -1),
            StairDir.S => (0, 1),
            StairDir.E => (1, 0),
            StairDir.W => (-1, 0),
            _ => (0, 0),
        };
    }

    [Serializable]
    public sealed class Stair
    {
        public string id = "stair";
        public byte style;
        public bool walls = true;

        public Stair Clone() => new Stair { id = id, style = style, walls = walls };
    }

    //! Grid +z is south; Unity uses -z.
    public sealed class StrataMap
    {
        public const int Layers = 12;
        //! 25cm per step, all the stairs and the player step offset count on this, dont change it
        public const float Rise = 0.25f;

        public string name = "Terrain";
        public string profile = "basic";
        public bool WideCodes => profile == "basic";
        public string tileSet = "";
        public int x0, z0, w, h;
        public int bakeChunk = 64;

        //! Cell corner order: NW, NE, SE, SW.
        public float[] corner;
        public byte[] kind;
        public byte[] wall;
        public byte[] rim;
        public short[] stairOf;
        public float[] natural;
        public List<Flight> flights = new List<Flight>();
        public List<Stair> stairs = new List<Stair>();

        public int splatRes = 2;
        public int splatW, splatH;
        //! Three RGBA planes store 12 paint layers.
        public Color32[][] splat = new Color32[3][];

        public long revision, bakedRevision;

        public int Cells => w * h;

        public static StrataMap Create(string name, int w, int h, int x0, int z0, float level = 0f, byte groundLayer = 0)
        {
            var m = new StrataMap { name = name, w = w, h = h, x0 = x0, z0 = z0 };
            m.Allocate();
            for (int i = 0; i < m.corner.Length; i++) m.corner[i] = level;
            for (int i = 0; i < m.natural.Length; i++) m.natural[i] = level;
            for (int k = 0; k < 3; k++)
            {
                var p = m.splat[k];
                for (int i = 0; i < p.Length; i++) p[i] = new Color32(0, 0, 0, 0);
            }
            m.SetLayerEverywhere(groundLayer);
            return m;
        }

        public void Allocate()
        {
            int n = w * h;
            corner = new float[n * 4];
            kind = new byte[n];
            wall = new byte[n];
            rim = new byte[n];
            stairOf = new short[n];
            for (int i = 0; i < n; i++) stairOf[i] = -1;
            natural = new float[(w + 1) * (h + 1)];
            splatW = w * splatRes; splatH = h * splatRes;
            for (int k = 0; k < 3; k++) splat[k] = new Color32[splatW * splatH];
        }

        void SetLayerEverywhere(byte layer)
        {
            int plane = layer / 4, ch = layer % 4;
            var p = splat[plane];
            for (int i = 0; i < p.Length; i++) { var c = p[i]; Set(ref c, ch, 255); p[i] = c; }
        }

        public bool InGrid(int c, int r) => c >= 0 && r >= 0 && c < w && r < h;
        public int Index(int c, int r) => r * w + c;

        public int CellAt(double x, double z)
        {
            int c = (int)Math.Floor(x - x0), r = (int)Math.Floor(z - z0);
            return InGrid(c, r) ? r * w + c : -1;
        }

        public float Top(int i) { int o = i * 4; return Mathf.Max(Mathf.Max(corner[o], corner[o + 1]), Mathf.Max(corner[o + 2], corner[o + 3])); }
        public float Bottom(int i) { int o = i * 4; return Mathf.Min(Mathf.Min(corner[o], corner[o + 1]), Mathf.Min(corner[o + 2], corner[o + 3])); }

        public bool IsFlat(int i)
        {
            int o = i * 4;
            return Math.Abs(corner[o] - corner[o + 1]) < 1e-4f && Math.Abs(corner[o] - corner[o + 2]) < 1e-4f && Math.Abs(corner[o] - corner[o + 3]) < 1e-4f;
        }

        public float Natural(int vc, int vr) => natural[vr * (w + 1) + vc];
        public float NaturalAtCell(int c, int r) => 0.25f * (Natural(c, r) + Natural(c + 1, r) + Natural(c + 1, r + 1) + Natural(c, r + 1));

        public Flight? FlightOf(int i) { int f = stairOf[i]; return f >= 0 && f < flights.Count ? flights[f] : (Flight?)null; }

        public double Height(double x, double z)
        {
            int i = CellAt(x, z);
            if (i < 0) return 0;
            if (kind[i] == (byte)CellKind.Stair && FlightOf(i) is Flight f) return StairHeight(f, x, z);
            double fx = x - x0 - Math.Floor(x - x0), fz = z - z0 - Math.Floor(z - z0);
            int o = i * 4;
            double top = corner[o] + ((double)corner[o + 1] - corner[o]) * fx, bot = corner[o + 3] + ((double)corner[o + 2] - corner[o + 3]) * fx;
            return top + (bot - top) * fz;
        }

        public static double StairHeight(Flight f, double x, double z)
        {
            if (f.dir == StairDir.Landing || f.steps <= 0) return f.y0;
            var (dx, dz) = Flight.Step(f.dir);
            double len = dx != 0 ? f.x1 - f.x0 : f.z1 - f.z0;
            double along = dx > 0 ? x - f.x0 : dx < 0 ? f.x1 - x : dz > 0 ? z - f.z0 : f.z1 - z;
            double t = Math.Min(Math.Max(along / len, 0), 0.99999);
            int k = (int)Math.Floor(t * f.steps);
            return f.y0 + ((double)f.y1 - f.y0) * (k + 1) / f.steps;
        }

        public static byte Get(Color32 c, int ch) => ch == 0 ? c.r : ch == 1 ? c.g : ch == 2 ? c.b : c.a;
        public static void Set(ref Color32 c, int ch, byte v) { if (ch == 0) c.r = v; else if (ch == 1) c.g = v; else if (ch == 2) c.b = v; else c.a = v; }

        public byte Weight(int texel, int layer) => Get(splat[layer >> 2][texel], layer & 3);
        public void SetWeight(int texel, int layer, byte v) { var p = splat[layer >> 2]; var c = p[texel]; Set(ref c, layer & 3, v); p[texel] = c; }

        public int TopLayer(int texel)
        {
            int best = 0, bw = -1;
            for (int k = 0; k < Layers; k++) { int v = Weight(texel, k); if (v > bw) { bw = v; best = k; } }
            return best;
        }

        public StrataMap Clone()
        {
            var m = (StrataMap)MemberwiseClone();
            m.corner = (float[])corner.Clone();
            m.kind = (byte[])kind.Clone();
            m.wall = (byte[])wall.Clone();
            m.rim = (byte[])rim.Clone();
            m.stairOf = (short[])stairOf.Clone();
            m.natural = (float[])natural.Clone();
            m.flights = new List<Flight>(flights);
            m.stairs = new List<Stair>(); foreach (var s in stairs) m.stairs.Add(s.Clone());
            m.splat = new Color32[3][];
            for (int k = 0; k < 3; k++) m.splat[k] = (Color32[])splat[k].Clone();
            return m;
        }

        public StrataMap CloneHeights()
        {
            var m = (StrataMap)MemberwiseClone();
            m.corner = (float[])corner.Clone();
            m.kind = (byte[])kind.Clone();
            m.stairOf = (short[])stairOf.Clone();
            m.flights = new List<Flight>(flights);
            return m;
        }

        public void Validate()
        {
            if (w <= 0 || h <= 0 || w > 8192 || h > 8192) throw new InvalidOperationException($"Strata: bad grid size {w} x {h}");
            int n = w * h;
            if (corner == null || corner.Length != n * 4) throw new InvalidOperationException("Strata: corner heights do not match the grid");
            if (kind?.Length != n || wall?.Length != n || rim?.Length != n || stairOf?.Length != n) throw new InvalidOperationException("Strata: cell data does not match the grid");
            if (natural == null || natural.Length != (w + 1) * (h + 1)) throw new InvalidOperationException("Strata: natural ground does not match the grid");
            if (splatW != w * splatRes || splatH != h * splatRes) throw new InvalidOperationException("Strata: ground paint does not match the grid");
            for (int k = 0; k < 3; k++) if (splat[k] == null || splat[k].Length != splatW * splatH) throw new InvalidOperationException("Strata: ground paint is incomplete");
            for (int i = 0; i < corner.Length; i++) if (float.IsNaN(corner[i]) || float.IsInfinity(corner[i]) || Math.Abs(corner[i]) > 10000f) throw new InvalidOperationException($"Strata: impossible height at cell {i / 4}");
            for (int i = 0; i < n; i++)
            {
                if (kind[i] > (byte)CellKind.Stair) throw new InvalidOperationException($"Strata: unknown cell kind at cell {i}");
                if (stairOf[i] >= flights.Count) throw new InvalidOperationException($"Strata: cell {i} points at a missing stair flight");
                if ((kind[i] == (byte)CellKind.Stair) != (stairOf[i] >= 0)) throw new InvalidOperationException($"Strata: cell {i} is half a stair");
            }
            foreach (var f in flights) if (f.stair < 0 || f.stair >= stairs.Count) throw new InvalidOperationException("Strata: a flight belongs to a missing stair");
            if (bakeChunk < 8 || bakeChunk > 256) bakeChunk = 64;
        }
    }
}
