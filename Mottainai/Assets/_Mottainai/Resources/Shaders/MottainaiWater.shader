Shader "Mottainai/Water"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.25, 0.72, 0.78, 1)
        _Deep ("Deep", Color) = (0.05, 0.32, 0.48, 1)
        _DirtColor ("Dirt", Color) = (0.38, 0.45, 0.18, 1)
        _NormalA ("Normal A", 2D) = "bump" {}
        _NormalB ("Normal B", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0.8
        _WaveHeight ("Wave Height", Float) = 0.12
        _Flow ("Flow speed", Float) = 2.5
        _HasMaps ("Has Maps", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-10" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalA); SAMPLER(sampler_NormalA);
            TEXTURE2D(_NormalB); SAMPLER(sampler_NormalB);
            CBUFFER_START(UnityPerMaterial)
                float4 _Shallow, _Deep, _DirtColor;
                float _NormalStrength, _WaveHeight, _Flow, _HasMaps;
            CBUFFER_END
            float4 _MottainaiDirt[16];
            float _MottainaiDirtCount;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 waveN : TEXCOORD1; float fog : TEXCOORD2; };

            float3 Wave(float2 p, float t, out float3 n)
            {
                float h = 0; float2 d = 0;
                float2 dir1 = float2(0.3, 1.0), dir2 = float2(-0.8, 0.6), dir3 = float2(0.9, 0.2);
                float k1 = 0.35, k2 = 0.55, k3 = 0.9;
                float a1 = _WaveHeight, a2 = _WaveHeight * 0.6, a3 = _WaveHeight * 0.35;
                float s1 = dot(dir1, p) * k1 + t * 1.3, s2 = dot(dir2, p) * k2 + t * 1.7, s3 = dot(dir3, p) * k3 + t * 2.3;
                h = a1 * sin(s1) + a2 * sin(s2) + a3 * sin(s3);
                d = a1 * k1 * cos(s1) * dir1 + a2 * k2 * cos(s2) * dir2 + a3 * k3 * cos(s3) * dir3;
                n = normalize(float3(-d.x, 1, -d.y));
                return float3(p.x, h, p.y);
            }

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                float3 n;
                float3 w = Wave(wp.xz, _Time.y, n);
                wp.y += w.y;
                o.positionWS = wp;
                o.waveN = n;
                o.positionCS = TransformWorldToHClip(wp);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 uv = i.positionWS.xz;
                float t = _Time.y;
                float3 n = i.waveN;
                if (_HasMaps > 0.5)
                {
                    float3 na = SAMPLE_TEXTURE2D(_NormalA, sampler_NormalA, uv * 0.06 + float2(0, -t * _Flow * 0.012)).rgb * 2 - 1;
                    float3 nb = SAMPLE_TEXTURE2D(_NormalB, sampler_NormalB, uv * 0.11 + float2(t * 0.01, -t * _Flow * 0.02)).rgb * 2 - 1;
                    float2 pert = (na.xy + nb.xy) * _NormalStrength;
                    n = normalize(float3(n.x + pert.x, n.y, n.z + pert.y));
                }
                else
                {
                    float r = sin(uv.x * 1.7 + t * 1.1) * sin(uv.y * 1.3 - t * _Flow * 0.6);
                    n = normalize(n + float3(r * 0.08, 0, r * 0.08));
                }
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light l = GetMainLight();
                float fres = pow(1.0 - saturate(dot(n, v)), 3.0);
                float3 col = lerp(_Deep.rgb, _Shallow.rgb, saturate(dot(n, l.direction) * 0.5 + 0.35));
                float dirt = 0;
                int cnt = (int)_MottainaiDirtCount;
                for (int k = 0; k < 16; k++)
                {
                    if (k >= cnt) break;
                    float4 d = _MottainaiDirt[k];
                    float dist = distance(i.positionWS.xz, d.xy);
                    dirt = max(dirt, saturate(1 - dist / d.z) * d.w);
                }
                col = lerp(col, _DirtColor.rgb, dirt * 0.65);
                float3 h = normalize(l.direction + v);
                float spec = pow(saturate(dot(n, h)), 96) * 1.2;
                col += spec * l.color + fres * 0.25;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
