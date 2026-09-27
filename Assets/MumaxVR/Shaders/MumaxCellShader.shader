// Copyright (C) 2026 Janine Graser and Dr. Thomas Schlake
// SPDX-License-Identifier: AGPL-3.0-or-later
// Additional attribution term (AGPL-3.0 section 7(b)): see NOTICE in the repository root.

// Instanced glyph shader for mumax cells. Each mesh (arrow or stick quad) is
// oriented to its cell's magnetization. Two color modes: mz red/white/blue
// (default) or the mumax-style hue wheel (in-plane angle = hue, mz =
// lightness). mumax stores magnetization Z-up, so the vertex stage swizzles
// positions/directions into Unity's Y-up space. Instances are indirected
// through _VisibleIds, filled by the MumaxCull.compute culling pass; glyphs
// shrink to nothing over [_FadeStart, _FadeEnd] so the LOD far cutoff
// doesn't pop.
Shader "Custom/MumaxCellShader"
{
    Properties
    {
        _Scale ("Mesh Scale", Float) = 0.5
        _Color ("Preview Color", Color) = (0.5, 0.5, 0.5, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off // the mid-LOD stick quads are flat and must show from both sides

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float spinZ : TEXCOORD0;
                float spinAngle : TEXCOORD1; // in-plane angle, mumax xy
                UNITY_VERTEX_OUTPUT_STEREO
            };

            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                StructuredBuffer<float3> _Positions;
                StructuredBuffer<float3> _Directions;
                StructuredBuffer<uint> _VisibleIds;
            #endif

            CBUFFER_START(UnityPerMaterial)
                float _Scale;
                float _GridSpacing;
                float4x4 _GridMatrix;
                half4 _Color;
                float _ColorMode; // 0 = mz red/blue, 1 = hue wheel
                float _FadeStart; // glyphs shrink between these camera
                float _FadeEnd;   // distances; _FadeEnd <= 0 disables
            CBUFFER_END

            void setup() { }

            float3x3 GetRotationMatrix(float3 dir)
            {
                float3 up = float3(0, 1, 0);
                float c = dot(up, dir);

                if (c < -0.9999) return float3x3(1,0,0, 0,-1,0, 0,0,-1);

                float3 v = cross(up, dir);
                float3x3 v_x = float3x3(0, -v.z, v.y, v.z, 0, -v.x, -v.y, v.x, 0);
                float3x3 I = float3x3(1,0,0, 0,1,0, 0,0,1);

                return I + v_x + mul(v_x, v_x) * (1.0 / (1.0 + c));
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posLocal = float3(0, 0, 0);
                float3 dirLocal = float3(0, 1, 0);
                float finalSpinZ = 0;
                float finalSpinAngle = 0;
                float fade = 1.0;

                #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    uint id = _VisibleIds[input.instanceID];
                    float3 rawPos = _Positions[id];
                    float3 rawDir = _Directions[id];

                    // mumax Z-up to Unity Y-up mapping
                    posLocal = float3(rawPos.x, rawPos.z, rawPos.y) * _GridSpacing;
                    dirLocal = normalize(float3(rawDir.x, rawDir.z, rawDir.y));
                    finalSpinZ = rawDir.z;
                    finalSpinAngle = atan2(rawDir.y, rawDir.x);

                    if (_FadeEnd > 0.0)
                    {
                        float3 cellWS = mul(_GridMatrix, float4(posLocal, 1.0)).xyz;
                        fade = 1.0 - smoothstep(_FadeStart, _FadeEnd, distance(cellWS, _WorldSpaceCameraPos));
                    }
                #endif

                float3 scaledVert = input.positionOS.xyz * (_Scale * fade);
                float3x3 rotMat = GetRotationMatrix(dirLocal);
                float3 posGridSpace = mul(rotMat, scaledVert) + posLocal;

                #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    float3 positionWS = mul(_GridMatrix, float4(posGridSpace, 1.0)).xyz;
                #else
                    float3 positionWS = mul(unity_ObjectToWorld, float4(posGridSpace, 1.0)).xyz;
                #endif

                output.positionCS = TransformWorldToHClip(positionWS);
                output.spinZ = finalSpinZ;
                output.spinAngle = finalSpinAngle;

                return output;
            }

            // mumax-style wheel: in-plane angle = hue, mz = lightness
            // (+z -> white, -z -> black)
            half3 HueWheel(float angle, float mz)
            {
                float h = frac(angle / 6.28318530718 + 1.0);
                half3 rgb = saturate(abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
                float l = 0.5 + 0.5 * clamp(mz, -1.0, 1.0);
                return l < 0.5
                    ? rgb * (2.0 * l)
                    : lerp(rgb, half3(1, 1, 1), 2.0 * (l - 0.5));
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    half3 finalColor;
                    if (_ColorMode > 0.5)
                    {
                        finalColor = HueWheel(input.spinAngle, input.spinZ);
                    }
                    else
                    {
                        half3 colorRed = half3(1, 0, 0);
                        half3 colorWhite = half3(1, 1, 1);
                        half3 colorBlue = half3(0, 0, 1);
                        finalColor = input.spinZ > 0
                            ? lerp(colorWhite, colorRed, input.spinZ)
                            : lerp(colorWhite, colorBlue, -input.spinZ);
                    }
                    return half4(finalColor, 1.0);
                #else
                    return _Color;
                #endif
            }
            ENDHLSL
        }
    }
}
