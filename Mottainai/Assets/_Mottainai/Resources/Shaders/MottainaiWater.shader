Shader "Mottainai/Water"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.447, 0.839, 0.906, 1)   // #72D6E7
        _Mid ("Mid", Color) = (0.129, 0.722, 0.780, 1)           // #21B8C7
        _Deep ("Deep", Color) = (0.086, 0.494, 0.608, 1)         // #167E9B
        _SeaDeep ("Sea deep", Color) = (0.05, 0.36, 0.62, 1)
        _DirtColor ("Dirt", Color) = (0.42, 0.47, 0.22, 1)
        _NormalA ("Normal A", 2D) = "bump" {}
        _NormalB ("Normal B", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0.7
        _WaveHeight ("Wave Height", Float) = 0.1
        _Flow ("Flow speed", Float) = 2.5
        _HasMaps ("Has Maps", Float) = 0
        _HalfWidth ("River half width", Float) = 12
        _FoamColor ("Foam", Color) = (0.97, 0.99, 1, 1)
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
                float4 _Shallow, _Mid, _Deep, _SeaDeep, _DirtColor;
                float _NormalStrength, _WaveHeight, _Flow, _HasMaps, _HalfWidth;
                float4 _FoamColor;
            CBUFFER_END
            // globals (set from C#)
            float4 _MottainaiFxRect;                         // x0, z0, 1/spanX, 1/spanZ of the effects map
            float4 _MottainaiSkyHorizon, _MottainaiSkyZenith; // sky gradient for reflections
            float4 _MottainaiSunDir;                          // visual sun (top-right) for glints
            float _MottainaiHigh;                             // quality: caustics + detail
            float _MottainaiSea;                              // 0 river .. 1 open sea (end of course)

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

            float Nr(float2 uv) { return SAMPLE_TEXTURE2D(_MottainaiNoise, sampler_MottainaiNoise, uv).r; }
            float Ng(float2 uv) { return SAMPLE_TEXTURE2D(_MottainaiNoise, sampler_MottainaiNoise, uv).g; }

            half4 frag (Varyings i) : SV_Target
            {
                float2 uv = i.positionWS.xz;
                float t = _Time.y;
                float3 n = i.waveN;
                if (_HasMaps > 0.5)
                {
                    // two flow-aligned scrolling normal maps
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
                float dist = length(_WorldSpaceCameraPos.xyz - i.positionWS);
                Light l = GetMainLight();

                // depth gradient: clear turquoise shallows at the banks -> cyan -> deep teal patches mid-river
                float bank = saturate((abs(i.positionWS.x) - (_HalfWidth - 4.5)) / 4.5);
                float deepPatch = smoothstep(0.35, 0.75, Nr(uv * 0.012 + float2(0.3, -t * 0.002))) * (1 - bank);
                float3 deep = lerp(_Deep.rgb, _SeaDeep.rgb, _MottainaiSea);
                float3 col = lerp(_Mid.rgb, deep, deepPatch * 0.75 + _MottainaiSea * 0.35);
                col = lerp(col, _Shallow.rgb * 1.06, bank * (1 - _MottainaiSea));
                col *= 0.86 + 0.2 * saturate(dot(n, l.direction) * 0.5 + 0.5);

                // fresnel sky tint
                float fres = pow(1.0 - saturate(dot(n, v)), 4.0);
                float3 rdir = reflect(-v, n);
                float3 sky = lerp(_MottainaiSkyHorizon.rgb, _MottainaiSkyZenith.rgb, saturate(rdir.y * 1.6));
                col = lerp(col, sky, saturate(fres * 0.65 + 0.03));

                // animated caustics (High): bright web where two drifting noise layers cross; stronger in the shallows
                if (_MottainaiHigh > 0.5)
                {
                    float c1 = Ng(uv * 0.21 + float2(t * 0.021, -t * 0.034) + n.xz * 0.05);
                    float c2 = Ng(uv * 0.17 + float2(-t * 0.027, -t * 0.017) + 0.5);
                    float web = 1 - saturate(abs(c1 - c2) * 9.0);
                    float caustic = web * web * web * saturate(1.15 - dist / 55.0);
                    col += caustic * (0.12 + 0.16 * bank) * (1 - _MottainaiSea * 0.6);
                }

                // effects map: r = foam (obstacles, ripples), g = waste dirt, b = raft wake, a = blob shadows
                float2 fxuv = (i.positionWS.xz - _MottainaiFxRect.xy) * _MottainaiFxRect.zw;
                float inside = step(0.0, fxuv.x) * step(fxuv.x, 1.0) * step(0.0, fxuv.y) * step(fxuv.y, 1.0);
                float4 fx = SAMPLE_TEXTURE2D(_MottainaiFx, sampler_MottainaiFx, fxuv) * inside;
                col = lerp(col, _DirtColor.rgb, fx.g * 0.55);
                col = lerp(col, deep * 0.72, fx.a * 0.42);

                // current streaks drifting downstream (shows the flow)
                float streak = SAMPLE_TEXTURE2D(_MottainaiNoise, sampler_MottainaiNoise, uv * float2(0.05, 0.012) + float2(0, -t * _Flow * 0.012)).r;
                col += smoothstep(0.62, 0.82, streak) * 0.05;

                // stylized foam: shoreline band + map, crisp cartoon edges broken up by drifting cells
                float shore = smoothstep(_HalfWidth - 1.1, _HalfWidth + 0.1, abs(i.positionWS.x)) * (1 - _MottainaiSea);
                float foam = max(max(shore, fx.r), fx.b);
                float cells = Ng(uv * 0.2 + float2(0, -t * _Flow * 0.07));
                float f = smoothstep(0.42, 0.5, cells * 0.55 + foam * 0.75);
                f = max(f, smoothstep(0.85, 0.95, foam));
                col = lerp(col, _FoamColor.rgb, saturate(f * saturate(foam * 3.0)));

                // sun glints toward the visual sun + soft specular from the key light
                float sparkle = SAMPLE_TEXTURE2D(_MottainaiNoise, sampler_MottainaiNoise, uv * 0.35 + t * 0.05).b;
                col += pow(saturate(dot(rdir, _MottainaiSunDir.xyz)), 60) * step(0.8, sparkle) * 1.6;
                float3 hv = normalize(l.direction + v);
                col += pow(saturate(dot(n, hv)), 96) * 0.9 * l.color;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
