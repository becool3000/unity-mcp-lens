Shader "Hidden/AIToolkit/ProgressDisk"
{
    Properties
    {
        _MainTex ("Dummy Texture (Used for Aspect Ratio)", 2D) = "white" {} // Needed for _TexelSize
        _StartValue ("Start Value", Range(0, 1)) = 0         // Start value from 0 to 1 (fraction of circle)
        _Value ("Value", Range(0, 1)) = 1                    // End value from 0 to 1 (fraction of circle)
        _InnerRadius ("Inner Radius", Range(0, 1)) = 0.55    // Inner radius (0 to 1, where 1 is half the height)
        _OuterRadius ("Outer Radius", Range(0, 1)) = 0.65    // Outer radius (0 to 1, where 1 is half the height)
        _BackgroundColor ("Background Color", Color) = (0,0,0,0) // Background color (can be transparent)
        _Color ("Disk Color", Color) = (0.5,0.5,0.5,1)       // Disk segment color (alpha is used)
    }
    SubShader
    {
        // Use Transparency settings for UI or overlays
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        LOD 100

        Pass
        {
            ZWrite Off          // Don't write to depth buffer (typical for UI/overlays)
            Cull Off            // Don't cull back faces (useful for quads)
            Blend SrcAlpha OneMinusSrcAlpha // Standard alpha blending

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0 // Needed for fwidth

            #include "UnityCG.cginc"

            sampler2D _MainTex; // Declare sampler even if not used for color
            float4 _MainTex_TexelSize; // Contains (1/width, 1/height, width, height)

            float _StartValue;
            float _Value;
            float _InnerRadius;
            float _OuterRadius;
            float4 _Color;
            float4 _BackgroundColor;

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

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                // Pass UVs directly, aspect correction happens in fragment shader
                o.uv = v.uv;
                return o;
            }

            // Helper to wrap angle to [-PI, PI]
            float wrapAngle(float angle) {
                return fmod(angle + UNITY_PI, UNITY_TWO_PI) - UNITY_PI;
            }

            float4 frag(v2f i) : SV_Target
            {
                // --- Aspect Ratio Correction ---
                float aspect = 1.0;
                if (_MainTex_TexelSize.x > 0.0) {
                    aspect = _MainTex_TexelSize.y / _MainTex_TexelSize.x;
                }
                float2 pos = (i.uv - 0.5) * 2.0;
                pos.x *= aspect;

                // --- Calculations in Corrected Space ---
                float dist = length(pos);
                // We still need the original angle for the wrapAngle helper if we were to use it,
                // but the new method avoids it for the main body.
                // float angle = atan2(pos.x, pos.y);

                // --- Define Arc ---
                float startNorm = frac(_StartValue);
                float range = _Value - _StartValue;

                if (abs(range) < 1e-5) return _BackgroundColor;
                if (range >= (1.0 - 1e-5)) range = 1.0;

                float startAngle = startNorm * UNITY_TWO_PI;
                float endAngle = startNorm * UNITY_TWO_PI + range * UNITY_TWO_PI;

                float arcCenter = (startAngle + endAngle) * 0.5;
                float arcHalfWidth = (endAngle - startAngle) * 0.5;

                // --- Signed Distance Field (SDF) ---
                // 1. Radial distance (distance from the ring shape)
                float dist_radial = max(dist - _OuterRadius, _InnerRadius - dist);

                // 2. Angular distance (distance from the valid arc) - ROBUST METHOD
                float dist_angular;
                {
                    // Rotate the pixel's position so the arc is centered on the +Y axis
                    float s = sin(-arcCenter);
                    float c = cos(-arcCenter);
                    float2x2 rot = float2x2(c, -s, s, c);
                    float2 rotated_pos = mul(rot, pos);

                    // Fold the space over the Y-axis. Now we only need to test against one edge.
                    rotated_pos.x = abs(rotated_pos.x);

                    // 'sc' is the direction vector of the arc's edge (at angle arcHalfWidth from +Y)
                    float2 sc = float2(sin(arcHalfWidth), cos(arcHalfWidth));

                    // Calculate signed distance to the edge line.
                    // This is dot(rotated_pos, normal_vector_of_edge).
                    // It's positive outside the wedge, negative inside.
                    // This works for arcHalfWidth > PI/2 as well, correctly defining the "long way around" wedge.
                    dist_angular = dot(rotated_pos, float2(sc.y, -sc.x));
                }


                // 3. End caps (circular caps at start and end angles)
                float capRadius = (_OuterRadius - _InnerRadius) * 0.5;
                float capCenterRadius = (_OuterRadius + _InnerRadius) * 0.5;
                float2 startCapCenter = float2(-sin(startAngle), cos(startAngle)) * capCenterRadius;
                float2 endCapCenter = float2(-sin(endAngle), cos(endAngle)) * capCenterRadius;
                float dist_startCap = length(pos - startCapCenter) - capRadius;
                float dist_endCap = length(pos - endCapCenter) - capRadius;

                float sdf;

                // If it's a full circle, just use the radial distance.
                if (range >= 1.0) {
                    sdf = dist_radial;
                } else {
                    // Union of the angular wedge and the two end caps
                    float dist_arc_with_caps = min(dist_angular, min(dist_startCap, dist_endCap));
                    // Intersection of that shape with the ring
                    sdf = max(dist_radial, dist_arc_with_caps);
                }

                // --- Anti-Aliasing using fwidth ---
                float aa = fwidth(sdf) * 0.707;
                float coverage = smoothstep(aa, -aa, sdf);

                // --- Final Color ---
                float4 finalColor = _Color;
                finalColor.a *= coverage;

                return lerp(_BackgroundColor, finalColor, finalColor.a);
            }

            ENDCG
        }
    }
    Fallback "Transparent/VertexLit" // Fallback for platforms that don't support the shader
}
