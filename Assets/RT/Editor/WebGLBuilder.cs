//place this script in the Editor folder within Assets.
 using UnityEditor;
 using System.Collections.Generic;
  
 //to be used on the command line:
 //$ Unity -quit -batchmode -executeMethod WebGLBuilder.build
  
 class WebGLBuilder 
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

     static void Build() 
    {
         BuildPipeline.BuildPlayer(GetScenes(), "build\\web", BuildTarget.WebGL, BuildOptions.None);
        //BuildPipeline.BuildPlayer(GetScenes(), "build\\web", BuildTarget.WebGL, BuildOptions.Development);
     }

	static void BuildBeta() 
    {
//       SetVirtualRealitySDKs
	 RTBuildTools.AddDefine(BuildTargetGroup.WebGL, "RT_BETA");
         BuildPipeline.BuildPlayer(GetScenes(), "build\\web", BuildTarget.WebGL, BuildOptions.None);
	 RTBuildTools.RemoveDefine(BuildTargetGroup.WebGL, "RT_BETA");
     }
 }