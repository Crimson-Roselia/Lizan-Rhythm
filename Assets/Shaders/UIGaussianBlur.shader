Shader "Lizan/UI/Gaussian Blur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 10)) = 0
        _UVRect ("Sprite UV Rect (Min XY, Max ZW)", Vector) = (0, 0, 1, 1)

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UIGaussianBlur"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                half4 mask : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _UVRect;
            float _Intensity;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;

            // Bilinear pairs of a normalized, symmetric nine-tap Gaussian kernel.
            // Their outer product evaluates a 2D blur in 25 texture reads.
            static const float2 GaussianOffsets = float2(1.38461538, 3.23076923);
            static const float3 GaussianWeights = float3(0.22702702, 0.31621622, 0.07027027);

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color * _Color;

                // Match the standard UI shader's RectMask2D soft clipping.
                float2 pixelSize = output.vertex.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clipRect = clamp(_ClipRect, -2e10, 2e10);
                output.mask = half4(
                    input.vertex.xy * 2.0 - clipRect.xy - clipRect.zw,
                    0.25 / (0.25 * float2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize)));

                return output;
            }

            half4 SampleSprite(float2 uv, float2 minUV, float2 maxUV)
            {
                // Clamp to texel centers, including when the texture uses Repeat.
                half4 color = tex2D(_MainTex, clamp(uv, minUV, maxUV)) + _TextureSampleAdd;
                // Filter premultiplied color to keep transparent edges from darkening.
                color.rgb *= color.a;
                return color;
            }

            half4 SampleRow(float2 uv, float2 stepUV, float2 minUV, float2 maxUV)
            {
                half4 color = SampleSprite(uv, minUV, maxUV) * GaussianWeights.x;
                float2 offset = float2(stepUV.x * GaussianOffsets.x, 0.0);
                color += (SampleSprite(uv - offset, minUV, maxUV)
                    + SampleSprite(uv + offset, minUV, maxUV)) * GaussianWeights.y;
                offset.x = stepUV.x * GaussianOffsets.y;
                color += (SampleSprite(uv - offset, minUV, maxUV)
                    + SampleSprite(uv + offset, minUV, maxUV)) * GaussianWeights.z;
                return color;
            }

            half4 GaussianBlur(float2 uv)
            {
                half4 color = 0;
                // Zero intensity must follow the original texture sampling exactly.
                UNITY_BRANCH
                if (_Intensity <= 0.0)
                {
                    color = tex2D(_MainTex, uv) + _TextureSampleAdd;
                    color.rgb *= color.a;
                }
                else
                {
                    float2 halfTexel = abs(_MainTex_TexelSize.xy) * 0.5;
                    float2 minUV = _UVRect.xy + halfTexel;
                    float2 maxUV = max(minUV, _UVRect.zw - halfTexel);
                    float2 stepUV = abs(_MainTex_TexelSize.xy) * _Intensity;
                    color = SampleRow(uv, stepUV, minUV, maxUV) * GaussianWeights.x;
                    float2 offset = float2(0.0, stepUV.y * GaussianOffsets.x);
                    color += (SampleRow(uv - offset, stepUV, minUV, maxUV)
                        + SampleRow(uv + offset, stepUV, minUV, maxUV)) * GaussianWeights.y;
                    offset.y = stepUV.y * GaussianOffsets.y;
                    color += (SampleRow(uv - offset, stepUV, minUV, maxUV)
                        + SampleRow(uv + offset, stepUV, minUV, maxUV)) * GaussianWeights.z;
                }

                return color;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 color = GaussianBlur(input.uv);
                color.rgb *= input.color.rgb * input.color.a;
                color.a *= input.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                color *= mask.x * mask.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }

    Fallback Off
}
