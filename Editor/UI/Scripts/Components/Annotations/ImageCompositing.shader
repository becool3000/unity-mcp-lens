Shader "Hidden/AI.Assistant/ImageCompositing"
{
    Properties
    {
        _MainTex ("Background Texture", 2D) = "white" {}
        _StrokeTex ("Stroke Texture", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _StrokeTex;
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 bg = tex2D(_MainTex, i.uv);
                fixed4 stroke = tex2D(_StrokeTex, i.uv);

                // Alpha blend: result = stroke * stroke.a + background * (1 - stroke.a)
                fixed alpha = stroke.a;
                fixed4 result = lerp(bg, stroke, alpha);
                result.a = 1.0; // Always output fully opaque

                return result;
            }
            ENDCG
        }
    }
}
