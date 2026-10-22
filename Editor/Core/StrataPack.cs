using System.Collections.Generic;
using UnityEngine;

namespace Strata
{
    [CreateAssetMenu(fileName = "Strata Pack", menuName = "Strata/Pack", order = 403)]
    public sealed class StrataPack : ScriptableObject
    {
        [Tooltip("The pack's name in the Materials tab (the asset's name when empty).")]
        public string displayName = "";
        public List<StrataMaterial> materials = new List<StrataMaterial>();
        public List<StrataStairType> stairs = new List<StrataStairType>();

        public string Title => string.IsNullOrEmpty(displayName) ? name : displayName;
    }
}
