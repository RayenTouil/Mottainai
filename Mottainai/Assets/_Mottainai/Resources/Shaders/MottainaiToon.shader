Shader "Mottainai/Toon"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        _BaseMap ("Texture", 2D) = "white" {}
        _Emission ("Emission", Range(0, 2)) = 0
        _Gloss ("Toon gloss", Range(0, 1)) = 0
        _FogMul ("Fog amount", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float _Emission;
                float _Gloss;
                float _FogMul;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; float3 positionWS : TEXCOORD2; float fog : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert (Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.positionWS = wp;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Light l = GetMainLight();
                float3 n = normalize(i.normalWS);
                float ndl = dot(n, l.direction);
                float toon = smoothstep(-0.05, 0.2, ndl) * 0.5 + 0.5;
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0) * 0.22;
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                float3 c = _BaseColor.rgb * tex.rgb;
                float3 col = c * (toon * l.color + SampleSH(n) * 0.35) + rim + c * _Emission;
                // crisp toy-plastic highlight (inflatable raft, bottles, cans)
                float3 hv = normalize(l.direction + v);
                col += smoothstep(0.5, 0.56, pow(saturate(dot(n, hv)), 28)) * _Gloss * 0.55 * l.color;
                col = lerp(col, MixFog(col, i.fog), _FogMul); // distant backdrop props use less fog
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
