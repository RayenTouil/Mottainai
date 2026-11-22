Shader "Mottainai/Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.22, 0.52, 0.9, 1)
        _Horizon ("Horizon", Color) = (0.72, 0.88, 0.98, 1)
        _SunColor ("Sun", Color) = (1, 0.95, 0.8, 1)
        _SunDir ("Sun Dir", Vector) = (-0.4, 0.55, 0.7, 0)
        _CloudColor ("Clouds", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "RenderPipeline"="UniversalPipeline" "PreviewType"="Skybox" }
        Pass
        {
            Name "Sky"
            Tags { "LightMode"="UniversalForward" }
            Cull Front
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Zenith, _Horizon, _SunColor, _SunDir, _CloudColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.dir = i.positionOS.xyz;
                return o;
            }

            TEXTURE2D(_MottainaiNoise); SAMPLER(sampler_MottainaiNoise);

            half4 frag (Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = saturate(d.y);
                float3 col = lerp(_Horizon.rgb, _Zenith.rgb, pow(h, 0.55));
                float3 sd = normalize(_SunDir.xyz);
                float s = saturate(dot(d, sd));
                col += _SunColor.rgb * pow(s, 12) * 0.15; // v3: no sun disk
                if (d.y > 0.02)
                {
                    float2 uv = d.xz / (d.y + 0.15) * 0.22 + float2(_Time.y * 0.0015, _Time.y * 0.003);
                    float c = SAMPLE_TEXTURE2D(_MottainaiNoise, sampler_MottainaiNoise, uv).r * 0.65 + SAMPLE_TEXTURE2D(_MottainaiNoise, sampler_MottainaiNoise, uv * 2.3 + 0.37).r * 0.35;
                    c = smoothstep(0.5, 0.78, c);
                    col = lerp(col, _CloudColor.rgb * (0.9 + 0.1 * s), c * saturate(d.y * 4) * 0.85);
                }
                col = lerp(col, _Horizon.rgb * 1.02, saturate(1 - d.y * 8) * 0.6);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
