using System;
using UnityEngine;

namespace Strata
{
    public sealed class StrataPalette
    {
        public string[] layers = new string[0];
        public Texture2D[] thumbs = new Texture2D[0];
        public (int index, string name)[] walls = new (int, string)[0];
        public (int index, string name)[] rims = new (int, string)[0];
        public (int index, string name, bool timber)[] stairs = new (int, string, bool)[0];
        public string[] stairSides;
        public Color[] wallColors = new Color[4], rimColors = new Color[4];
        public StrataTileSet library;
        public int stamp;

        public string StairName(int index) { foreach (var s in stairs) if (s.index == index) return s.name; return stairs.Length > 0 ? stairs[0].name : "Stone"; }
        public bool StairTimber(int index) { foreach (var s in stairs) if (s.index == index) return s.timber; return false; }
        public string SidesLabel(int index) => stairSides != null && index >= 0 && index < stairSides.Length ? stairSides[index] : StairTimber(index) ? null : "Cheek walls";

        public string WallName(int index) { foreach (var w in walls) if (w.index == index) return w.name; return walls.Length > 0 ? walls[0].name : "Wall"; }
        public string RimName(int index) { foreach (var r in rims) if (r.index == index) return r.name; return rims.Length > 0 ? rims[0].name : "Rim"; }

        public static Texture2D[] Thumbs(Texture2DArray array, int count)
        {
            var o = new Texture2D[count];
            if (!array) return o;
            int mip = 0;
            while (mip < array.mipmapCount - 1 && (array.width >> mip) > 32) mip++;
            int size = Math.Max(1, array.width >> mip), sizeY = Math.Max(1, array.height >> mip);
            for (int k = 0; k < Math.Min(count, array.depth); k++)
            {
                try
                {
                    bool srgb = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(array.graphicsFormat);
                    var t = new Texture2D(size, sizeY, array.format, false, !srgb) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave, name = "Strata thumb" };
                    Graphics.CopyTexture(array, k, mip, t, 0, 0);
                    o[k] = t;
                }
                catch (Exception) { o[k] = null; }
            }
            return o;
        }

        public void Release()
        {
            foreach (var t in thumbs) if (t) UnityEngine.Object.DestroyImmediate(t);
            thumbs = new Texture2D[0];
        }
    }
}
