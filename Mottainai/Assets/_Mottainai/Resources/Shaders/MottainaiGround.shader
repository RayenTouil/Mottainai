Shader "Mottainai/Ground"
{
    Properties
    {
        _GrassA ("Grass light", Color) = (0.52, 0.79, 0.35, 1)
        _GrassB ("Grass dark", Color) = (0.31, 0.58, 0.25, 1)
        _Path ("Path", Color) = (0.89, 0.79, 0.60, 1)
        _Earth ("Earth", Color) = (0.62, 0.47, 0.33, 1)
        _PathX ("Path |x|", Float) = 17
        _PathW ("Path width", Float) = 1.8
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
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MottainaiNoise); SAMPLER(sampler_MottainaiNoise);
            CBUFFER_START(UnityPerMaterial)
                float4 _GrassA, _GrassB, _Path, _Earth;
                float _PathX, _PathW;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                o.positionWS = wp;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.positionCS = TransformWorldToHClip(wp);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float4 Nz(float2 uv) { return SAMPLE_TEXTURE2D(_MottainaiNoise, sampler_MottainaiNoise, uv); }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float2 p = i.positionWS.xz;
                // grass: large soft light/dark patches
                float v = Nz(p * 0.045).r * 0.6 + Nz(p * 0.16).g * 0.4;
                float3 col = lerp(_GrassB.rgb, _GrassA.rgb, smoothstep(0.38, 0.62, v));
                col *= 0.94 + 0.12 * Nz(p * 0.6).r; // fine blades
                // gently winding park path along each bank
                float px = _PathX + sin(p.y * 0.045) * 0.8 + sin(p.y * 0.013 + 1.7) * 0.6;
                float d = abs(abs(p.x) - px);
                float path = 1 - smoothstep(_PathW * 0.5 - 0.12, _PathW * 0.5 + 0.12, d);
                // small flowers: sparse speckles (pink / yellow / white), never on the path
                float4 fz = Nz(p * 0.85);
                float flower = step(0.84, fz.b) * (1 - path) * step(0.5, n.y);
                float pick = Nz(p * 0.21 + 0.5).r;
                float3 fcol = pick < 0.42 ? float3(1.0, 0.55, 0.72) : pick < 0.58 ? float3(1.0, 0.88, 0.3) : float3(1, 1, 1);
                col = lerp(col, fcol, flower);
                col = lerp(col, _Path.rgb * (0.95 + 0.08 * fz.g), path * step(0.5, n.y));
                // bank walls: warm earth with soft layers
                float wall = 1 - smoothstep(0.35, 0.7, n.y);
                col = lerp(col, _Earth.rgb * (0.88 + 0.14 * Nz(float2(p.y * 0.08, i.positionWS.y * 0.6)).r), wall);

                Light l = GetMainLight();
                float toon = smoothstep(-0.05, 0.2, dot(n, l.direction)) * 0.5 + 0.5;
                col = col * (toon * l.color * 0.92 + SampleSH(n) * 0.38);
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
