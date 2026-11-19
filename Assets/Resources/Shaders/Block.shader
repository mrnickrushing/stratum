// Every block face in the world.
//   COLOR      r sky light, g block light, b ambient occlusion (baked per corner by the mesher)
//   TEXCOORD0  u, v, texture layer
//   TEXCOORD1  rgb biome tint, a flags (see Core/Mesher.cs Flag)
// Sky light is scaled by the time of day here, so day and night never need a remesh.
Shader "Stratum/Block"
{
    Properties
    {
        _LightOverride ("Light override (sky, block, -, on)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D_ARRAY(_BlockAlbedo); SAMPLER(sampler_BlockAlbedo);
        TEXTURE2D_ARRAY(_BlockNormal); SAMPLER(sampler_BlockNormal);
        CBUFFER_START(UnityPerMaterial)
            float4 _LightOverride;
        CBUFFER_END
        float4 _SkyAmbient;     // rgb ambient from the sky at full sky light
        float4 _TorchColor;
        float _Wind;

        float LevelToBrightness(float l) { return pow(0.80, (1 - l) * 15); }

        float3 Sway(float3 ws, float3 os, float flags, float v)
        {
            int f = (int)(flags * 255 + 0.5);
            if ((f & 8) == 0) return ws;
            float t = _Time.y * 1.6;
            float w = sin(t + ws.x * 0.55 + ws.z * 0.35) + sin(t * 1.7 + ws.x * 0.21 - ws.z * 0.6) * 0.5;
            float amp = 0.035 + ((f & 64) != 0 ? v * 0.09 : 0);
            ws.x += w * amp * _Wind;
            ws.z += cos(t * 1.3 + ws.z * 0.4) * amp * 0.6 * _Wind;
            return ws;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma require 2darray
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 light : COLOR;
                float3 uv : TEXCOORD0;
                float4 tint : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 uv : TEXCOORD2;
                float4 tint : TEXCOORD3;
                float4 light : TEXCOORD4;
            };

            // Fog by true distance from the camera rather than depth: depth runs short toward the screen's sides,
            // where the round edge of the loaded world would show through. 1 = clear, 0 = all fog.
            float FogClear(float3 ws)
            {
            #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                float3 d = ws - _WorldSpaceCameraPos;
                // Height counts for less, so the ground below stays clear from high up; never less than the
                // flat distance, so the edge stays hidden.
                return saturate(length(float3(d.x, d.y * 0.4, d.z)) * unity_FogParams.z + unity_FogParams.w);
            #else
                return 1;
            #endif
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS);
                ws = Sway(ws, v.positionOS, v.tint.a, v.uv.y);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.tint = v.tint;
                o.light = _LightOverride.w > 0.5 ? float4(_LightOverride.x, _LightOverride.y, v.light.b, 1) : v.light;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                int flags = (int)(i.tint.a * 255 + 0.5);
                float4 tex = SAMPLE_TEXTURE2D_ARRAY(_BlockAlbedo, sampler_BlockAlbedo, i.uv.xy, i.uv.z);
                if ((flags & 4) != 0) clip(tex.a - 0.5);
                float3 albedo = tex.rgb;
                if ((flags & 1) != 0) albedo *= i.tint.rgb;
                else if ((flags & 2) != 0) albedo *= lerp(1, i.tint.rgb, tex.a);

                // Normal map in the face's own frame.
                float3 n = normalize(i.normalWS);
                float3 t = abs(n.y) > 0.5 ? float3(1, 0, 0) : (abs(n.x) > 0.5 ? float3(0, 0, n.x) : float3(-n.z, 0, 0));
                float3 b = abs(n.y) > 0.5 ? float3(0, 0, 1) : float3(0, 1, 0);
                float2 nt = SAMPLE_TEXTURE2D_ARRAY(_BlockNormal, sampler_BlockNormal, i.uv.xy, i.uv.z).rg * 2 - 1;
                bool plant = (flags & 64) != 0;
                float3 nw = plant ? float3(0, 1, 0) : normalize(n + t * nt.x * 0.8 + b * nt.y * 0.8);

                float sky = i.light.r, blk = i.light.g, ao = i.light.b;
                float skyB = LevelToBrightness(sky), blkB = LevelToBrightness(blk);
                float faceShade = plant ? 0.85 : (n.y > 0.5 ? 1.0 : (n.y < -0.5 ? 0.55 : (abs(n.x) > 0.5 ? 0.82 : 0.72)));
                float aoF = lerp(0.38, 1.0, ao);

                float3 ambient = _SkyAmbient.rgb * skyB * faceShade + _TorchColor.rgb * blkB * (0.75 + 0.25 * faceShade) + float3(0.026, 0.030, 0.038) * faceShade;

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light sun = GetMainLight(shadowCoord);
                float ndl = plant ? 0.7 : saturate(dot(nw, sun.direction));
                float sunlit = smoothstep(0.55, 0.95, sky);
                float3 direct = sun.color * ndl * sun.shadowAttenuation * sunlit;

                float3 col = albedo * (ambient * aoF + direct * (0.55 + 0.45 * aoF));
                if ((flags & 16) != 0) col += albedo * 1.4;
                if ((flags & 32) != 0) col = lerp(col, albedo * 2.2, tex.a);
                col = lerp(unity_FogColor.rgb, col, FogClear(i.positionWS));
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct A { float3 positionOS : POSITION; float3 normalOS : NORMAL; float3 uv : TEXCOORD0; float4 tint : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; float3 uv : TEXCOORD0; float flags : TEXCOORD1; };
            V vert(A a)
            {
                V o;
                float3 ws = Sway(TransformObjectToWorld(a.positionOS), a.positionOS, a.tint.a, a.uv.y);
                float3 n = TransformObjectToWorldNormal(a.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - ws);
                #else
                    float3 ld = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, n, ld));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs; o.uv = a.uv; o.flags = a.tint.a;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                int f = (int)(i.flags * 255 + 0.5);
                if ((f & 4) != 0) clip(SAMPLE_TEXTURE2D_ARRAY(_BlockAlbedo, sampler_BlockAlbedo, i.uv.xy, i.uv.z).a - 0.5);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float3 positionOS : POSITION; float3 uv : TEXCOORD0; float4 tint : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; float3 uv : TEXCOORD0; float flags : TEXCOORD1; };
            V vert(A a)
            {
                V o;
                o.positionCS = TransformWorldToHClip(Sway(TransformObjectToWorld(a.positionOS), a.positionOS, a.tint.a, a.uv.y));
                o.uv = a.uv; o.flags = a.tint.a;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                int f = (int)(i.flags * 255 + 0.5);
                if ((f & 4) != 0) clip(SAMPLE_TEXTURE2D_ARRAY(_BlockAlbedo, sampler_BlockAlbedo, i.uv.xy, i.uv.z).a - 0.5);
                return 0;
            }
            ENDHLSL
        }
    }
}
