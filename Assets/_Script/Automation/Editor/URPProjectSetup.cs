using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Explicit, repeatable migration of active application rendering assets only.</summary>
public static class URPProjectSetup
{
    const string Root = "Assets/Settings/Rendering";

    [MenuItem("Tools/RT Automation/Configure Active Features for URP")]
    public static void Configure()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before configuring URP.");
        if (EditorSceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the open scene before configuring URP.");
        Directory.CreateDirectory(Root);
        AssetDatabase.Refresh();
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/ForwardRenderer.asset");
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.renderingMode = RenderingMode.Forward;
            AssetDatabase.CreateAsset(renderer, Root + "/ForwardRenderer.asset");
        }
        ResourceReloader.ReloadAllNullIn(renderer, "Packages/com.unity.render-pipelines.universal");
        EditorUtility.SetDirty(renderer);
        var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        var levels = quality.FindProperty("m_QualitySettings");
        UniversalRenderPipelineAsset defaultPipeline = null;
        for (int i = 0; i < levels.arraySize; i++)
        {
            var level = levels.GetArrayElementAtIndex(i);
            string path = Root + "/" + level.FindPropertyRelative("name").stringValue.Replace(" ", "") + ".asset";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (pipeline == null) { pipeline = UniversalRenderPipelineAsset.Create(renderer); AssetDatabase.CreateAsset(pipeline, path); }
            pipeline.msaaSampleCount = Math.Max(1, level.FindPropertyRelative("antiAliasing").intValue);
            pipeline.renderScale = 1;
            pipeline.supportsHDR = true;
            pipeline.supportsCameraDepthTexture = false;
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.shadowDistance = level.FindPropertyRelative("shadowDistance").floatValue;
            pipeline.shadowCascadeCount = Math.Max(1, level.FindPropertyRelative("shadowCascades").intValue);
            pipeline.cascade2Split = level.FindPropertyRelative("shadowCascade2Split").floatValue;
            pipeline.cascade4Split = level.FindPropertyRelative("shadowCascade4Split").vector3Value;
            var settings = new SerializedObject(pipeline);
            int shadows = level.FindPropertyRelative("shadows").intValue;
            settings.FindProperty("m_MainLightShadowsSupported").boolValue = shadows > 0;
            settings.FindProperty("m_AdditionalLightShadowsSupported").boolValue = shadows > 0;
            settings.FindProperty("m_SoftShadowsSupported").boolValue = shadows == 2;
            settings.FindProperty("m_AdditionalLightsPerObjectLimit").intValue = level.FindPropertyRelative("pixelLightCount").intValue;
            settings.FindProperty("m_MainLightShadowmapResolution").intValue = 512 << level.FindPropertyRelative("shadowResolution").intValue;
            settings.ApplyModifiedPropertiesWithoutUndo();
            level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = pipeline;
            EditorUtility.SetDirty(pipeline);
            if (i == QualitySettings.GetQualityLevel()) defaultPipeline = pipeline;
        }
        quality.ApplyModifiedPropertiesWithoutUndo();
        GraphicsSettings.defaultRenderPipeline = defaultPipeline;

        var sprite = MaterialAt("SpriteUnlit", "Universal Render Pipeline/2D/Sprite-Unlit-Default");
        var line = MaterialAt("LineUnlit", "Universal Render Pipeline/Particles/Unlit");
        line.SetFloat("_Surface", 1); line.SetFloat("_Blend", 0); line.SetFloat("_Cull", 0);
        line.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); line.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        line.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); line.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        line.SetFloat("_ZWrite", 0); line.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        line.SetOverrideTag("RenderType", "Transparent"); line.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(line);
        ConvertUnlit("Assets/Images/MovieMaterial.mat");
        ConvertUnlit("Assets/Images/TileMaterial.mat");

        const string picPath = "Assets/_Prefabs/DefaultPicPreFab.prefab";
        var pic = PrefabUtility.LoadPrefabContents(picPath);
        try
        {
            foreach (var r in pic.GetComponentsInChildren<SpriteRenderer>(true)) r.sharedMaterial = sprite;
            foreach (var r in pic.GetComponentsInChildren<LineRenderer>(true)) r.sharedMaterial = line;
            pic.GetComponent<PicMain>().m_selectionFrameMaterial = line;
            PrefabUtility.SaveAsPrefabAsset(pic, picPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(pic); }

        AssetDatabase.SaveAssets();
        // URP creates these on first import. Keep rendering configuration together.
        foreach (string name in new[] { "UniversalRenderPipelineGlobalSettings", "DefaultVolumeProfile" })
            if (AssetDatabase.LoadMainAssetAtPath("Assets/" + name + ".asset") != null && AssetDatabase.LoadMainAssetAtPath(Root + "/" + name + ".asset") == null)
                AssetDatabase.MoveAsset("Assets/" + name + ".asset", Root + "/" + name + ".asset");
        var clearShader = Shader.Find("Hidden/AITools/DBufferClear");
        if (clearShader == null) throw new InvalidOperationException("Missing URP clear shader.");
        GraphicsSettings.GetRenderPipelineSettings<UniversalRendererResources>().decalDBufferClear = clearShader;
        EditorUtility.SetDirty(AssetDatabase.LoadMainAssetAtPath(Root + "/UniversalRenderPipelineGlobalSettings.asset"));
        AssetDatabase.SaveAssets();
        Debug.Log("[URP] Active-feature materials and all quality levels configured. Gamma and scene cameras are unchanged.");
    }

    static Material MaterialAt(string name, string shaderName)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null) throw new InvalidOperationException("Missing shader: " + shaderName);
        string path = Root + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        return material;
    }

    static void ConvertUnlit(string path)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material.shader.name == "Universal Render Pipeline/Unlit") return;
        var texture = material.mainTexture; var scale = material.mainTextureScale; var offset = material.mainTextureOffset;
        material.shader = Shader.Find("Universal Render Pipeline/Unlit");
        material.SetTexture("_BaseMap", texture); material.SetTextureScale("_BaseMap", scale); material.SetTextureOffset("_BaseMap", offset);
        material.SetColor("_BaseColor", Color.white);
        material.shaderKeywords = Array.Empty<string>();
        EditorUtility.SetDirty(material);
    }
}
