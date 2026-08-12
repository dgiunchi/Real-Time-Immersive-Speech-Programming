// DreamCodeVR+ — procedural gradient skybox.
//
// A three-stop vertical gradient with a soft horizon glow. No cubemap, no texture
// sampling, no lighting: a handful of ALU per pixel, which on a tile-based mobile GPU
// is close to free. Replaces an HDRI entirely and keeps the APK small.
Shader "DreamCodeVRPlus/SkyGradient"
{
    Properties
    {
        _GroundColor ("Ground / Low", Color) = (0.02, 0.03, 0.05, 1)
        _HorizonColor("Horizon",      Color) = (0.05, 0.16, 0.26, 1)
        _SkyColor    ("Zenith",       Color) = (0.01, 0.02, 0.05, 1)
        _GlowColor   ("Horizon Glow", Color) = (0.09, 0.55, 0.72, 1)
        _GlowPower   ("Glow Falloff", Range(1, 64)) = 14
        _Exponent    ("Gradient Bias",Range(0.2, 4)) = 1.1
    }

    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background"
               "RenderPipeline"="UniversalPipeline" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

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
                float3 dirWS      : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _GroundColor, _HorizonColor, _SkyColor, _GlowColor;
            half  _GlowPower, _Exponent;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dirWS = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dirWS);
                // 0 at the horizon, 1 straight up, -1 straight down.
                float h = d.y;
                float up = pow(saturate(h), _Exponent);
                float down = saturate(-h);

                half3 col = lerp(_HorizonColor.rgb, _SkyColor.rgb, up);
                col = lerp(col, _GroundColor.rgb, down);

                // Tight band of light hugging the horizon — reads as atmosphere and
                // gives the distant silhouettes something to sit against.
                float glow = pow(saturate(1.0 - abs(h)), _GlowPower);
                col += _GlowColor.rgb * glow;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
