// DreamCodeVR+ — city tower surface.
//
// The skyline was flat near-black slabs. Silhouette alone gave depth but no life, and at
// distance the whole band read as a hole cut in the sky rather than as architecture.
//
// This gives every tower three things, all procedural — no texture, no lightmap, one
// material shared by the entire band:
//
//   * a vertical gradient, deep at the base rising to the scene's teal, so towers sit in
//     the atmosphere instead of being pasted on it;
//   * a window grid derived from object space, with only some cells lit, seeded per tower
//     so no two repeat;
//   * a slow per-window twinkle, at a rate low enough to read as occupancy rather than
//     flicker — this fills a large part of the peripheral field and anything faster would
//     be uncomfortable and, in aggregate, a photosensitivity concern.
//
// Cost is a handful of ALU per fragment with no texture fetch, which on a mobile GPU is
// cheaper than the bandwidth a window texture would need.
Shader "DreamCodeVRPlus/Building"
{
    Properties
    {
        _BaseColor   ("Base (bottom)", Color) = (0.020, 0.030, 0.048, 1)
        _TopColor    ("Base (top)",    Color) = (0.055, 0.105, 0.150, 1)
        _WindowColor ("Window",        Color) = (0.20, 0.85, 1.00, 1)
        _WarmColor   ("Warm Window",   Color) = (1.00, 0.72, 0.35, 1)
        _WindowDensity ("Window Density", Range(1, 40)) = 14
        _LitFraction ("Lit Fraction",  Range(0, 1)) = 0.34
        _WarmFraction("Warm Fraction", Range(0, 1)) = 0.16
        _Twinkle     ("Twinkle Speed", Range(0, 2)) = 0.35
        _Emission    ("Window Emission", Range(0, 6)) = 2.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry"
               "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull Back
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
                float3 normalOS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _BaseColor, _TopColor, _WindowColor, _WarmColor;
            half  _WindowDensity, _LitFraction, _WarmFraction, _Twinkle, _Emission;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS = IN.normalOS;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Height across the mesh, 0 at the base and 1 at the top.
                float h = saturate(IN.positionOS.y + 0.5);
                half3 body = lerp(_BaseColor.rgb, _TopColor.rgb, h);

                // Pick the two axes that face the viewer for this side, so windows read
                // correctly on all four faces without a UV set.
                float3 n = abs(IN.normalOS);
                float2 uv = n.z > 0.5 ? IN.positionOS.xy
                          : (n.x > 0.5 ? IN.positionOS.zy : IN.positionOS.xz);

                // Roof: no windows, just the top tone.
                if (n.y > 0.5) { return half4(_TopColor.rgb * 0.8, 1); }

                float2 cell = floor(uv * _WindowDensity);
                float2 f = frac(uv * _WindowDensity);

                // Window pane inside each cell, with a margin so the grid reads as
                // structure rather than as a checkerboard.
                float pane = step(0.18, f.x) * step(f.x, 0.82)
                           * step(0.24, f.y) * step(f.y, 0.76);

                // Per-cell seed, offset by the object's world position so towers differ.
                float seed = hash21(cell + floor(IN.positionWS.xz * 0.37));
                float lit = step(1.0 - _LitFraction, seed);

                // Slow twinkle, independent per window.
                float tw = 0.75 + 0.25 * sin(_Time.y * _Twinkle + seed * 43.0);

                // A minority of warm windows, so the band is not uniformly cyan.
                float warm = step(1.0 - _WarmFraction, hash21(cell * 1.7 + 11.0));
                half3 wcol = lerp(_WindowColor.rgb, _WarmColor.rgb, warm);

                half3 col = body + wcol * (pane * lit * tw * _Emission * 0.28);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
