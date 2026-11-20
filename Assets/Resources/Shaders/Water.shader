// Water: rippled surface, sky reflection with Fresnel, sun glints, and colour that deepens with the depth of
// water behind it (needs the URP depth texture).
Shader "Stratum/Water"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Name "Water"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #pragma require 2darray
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D_ARRAY(_BlockNormal); SAMPLER(sampler_BlockNormal);
            float4 _SkyAmbient, _SkyZenith, _SkyHorizon, _TorchColor, _CaveGlow;
            float _WaterLayer, _Underwater;

            struct A { float3 positionOS : POSITION; float3 normalOS : NORMAL; float4 light : COLOR; float3 uv : TEXCOORD0; };
            struct V
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1;
                float4 light : TEXCOORD2; float4 screen : TEXCOORD3;
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

            V vert(A a)
            {
                V o;
                float3 ws = TransformObjectToWorld(a.positionOS);
                if (a.normalOS.y > 0.5) ws.y += sin(_Time.y * 1.3 + ws.x * 0.7 + ws.z * 0.45) * 0.025 - 0.03;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.light = a.light;
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            float LevelToBrightness(float l) { return pow(0.80, (1 - l) * 15); }

            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float2 p = abs(n.y) > 0.5 ? i.positionWS.xz : (abs(n.x) > 0.5 ? i.positionWS.zy : i.positionWS.xy);
                float t = _Time.y;
                float2 n1 = SAMPLE_TEXTURE2D_ARRAY(_BlockNormal, sampler_BlockNormal, p * 0.21 + float2(t * 0.035, t * 0.02), _WaterLayer).rg * 2 - 1;
                float2 n2 = SAMPLE_TEXTURE2D_ARRAY(_BlockNormal, sampler_BlockNormal, p * 0.47 + float2(-t * 0.05, t * 0.031), _WaterLayer).rg * 2 - 1;
                float2 rip = (n1 + n2) * 0.35;
                float3 nw = abs(n.y) > 0.5 ? normalize(float3(rip.x, 1, rip.y) * float3(1, sign(n.y), 1)) : normalize(n + float3(rip.x, rip.y, 0) * 0.3);
                if (!front) nw = -nw;

                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float ndv = saturate(dot(nw, viewDir));
                float fresnel = 0.04 + 0.96 * pow(1 - ndv, 5);

                float skyB = LevelToBrightness(i.light.r), blkB = LevelToBrightness(i.light.g);
                float3 lightAmt = _SkyAmbient.rgb * skyB + _TorchColor.rgb * blkB + 0.01 + _CaveGlow.rgb;

                // How much water lies behind this pixel.
                float2 suv = i.screen.xy / i.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float surf = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float depth = max(0, scene - surf);
                float absorb = 1 - exp(-depth * 0.22);

                float3 shallow = float3(0.10, 0.42, 0.50), deep = float3(0.02, 0.10, 0.22);
                float3 body = lerp(shallow, deep, absorb) * lightAmt;

                float3 r = reflect(-viewDir, nw);
                float3 skyCol = lerp(_SkyHorizon.rgb, _SkyZenith.rgb, saturate(r.y * 1.5)) * skyB;

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light sun = GetMainLight(shadowCoord);
                float3 h = normalize(sun.direction + viewDir);
                float spec = pow(saturate(dot(nw, h)), 260) * 6 * sun.shadowAttenuation * smoothstep(0.6, 0.95, i.light.r);

                float3 col = lerp(body, skyCol, front ? fresnel : 0.0) + sun.color * spec;
                float alpha = saturate(lerp(0.55, 0.96, absorb) + fresnel * 0.4);
                if (!front) { col = body * 1.2; alpha = 0.6; }
                float clear = FogClear(i.positionWS);
                col = lerp(unity_FogColor.rgb, col, clear);
                alpha = lerp(1, alpha, clear);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
