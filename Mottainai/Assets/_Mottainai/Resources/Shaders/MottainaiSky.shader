Shader "Mottainai/Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.16, 0.55, 0.95, 1)
        _Mid ("Mid", Color) = (0.42, 0.74, 0.98, 1)
        _Horizon ("Horizon (= fog)", Color) = (0.80, 0.92, 0.99, 1)
        _SunColor ("Sun", Color) = (1, 0.96, 0.82, 1)
        _SunDir ("Sun Dir (visual)", Vector) = (0.45, 0.42, 0.78, 0)
        _CloudColor ("Clouds lit", Color) = (1, 1, 1, 1)
        _CloudShade ("Clouds shade", Color) = (0.74, 0.84, 0.95, 1)
        _MountainNear ("Mountains near", Color) = (0.42, 0.66, 0.52, 1)
        _MountainFar ("Mountains far", Color) = (0.58, 0.76, 0.80, 1)
        _Rays ("Sun rays (High)", Float) = 1
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
                float4 _Zenith, _Mid, _Horizon, _SunColor, _SunDir, _CloudColor, _CloudShade, _MountainNear, _MountainFar;
                float _Rays;
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

            float N(float2 uv) { return SAMPLE_TEXTURE2D(_MottainaiNoise, sampler_MottainaiNoise, uv).r; }

            half4 frag (Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float3 sd = normalize(_SunDir.xyz);
                float s = dot(d, sd);

                // 3-stop gradient: pale horizon (= fog) -> sky blue -> deep zenith
                float3 col = lerp(_Horizon.rgb, _Mid.rgb, smoothstep(0.0, 0.22, h));
                col = lerp(col, _Zenith.rgb, smoothstep(0.18, 0.95, h));
                col += _SunColor.rgb * (pow(saturate(s), 8) * 0.22 + pow(saturate(s), 48) * 0.35); // warm glow around the sun

                // soft cartoon sun rays fanning from the sun (High)
                if (_Rays > 0.5)
                {
                    float3 up = abs(sd.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                    float3 u = normalize(cross(up, sd)), v = cross(sd, u);
                    float3 t = d - sd * s;
                    float ang = atan2(dot(t, v), dot(t, u));
                    float r = SAMPLE_TEXTURE2D(_MottainaiNoise, sampler_MottainaiNoise, float2(ang * 0.55, _Time.y * 0.006)).g;
                    float rays = smoothstep(0.5, 0.78, r) * pow(saturate(s), 5) * saturate(h * 4 + 0.4);
                    col += _SunColor.rgb * rays * 0.28;
                }

                // puffy clouds: two fbm octaves, crisp-ish edges, lit side toward the sun
                if (h > 0.0)
                {
                    float2 uv = d.xz / (h + 0.14) * 0.16 + float2(_Time.y * 0.0012, _Time.y * 0.0024);
                    float c = N(uv) * 0.62 + N(uv * 2.1 + 0.37) * 0.38;
                    float dens = smoothstep(0.54, 0.64, c) * saturate(h * 5.0);
                    float2 toSun = normalize(sd.xz + 1e-4) * 0.025;
                    float c2 = N(uv - toSun) * 0.62 + N((uv - toSun) * 2.1 + 0.37) * 0.38;
                    float lit = saturate((c - c2) * 7.0 + 0.62);
                    float3 cloud = lerp(_CloudShade.rgb, _CloudColor.rgb, lit) + _SunColor.rgb * pow(saturate(s), 6) * 0.25;
                    col = lerp(col, cloud, dens * 0.96);
                }

                // sun disc on top of everything (soft edge)
                col = lerp(col, float3(1.0, 0.99, 0.93), smoothstep(0.9990, 0.9994, s));

                // distant layered mountains on the sides; open sea horizon straight ahead (+Z, downstream)
                float az = atan2(d.x, d.z);
                float side = smoothstep(0.35, 0.9, abs(az));
                float ridgeFar = (0.035 + 0.06 * N(float2(az * 0.9, 0.11))) * side;
                float ridgeNear = (0.015 + 0.05 * N(float2(az * 1.7 + 3.1, 0.47))) * smoothstep(0.55, 1.2, abs(az));
                if (h < ridgeFar) col = lerp(_MountainFar.rgb, _Horizon.rgb, 0.35);
                if (h < ridgeNear) col = lerp(_MountainNear.rgb, _Horizon.rgb, 0.15 + 0.25 * saturate(1 - abs(az) / 2.5));
                col = lerp(col, _Horizon.rgb, saturate(1 - abs(h) * 30) * 0.35); // haze at the horizon line
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
