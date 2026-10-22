using System.Collections.Generic;
using UnityEngine;

namespace Strata
{
    [CreateAssetMenu(fileName = "Pixel Bake", menuName = "Strata/Pixel Bake Profile", order = 404)]
    public sealed class StrataBakeProfile : ScriptableObject
    {
        public enum Style { Sample = 0, Masonry = 1, Slabs = 2, Shingles = 3, Planks = 4, Keep = 5 }

        [System.Serializable]
        public sealed class Rule
        {
            [Tooltip("The prop material this rule paints. Matched by name prefix when the material itself isn't set.")]
            public Material source;
            public string nameStartsWith = "";
            [Tooltip("The pixel art the faces are painted with (its albedo, at sixteen texels a metre).")]
            public StrataMaterial paint;
            public Style style = Style.Sample;
            [Tooltip("Masonry: course height in texels. Shingles and planks: row height.")]
            public int course = 6;
            public int blockMin = 9, blockMax = 16;
            [Tooltip("Slabs: slab size in texels.")]
            public int slab = 9;
        }

        [Tooltip("Texels per metre on every baked face.")]
        public float density = 16f;
        [Tooltip("Texels per metre of the props' own materials, for the faces painted from them.")]
        public float sourceDensity = 32f;
        [Tooltip("Where the baked meshes, atlases and materials go (empty: a Baked folder beside this profile).")]
        public string outputFolder = "";
        public List<Rule> rules = new List<Rule>();
        [Tooltip("Materials no rule names keep their texture, sampled at the density, with painted edges.")]
        public bool bakeUnmatched = true;

        public Rule RuleFor(Material m)
        {
            if (!m) return null;
            foreach (var r in rules) if (r.source == m) return r;
            Rule best = null;
            foreach (var r in rules) if (!r.source && !string.IsNullOrEmpty(r.nameStartsWith) && m.name.StartsWith(r.nameStartsWith) && (best == null || r.nameStartsWith.Length > best.nameStartsWith.Length)) best = r;
            return best;
        }
    }
}
