//place this script in the Editor folder within Assets.
 using UnityEditor;
 using System.Collections.Generic;
   
 //to be used on the command line:
 //$ Unity -quit -batchmode -executeMethod WebGLBuilder.build
  
 class LinuxHeadlessBuilder 
{


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

     static void BuildBeta() 
    {
	 RTBuildTools.AddDefine(BuildTargetGroup.Standalone, "RT_BETA");
	     BuildPipeline.BuildPlayer(GetScenes(), "build\\linux\\Linux64Server", BuildTarget.StandaloneLinux64, BuildOptions.EnableHeadlessMode);
   	 RTBuildTools.RemoveDefine(BuildTargetGroup.Standalone, "RT_BETA");
     }

	static void BuildBetaDebug() 
    {
	 RTBuildTools.AddDefine(BuildTargetGroup.Standalone, "RT_BETA");
	     BuildPipeline.BuildPlayer(GetScenes(), "build\\linux\\Linux64Server", BuildTarget.StandaloneLinux64, BuildOptions.Development | BuildOptions.EnableHeadlessMode);
   	 RTBuildTools.RemoveDefine(BuildTargetGroup.Standalone, "RT_BETA");
     }

	static void BuildDebug() 
    {
	     BuildPipeline.BuildPlayer(GetScenes(), "build\\linux\\Linux64Server", BuildTarget.StandaloneLinux64, BuildOptions.Development | BuildOptions.EnableHeadlessMode);
     }

	static void BuildRelease() 
    {
     	   BuildPipeline.BuildPlayer(GetScenes(), "build\\linux\\Linux64Server", BuildTarget.StandaloneLinux64, BuildOptions.EnableHeadlessMode);
     }


 }