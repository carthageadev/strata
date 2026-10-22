using UnityEngine;

namespace Strata
{
    [CreateAssetMenu(fileName = "New Stair Type", menuName = "Strata/Stair Type", order = 402)]
    public sealed class StrataStairType : ScriptableObject
    {
        public enum Build { Stone = 0, Timber = 1, Models = 2 }

        [Tooltip("The name shown in the Stairs brush (the asset's name when empty).")]
        public string displayName = "";
        public Build build = Build.Stone;
        [Tooltip("Stone: the stepped body.")] public StrataMaterial body;
        [Tooltip("Stone: the treads and the copings on cheek walls.")] public StrataMaterial tread;
        [Tooltip("Stone: the cheek walls.")] public StrataMaterial cheek;
        [Tooltip("Timber: the planks.")] public StrataMaterial planks;
        [Tooltip("Timber: the posts.")] public StrataMaterial posts;
        [Tooltip("Models: one step, repeated up the flight and fitted to each step. Forward (+Z) climbs, +Y is up.")] public GameObject step;
        [Tooltip("Models: the bottom step (the step model when empty).")] public GameObject firstStep;
        [Tooltip("Models: the top step (the step model when empty).")] public GameObject lastStep;
        [Tooltip("Models: fill under each step with more steps, down to the foot, so the stair is solid.")] public bool stack = true;
        [Tooltip("Models: turn the models (quarter turns) if they face another way.")] [Range(0, 3)] public int turn;

        public Material[] built = new Material[0];

        public string Title => string.IsNullOrEmpty(displayName) ? name : displayName;
        public bool Timber => build == Build.Timber;
        public bool Models => build == Build.Models;
        public int Parts => Timber ? 2 : Models ? 0 : 3;
        public StrataMaterial Part(int k) => Timber ? (k == 0 ? planks : posts) : Models ? null : (k == 0 ? body : k == 1 ? tread : cheek);
        public static string PartName(bool timber, int k) => timber ? (k == 0 ? "Planks" : "Posts") : (k == 0 ? "Body" : k == 1 ? "Treads" : "Cheek walls");
        public Texture Preview => Models ? (step ? UnityEditor.AssetPreview.GetAssetPreview(step) ?? UnityEditor.AssetPreview.GetMiniThumbnail(step) : null) : (Part(0) ? Part(0).albedo : null);
    }
}
