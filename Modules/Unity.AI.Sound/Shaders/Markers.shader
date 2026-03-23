Shader "Hidden/AIToolkit/Markers"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _RenderParams ("Render Parameters", Vector) = (1, 1, 1, 1)
        _CursorColor ("Cursor Color", Color) = (1, 1, 1, 1)
        _StartMarkerColor ("Start Marker Color", Color) = (0.18, 0.7, 0.45, 1)
        _EndMarkerColor ("End Marker Color", Color) = (0.18, 0.7, 0.45, 1)
        _OutsideZoneWaveColor ("Outside Zone Wave Color", Color) = (0.5, 0.27, 0, 1)
        _PlaybackPosition ("Playback Position", Float) = 0.0
        _StartMarkerPosition ("Start Marker Position", Float) = 0.0
        _EndMarkerPosition ("End Marker Position", Float) = 1.0
        _ShowCursor ("Show Cursor", Float) = 1.0
        _ShowMarker ("Show Marker", Float) = 0.0
        _ShowControlLines ("Show Control Lines", Int) = 0
        _ShowControlPoints ("Show Control Point", Int) = 0
        _SelectedPointIndex ("Selected Control Point", Int) = -1
        _ControlPointCount ("Control Point Count", Int) = 0
        _ControlPointColor ("Control Point Color", Color) = (0.92, 0.92, 0.92, 1)
        _SelectedControlPointColor ("Selected Control Point Color", Color) = (0, 0.6, 1, 1)
        _ControlLineColor ("Control Line Color", Color) = (0.18, 0.7, 0.45, 1)
        _LineThickness ("Line Thickness", Float) = 0.001
        _PointSize ("Point Size", Float) = 0.032
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
            SamplerState linear_clamp;
            float4 _RenderParams;
            float4 _CursorColor;
            float4 _StartMarkerColor;
            float4 _EndMarkerColor;
            float4 _OutsideZoneWaveColor;
            float _PlaybackPosition;
            float _StartMarkerPosition;
            float _EndMarkerPosition;
            float _ShowCursor;
            float _ShowMarker;
            int _ShowControlLines;
            int _ShowControlPoints;
            float4 _ControlPointColor;
            float4 _SelectedControlPointColor;
            float4 _ControlLineColor;
            float _LineThickness;
            float _PointSize;
            float _RecPixelSize;
            float _Padding;

            StructuredBuffer<float2> _ControlPoints;
            int _ControlPointCount;
            int _SelectedPointIndex;

            float distance(const float2 xy, const float2 p1, const float2 p2)
            {
                const fixed2 ba = p2 - p1;
                const fixed2 pa = xy - p1;
                const fixed2 h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0) * ba;
                return length(pa - h);
            }

            float4 sdf(const float d, const float r, const float4 c, const float4 b)
            {
                return lerp(b, c, smoothstep(r, r - fwidth(d), d));
            }

            float4 draw_point(const float2 xy, const float2 p, const float r, const float4 c, const float4 b)
            {
                const float d = length(xy - p);
                return sdf(d, r, c, b);
            }

            float4 draw_line(const float2 xy, const float2 p1, const float2 p2, const float r, const float4 c, const float4 b)
            {
                const float d = distance(xy, p1, p2);
                return sdf(d, r, c, b);
            }

            float4 draw_point_screen(const float2 xy, const float2 p, const float r, const float4 c, const float4 b)
            {
                return draw_point(xy * _RenderParams.xy, p * _RenderParams.xy, r, c, b);
            }

            float4 draw_line_screen(const float2 xy, const float2 p1, const float2 p2, const float r, const float4 c, const float4 b)
            {
                return draw_line(xy * _RenderParams.xy, p1 * _RenderParams.xy, p2 * _RenderParams.xy, r, c, b);
            }

            float4 frag(varyings i) : SV_Target
            {
                // Allow about 8 (PADDING) pixels above and below for some space for round markers
                i.uv = (i.uv - float2(0, 0.5)) / float2(1, 1 - _Padding * _RenderParams.w) + float2(0, 0.5);

                // draw background
                float4 final_color = _MainTex.Sample(linear_clamp, i.uv);

                // draw 0-line (purely procedural, no need for extra samples)
                const float line_thickness = clamp(_RecPixelSize * _RenderParams.y, 1, 1);
                const float2 axis1 = float2(-LARGE, 0.5);
                const float2 axis2 = float2(LARGE, 0.5);
                const float4 color = draw_line_screen(i.uv, axis1, axis2, line_thickness, _OutsideZoneWaveColor, final_color);
                final_color = max(color, final_color);

                // playback (purely procedural, no need for extra samples, true for any other draws at this point)
                const float marker_thickness = clamp(_LineThickness * _RenderParams.y, 1.5, 3);
                if (_ShowCursor > 0.5)
                {
                    const float2 p1 = float2(_PlaybackPosition, 0);
                    const float2 p2 = float2(_PlaybackPosition, 1);
                    final_color = draw_line_screen(i.uv, p1, p2, marker_thickness, _CursorColor, final_color);
                }

                // markers
                if (_ShowMarker > 0.5)
                {
                    // Start marker
                    const float2 p1 = float2(_StartMarkerPosition, 0);
                    const float2 p2 = float2(_StartMarkerPosition, 1);
                    final_color = draw_line_screen(i.uv, p1, p2, marker_thickness, _StartMarkerColor, final_color);
                    // End marker
                    const float2 p3 = float2(_EndMarkerPosition, 0);
                    const float2 p4 = float2(_EndMarkerPosition, 1);
                    final_color = draw_line_screen(i.uv, p3, p4, marker_thickness, _EndMarkerColor, final_color);
                }

                // control lines
                if (_ShowControlLines > 0 && _ControlPointCount > 0)
                {
                    const float2 p0 = float2(-LARGE, _ControlPoints[0].y);
                    const float2 pn = float2(LARGE, _ControlPoints[_ControlPointCount - 1].y);
                    for (int j = 0; j <= _ControlPointCount; j++)
                    {
                        const float2 p1 = j == 0 ? p0 : _ControlPoints[j-1];
                        const float2 p2 = j == _ControlPointCount ? pn : _ControlPoints[j];
                        final_color = draw_line_screen(i.uv, p1, p2, marker_thickness, _ControlLineColor, final_color);
                    }
                }

                if (_ShowControlPoints > 0)
                {
                    // control lines
                    if (_ControlPointCount == 0)
                    {
                        const float2 p1 = float2(-LARGE, 1);
                        const float2 p2 = float2(LARGE, 1);
                        final_color = draw_line_screen(i.uv, p1, p2, marker_thickness, _ControlLineColor, final_color);
                    }

                    // control points
                    const float r = clamp(_PointSize * _RenderParams.y, 1.5, 8);
                    for (int j = 0; j < _ControlPointCount; j++)
                    {
                        const float2 p = _ControlPoints[j];
                        final_color = draw_point_screen(i.uv, p, r, j == _SelectedPointIndex ? _SelectedControlPointColor : _ControlPointColor, final_color);
                    }
                }

                return final_color;
            }
            ENDCG
        }
    }
}
