Shader "Hidden/AIToolkit/AlphaToGray"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _GrayValue ("Gray Value", Range(0,1)) = 0.5
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
            float4 _MainTex_ST;
            float _GrayValue;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                // Replace transparent pixels with mid-gray
                // Lerp between gray and original color based on alpha
                col.rgb = lerp(fixed3(_GrayValue, _GrayValue, _GrayValue), col.rgb, col.a);
                col.a = 1.0; // Output opaque

                return col;
            }
            ENDCG
        }
    }
}
