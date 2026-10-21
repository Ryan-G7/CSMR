Shader "CarAR/VRComfort"
{
    Properties { _Fade("Fade", Range(0,1)) = 0 _Vignette("Vignette", Range(0,1)) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay+100" "RenderType"="Transparent" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial) float _Fade, _Vignette; CBUFFER_END
            V Vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; return o; }
            half4 Frag(V v):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
                // Center independently in each eye; a near-camera mesh's UVs
                // would introduce stereo parallax into the comfort mask.
                float2 p=GetNormalizedScreenSpaceUV(v.positionCS)-.5;
                p.x*=_ScreenParams.x/_ScreenParams.y;
                return half4(0,0,0,max(_Fade,smoothstep(.24,.5,length(p))*_Vignette));
            }
            ENDHLSL
        }
    }
}
