// Other players' figures and the creatures: a colour per part with a fine pixel grain, lit like the blocks around them (sky and torch light from where
// they stand, sun and shadows), with the terrain's distance fog. Casts shadows; writes depth for water.
Shader "Stratum/Avatar"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Light ("Light (sky, block)", Vector) = (1, 0, 0, 0)
        _Flash ("Flash (rgb, amount)", Color) = (1, 0, 0, 0)
        _Glow ("Glow", Float) = 0
        _Grain ("Grain", Float) = 0.07
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _Light;
            float4 _Flash;
            float _Glow;
            float _Grain;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float4 _SkyAmbient;
            float4 _TorchColor;
            struct A { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionOS : TEXCOORD2; };

            float LevelToBrightness(float l) { return pow(0.80, (1 - l) * 15); }
            float FogClear(float3 ws)
            {
            #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                float3 d = ws - _WorldSpaceCameraPos;
                return saturate(length(float3(d.x, d.y * 0.4, d.z)) * unity_FogParams.z + unity_FogParams.w);
            #else
                return 1;
            #endif
            }

            V vert(A a)
            {
                V o;
                o.positionWS = TransformObjectToWorld(a.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.positionOS = a.positionOS;
                return o;
            }
            // Lit exactly as a block face would be where they stand (Block.shader), with the light levels the
            // world stores there passed in per figure.
            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float sky = _Light.x, blk = _Light.y;
                float skyB = LevelToBrightness(sky), blkB = LevelToBrightness(blk);
                float faceShade = lerp(0.72, 1.0, saturate(n.y)) * lerp(1.0, 0.76, saturate(-n.y));
                float3 ambient = _SkyAmbient.rgb * skyB * faceShade + _TorchColor.rgb * blkB * (0.75 + 0.25 * faceShade) + float3(0.026, 0.030, 0.038) * faceShade;
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float ndl = saturate(dot(n, sun.direction));
                float3 direct = sun.color * ndl * sun.shadowAttenuation * smoothstep(0.55, 0.95, sky);
                // Eight "pixels" across each face of a part, each a shade lighter or darker, like the blocks.
                float3 cell = floor((i.positionOS + 0.5) * 8 + 0.001);
                float h = frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
                float3 base = _BaseColor.rgb * (1 + (h - 0.5) * 2 * _Grain);
                float3 col = base * (ambient * 0.92 + direct * 0.95);
                col = lerp(col, _Flash.rgb, _Flash.a);
                col += base * _Glow;
                col = lerp(unity_FogColor.rgb, col, FogClear(i.positionWS));
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct A { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert(A a) : SV_POSITION
            {
                float3 ws = TransformObjectToWorld(a.positionOS);
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
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float3 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
