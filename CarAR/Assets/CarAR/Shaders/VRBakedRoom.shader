// Cycles outputs scene-linear shaded diffuse color. Sample it directly instead
// of routing an external EXR through Unity's platform lightmap RGBM decoder.
Shader "CarAR/VRBakedRoom"
{
    Properties
    {
        _SourceLighting("Cycles shaded diffuse",2D)="black" {}
        _BaseMap("UV transform",2D)="white" {}
        _BaseColor("Base color",Color)=(1,1,1,1)
        _BumpMap("Normal",2D)="bump" {}
        _BumpScale("Normal scale",Float)=1
        _MetallicGlossMap("Metallic / smoothness",2D)="white" {}
        _Smoothness("Smoothness",Range(0,1))=.5
        _EnvironmentReflections("Local reflections",Float)=1
        _VehicleShadowStrength("Dynamic vehicle shadow strength",Range(0,1))=.7
        _Cull("Cull",Float)=0
        _Cutoff("Cutoff",Float)=0
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        Pass
        {
            Tags {"LightMode"="UniversalForward"}
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_SourceLighting);SAMPLER(sampler_SourceLighting);
            TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap);SAMPLER(sampler_MetallicGlossMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BumpScale,_Smoothness,_EnvironmentReflections,_VehicleShadowStrength,_Cull,_Cutoff;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;
                float2 uv:TEXCOORD0;float2 uv2:TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;
                half3 normalWS:TEXCOORD1;half4 tangentWS:TEXCOORD2;
                float2 uv:TEXCOORD3;float2 lightingUV:TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes v)
            {
                Varyings o=(Varyings)0;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n=GetVertexNormalInputs(v.normalOS,v.tangentOS);
                o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=n.normalWS;
                o.tangentWS=half4(n.tangentWS,v.tangentOS.w*GetOddNegativeScale());
                o.uv=v.uv*_BaseMap_ST.xy+_BaseMap_ST.zw;
                o.lightingUV=v.uv2*unity_LightmapST.xy+unity_LightmapST.zw;
                return o;
            }
            half4 Frag(Varyings v):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
                half3 n=normalize(v.normalWS);
                #if defined(_NORMALMAP)
                    half3 t=normalize(v.tangentWS.xyz);half3 b=cross(n,t)*v.tangentWS.w;
                    half3 normalTS=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,v.uv),_BumpScale);
                    n=normalize(TransformTangentToWorld(normalTS,half3x3(t,b,n)));
                #endif
                InputData d=(InputData)0;
                d.positionWS=v.positionWS;d.normalWS=n;d.viewDirectionWS=GetWorldSpaceNormalizeViewDir(v.positionWS);
                d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(v.positionCS);d.shadowMask=1;
                d.shadowCoord=TransformWorldToShadowCoord(v.positionWS);
                // The color bake already contains source diffuse reflectance.
                // Cancel URP's dielectric energy split to apply it exactly once.
                d.bakedGI=SAMPLE_TEXTURE2D(_SourceLighting,sampler_SourceLighting,v.lightingUV).rgb/.96h;
                // The movable car is absent from the Cycles bake. Its realtime
                // visibility map darkens baked receivers without adding the
                // ceiling illumination a second time. Retain indirect fill.
                Light vehicleShadow=GetMainLight(d.shadowCoord);
                d.bakedGI*=lerp(1.0h,vehicleShadow.shadowAttenuation,_VehicleShadowStrength);
                SurfaceData s=(SurfaceData)0;s.albedo=1;s.normalTS=half3(0,0,1);s.occlusion=1;s.alpha=1;
                s.smoothness=_Smoothness;
                #if defined(_METALLICSPECGLOSSMAP)
                    s.smoothness*=SAMPLE_TEXTURE2D(_MetallicGlossMap,sampler_MetallicGlossMap,v.uv).a;
                #endif
                return half4(UniversalFragmentPBR(d,s).rgb,1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
