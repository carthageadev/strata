Shader "Strata/TileWalls"
{
    Properties
    {
        [NoScaleOffset] _WallAlbedo ("Wall Layer Albedo (array)", 2DArray) = "" {}
        [NoScaleOffset] _WallNormal ("Wall Layer Normal (array)", 2DArray) = "" {}
        [NoScaleOffset] _WallORM ("Wall Layer ORM (array)", 2DArray) = "" {}
        [NoScaleOffset] _EdgeAlbedo ("Edge Albedo (array)", 2DArray) = "" {}
        [NoScaleOffset] _EdgeTable ("Edge Table", 2D) = "black" {}
        [HideInInspector] _StrataCodes ("Code Format", Float) = 0
        _Brightness ("Brightness", Range(0.5, 1.5)) = 1
        _MossTint ("Turf Colour", Color) = (0.34, 0.47, 0.17, 1)
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
            float _Brightness, _StrataCodes;
            half4 _MossTint, _CopingDark, _CopingLight, _StonesDark, _StonesLight;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 uv1 : TEXCOORD1; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
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

            TEXTURE2D_ARRAY(_WallAlbedo); SAMPLER(sampler_WallAlbedo);
            TEXTURE2D_ARRAY(_WallNormal);
            TEXTURE2D_ARRAY(_WallORM);
            TEXTURE2D_ARRAY(_EdgeAlbedo);
            TEXTURE2D(_EdgeTable);

            float SideGrid(int k) { float g = LOAD_TEXTURE2D(_EdgeTable, int2(k, 3)).r; return g > 0.5 ? g : ST_TEX; }

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 topBot : TEXCOORD2;
                float4 code : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                float4 nb : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.topBot = v.uv;
                o.code = v.color;
                o.nb = v.uv1;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 Nw = normalize(ST_ToGrid(i.normalWS));
                float3 P = ST_ToGrid(i.positionWS);
                bool alongZ = abs(Nw.x) > 0.5;
                float along = alongZ ? (Nw.x > 0.0 ? -P.z : P.z) : (Nw.z > 0.0 ? P.x : -P.x);
                float tu = floor(along * ST_TEX);
                float fromTop = floor((i.topBot.x - P.y) * ST_TEX);
                float fromBot = floor((P.y - i.topBot.y) * ST_TEX);
                //! R packs style/edge, G packs neighbours; UV0/UV1 store wall/neighbour heights.
                int e0 = (int)round(i.code.r * 256.0), e1 = (int)round(i.code.g * 256.0);
                int style = e0 & 15, rim = (e0 >> 4) & 15, sL = e1 & 15, sR = (e1 >> 4) & 15;
                float u = i.code.b;
                int layer = style;
                if (sL != style || sR != style)
                {
                    float G = min(SideGrid(style), min(sL != style ? SideGrid(sL) : ST_TEX * 4.0, sR != style ? SideGrid(sR) : ST_TEX * 4.0));
                    float gu = floor(along * G), gr = floor((i.topBot.x - P.y) * G);
                    float uq = (min(floor(u * G), ceil(G) - 1.0) + 0.5) / G;
                    float yq = i.topBot.x - (gr + 0.5) / G;
                    float nb = stHash(float2(floor(gu / 3.0), floor(gr / 2.0)) + float2(17.3, 5.1)) * 0.85 + stHash(float2(gu, gr) + 3.7) * 0.15;
                    float slack = (1.0 + floor(stHash(float2(gu, 41.7)) * 3.0)) / G;
                    bool inL = yq <= i.nb.x + slack && yq >= i.nb.y - 1.0 / G;
                    bool inR = yq <= i.nb.z + slack && yq >= i.nb.w - 1.0 / G;
                    float pL = sL != style && inL ? (1.0 - smoothstep(0.0, 0.5, uq)) * 0.5 : 0.0;
                    float pR = sR != style && inR ? smoothstep(0.5, 1.0, uq) * 0.5 : 0.0;
                    if (nb < pL) layer = sL; else if (nb > 1.0 - pR) layer = sR;
                }
                float2 uv = float2((tu + 0.5) / 256.0, 1.0 - (fromTop + 0.5) / 256.0);
                float2 gx = float2(ddx(along), ddx(P.y)) / 16.0, gy = float2(ddy(along), ddy(P.y)) / 16.0;
                float4 alb = SAMPLE_TEXTURE2D_ARRAY_GRAD(_WallAlbedo, sampler_WallAlbedo, uv, layer, gx, gy);
                float4 nrm = SAMPLE_TEXTURE2D_ARRAY_GRAD(_WallNormal, sampler_WallAlbedo, uv, layer, gx, gy);
                float4 tO = SAMPLE_TEXTURE2D_ARRAY_GRAD(_WallORM, sampler_WallAlbedo, uv, layer, gx, gy);
                float3 col = alb.rgb;
                float aT = alongZ ? floor(P.z * ST_TEX) : floor(P.x * ST_TEX);
                float faceSign = alongZ ? (Nw.x > 0.0 ? -1.0 : 1.0) : (Nw.z > 0.0 ? 1.0 : -1.0);
                float4 eP = LOAD_TEXTURE2D(_EdgeTable, int2(rim, 0));
                int pat = (int)round(eP.r * 255.0);
                bool eTex = eP.g > 0.5;
                float3 eDark = LOAD_TEXTURE2D(_EdgeTable, int2(rim, 1)).rgb, eLight = LOAD_TEXTURE2D(_EdgeTable, int2(rim, 2)).rgb;
                float3 eCol = eTex ? SAMPLE_TEXTURE2D_ARRAY_GRAD(_EdgeAlbedo, sampler_WallAlbedo, uv, rim, gx, gy).rgb : eDark;
                if (pat >= 2)
                {
                    float rows = pat == 2 ? 3.0 : 4.0;
                    if (fromTop < rows)
                    {
                        float4 r = stRim(aT, 0.0, pat);
                        col = stEdgeColor(r, eDark, eLight, eTex, eCol) * 0.92;
                        float vv = 1.0 - (fromTop + 0.5) / rows * 2.0;
                        float2 dn = pat == 3 ? float2(r.z * 0.6 * faceSign, vv * 0.75) : float2(0.0, fromTop < 0.5 ? 0.6 : 0.0);
                        nrm = float4(normalize(float3(dn, 1.0)) * 0.5 + 0.5, 0.8);
                    }
                    else if (fromTop < rows + 1.0) col *= 0.4;
                    else if (fromTop < rows + 2.0) col *= 0.7;
                }
                else if (pat == 1)
                {
                    float fringe = 2.0 + floor(stHash(float2(aT, 2.3)) * 3.0);
                    if (fromTop < fringe)
                    {
                        float3 g = fromTop < 0.5 ? eCol : eCol * float3(0.59, 0.66, 0.65);
                        col = g * (0.9 + 0.2 * stHash(float2(aT, fromTop)));
                        nrm = float4(0.5, 0.75, 0.86, 0.7);
                    }
                    else if (fromTop < fringe + 1.0) col *= 0.45;
                    else if (fromTop < fringe + 2.0) col *= 0.74;
                }
                if (fromBot < 5.0) col *= fromBot < 1.0 ? 0.55 : (fromBot < 2.0 ? 0.66 : (fromBot < 3.5 ? 0.78 : 0.9));
                col *= lerp(0.82, 1.0, tO.r) * _Brightness;

                float3 tN = nrm.rgb * 2.0 - 1.0;
                float3 T = alongZ ? float3(0.0, 0.0, Nw.x > 0.0 ? -1.0 : 1.0) : float3(Nw.z > 0.0 ? 1.0 : -1.0, 0.0, 0.0);
                float3 normalWS = normalize(ST_ToGrid(T * tN.x + float3(0, 1, 0) * tN.y + Nw * max(tN.z, 0.25)));

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
            ZWrite On ZTest LEqual ColorMask 0 Cull Off

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
