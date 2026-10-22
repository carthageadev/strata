namespace Strata.Formats
{
    public static class StrataProfiles
    {
        public static StrataPalette Palette(StrataTerrain t)
        {
            if (t.map.profile == VillageV2Profile.Id) return VillageV2Profile.Palette(t);
            return BasicProfile.Palette(t);
        }

        public static void Configure(StrataTerrain t)
        {
            if (t.map.profile == BasicProfile.Id) BasicProfile.Configure(t);
        }

        public static StrataTileSet Library(StrataTerrain t) => t != null && t.map.profile == BasicProfile.Id ? BasicProfile.TilesOf(t.map) : null;

        public static int Stamp(StrataTerrain t) => Library(t) is StrataTileSet ts && ts ? ts.buildStamp * 397 ^ ts.GetInstanceID() : 0;

        public static string DisplayName(StrataTerrain t) => t.map.profile == VillageV2Profile.Id ? "Village V2" : "Strata tiles";
    }
}
