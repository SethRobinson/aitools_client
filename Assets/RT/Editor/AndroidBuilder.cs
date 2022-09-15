//place this script in the Editor folder within Assets.
 using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
  
 //to be used on the command line:
 //$ Unity -quit -batchmode -executeMethod AndroidBuilder.build
  
 class AndroidBuilder
{

    static string GetProjectName()
    {
        string[] s = Application.dataPath.Split('/');
        string temp =  s[s.Length - 2];
        temp = temp.Replace("TempAndroid", ""); //I add this when making temp dirs to build in, so this removes it so the filename can be correct
        return temp;
    }

    static string[] GetScenes()
	{
		List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
 		List<string> enabledScenes = new List<string>();
		
		 foreach (EditorBuildSettingsScene scene in scenes)
 			{
    		if (scene.enabled)
     		{
         	enabledScenes.Add(scene.path);
     		}
		 }
 
		return enabledScenes.ToArray();
	}

     static void BuildForceVR() 
    {
       // EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.Android);
       // UnityEditorInternal.VR.VREditor.SetVREnabledDevicesOnTargetGroup(BuildTargetGroup.Standalone,  new string[] { "OpenVR", "Oculus" });
        //BuildPipeline.BuildPlayer(GetScenes(), "build\\android\\" + GetProjectName() + ".apk", BuildTarget.Android, BuildOptions.None);

   	BuildPipeline.BuildPlayer(GetScenes(), "build\\android\\"+ GetProjectName() + ".apk", BuildTarget.Android, BuildOptions.None);
 

        //BuildPipeline.BuildPlayer(GetScenes(), "build\\win", BuildTarget.Android, BuildOptions.Development);
     }

	static void BuildBeta() 
    {
	EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.Android);
	 RTBuildTools.AddDefine(BuildTargetGroup.Standalone, "RT_BETA");
	 BuildPipeline.BuildPlayer(GetScenes(), "build\\android\\"+ GetProjectName() + "_beta.app", BuildTarget.Android, BuildOptions.None);
   	 RTBuildTools.RemoveDefine(BuildTargetGroup.Standalone, "RT_BETA");
     }

	static void BuildRelease() 
    {
	EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.Android);	
     	BuildPipeline.BuildPlayer(GetScenes(), "build\\android\\"+ GetProjectName() + ".apk", BuildTarget.Android, BuildOptions.None);
     }
 }