// Source-authored view response for Blender Glossy/Transparent mixes and emitters.
Shader "CarAR/BlenderViewSurface"
{
    Properties
    {
        _BaseColor("Source linear reflection colour", Vector) = (1,1,1,1)
        _BaseMap("Cycles reflection colour", 2D) = "white" {}
        _Response("Source view response", 2D) = "white" {}
        _Roughness("Source roughness", Float) = 0
        _EmissionOnly("Source emission shader", Float) = 0
        [HideInInspector] _ZWrite("Depth write", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_Response); SAMPLER(sampler_Response);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; float _Roughness, _EmissionOnly;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; half fog:TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes v)
            {
                Varyings o=(Varyings)0; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS; o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.uv=v.uv; o.fog=ComputeFogFactor(p.positionCS.z); return o;
            }
            half4 Frag(Varyings v):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
                half3 n=normalize(v.normalWS); half3 view=GetWorldSpaceNormalizeViewDir(v.positionWS);
                half cosine=abs(dot(n,view));
                half4 response=SAMPLE_TEXTURE2D(_Response,sampler_Response,float2(cosine,.5));
                if (_EmissionOnly>.5) return half4(MixFog(response.rgb,v.fog),1);
                half3 reflection=GlossyEnvironmentReflection(reflect(-view,n),v.positionWS,_Roughness,1,GetNormalizedScreenSpaceUV(v.positionCS));
                half3 color=reflection*_BaseColor.rgb*SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,v.uv).rgb*response.rgb;
                return half4(MixFog(color,v.fog),response.a);
            }
            ENDHLSL
        }
    }
}
