// Skybox: day gradient, sunset glow toward the sun, the sun and moon, and stars at night.
Shader "Stratum/Sky"
{
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _SkyZenith, _SkyHorizon, _SunGlow;
            float3 _SunDir;
            float _Night;

            struct A { float3 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert(A a) { V o; o.positionCS = TransformObjectToHClip(a.positionOS); o.dir = a.positionOS; return o; }

            float Hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y;
                float h = pow(saturate(1 - max(up, 0)), 3);
                float3 col = lerp(_SkyZenith.rgb, _SkyHorizon.rgb, h);
                if (up < 0) col = lerp(_SkyHorizon.rgb, _SkyHorizon.rgb * 0.55, saturate(-up * 3));

                float sd = dot(d, _SunDir);
                // Warm glow around the sun, stronger near the horizon.
                col += _SunGlow.rgb * pow(saturate(sd), 6) * (0.4 + h);
                // Sun disc.
                col += float3(1.6, 1.4, 1.1) * smoothstep(0.9993, 0.9997, sd) * (1 - _Night);
                col += float3(1.0, 0.9, 0.7) * pow(saturate(sd), 300) * 0.8 * (1 - _Night);
                // Moon, opposite the sun.
                float md = dot(d, -_SunDir);
                col += float3(0.75, 0.8, 0.95) * smoothstep(0.9990, 0.9994, md) * _Night;
                col += float3(0.2, 0.25, 0.35) * pow(saturate(md), 60) * _Night;

                // Stars.
                if (_Night > 0.01 && up > -0.05)
                {
                    float3 g = floor(d * 260);
                    float s = Hash(g);
                    float tw = 0.6 + 0.4 * sin(_Time.y * (2 + s * 5) + s * 40);
                    float star = step(0.9965, s) * tw * smoothstep(-0.05, 0.25, up);
                    col += star * _Night * float3(0.9, 0.95, 1.0);
                }
                // At and below the horizon the sky turns into the fog colour, so the fogged far edge of the world
                // meets it without a seam (seen from high up, the world ends below the horizon).
                col = lerp(col, unity_FogColor.rgb, smoothstep(0.10, -0.02, up));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
