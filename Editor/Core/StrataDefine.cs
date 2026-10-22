using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace Strata
{
    [InitializeOnLoad]
    static class StrataDefine
    {
        const string Symbol = "STRATA";

        static StrataDefine()
        {
            var target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            PlayerSettings.GetScriptingDefineSymbols(target, out var defines);
            if (!defines.Contains(Symbol)) PlayerSettings.SetScriptingDefineSymbols(target, defines.Append(Symbol).ToArray());
        }
    }
}
