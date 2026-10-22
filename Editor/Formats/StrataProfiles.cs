using UnityEditor;
using System.Linq;
using System;
namespace Strata.Formats
{
    public static class StrataProfiles
    {
        public static StrataPalette Palette(StrataTerrain t)
        {
            return Find(t)?.Palette(t) ?? BasicProfile.Palette(t);
        }

        public static void Configure(StrataTerrain t)
        {
            if (t.map.profile == BasicProfile.Id) BasicProfile.Configure(t);
        }

        public static StrataTileSet Library(StrataTerrain t) => t != null && t.map.profile == BasicProfile.Id ? BasicProfile.TilesOf(t.map) : null;

        public static int Stamp(StrataTerrain t) => Library(t) is StrataTileSet ts && ts ? ts.buildStamp * 397 ^ ts.GetInstanceID() : 0;

        public static string DisplayName(StrataTerrain t) => Find(t)?.Name ?? "Strata tiles";

        static IStrataFormat[] formats;
        public static IStrataFormat[] Formats => formats ??= TypeCache.GetTypesDerivedFrom<IStrataFormat>()
            .Where(x => !x.IsAbstract && !x.IsInterface).Select(x => (IStrataFormat)Activator.CreateInstance(x)).ToArray();

        public static IStrataFormat Find(StrataTerrain t) => t == null ? null : Formats.FirstOrDefault(f => f.Id == t.map.profile);
    }
}
