using UnityEngine;

namespace Strata
{
    static class StrataIds
    {
        public static string Key(Object o) =>
#if UNITY_6000_3_OR_NEWER
            o.GetEntityId().ToString();
#else
            o.GetInstanceID().ToString();
#endif

        public static int Hash(Object o) =>
#if UNITY_6000_3_OR_NEWER
            o.GetEntityId().GetHashCode();
#else
            o.GetInstanceID();
#endif
    }
}
