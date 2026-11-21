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
        _HalfWidth ("River half width", Float) = 12
        _FoamColor ("Foam", Color) = (0.95, 0.98, 1, 1)
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
            TEXTURE2D(_MottainaiFx); SAMPLER(sampler_MottainaiFx);
            TEXTURE2D(_MottainaiNoise); SAMPLER(sampler_MottainaiNoise);
            CBUFFER_START(UnityPerMaterial)
                float4 _Shallow, _Deep, _DirtColor;
                float _NormalStrength, _WaveHeight, _Flow, _HasMaps, _HalfWidth;
                float4 _FoamColor;
            CBUFFER_END
            // globals (set from C#)
            float4 _MottainaiFxRect;                         // x0, z0, 1/spanX, 1/spanZ of the effects map
            float4 _MottainaiSkyHorizon, _MottainaiSkyZenith; // sky gradient for reflections

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 waveN : TEXCOORD1; float fog : TEXCOORD2; };

            float WaveHeight(float2 p, float t, out float3 n)
            {
                float2 dir1 = float2(0.3, 1.0), dir2 = float2(-0.8, 0.6), dir3 = float2(0.9, 0.2);
                float k1 = 0.35, k2 = 0.55, k3 = 0.9;
                float a1 = _WaveHeight, a2 = _WaveHeight * 0.6, a3 = _WaveHeight * 0.35;
                float s1 = dot(dir1, p) * k1 + t * 1.3, s2 = dot(dir2, p) * k2 + t * 1.7, s3 = dot(dir3, p) * k3 + t * 2.3;
                float h = a1 * sin(s1) + a2 * sin(s2) + a3 * sin(s3);
                float2 d = a1 * k1 * cos(s1) * dir1 + a2 * k2 * cos(s2) * dir2 + a3 * k3 * cos(s3) * dir3;
                n = normalize(float3(-d.x, 1, -d.y));
                return h;
            }

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                float3 n;
                wp.y += WaveHeight(wp.xz, _Time.y, n);
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
                    float r0 = sin(uv.x * 1.7 + t * 1.1) * sin(uv.y * 1.3 - t * _Flow * 0.6);
                    n = normalize(n + float3(r0 * 0.08, 0, r0 * 0.08));
                }
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light l = GetMainLight();
                float fres = pow(1.0 - saturate(dot(n, v)), 4.0);
                float3 col = lerp(_Deep.rgb, _Shallow.rgb, saturate(dot(n, l.direction) * 0.5 + 0.35));

                // cheap sky reflection (same gradient as the sky dome)
                float3 rdir = reflect(-v, n);
                float3 sky = lerp(_MottainaiSkyHorizon.rgb, _MottainaiSkyZenith.rgb, saturate(rdir.y * 1.6));
                col = lerp(col, sky, saturate(fres * 0.7 + 0.04));

                // effects map: r = obstacle foam, g = waste dirt, b = raft wake (CPU rasterized)
                float2 fxuv = (i.positionWS.xz - _MottainaiFxRect.xy) * _MottainaiFxRect.zw;
                float inside = step(0.0, fxuv.x) * step(fxuv.x, 1.0) * step(0.0, fxuv.y) * step(fxuv.y, 1.0);
                float4 fx = SAMPLE_TEXTURE2D(_MottainaiFx, sampler_MottainaiFx, fxuv) * inside;
                col = lerp(col, _DirtColor.rgb, fx.g * 0.65);

                // lighter, greener shallows near the banks
                float edge = saturate((abs(i.positionWS.x) - (_HalfWidth - 3.5)) / 3.5);
                col = lerp(col, col * float3(0.95, 1.08, 0.95) + 0.06, edge);

                // current streaks drifting downstream (shows the flow)
                float streak = SAMPLE_TEXTURE2D(_MottainaiNoise, sampler_MottainaiNoise, uv * float2(0.05, 0.012) + float2(0, -t * _Flow * 0.012)).r;
                col += smoothstep(0.62, 0.82, streak) * 0.05;

                // foam: shoreline + map, broken up by drifting noise
                float shore = smoothstep(_HalfWidth - 0.9, _HalfWidth + 0.2, abs(i.positionWS.x));
                float foam = max(max(shore, fx.r), fx.b * 0.95);
                float cells = SAMPLE_TEXTURE2D(_MottainaiNoise, sampler_MottainaiNoise, uv * 0.18 + float2(0, -t * _Flow * 0.07)).g;
                foam *= smoothstep(0.32, 0.52, cells + foam * 0.45);
                col = lerp(col, _FoamColor.rgb, saturate(foam));

                // sun glints + specular
                float sparkle = SAMPLE_TEXTURE2D(_MottainaiNoise, sampler_MottainaiNoise, uv * 0.35 + t * 0.05).b;
                col += pow(saturate(dot(rdir, l.direction)), 250) * step(0.78, sparkle) * 1.4;
                float3 h = normalize(l.direction + v);
                col += pow(saturate(dot(n, h)), 96) * 1.2 * l.color;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
