Shader "Mottainai/Transparent"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,0.5)
        _Fresnel ("Fresnel", Range(0, 4)) = 0
        _MainTex ("Alpha mask (optional)", 2D) = "white" {}
        _UseTex ("Use mask", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Fresnel;
                float _UseTex;
            CBUFFER_END
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; float2 uv : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert (Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float f = pow(1.0 - saturate(dot(n, v)), 2.0) * _Fresnel;
                float m = _UseTex > 0.5 ? SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a : 1.0;
                return half4(_BaseColor.rgb + f * 0.6, saturate(_BaseColor.a + f * 0.5) * m);
            }
            ENDHLSL
        }
    }
}
