Shader "Hidden/AIToolkit/Oscillogram"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _RenderParams ("Render Parameters", Vector) = (1, 1, 1, 1)
        _WaveColor ("Wave Color", Color) = (1, 0.55, 0, 1)
        _BackgroundColor ("Background Color", Color) = (0, 0, 0, 0)
        _OutsideZoneWaveColor ("Outside Zone Wave Color", Color) = (0.5, 0.27, 0, 1)
        _OutsideZoneColor ("Outside Zone Background Color", Color) = (0.0, 0.0, 0.0, 0.5)
        _StartMarkerPosition ("Start Marker Position", Float) = 0.0
        _EndMarkerPosition ("End Marker Position", Float) = 1.0
        _ShowControlPoints ("Show Control Point", Int) = 0
        _RecPixelSize ("Reciprocal Pixel Size", Float) = 0.005
        _Padding ("Padding", Float) = 0.0
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

            #define EPSILON 1e-5
            #define INFINITY (1.0f/EPSILON)
            #define LARGE 100

            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature INTERPOLATE

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
            float4 _MainTex_TexelSize;
            SamplerState point_clamp;
            float4 _RenderParams;
            float4 _WaveColor;
            float4 _BackgroundColor;
            float4 _OutsideZoneWaveColor;
            float4 _OutsideZoneColor;
            float _StartMarkerPosition;
            float _EndMarkerPosition;
            int _ShowControlPoints;
            float _RecPixelSize;
            float _Padding;

            // b-spline
            float basis0(const float t) { return (1.0 - t) * (1.0 - t) * 0.5; }
            float basis1(const float t) { return 0.5 + t * (1.0 - t); }
            float basis2(const float t) { return t * t * 0.5; }

            float4 frag(varyings i) : SV_Target
            {
                // Allow about 8 (PADDING) pixels above and below for some space for round markers
                i.uv = (i.uv - float2(0, 0.5)) / float2(1, 1 - _Padding * _RenderParams.w) + float2(0, 0.5);

                // when we render control points we darken the waveform as if 'pushed' to the background
                bool outside_zone_wave = _ShowControlPoints > 0;
                bool outside_zone_background = false;

                {
                    // Start and end clipping
                    const float2 p0 = float2(_StartMarkerPosition, 0 - _Padding / 2 * _RenderParams.w);
                    const float2 pn = float2(_EndMarkerPosition, 1 + _Padding / 2 * _RenderParams.w);
                    if ((i.uv.x < p0.x || i.uv.x > pn.x) && i.uv.y > p0.y && i.uv.y < pn.y)
                    {
                        outside_zone_wave = true;
                        outside_zone_background = true;
                    }
                }

                // draw background
                float4 final_color = outside_zone_background ? _OutsideZoneColor : _BackgroundColor;

                // draw waveform, it's already panned and zoomed on the cpu
                const bool out_of_bounds = i.uv.x < 0.0 || i.uv.x > 1.0;
                const int count = 3;
#ifndef INTERPOLATE
                // multisample
                float val = 0;
                for (int j = 0; j < count; j++)
                {
                    float2 iuv = i.uv;
                    iuv.x += j * (_RenderParams.z / count);
                    const float t = iuv.x * _MainTex_TexelSize.z;
                    const int base_index = int(t);

                    const float2 uv = float2((base_index + 0.5) / _MainTex_TexelSize.z, 0.5);
                    float2 sample = out_of_bounds ? float2(0, 0) : _MainTex.Sample(point_clamp, uv).rg;
                    sample.g = out_of_bounds ? sample.g : max(sample.g, sample.r + _RenderParams.w / 2);

                    const float y = iuv.y * 2 - 1;
                    const float d = abs(y - clamp(y, sample.r, sample.g));

                    val += (1 - saturate(d * _RenderParams.y)) / count;
                }
                final_color = lerp(outside_zone_wave ? _OutsideZoneWaveColor : _WaveColor, final_color, 1-sqrt(val));
#else
                // interpolate
                float2 iuv = i.uv;
                const float t = iuv.x * _MainTex_TexelSize.z;
                const int base_index = int(t);

                const int indices[count] = { base_index - 1, base_index, base_index + 1 };
                float4 samples[count];
                for (int j = 0; j < count; ++j)
                {
                    float2 uv = float2((indices[j] + 0.5) / _MainTex_TexelSize.z, 0.5);
                    float2 sample = out_of_bounds ? float2(0, 0) : _MainTex.Sample(point_clamp, uv).xy;
                    samples[j] = float4(uv, sample.r - _RenderParams.w, sample.g + _RenderParams.w);
                }

                const float fractional_part = t - base_index;
                const float p1 = basis0(fractional_part) * samples[0].z + basis1(fractional_part) * samples[1].z + basis2(fractional_part) * samples[2].z;
                const float p2 = basis0(fractional_part) * samples[0].w + basis1(fractional_part) * samples[1].w + basis2(fractional_part) * samples[2].w;

                const float y = iuv.y * 2 - 1;
                const float distance = abs(y - clamp(y, p1, p2));

                final_color = lerp(outside_zone_wave ? _OutsideZoneWaveColor : _WaveColor, final_color, saturate(distance * _RenderParams.y));
#endif
                return final_color;
            }
            ENDCG
        }
    }
}
