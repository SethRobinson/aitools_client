// URP 17.6's packaged clear shader has no non-decal variant. With decals disabled,
// all its variants are stripped even though the pipeline retains the resource.
// Keep a valid default variant while preserving the same MRT clear values.
Shader "Hidden/AITools/DBufferClear"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "DBufferClear"
            ZTest Always
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Clear
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            struct ClearTargets
            {
                half4 albedo : SV_Target0;
                #if defined(_DBUFFER_MRT2) || defined(_DBUFFER_MRT3)
                half4 normal : SV_Target1;
                #endif
                #if defined(_DBUFFER_MRT3)
                half4 surface : SV_Target2;
                #endif
            };

            ClearTargets Clear(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ClearTargets output;
                output.albedo = half4(0, 0, 0, 1);
                #if defined(_DBUFFER_MRT2) || defined(_DBUFFER_MRT3)
                output.normal = half4(0.5, 0.5, 0.5, 1);
                #endif
                #if defined(_DBUFFER_MRT3)
                output.surface = half4(0, 0, 0, 1);
                #endif
                return output;
            }
            ENDHLSL
        }
    }
}
