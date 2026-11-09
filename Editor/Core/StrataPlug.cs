using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Strata
{
    public abstract class StrataPlug
    {
        static StrataPlug[] all;

        public static StrataPlug[] All => all ??= TypeCache.GetTypesDerivedFrom<StrataPlug>()
            .Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => (StrataPlug)Activator.CreateInstance(t)).ToArray();

        public static bool Multi => All.Any(p => p.MultiTerrain);

        public static IStrataWallShape ShapeFor(StrataTerrain t)
        {
            foreach (var p in All) { var s = p.WallShape(t); if (s != null) return s; }
            return null;
        }

        public static StrataMap BorderOf(StrataTerrain t)
        {
            foreach (var p in All) { var b = p.Border(t); if (b != null) return b; }
            return null;
        }

        public static bool OwnsShader(Shader s) => s && All.Any(p => p.Owns(s));

        public virtual bool MultiTerrain => false;
        public virtual void Joined(StrataTerrain t) { }
        public virtual void Rearranged() { }
        public virtual StrataMap Border(StrataTerrain t) => null;
        public virtual void Edited(StrataTerrain t, int x0, int z0, int x1, int z1) { }

        public virtual IStrataWallShape WallShape(StrataTerrain t) => null;
        public virtual void Configure(StrataTerrain t) { }
        public virtual void Apply(StrataTileSet ts, Material ground, Material walls) { }
        public virtual void EdgeTable(StrataTileSet ts, Color[] px, List<StrataMaterial> wallSlots, List<StrataMaterial> topSlots) { }
        public virtual float StairScale(StrataTileSet ts) => 1f;
        public virtual bool Owns(Shader s) => false;

        public virtual void Created(StrataMaterial m, StrataMaterial like, string name, StrataTileSet.Role role, bool stair) { }
        public virtual void SetSettings(StrataTileSet ts, Action changed) { }
        public virtual void MaterialHeader(StrataMaterial m, SerializedObject so) { }
        public virtual bool StraightBorders(StrataMaterial m, SerializedObject so) => false;
        public virtual void MaterialSettings(StrataMaterial m, SerializedObject so, StrataTileSet library, bool all) { }
    }

    public interface IStrataWallShape
    {
        int Reach { get; }
        bool Claims(StrataMap m, Wall w);
        bool ClaimsStyle(int style);
        void Build(StrataMap m, Wall w, double pivotX, double pivotZ, float codeA, MeshData into, List<int> tris);
    }

    [Serializable]
    public struct StrataExtra
    {
        public string key;
        public string value;

        public static string Get(List<StrataExtra> list, string key)
        {
            if (list == null) return null;
            foreach (var e in list) if (e.key == key) return e.value;
            return null;
        }

        public static void Set(List<StrataExtra> list, string key, string value)
        {
            int k = list.FindIndex(e => e.key == key);
            if (value == null) { if (k >= 0) list.RemoveAt(k); return; }
            if (k >= 0) list[k] = new StrataExtra { key = key, value = value };
            else list.Add(new StrataExtra { key = key, value = value });
        }
    }
}
