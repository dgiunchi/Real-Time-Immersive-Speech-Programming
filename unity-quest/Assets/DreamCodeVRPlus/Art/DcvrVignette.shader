// DreamCodeVR+ — comfort vignette.
//
// A soft tunnel that closes slightly WHILE the wearer is moving and opens again the
// moment they stop. Restricting peripheral optic flow during artificial locomotion is
// the best-evidenced mitigation for simulator sickness, and it is the one full-field
// effect worth its cost here: a single transparent quad, no texture, no post pass.
//
// It is deliberately subtle and motion-gated. A vignette that is always on reads as a
// dirty lens and adds nothing.
Shader "DreamCodeVRPlus/Vignette"
{
    Properties
    {
        _Color  ("Colour",       Color) = (0, 0, 0, 1)
        _Inner  ("Inner Radius", Range(0, 1.5)) = 0.62
        _Outer  ("Outer Radius", Range(0, 2.0)) = 1.05
        _Amount ("Amount",       Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay"
               "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

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
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _Color;
            half  _Inner, _Outer, _Amount;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 d = IN.uv - 0.5;
                float r = length(d) * 2.0;
                // Tighten the aperture as _Amount rises, so the tunnel closes with speed.
                float inner = lerp(_Inner + 0.5, _Inner, _Amount);
                float outer = lerp(_Outer + 0.5, _Outer, _Amount);
                float a = smoothstep(inner, outer, r) * _Amount;
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
