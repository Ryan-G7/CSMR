Shader "Custom/MaskTransparency"
{
    Properties
    {
        _Color ("颜色", Color) = (1,1,1,0)
    }
    
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100
        
        // 关闭深度写入，开启混合
        ZWrite Off
        Blend SrcAlpha Zero
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
            };
            
            struct v2f
            {
                float4 vertex : SV_POSITION;
            };
            
            fixed4 _Color;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                // 返回颜色，但强制Alpha为0（完全透明）
                fixed4 col = _Color;
                col.a = 0;
                return col;
            }
            ENDCG
        }
    }
}