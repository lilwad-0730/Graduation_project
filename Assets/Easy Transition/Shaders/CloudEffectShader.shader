Shader "UI/EasyTransitionCloud"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _CloudColor ("Cloud Color", Color) = (0, 0, 0, 1)
        _Cutoff ("Cutoff", Range(0, 1)) = 0
        _Smoothness ("Cloud Edge Softness", Range(0.005, 0.25)) = 0.06
        _CloudScale ("Cloud Scale", Range(1, 12)) = 4.5
        _Drift ("Cloud Drift", Vector) = (0.08, 0.025, 0, 0)
        _RectSize ("Rect Size", Vector) = (1, 1, 0, 0)

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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "CloudTransition"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            fixed4 _CloudColor;
            float _Cutoff;
            float _Smoothness;
            float _CloudScale;
            float4 _Drift;
            float4 _ClipRect;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;

                [unroll]
                for (int octave = 0; octave < 5; octave++)
                {
                    value += valueNoise(p) * amplitude;
                    p = p * 2.03 + float2(17.13, 9.71);
                    amplitude *= 0.5;
                }

                return value;
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float aspect = max(_ScreenParams.x / max(_ScreenParams.y, 1.0), 1.0);
                float2 uv = input.texcoord;
                uv.x *= aspect;

                float2 drift = _Drift.xy * _Time.y;
                float detail = fbm(uv * _CloudScale + drift);
                float billow = fbm(uv * (_CloudScale * 0.55) - drift * 0.65 + float2(5.17, 8.31));
                float cloudShape = (detail * 0.68 + billow * 0.32 - 0.5) * 0.95;

                float front = lerp(-0.8, aspect + 0.8, saturate(_Cutoff));
                float coverage = smoothstep(-_Smoothness, _Smoothness, front - uv.x + cloudShape);

                if (_Cutoff <= 0.0001)
                    coverage = 0.0;
                else if (_Cutoff >= 0.9999)
                    coverage = 1.0;

                fixed4 color = _CloudColor * input.color;
                color.a *= coverage;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
