// DreamCodeVR+ — the holographic surface used by the platform rings, the security
// shield, and the personal-space sphere.
//
// Unlit by design: colour comes from emission, not from lights, so the scene needs no
// realtime shadow casters and stays cheap on a standalone headset. Three effects, all
// pure ALU:
//   * fresnel rim      — edges glow, faces stay glassy (the "hologram" read)
//   * scanline sweep   — a band travelling along local Y
//   * pulse            — global intensity breathing, driven from script for state changes
//
// _Alpha and _Color are set per-instance so one material serves cyan (safe),
// amber (validating) and red (blocked) without extra draw-call state.
Shader "DreamCodeVRPlus/Holo"
{
    Properties
    {
        _Color      ("Colour",            Color) = (0.15, 0.85, 1.0, 1)
        _Alpha      ("Base Alpha",        Range(0,1)) = 0.25
        _RimPower   ("Rim Falloff",       Range(0.5, 12)) = 3.0
        _RimBoost   ("Rim Intensity",     Range(0, 8)) = 2.2
        _ScanSpeed  ("Scan Speed",        Range(-4, 4)) = 0.5
        _ScanDensity("Scan Density",      Range(0, 120)) = 18
        _ScanBoost  ("Scan Intensity",    Range(0, 3)) = 0.35
        _Pulse      ("Pulse (script)",    Range(0, 4)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent"
               "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        // Additive-over-alpha keeps the glow reading as light rather than paint, and
        // ZWrite Off avoids sorting artefacts between the nested rings.
        Blend SrcAlpha One
        ZWrite Off
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
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(half4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(half,  _Alpha)
                UNITY_DEFINE_INSTANCED_PROP(half,  _Pulse)
            UNITY_INSTANCING_BUFFER_END(Props)

            half _RimPower, _RimBoost, _ScanSpeed, _ScanDensity, _ScanBoost;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionOS = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 tint  = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                half  alpha = UNITY_ACCESS_INSTANCED_PROP(Props, _Alpha);
                half  pulse = UNITY_ACCESS_INSTANCED_PROP(Props, _Pulse);

                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 N = normalize(IN.normalWS);
                // Two-sided: shells are viewed from inside as well as outside.
                float ndv = saturate(abs(dot(N, V)));
                float rim = pow(1.0 - ndv, _RimPower) * _RimBoost;

                float scan = sin(IN.positionOS.y * _ScanDensity - _Time.y * _ScanSpeed * 6.283);
                scan = saturate(scan) * _ScanBoost;

                float intensity = alpha + rim + scan + pulse;
                return half4(tint.rgb * intensity, saturate(intensity));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
