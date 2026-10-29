Shader "Strata/TileGround"
{
    Properties
    {
        [NoScaleOffset] _GroundAlbedo ("Layer Albedo (array)", 2DArray) = "" {}
        [NoScaleOffset] _GroundNormal ("Layer Normal + Height (array)", 2DArray) = "" {}
        [NoScaleOffset] _GroundORM ("Layer ORM (array)", 2DArray) = "" {}
        [NoScaleOffset] _Splat0 ("Splat 0-3", 2D) = "black" {}
        [NoScaleOffset] _Splat1 ("Splat 4-7", 2D) = "black" {}
        [NoScaleOffset] _Splat2 ("Splat 8-11", 2D) = "black" {}
        [NoScaleOffset] _Mask ("Ledge Mask (drop, rise, rims, stair)", 2D) = "black" {}
        [NoScaleOffset] _NoiseMap ("Noise", 2D) = "gray" {}
        [NoScaleOffset] _EdgeAlbedo ("Edge Albedo (array)", 2DArray) = "" {}
        [NoScaleOffset] _EdgeTable ("Edge Table", 2D) = "black" {}
        [HideInInspector] _StrataCodes ("Code Format", Float) = 0
        _World ("Paint Rect (x0, z0, w, h)", Vector) = (-32, -32, 64, 64)
        _Grid ("Grid Rect (x0, z0, w, h)", Vector) = (-32, -32, 64, 64)
        _Prio0 ("Priority 0-3", Vector) = (0, 0, 0, 0)
        _Prio1 ("Priority 4-7", Vector) = (0, 0, 0, 0)
        _Prio2 ("Priority 8-11", Vector) = (0, 0, 0, 0)
        _Class0 ("Class 0-3", Vector) = (0, 0, 0, 0)
        _Class1 ("Class 4-7", Vector) = (0, 0, 0, 0)
        _Class2 ("Class 8-11", Vector) = (0, 0, 0, 0)
        _Wob0 ("Wobble 0-3", Vector) = (0, 0, 0, 0)
        _Wob1 ("Wobble 4-7", Vector) = (0, 0, 0, 0)
        _Wob2 ("Wobble 8-11", Vector) = (0, 0, 0, 0)
        _Rag0 ("Raggedness 0-3", Vector) = (0, 0, 0, 0)
        _Rag1 ("Raggedness 4-7", Vector) = (0, 0, 0, 0)
        _Rag2 ("Raggedness 8-11", Vector) = (0, 0, 0, 0)
        _Macro ("Macro Colour Drift", Range(0, 1)) = 0.6
        _Brightness ("Brightness", Range(0.5, 1.5)) = 1
        _SlopeLayer ("Layer on Steep Slopes (-1 none)", Float) = -1
        _TurfLip ("Turf Lip", Color) = (0.46, 0.6, 0.26, 1)
        _CopingDark ("Coping (dark)", Color) = (0.36, 0.32, 0.24, 1)
        _CopingLight ("Coping (light)", Color) = (0.54, 0.49, 0.37, 1)
        _StonesDark ("Field Stones (dark)", Color) = (0.4, 0.3, 0.16, 1)
        _StonesLight ("Field Stones (light)", Color) = (0.6, 0.48, 0.28, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "StrataTiles.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _World, _Grid;
            float4 _Prio0, _Prio1, _Prio2, _Class0, _Class1, _Class2, _Wob0, _Wob1, _Wob2, _Rag0, _Rag1, _Rag2;
            float _Macro, _Brightness, _SlopeLayer, _StrataCodes;
            half4 _TurfLip, _CopingDark, _CopingLight, _StonesDark, _StonesLight;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_GroundAlbedo); SAMPLER(sampler_GroundAlbedo);
            TEXTURE2D_ARRAY(_GroundNormal);
            TEXTURE2D_ARRAY(_GroundORM);
            TEXTURE2D(_Splat0); SAMPLER(sampler_Splat0);
            TEXTURE2D(_Splat1);
            TEXTURE2D(_Splat2);
            TEXTURE2D(_Mask);
            TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
            TEXTURE2D_ARRAY(_EdgeAlbedo);
            TEXTURE2D(_EdgeTable);

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Tab(float4 a, float4 b, float4 c, int k) { float4 v = k < 4 ? a : (k < 8 ? b : c); return v[k & 3]; }
            float Prio(int k) { return Tab(_Prio0, _Prio1, _Prio2, k); }
            float Cls(int k) { return Tab(_Class0, _Class1, _Class2, k); }
            float Wob(int k) { return Tab(_Wob0, _Wob1, _Wob2, k); }
            float Rag(int k) { return Tab(_Rag0, _Rag1, _Rag2, k); }

            float TopGrid(int k) { float g = LOAD_TEXTURE2D(_EdgeTable, int2(k, 4)).r; return g > 0.5 ? min(g, ST_TEX) : ST_TEX; }

            void Strongest(float2 p, out int a, out int b, out float wa, out float wb)
            {
                float2 uv = (p - _World.xy) / _World.zw;
                float4 s0 = SAMPLE_TEXTURE2D_LOD(_Splat0, sampler_Splat0, uv, 0);
                float4 s1 = SAMPLE_TEXTURE2D_LOD(_Splat1, sampler_Splat0, uv, 0);
                float4 s2 = SAMPLE_TEXTURE2D_LOD(_Splat2, sampler_Splat0, uv, 0);
                float w[12] = { s0.r, s0.g, s0.b, s0.a, s1.r, s1.g, s1.b, s1.a, s2.r, s2.g, s2.b, s2.a };
                a = 0; b = 1; wa = -1.0; wb = -1.0;
                [unroll] for (int k = 0; k < 12; k++)
                {
                    float v = w[k];
                    if (v > wa) { b = a; wb = wa; a = k; wa = v; }
                    else if (v > wb) { b = k; wb = v; }
                }
            }

            int LayerAt(float2 p)
            {
                int a, b; float wa, wb;
                Strongest(p, a, b, wa, wb);
                float G = min(TopGrid(a), TopGrid(b));
                if (G < ST_TEX) { p = (floor(p * G) + 0.5) / G; Strongest(p, a, b, wa, wb); }
                float2 q = floor(p * G);
                float amp = 0.5 * (Wob(a) + Wob(b)), rg = 0.5 * (Rag(a) + Rag(b));
                float n = (stVN(q * 0.22 + (float)a * 7.13) - stVN(q * 0.22 + (float)b * 7.13)) * amp;
                n += (stHash(q + (float)(a * 13 + b)) - 0.5) * rg;
                return (wa - wb + n > 0.0) ? a : b;
            }

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 Nw = normalize(ST_ToGrid(i.normalWS));
                float2 wp = float2(i.positionWS.x, -i.positionWS.z);
                float2 tix = floor(wp * ST_TEX);
                float2 tcc = (tix + 0.5) / ST_TEX;
                float dT = 1.0 / ST_TEX;
                int lay = LayerAt(tcc);
                if (Nw.y < 0.78 && _SlopeLayer >= 0) lay = (int)_SlopeLayer;
                float Gl = TopGrid(lay), dG = 1.0 / Gl;
                float2 pc = (floor(tcc * Gl) + 0.5) / Gl;
                int lN = LayerAt(pc + float2(0.0, -dG)), lS = LayerAt(pc + float2(0.0, dG));
                int lE = LayerAt(pc + float2(dG, 0.0)), lW = LayerAt(pc + float2(-dG, 0.0));
                float2 fuv = float2(tix.x + 0.5, -tix.y - 0.5) / 256.0;
                float2 gx = float2(ddx(wp.x), -ddx(wp.y)) / 16.0, gy = float2(ddy(wp.x), -ddy(wp.y)) / 16.0;
                float4 alb = SAMPLE_TEXTURE2D_ARRAY_GRAD(_GroundAlbedo, sampler_GroundAlbedo, fuv, lay, gx, gy);
                float4 nrm = SAMPLE_TEXTURE2D_ARRAY_GRAD(_GroundNormal, sampler_GroundAlbedo, fuv, lay, gx, gy);
                float4 tO = SAMPLE_TEXTURE2D_ARRAY_GRAD(_GroundORM, sampler_GroundAlbedo, fuv, lay, gx, gy);
                float3 col = alb.rgb;

                float pm = Prio(lay), cm = Cls(lay);
                float3 tint = 1.0;
                if (Prio(lS) < pm - 0.25 && Cls(lS) != cm)
                    tint = cm == 1.0 ? float3(0.6, 0.68, 0.56) : (cm == 2.0 ? float3(1.12, 1.1, 1.06) : float3(0.82, 0.82, 0.82));
                else if (Prio(lN) < pm - 0.25 && Cls(lN) != cm)
                    tint = cm == 1.0 ? float3(1.16, 1.2, 1.08) : (cm == 2.0 ? float3(1.2, 1.18, 1.12) : float3(1.06, 1.06, 1.06));
                else if ((Prio(lE) < pm - 0.25 && Cls(lE) != cm) || (Prio(lW) < pm - 0.25 && Cls(lW) != cm))
                    tint = cm == 1.0 ? float3(0.8, 0.86, 0.76) : (cm == 2.0 ? float3(1.1, 1.08, 1.04) : float3(0.9, 0.9, 0.9));
                else if (Prio(lN) > pm + 0.25 && Cls(lN) != cm)
                    tint = Cls(lN) == 2.0 ? float3(0.62, 0.6, 0.6) : float3(0.7, 0.68, 0.72);
                else if ((Prio(lE) > pm + 0.25 && Cls(lE) != cm) || (Prio(lW) > pm + 0.25 && Cls(lW) != cm))
                    tint = 0.84;
                else if (lay != lN && Cls(lN) == cm && cm == 2.0)
                    tint = 0.72;
                col *= tint;

                int2 cell = int2(floor(wp - _Grid.xy));
                //! Ledge RG store drop/rise bits; B packs N/E edges, A packs S/W edges.
                float4 mk = LOAD_TEXTURE2D(_Mask, cell) * 255.0 + 0.5;
                int drop = (int)mk.r, rise = (int)mk.g, rimsNE = (int)mk.b, rimsSW = (int)mk.a;
                float2 lt = tix - floor(wp) * ST_TEX;
                float dN = lt.y, dS = ST_TEX - 1.0 - lt.y, dW = lt.x, dE = ST_TEX - 1.0 - lt.x;
                float rimD = 99.0; int rimSide = -1;
                if ((drop & 1) != 0 && dN < rimD) { rimD = dN; rimSide = 0; }
                if ((drop & 2) != 0 && dE < rimD) { rimD = dE; rimSide = 1; }
                if ((drop & 4) != 0 && dS < rimD) { rimD = dS; rimSide = 2; }
                if ((drop & 8) != 0 && dW < rimD) { rimD = dW; rimSide = 3; }
                if ((drop & 16) != 0 && (drop & 3) == 0) { float d = max(dN, dE); if (d < rimD) { rimD = d; rimSide = dN < dE ? 1 : 0; } }
                if ((drop & 32) != 0 && (drop & 6) == 0) { float d = max(dS, dE); if (d < rimD) { rimD = d; rimSide = dS < dE ? 1 : 2; } }
                if ((drop & 64) != 0 && (drop & 12) == 0) { float d = max(dS, dW); if (d < rimD) { rimD = d; rimSide = dS < dW ? 3 : 2; } }
                if ((drop & 128) != 0 && (drop & 9) == 0) { float d = max(dN, dW); if (d < rimD) { rimD = d; rimSide = dN < dW ? 3 : 0; } }
                if (rimSide >= 0)
                {
                    int edge = ((rimSide < 2 ? rimsNE : rimsSW) >> ((rimSide & 1) * 4)) & 15;
                    float4 eP = LOAD_TEXTURE2D(_EdgeTable, int2(edge, 0));
                    int style = (int)round(eP.r * 255.0);
                    bool eTex = eP.g > 0.5;
                    float3 eDark = LOAD_TEXTURE2D(_EdgeTable, int2(edge, 1)).rgb, eLight = LOAD_TEXTURE2D(_EdgeTable, int2(edge, 2)).rgb;
                    float3 eCol = eTex ? SAMPLE_TEXTURE2D_ARRAY_GRAD(_EdgeAlbedo, sampler_GroundAlbedo, fuv, edge, gx, gy).rgb : eLight;
                    float along = (rimSide == 0 || rimSide == 2) ? tix.x : tix.y;
                    bool corner2 = ((drop & 1) != 0 || (drop & 4) != 0) && ((drop & 2) != 0 || (drop & 8) != 0);
                    if (style >= 2)
                    {
                        float4 r = stRim(along, rimD, style);
                        float dA = (drop & 1) != 0 ? dN : ((drop & 4) != 0 ? dS : 99.0), dB = (drop & 2) != 0 ? dE : ((drop & 8) != 0 ? dW : 99.0);
                        float depth = style == 2 ? 4.0 : 6.0;
                        if (corner2 && dA < depth && dB < depth)
                        {
                            float cA = 1.0 - (dA + 0.5) / depth * 2.0, cB = 1.0 - (dB + 0.5) / depth * 2.0;
                            r = float4(stHash(tix * 0.01 + 3.3), (dA > depth - 1.5 && dB > depth - 1.5 && style == 3) ? 2.0 : 0.0, 0.0, 0.0);
                            float2 en = float2(((drop & 2) != 0 ? cB : -cB), ((drop & 1) != 0 ? cA : -cA)) * 0.8;
                            if (r.y < 1.5) { col = stEdgeColor(r, eDark, eLight, eTex, eCol); nrm = float4(normalize(float3(en, 1.0)) * 0.5 + 0.5, 0.85); }
                        }
                        else if (r.y < 1.5)
                        {
                            col = stEdgeColor(r, eDark, eLight, eTex, eCol);
                            float2 dn = style == 3 ? r.zw * 0.85 : float2(0.0, r.w > 0.45 ? 0.55 : 0.0);
                            float2 en = rimSide == 0 ? dn : (rimSide == 2 ? float2(dn.x, -dn.y) : (rimSide == 1 ? float2(dn.y, -dn.x) : float2(-dn.y, -dn.x)));
                            nrm = float4(normalize(float3(en, 1.0)) * 0.5 + 0.5, 0.85);
                        }
                        else if (r.y > 1.5 && rimD < depth + 0.5) col *= 0.5;
                        else if (rimD < depth + 1.0) col *= 0.55;
                        else if (rimD < depth + 2.0) col *= 0.82;
                    }
                    else if (style == 1)
                    {
                        float rag = stHash(float2(along, 2.3));
                        float lip = 1.0 + step(0.55, rag) + step(0.85, rag);
                        if (rimD < 0.5) col = lerp(col, eCol, 0.5);
                        else if (rimD < lip + 0.5) col *= 1.1;
                        if (rimD < lip + 0.5)
                        {
                            float2 dn = float2(0.0, 0.45);
                            float2 en = rimSide == 0 ? dn : (rimSide == 2 ? float2(dn.x, -dn.y) : (rimSide == 1 ? float2(dn.y, -dn.x) : float2(-dn.y, -dn.x)));
                            nrm = float4(normalize(float3(en, 1.0)) * 0.5 + 0.5, nrm.a);
                        }
                    }
                }
                float aoD = 99.0;
                if ((rise & 1) != 0) aoD = min(aoD, dN);
                if ((rise & 2) != 0) aoD = min(aoD, dE);
                if ((rise & 4) != 0) aoD = min(aoD, dS);
                if ((rise & 8) != 0) aoD = min(aoD, dW);
                if ((rise & 16) != 0 && (rise & 3) == 0) aoD = min(aoD, max(dN, dE));
                if ((rise & 32) != 0 && (rise & 6) == 0) aoD = min(aoD, max(dS, dE));
                if ((rise & 64) != 0 && (rise & 12) == 0) aoD = min(aoD, max(dS, dW));
                if ((rise & 128) != 0 && (rise & 9) == 0) aoD = min(aoD, max(dN, dW));
                if (aoD < 6.0) col *= aoD < 1.0 ? 0.5 : (aoD < 2.0 ? 0.62 : (aoD < 3.0 ? 0.74 : (aoD < 4.5 ? 0.85 : 0.93)));

                float mv = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, wp * 0.012).b;
                col *= lerp(float3(1, 1, 1), lerp(float3(0.9, 0.95, 0.92), float3(1.08, 1.04, 0.96), mv), _Macro);
                col *= i.color.rgb * lerp(0.82, 1.0, tO.r) * _Brightness;

                float3 tN = nrm.rgb * 2.0 - 1.0;
                float3 T = normalize(float3(1.0, 0.0, 0.0) - Nw * Nw.x);
                float3 B = normalize(cross(Nw, T));
                float3 normalWS = normalize(ST_ToGrid(T * tN.x + B * tN.y + Nw * max(tN.z, 0.2)));

                SurfaceData s = (SurfaceData)0;
                s.albedo = col;
                s.alpha = 1.0;
                s.metallic = tO.b;
                s.smoothness = 1.0 - tO.g;
                s.occlusion = 1.0;
                s.normalTS = half3(0, 0, 1);

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = normalWS;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fogFactor;
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.bakedGI = SampleSH(normalWS);
                d.shadowMask = half4(1, 1, 1, 1);

                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = MixFog(c.rgb, i.fogFactor);
                return half4(c.rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            float4 vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir)));
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            float4 vert(Attributes v) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(v); return TransformObjectToHClip(v.positionOS.xyz); }
            half frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct V2F { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            V2F vert(Attributes v) { V2F o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag(V2F i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0.0); }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
