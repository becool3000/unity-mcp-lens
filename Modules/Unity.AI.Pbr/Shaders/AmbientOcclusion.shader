/*
* @author Morten Mikkelsen
* Contact: mortenm@unity3d.com
*/

Shader "Hidden/AIToolkit/AmbientOcclusion"
{

    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _DispScale("Displacement Scale", Float) = 23.0
        _Clamp("Clamp", Float) = 0.0
    }

    SubShader
    {

        ZTest Always Cull Off ZWrite Off

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        uniform float4 _MainTex_TexelSize;
        float _DispScale;
        float _Clamp;

        #ifndef M_PI
        #define M_PI        3.1415926535897932384626433832795
        #endif

        struct appdata_t
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct v2f
        {
            float4 vertex : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        v2f vert(appdata_t v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.uv = v.uv;
            return o;
        }
        ENDCG


        // Pass 0
        Pass
        {
            Name "Unpack"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float FindTan(const float3 vPcen, const float3 vPcur)
            {
                float3 vD = vPcur - vPcen;
                float b = vD.z;

                return b / sqrt(vD.x * vD.x + vD.y * vD.y);
            }

            bool DoClip(out float2 vRes, float2 Pa, float2 Pb, int iW, int iH)
            {
                float fXa = Pa.x, fYa = Pa.y, fXb = Pb.x, fYb = Pb.y;

                float fX0 = fXb;
                float fY0 = fYb;

                bool bIfClip = false;

                //#ifdef CLIP_ENABLED
                if (_Clamp != 0)
                {
                    bool bOutSBound = fX0 < 0 || fX0 > iW;
                    bool bOutTBound = fY0 < 0 || fY0 > iH;
                    bIfClip = bOutSBound || bOutTBound;

                    if (bIfClip)
                    {
                        if (bOutSBound)
                        {
                            float fB = fX0 < 0 ? 0 : iW;
                            fY0 = fYa + ((fB - fXa) / (fX0 - fXa)) * (fY0 - fYa);
                            fX0 = fB;

                            bOutTBound = fY0 < 0 || fY0 > iH;
                        }

                        if (bOutTBound)
                        {
                            float fB = fY0 < 0 ? 0 : iH;
                            fX0 = fXa + ((fB - fYa) / (fY0 - fYa)) * (fX0 - fXa);
                            fY0 = fB;
                        }
                    }
                }
                //#endif

                vRes = float2(fX0, fY0);

                return bIfClip;
            }

            float4 frag(v2f i) : SV_Target
            {
                //ivec2 iPixLoc = ivec2( floor(gl_FragCoord.xy) );
                //int index = ((g_iHeight-1)-iPixLoc.y)*g_iWidth + iPixLoc.x;
                //float fval = texelFetch(g_HeightsBuffer, index).x;

                float adjustedDispScale = _DispScale;
                float fXcen = i.vertex.x;
                float fYcen = i.vertex.y;
                int iW = _MainTex_TexelSize.z;
                int iH = _MainTex_TexelSize.w;

                float fHeight_cen = 0;

                float dHdx = 0;
                float dHdy = 0;
                for (int j = 0; j < 3; j++)
                    for (int i = 0; i < 3; i++)
                    {
                        //float fH = Sample(mip_loc, pfHeights, iW, iH, (fXcen+i-1)/iW, (fYcen+j-1)/iH, 0.0);
                        float fH = adjustedDispScale * tex2Dlod(
                            _MainTex, float4((fXcen + i - 1) / iW, (fYcen + j - 1) / iH, 0.0, 0.0)).x;

                        int iWeightU = (i - 1) * ((j & 1) + 1);
                        int iWeightV = (j - 1) * ((i & 1) + 1);

                        dHdx += iWeightU * fH;
                        dHdy += iWeightV * fH;

                        if (i == 1 && j == 1) fHeight_cen = fH;
                    }

                // normalize weights
                dHdx /= 8;
                dHdy /= 8;

                // local position:  (fXcen, fYcen, fHeight_cen)
                float3 vPcen = float3(fXcen, fYcen, fHeight_cen);

                // local normal: (-dHdx, -dHdy, 1)
                float3 vNcen = normalize(float3(-dHdx, -dHdy, 1.0));
                //vec3 vNcen = vec3(0,0,1);

                int iQsteps = 2;
                int dim = iQsteps * 2 + 1;

                int iNrRots = 4 * (dim - 1);

                float fOcc = 0; // all shadow

                for (int r = 0; r < iNrRots; r++)
                {
                    float fAngle = (r * 2 * M_PI) / iNrRots;
                    float fVx = cos(fAngle);
                    float fVy = sin(fAngle);

                    float fTanT = -(vNcen.x * fVx + vNcen.y * fVy) / vNcen.z;
                    float fSinT = fTanT / sqrt(1 + fTanT * fTanT);

                    float sgn_x = fVx < 0 ? (-1.0) : 1.0;
                    float sgn_y = fVy < 0 ? (-1.0) : 1.0;

                    float fVx_abs = abs(fVx);
                    float fVy_abs = abs(fVy);
                    bool bSwaped = fVy_abs < fVx_abs;
                    float fVxo = bSwaped ? fVy_abs : fVx_abs; // symmetries done
                    float fVyo = bSwaped ? fVx_abs : fVy_abs;

                    //assert(fVyo>0);

                    //float fMaxTan = 0.0;
                    float fMaxTan = 0; //-FLT_MAX;

                    // offset ST by (fVxo/fVyo, 1.0)
                    for (int s = 0; s < iQsteps; s++)
                    {
                        float fVxs_tmp = (fVxo * (s + 1)) / fVyo;
                        float fVys_tmp = (s + 1);

                        float fVxs = sgn_x * (bSwaped ? fVys_tmp : fVxs_tmp);
                        float fVys = sgn_y * (bSwaped ? fVxs_tmp : fVys_tmp);

                        // current point: (fX0, fY0, fHeight0)
                        float fXtmp = fXcen + fVxs;
                        float fYtmp = fYcen + fVys;
                        float fX0, fY0;
                        float2 vCres;
                        bool bIfClip = DoClip(vCres, float2(fXcen, fYcen), float2(fXtmp, fYtmp), iW, iH);
                        fX0 = vCres.x;
                        fY0 = vCres.y;

                        //float fHeight0 = Sample(mip_loc, pfHeights, iW, iH, (fX0/iW), (fY0/iH), 0.0);
                        float fHeight0 = adjustedDispScale * tex2Dlod(_MainTex, float4(fX0 / iW, fY0 / iH, 0.0, 0.0)).x;
                        float3 vPcur = float3(fX0, fY0, fHeight0);


                        float fTan = FindTan(vPcen, vPcur);
                        if (s == 0) fMaxTan = fTan;
                        else if (fMaxTan < fTan) fMaxTan = fTan;
                    }

                    // do coarser searching
                    float dHalfAngle = (2 * M_PI) / (2 * iNrRots);

                    float fVxs_tmp = (fVxo * iQsteps) / fVyo;
                    float fVys_tmp = iQsteps;
                    float dist = sqrt(fVxs_tmp * fVxs_tmp + fVys_tmp * fVys_tmp);
                    //float nx = cos(dHalfAngle);
                    float ny = sin(dHalfAngle);
                    float fRad = ny * dist;
                    dist += fRad;

                    int iMax = 16;
                    float fStop = sqrt(float(iW * iW + iH * iH));
                    int i = 0;
                    while (i < iMax && dist < fStop)
                    {
                        float prev_dist = dist;
                        dist /= (1 - ny);

                        // build lookup
                        float fVxs = dist * fVx;
                        float fVys = dist * fVy;

                        // current point: (fX0, fY0, fHeight0)
                        float fXtmp = fXcen + fVxs;
                        float fYtmp = fYcen + fVys;
                        float fX0, fY0;

                        float2 vCres;
                        bool bIfClip = DoClip(vCres, float2(fXcen, fYcen), float2(fXtmp, fYtmp), iW, iH);
                        fX0 = vCres.x;
                        fY0 = vCres.y;

                        if (bIfClip)
                        {
                            float dx = fX0 - fXcen;
                            float dy = fY0 - fYcen;
                            dist = sqrt(dx * dx + dy * dy);
                        }

                        // calc LOD
                        fRad = ny * dist;
                        float fArea = fRad * fRad * M_PI;
                        //float fLod = 0.5*(log(fArea) / log(2.0));
                        float fLod = 0.5 * log2(fArea);

                        //float fHeight0 = SampleTri(mip_loc, pfHeights, iW, iH, fX0/iW, fY0/iH, fLod);
                        float fHeight0 = adjustedDispScale * tex2Dlod(_MainTex, float4(fX0 / iW, fY0 / iH, 0.0, fLod)).x;
                        float3 vPcur = float3(fX0, fY0, fHeight0);


                        float fTan = FindTan(vPcen, vPcur);
                        if (fMaxTan < fTan) fMaxTan = fTan;

                        // next tangent
                        dist += fRad;
                        ++i;

                        // stop
                        if (bIfClip) i == iMax;
                    }

                    // accumulate
                    //float fAngleTan = atan(fMaxTan);
                    //float si = sinf(fAngleTan);

                    //float si = (fMaxTan*fMaxTan) / (1+fMaxTan*fMaxTan);
                    float si = fMaxTan / sqrt(1 + fMaxTan * fMaxTan);

                    if (si > fSinT)
                    {
                        //float ao = (si - fSinT);
                        float ao = vNcen.z * (si * si - fSinT * fSinT);
                        float fDot;
                        //#if 1
                        fDot = fVx * vNcen.x + fVy * vNcen.y;
                        ao += fDot * ((asin(si) - asin(fSinT)) + (si * sqrt(1 - si * si) - fSinT * sqrt(
                            1 - fSinT * fSinT)));
                        //#endif
                        if (ao > 0) fOcc += ao;
                    }
                }

                // apply LP AO
                fOcc /= iNrRots;
                float fAO = 1 - fOcc;

                //gl_FragColor = vec4(fAO, fAO, fAO, fAO);
                return float4(fAO, fAO, fAO, fAO);
            }
            ENDCG
        }
    }
    Fallback Off
}
