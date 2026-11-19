// Selection outline (_Mode 0: flat colour) and block cracks while mining (_Mode 1: darkens by the crack map).
Shader "Stratum/Overlay"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0, 0, 0.6)
        _MainTex ("Cracks", 2D) = "white" {}
        _Mode ("Mode", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Offset -1, -1
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color; float4 _MainTex_ST; float _Mode;
            CBUFFER_END
            struct A { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a) { V o; o.positionCS = TransformObjectToHClip(a.positionOS); o.uv = a.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                if (_Mode < 0.5) return _Color;
                float c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;
                return half4(0, 0, 0, c * 0.75);
            }
            ENDHLSL
        }
    }
}
