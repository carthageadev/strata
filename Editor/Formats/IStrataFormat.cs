using System;

namespace Strata.Formats
{
    public interface IStrataFormat
    {
        string Id { get; }
        string Name { get; }
        StrataPalette Palette(StrataTerrain t);
        void TerrainTab(Action<Func<string>> run);
    }
}
