using UnityEngine;

namespace Strata
{
    [CreateAssetMenu(fileName = "New Strata Material", menuName = "Strata/Material", order = 401)]
    public sealed class StrataMaterial : ScriptableObject
    {
        public enum EdgePattern { Overhang = 1, Slabs = 2, Stones = 3 }
        [System.Flags] public enum Use { None = 0, Top = 1, Side = 2, Edge = 4 }

        [Tooltip("The name shown in the brushes (the asset's name when empty).")]
        public string displayName = "";
        [Tooltip("What it is meant as: added to a tile set with a click, it goes in these.")]
        public Use meantAs = Use.Top;

        [Tooltip("Colour. 256 x 256 for sixteen texels per metre.")]
        public Texture2D albedo;
        [Tooltip("Normal map (a Unity normal map or plain RGB).")]
        public Texture2D normal;
        [Tooltip("Metallic map: R metallic, A smoothness.")]
        public Texture2D metallic;
        [Tooltip("Ambient occlusion (greyscale).")]
        public Texture2D occlusion;
        [Tooltip("Strata's packed map (R occlusion, G roughness, B metallic); used instead of the metallic and occlusion maps when set.")]
        public Texture2D packed;

        public Color tint = Color.white;
        [Tooltip("How many texels one pixel of the art covers (low-resolution art drawn big). Blends and borders follow it. 0: found from the albedo.")]
        public int pixelSize = 0;
        [Tooltip("Smoothness without a metallic map (with one, it scales the map's alpha).")]
        [Range(0, 1)] public float smoothness = 0.1f;
        [Tooltip("Metalness without a metallic map.")]
        [Range(0, 1)] public float metalness = 0f;

        [Tooltip("As a top: which layer lies on top where two meet (higher wins the border).")]
        [Range(0, 12)] public float priority = 5;
        [Tooltip("As a top: layers of different families get a lit lip and a shadow where they meet.")]
        public StrataTileSet.Family family = StrataTileSet.Family.Turf;
        [Tooltip("As a top: how much the border with other layers wanders.")]
        [Range(0, 1)] public float wobble = 0.4f;
        [Tooltip("As a top: how ragged the border is, texel by texel.")]
        [Range(0, 0.3f)] public float ragged = 0.12f;

        [Tooltip("As an edge: a fringe hanging over the wall (like turf), cut slabs (like coping), or rounded stones.")]
        public EdgePattern edgePattern = EdgePattern.Overhang;
        [Tooltip("As an edge: draw it with the albedo texture (else with the two colours).")]
        public bool edgeTexture = true;
        [Tooltip("As an edge: the fringe hanging down the wall, or the joints and the darker stones.")]
        public Color edgeDark = new Color(0.34f, 0.47f, 0.17f);
        [Tooltip("As an edge: the sunlit lip on the ground, or the lighter stones.")]
        public Color edgeLight = new Color(0.46f, 0.6f, 0.26f);

        public string Title => string.IsNullOrEmpty(displayName) ? name : displayName;
        public bool EdgeTextured => edgeTexture && albedo;
    }
}
