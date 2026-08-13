// DreamCodeVR+ — city tower surface.
//
// TWO BUGS THIS REWRITE FIXES, both reported from the headset as "some buildings are
// black, some are glitchy":
//
//  1. WINDOW CELLS WERE SIZED IN OBJECT SPACE. A Unity cube spans +/-0.5 whatever its
//     scale, so a 7 x 60 m tower and a 14 x 22 m tower both got the same 13x13 grid —
//     stretched into enormous smeared rectangles on tall meshes and packed tight on
//     squat ones. Windows are now measured in METRES, derived from the object's scale
//     out of the model matrix, so every building in the skyline has windows the same
//     real size regardless of its dimensions.
//
//  2. THE PER-TOWER SEED CAME FROM THE FRAGMENT'S WORLD POSITION. It was intended to
//     vary the pattern per building, but sampling it per fragment meant the seed changed
//     ACROSS a single surface, so towers broke into blocks of lit and unlit windows with
//     hard seams between them — the "glitch". The seed now comes from the object's own
//     translation in the model matrix: one value per building, constant over its surface.
//
// Everything is still procedural: no texture, no lightmap, one material per band. The
// variation between towers is produced by the shader from each object's transform, so a
// shared material still yields a skyline where no two buildings look alike.
Shader "DreamCodeVRPlus/Building"
{
    Properties
    {
        _BaseColor   ("Base (bottom)", Color) = (0.020, 0.030, 0.048, 1)
        _TopColor    ("Base (top)",    Color) = (0.055, 0.105, 0.150, 1)
        _WindowColor ("Window",        Color) = (0.20, 0.85, 1.00, 1)
        _WarmColor   ("Warm Window",   Color) = (1.00, 0.72, 0.35, 1)
        _WindowWidth ("Window Width m",  Range(0.4, 6)) = 1.6
        _WindowHeight("Window Height m", Range(0.4, 8)) = 2.2
        _LitFraction ("Lit Fraction",  Range(0, 1)) = 0.38
        _WarmFraction("Warm Fraction", Range(0, 1)) = 0.18
        _Twinkle     ("Twinkle Speed", Range(0, 2)) = 0.30
        _Emission    ("Window Emission", Range(0, 6)) = 2.6
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
                float3 scale      : TEXCOORD2;   // object's world size, from the model matrix
                float2 seedXZ     : TEXCOORD3;   // object's world translation: ONE per building
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _BaseColor, _TopColor, _WindowColor, _WarmColor;
            half  _WindowWidth, _WindowHeight, _LitFraction, _WarmFraction, _Twinkle, _Emission;

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

                // Object scale from the model matrix column lengths, so window size can be
                // expressed in metres rather than in fractions of a mesh.
                float3x3 m = (float3x3)UNITY_MATRIX_M;
                OUT.scale = float3(length(m._m00_m10_m20),
                                   length(m._m01_m11_m21),
                                   length(m._m02_m12_m22));

                // Model-matrix translation: constant across this object's surface, which is
                // exactly what a per-building seed needs to be.
                OUT.seedXZ = float2(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m23);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float h = saturate(IN.positionOS.y + 0.5);
                half3 body = lerp(_BaseColor.rgb, _TopColor.rgb, h);

                float3 n = abs(IN.normalOS);

                // Roofs get no windows.
                if (n.y > 0.5) { return half4(_TopColor.rgb * 0.75, 1); }

                // Pick the two axes facing outward on this side, and convert to METRES by
                // multiplying through the object's scale. This is the fix that makes every
                // tower's windows the same real size.
                float2 uvM = n.z > 0.5
                    ? float2(IN.positionOS.x * IN.scale.x, IN.positionOS.y * IN.scale.y)
                    : float2(IN.positionOS.z * IN.scale.z, IN.positionOS.y * IN.scale.y);

                float2 grid = float2(uvM.x / max(_WindowWidth, 0.1),
                                     uvM.y / max(_WindowHeight, 0.1));
                float2 cell = floor(grid);
                float2 f = frac(grid);

                // Pane with a margin, so the grid reads as structure not a checkerboard.
                float pane = step(0.16, f.x) * step(f.x, 0.84)
                           * step(0.20, f.y) * step(f.y, 0.80);

                // ONE seed per building, offset by the cell — constant across the surface.
                float building = hash21(floor(IN.seedXZ * 0.5));
                float seed = hash21(cell + building * 97.0);
                float lit = step(1.0 - _LitFraction, seed);

                float tw = 0.78 + 0.22 * sin(_Time.y * _Twinkle + seed * 43.0);

                float warm = step(1.0 - _WarmFraction, hash21(cell * 1.7 + building * 31.0));
                half3 wcol = lerp(_WindowColor.rgb, _WarmColor.rgb, warm);

                // Floors near the base stay darker, which reads as a plinth and stops every
                // tower looking like it is lit uniformly from top to bottom.
                float plinth = smoothstep(0.0, 0.12, h);

                half3 col = body + wcol * (pane * lit * tw * plinth * _Emission * 0.30);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
