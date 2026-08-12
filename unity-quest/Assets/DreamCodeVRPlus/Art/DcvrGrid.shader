// DreamCodeVR+ — the ground plane: an emissive grid dissolving into fog.
//
// The grid is generated from world-space position with screen-space derivatives, so the
// lines stay one pixel wide at any distance instead of aliasing into noise — the usual
// failure of a tiled grid texture in VR, where the user can lean right down to the floor.
// Opaque, unlit, no shadows, one texture-free pass.
Shader "DreamCodeVRPlus/Grid"
{
    Properties
    {
        _BaseColor ("Base",            Color) = (0.012, 0.017, 0.028, 1)
        _LineColor ("Grid Line",       Color) = (0.08, 0.42, 0.55, 1)
        _Spacing   ("Cell Size (m)",   Range(0.1, 10)) = 1.0
        _LineWidth ("Line Width",      Range(0.5, 4)) = 1.2
        _FadeStart ("Fade Start (m)",  Float) = 12
        _FadeEnd   ("Fade End (m)",    Float) = 46
        _Pulse     ("Pulse (script)",  Range(0, 3)) = 0
        _PulseColor("Pulse Colour",    Color) = (0.15, 0.85, 1.0, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _BaseColor, _LineColor, _PulseColor;
            half  _Spacing, _LineWidth, _Pulse;
            float _FadeStart, _FadeEnd;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = IN.positionWS.xz / max(_Spacing, 0.001);
                // Distance to the nearest cell edge, normalised by the pixel footprint,
                // which is what keeps the line a constant width on screen.
                float2 grid = abs(frac(uv - 0.5) - 0.5) / max(fwidth(uv), 1e-5);
                float  line_ = 1.0 - saturate(min(grid.x, grid.y) / _LineWidth);

                float dist = distance(IN.positionWS.xz, GetCameraPositionWS().xz);
                float fade = 1.0 - saturate((dist - _FadeStart) / max(_FadeEnd - _FadeStart, 0.001));

                // Concentric ring travelling outward from the origin on a state change.
                float ring = 0;
                if (_Pulse > 0.001)
                {
                    float r = length(IN.positionWS.xz);
                    ring = saturate(1.0 - abs(r - _Pulse * 14.0) * 1.6) * saturate(3.0 - _Pulse);
                }

                half3 col = _BaseColor.rgb + _LineColor.rgb * line_ * fade;
                col += _PulseColor.rgb * ring;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
