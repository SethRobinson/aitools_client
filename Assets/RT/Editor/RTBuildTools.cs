//place this script in the Editor folder within Assets.
using UnityEditor;
using System.Collections;
using System.Collections.Generic;

//to be used on the command line:
//$ Unity -quit -batchmode -executeMethod WebGLBuilder.build

class RTBuildTools
{

    public static void AddDefine(BuildTargetGroup buildGroup, string newDefine)
    {
        UnityEngine.Debug.Log("Adding define: '" + newDefine + "'");

        string defines;
        defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildGroup);
        defines = AddCompilerDefines(defines, newDefine);
        PlayerSettings.SetScriptingDefineSymbolsForGroup(buildGroup, defines);
    }

    public static void RemoveDefine(BuildTargetGroup buildGroup, string newDefine)
    {
        UnityEngine.Debug.Log("Removing define: '" + newDefine + "'");

        string defines;
        defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildGroup);
        defines = RemoveCompilerDefines(defines, newDefine);
        PlayerSettings.SetScriptingDefineSymbolsForGroup(buildGroup, defines);
    }


    public static string AddCompilerDefines(string defines, params string[] toAdd)
    {
        List<string> splitDefines = new List<string>(defines.Split(new char[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));
        foreach (var add in toAdd)
            if (!splitDefines.Contains(add))
                splitDefines.Add(add);

        return string.Join(";", splitDefines.ToArray());
    }

    public static string RemoveCompilerDefines(string defines, params string[] toRemove)
    {
        List<string> splitDefines = new List<string>(defines.Split(new char[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));
        foreach (var remove in toRemove)
            splitDefines.Remove(remove);

        return string.Join(";", splitDefines.ToArray());
    }
}