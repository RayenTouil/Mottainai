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

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p) { float v = 0, a = 0.5; for (int k = 0; k < 4; k++) { v += a * noise(p); p *= 2.03; a *= 0.5; } return v; }

            half4 frag (Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = saturate(d.y);
                float3 col = lerp(_Horizon.rgb, _Zenith.rgb, pow(h, 0.55));
                float3 sd = normalize(_SunDir.xyz);
                float s = saturate(dot(d, sd));
                col += _SunColor.rgb * (pow(s, 900) * 3 + pow(s, 12) * 0.25);
                if (d.y > 0.02)
                {
                    float2 uv = d.xz / (d.y + 0.15) * 1.6 + float2(_Time.y * 0.012, _Time.y * 0.02);
                    float c = smoothstep(0.52, 0.8, fbm(uv));
                    col = lerp(col, _CloudColor.rgb * (0.9 + 0.1 * s), c * saturate(d.y * 4) * 0.85);
                }
                col = lerp(col, _Horizon.rgb * 1.02, saturate(1 - d.y * 8) * 0.6);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
