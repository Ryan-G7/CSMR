// URP reconstruction of the three authored Carpaint BSDF lobes. All finish
// values and procedural input textures are exported from AMG.blend.
Shader "CarAR/BlenderLayeredPaint"
{
    Properties
    {
        _BaseColor("Source linear flake colour", Vector) = (1,1,1,1)
        _PrincipledColor("Source linear Principled colour", Vector) = (1,1,1,1)
        _PrincipledMetallic("Source Principled metallic", Float) = 1
        _PrincipledRoughness("Source Principled roughness", Float) = 1
        _PrincipledMix("Source Principled mix", Float) = 1
        _CoatColor("Source linear outer Glossy colour", Vector) = (1,1,1,1)
        _CoatRoughness("Source outer Glossy roughness", Float) = 0
        _CoatIOR("Source Fresnel IOR", Float) = 1.5
        _FlakeRoughness("Cycles flake roughness", 2D) = "white" {}
        _FlakeNormal("Cycles flake normal", 2D) = "bump" {}
        _CoatNormal("Cycles outer Glossy normal", 2D) = "bump" {}
        [HideInInspector] _BaseMap("Base map", 2D) = "white" {}
        [HideInInspector] _Cutoff("Alpha cutoff", Float) = 0
        [HideInInspector] _Cull("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_FlakeRoughness); SAMPLER(sampler_FlakeRoughness);
            TEXTURE2D(_FlakeNormal); SAMPLER(sampler_FlakeNormal);
            TEXTURE2D(_CoatNormal); SAMPLER(sampler_CoatNormal);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _PrincipledColor, _CoatColor;
                float _PrincipledMetallic, _PrincipledRoughness, _PrincipledMix, _CoatRoughness, _CoatIOR;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half4 tangentWS:TEXCOORD2; float2 uv:TEXCOORD3; half fog:TEXCOORD4; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes v)
            {
                Varyings o=(Varyings)0; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n=GetVertexNormalInputs(v.normalOS,v.tangentOS);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS; o.normalWS=n.normalWS;
                o.tangentWS=half4(n.tangentWS,v.tangentOS.w*GetOddNegativeScale()); o.uv=v.uv; o.fog=ComputeFogFactor(p.positionCS.z);
                return o;
            }
            InputData Input(Varyings v, half3 normalTS, half face)
            {
                InputData d=(InputData)0;
                half3 n=normalize(v.normalWS)*face;
                half3 t=normalize(v.tangentWS.xyz); half3 b=cross(n,t)*v.tangentWS.w;
                d.normalWS=normalize(TransformTangentToWorld(normalTS,half3x3(t,b,n)));
                d.positionWS=v.positionWS; d.viewDirectionWS=GetWorldSpaceNormalizeViewDir(v.positionWS);
                d.shadowCoord=TransformWorldToShadowCoord(v.positionWS); d.bakedGI=SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(v.positionCS); d.shadowMask=1;
                d.vertexLighting=VertexLighting(v.positionWS,d.normalWS);
                return d;
            }
            half3 Lobe(InputData d, half3 color, half metallic, half roughness)
            {
                SurfaceData s=(SurfaceData)0; s.albedo=color; s.metallic=metallic;
                s.smoothness=1-saturate(roughness); s.normalTS=half3(0,0,1); s.occlusion=1; s.alpha=1;
                return UniversalFragmentPBR(d,s).rgb;
            }
            half4 Frag(Varyings v, FRONT_FACE_TYPE front:FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
                half face=IS_FRONT_VFACE(front,1,-1);
                half3 flakeNormal=UnpackNormal(SAMPLE_TEXTURE2D(_FlakeNormal,sampler_FlakeNormal,v.uv));
                half3 coatNormal=UnpackNormal(SAMPLE_TEXTURE2D(_CoatNormal,sampler_CoatNormal,v.uv));
                InputData baseData=Input(v,half3(0,0,1),face);
                InputData flakeData=Input(v,flakeNormal,face);
                InputData coatData=Input(v,coatNormal,face);
                half roughness=SAMPLE_TEXTURE2D(_FlakeRoughness,sampler_FlakeRoughness,v.uv).r;
                half3 base=Lobe(baseData,_PrincipledColor.rgb,_PrincipledMetallic,_PrincipledRoughness);
                half3 flake=Lobe(flakeData,_BaseColor.rgb,1,roughness);
                half3 coat=Lobe(coatData,_CoatColor.rgb,1,_CoatRoughness);
                // Exact dielectric Fresnel used by the source outer Mix Shader.
                float cosine=saturate(dot(baseData.normalWS,baseData.viewDirectionWS));
                float g=sqrt(max(_CoatIOR*_CoatIOR+cosine*cosine-1,0));
                float a=(g-cosine)/max(g+cosine,1e-5);
                float b=(cosine*(g+cosine)-1)/max(cosine*(g-cosine)+1,1e-5);
                float fresnel=saturate(.5*a*a*(1+b*b));
                return half4(MixFog(lerp(lerp(flake,base,_PrincipledMix),coat,fresnel),v.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
