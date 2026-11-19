// A soft cloud layer drawn on a large plane that follows the camera; the cloud map drifts with the wind.
Shader "Stratum/Clouds"
{
    Properties { _MainTex ("Cloud map", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END
            float4 _CloudLit, _CloudShade;
            float _CloudCover;

            struct A { float3 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            V vert(A a) { V o; o.positionWS = TransformObjectToWorld(a.positionOS); o.positionCS = TransformWorldToHClip(o.positionWS); return o; }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.positionWS.xz / 1400.0 + float2(_Time.y * 0.0012, _Time.y * 0.0004);
                float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r;
                float b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv * 2.7 + 0.31).r;
                float c = a * 0.7 + b * 0.3;
                float cover = smoothstep(_CloudCover, _CloudCover + 0.22, c);
                float thick = smoothstep(_CloudCover + 0.05, _CloudCover + 0.45, c);
                float3 col = lerp(_CloudLit.rgb, _CloudShade.rgb, thick * 0.8);
                float dist = length(i.positionWS.xz - _WorldSpaceCameraPos.xz);
                float fade = 1 - smoothstep(900, 1500, dist);
                return half4(col, cover * fade * 0.92);
            }
            ENDHLSL
        }
    }
}
