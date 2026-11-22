Shader "Mottainai/Water"
{
    // ART_DIRECTION_v3 §4: unlit opaque, bank-distance depth gradient, fresnel tint, edge foam, flow, caustics, crossed sine waves.
    Properties
    {
        _Shallow ("water.shallow", Color) = (0.247, 0.827, 0.890, 1)
        _Deep ("water.deep", Color) = (0.090, 0.549, 0.722, 1)
        _Horizon ("water.horizon", Color) = (0.561, 0.902, 0.941, 1)
        _FoamColor ("foam", Color) = (0.957, 0.984, 1, 1)
        _CausticColor ("caustic", Color) = (0.812, 0.984, 1, 1)
        _Caustics ("Caustics", 2D) = "black" {}
        _HalfWidth ("River half width", Float) = 12
        _Flow ("Flow (units/s)", Float) = 0.4
        _Tier ("Tier 0 Low 1 Med 2 High", Float) = 0
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

            TEXTURE2D(_Caustics); SAMPLER(sampler_Caustics);
            TEXTURE2D(_MottainaiFx); SAMPLER(sampler_MottainaiFx);
            TEXTURE2D(_MottainaiNoise); SAMPLER(sampler_MottainaiNoise);
            CBUFFER_START(UnityPerMaterial)
                float4 _Shallow, _Deep, _Horizon, _FoamColor, _CausticColor;
                float _HalfWidth, _Flow, _Tier;
            CBUFFER_END
            float4 _MottainaiFxRect; // x0, z0, 1/spanX, 1/spanZ of the effects map (R obstacle foam collars, B raft wake)

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 n : TEXCOORD1; float fog : TEXCOORD2; };

            // two crossed sines (0 and 70 deg), amplitude 0.06 m, wavelength 4 m, speed 1.2
            float Wave(float2 p, float t, out float3 n)
            {
                const float k = 6.2831853 / 4.0;
                const float a = 0.06;
                float2 d1 = float2(0, 1), d2 = float2(0.9397, 0.3420);
                float s1 = dot(d1, p) * k - t * 1.2, s2 = dot(d2, p) * k - t * 1.2;
                float2 g = a * k * (cos(s1) * d1 + cos(s2) * d2);
                n = normalize(float3(-g.x, 1, -g.y));
                return a * (sin(s1) + sin(s2));
            }

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                float3 n;
                wp.y += Wave(wp.xz, _Time.y, n);
                o.positionWS = wp; o.n = n;
                o.positionCS = TransformWorldToHClip(wp);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float t = _Time.y;
                // river UVs: u = 0 left bank .. 1 right bank; v = river widths along the river
                float u = saturate((i.positionWS.x + _HalfWidth) / (2.0 * _HalfWidth));
                float v = i.positionWS.z / (2.0 * _HalfWidth);
                float d = min(u, 1.0 - u);
                float3 col = lerp(_Shallow.rgb, _Deep.rgb, smoothstep(0.0, 0.35, d));

                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                col = lerp(col, _Horizon.rgb, pow(1.0 - saturate(dot(normalize(i.n), V)), 3.0) * 0.35);

                // caustics (tiling 6), flowing along +v
                float flow = -t * _Flow;
                float2 cuv = float2(u, v) * 2.0; // ~3 m cells
                float c = SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics, cuv + float2(0.03 * t, 0.05 * t + flow)).r;
                if (_Tier > 0.5) c *= SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics, cuv + float2(-0.04 * t + 0.37, 0.02 * t + flow + 0.21)).r * 1.6;
                col += _CausticColor.rgb * c * 0.15;
                if (_Tier > 1.5) col += step(0.9, c) * 0.3;

                // edge foam band (d < 0.04), broken by scrolling noise (tiling 8, speed 0.15)
                float nz = SAMPLE_TEXTURE2D(_MottainaiNoise, sampler_MottainaiNoise, float2(u, v) * 8.0 + float2(0, -t * 0.15)).g;
                float edgeFoam = 1.0 - smoothstep(0.02, 0.04, d + (nz - 0.5) * 0.03);

                // effects map: obstacle foam collars (R, 70%) + raft V-wake (B, <= 80%)
                float2 fxuv = (i.positionWS.xz - _MottainaiFxRect.xy) * _MottainaiFxRect.zw;
                float inside = step(0.0, fxuv.x) * step(fxuv.x, 1.0) * step(0.0, fxuv.y) * step(fxuv.y, 1.0);
                float4 fx = SAMPLE_TEXTURE2D(_MottainaiFx, sampler_MottainaiFx, fxuv) * inside;
                float fxFoam = max(fx.r * 0.7, fx.b * 0.8) * smoothstep(0.2, 0.5, nz + max(fx.r, fx.b) * 0.3);

                col = lerp(col, _FoamColor.rgb, saturate(max(edgeFoam, fxFoam)));
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
