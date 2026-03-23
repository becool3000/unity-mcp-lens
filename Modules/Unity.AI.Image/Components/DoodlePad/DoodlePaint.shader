Shader "Hidden/DoodlePaint"
{
    Properties
    {
        // Declare "_Color" as Property to support implicit color space conversions
        _Color("Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Lighting Off
        Blend One Zero
        ZWrite Off
        Cull Off

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

            float4 _Pos;
            float  _Radius;
            float4 _Color;
            float _AspectRatio;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float distToSegment(float2 p, float2 a, float2 b) {
                float2 ba = b - a;
                float2 pa = p - a;
                float2 h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0) * ba;
                return length(pa - h);
            }

            float drawLine(float2 uv, float2 a, float2 b, float thickness) {
                float dist = distToSegment(uv, a, b);
                return smoothstep(thickness, thickness - fwidth(dist), dist);
            }

            float4 frag(v2f i) : SV_Target
            {
                // Adjust UV coordinates for aspect ratio
                float2 uv = i.uv;
                uv.x *= _AspectRatio;

                // Draw line
                float aLine = drawLine(uv, _Pos.xy, _Pos.zw, _Radius);
                float4 color = tex2D(_MainTex, i.uv);
                color = lerp(color, _Color, aLine);
                return color;
            }
            ENDCG
        }
    }
}
