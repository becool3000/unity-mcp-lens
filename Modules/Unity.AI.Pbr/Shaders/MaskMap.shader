Shader "Hidden/AIToolkit/MaskMap"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _RoughnessTex ("Roughness Texture", 2D) = "black" {}
        _OcclusionTex ("Occlusion Texture", 2D) = "white" {}
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
            Texture2D _RoughnessTex;
            Texture2D _OcclusionTex;
            SamplerState point_clamp;

            float4 frag(varyings i) : SV_Target
            {
                float4 final_color = float4(0,0,0,0);

                float4 metalness_color = _MainTex.Sample(point_clamp, i.uv);
                float4 roughness_color = _RoughnessTex.Sample(point_clamp, i.uv);
                float4 occlusion_color = _OcclusionTex.Sample(point_clamp, i.uv);

                float metalness = dot(metalness_color.rgb, float3(0.2126, 0.7152, 0.0722));
                float occlusion = dot(occlusion_color.rgb, float3(0.2126, 0.7152, 0.0722));
                float detail = 1;
                final_color.rgb = float3(metalness, occlusion, detail);
                final_color.a = 1 - saturate(dot(roughness_color.rgb, float3(0.2126, 0.7152, 0.0722)));

                return final_color;
            }

            ENDCG
        }
    }
}
