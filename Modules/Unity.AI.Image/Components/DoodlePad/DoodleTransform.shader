Shader "Hidden/DoodleTransform"
{
    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"
        }
        LOD 100

        ZWrite Off

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
            float4 _MainTex_TexelSize;

            float2 _Offset = float2(0.0, 0.0);
            float _Rotation = 0.0;
            float2 _Scale = float2(1.0, 1.0);

            float2 Rotate(float2 p, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                const float2x2 rot = float2x2(c, -s, s, c);
                return mul(rot, p);
            }

            float2 Transform(float2 p, float2 offset, float rotation, float2 scale)
            {
                p *= scale;
                p = Rotate(p, rotation);
                p += float2(offset.x, - offset.y);
                return p;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.vertex.xy = Transform(o.vertex.xy, _Offset, _Rotation, _Scale);

                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                return col;
            }
            ENDCG
        }
    }
}