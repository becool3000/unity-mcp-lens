Shader "Hidden/AIToolkit/Smoothness"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct varyings
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            varyings vert(const attributes v)
            {
                varyings o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            Texture2D _MainTex;
            SamplerState point_clamp;

            float4 frag(varyings i) : SV_Target
            {
                float4 final_color = float4(0,0,0,0);
                float4 roughness_color = _MainTex.Sample(point_clamp, i.uv);

                final_color.rgb = 1 - roughness_color.rgb;
                final_color.a = roughness_color.a;

                return final_color;
            }

            ENDCG
        }
    }
}
