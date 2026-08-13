// DreamCodeVR+ — materialization dissolve with a glowing edge.
//
// A generated object should look ASSEMBLED rather than switched on. This clips the surface
// against a procedural noise field and lights the band right at the clip threshold, so the
// object resolves out of nothing with a bright travelling edge instead of popping in.
//
// Noise is computed in the shader rather than sampled from a texture: no asset to ship, no
// texture memory, and the whole effect costs a handful of ALU per fragment. On a mobile GPU
// that is far cheaper than the bandwidth a noise texture would cost.
//
// _Cutoff drives the whole thing: 1 = fully dissolved (invisible), 0 = fully solid.
Shader "DreamCodeVRPlus/Dissolve"
{
    Properties
    {
        _BaseColor ("Base Colour",  Color) = (0.6, 0.7, 0.85, 1)
        _EdgeColor ("Edge Colour",  Color) = (0.15, 0.85, 1.0, 1)
        _Cutoff    ("Dissolve",     Range(0, 1)) = 1
        _EdgeWidth ("Edge Width",   Range(0.001, 0.4)) = 0.12
        _NoiseScale("Noise Scale",  Range(0.5, 20)) = 6
        _EdgeBoost ("Edge Boost",   Range(1, 8)) = 3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry"
               "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _BaseColor, _EdgeColor;
            half  _Cutoff, _EdgeWidth, _NoiseScale, _EdgeBoost;

            // Cheap value noise. Three octaves is enough to read as organic at this scale
            // and keeps the instruction count low.
            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + float3(0.71, 0.113, 0.419));
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);   // smoothstep weights
                float n000 = hash31(i + float3(0, 0, 0));
                float n100 = hash31(i + float3(1, 0, 0));
                float n010 = hash31(i + float3(0, 1, 0));
                float n110 = hash31(i + float3(1, 1, 0));
                float n001 = hash31(i + float3(0, 0, 1));
                float n101 = hash31(i + float3(1, 0, 1));
                float n011 = hash31(i + float3(0, 1, 1));
                float n111 = hash31(i + float3(1, 1, 1));
                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Object space, so the pattern stays welded to the object as it moves.
                float n = vnoise(IN.positionOS * _NoiseScale);
                n = n * 0.65 + vnoise(IN.positionOS * _NoiseScale * 2.3) * 0.35;

                float d = n - _Cutoff;
                clip(d);                       // everything below the threshold is gone

                // Light the band just above the threshold; that band IS the visible edge.
                float edge = 1.0 - saturate(d / max(_EdgeWidth, 1e-4));

                // A little normal-based shading so the solid part is not flat.
                float shade = 0.55 + 0.45 * saturate(dot(normalize(IN.normalWS), float3(0.3, 0.9, -0.2)));

                half3 col = _BaseColor.rgb * shade;
                col = lerp(col, _EdgeColor.rgb * _EdgeBoost, edge * edge);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
