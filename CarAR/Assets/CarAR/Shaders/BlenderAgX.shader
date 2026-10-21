Shader "CarAR/BlenderAgX"
{
    Properties { _Lut("Blender view LUT",3D)="" {} _Domain("Log2 domain",Vector)=(-12.47393,6.52607,32,0) _ExposureEV("Realtime lighting calibration (EV)",Float)=0 }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline"}
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            TEXTURE3D(_Lut);SAMPLER(sampler_Lut);
            float4 _Domain;
            float _ExposureEV;
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 source=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,input.texcoord);
                float3 logColor=saturate((log2(max(source.rgb*exp2(_ExposureEV),exp2(_Domain.x)))-_Domain.x)/(_Domain.y-_Domain.x));
                float3 uv=logColor*((_Domain.z-1)/_Domain.z)+.5/_Domain.z;
                return half4(SAMPLE_TEXTURE3D(_Lut,sampler_Lut,uv).rgb,source.a);
            }
            ENDHLSL
        }
    }
}
