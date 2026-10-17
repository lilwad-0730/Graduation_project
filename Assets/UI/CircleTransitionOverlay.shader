// Easy Transition 的 CircleEffectShader（Shader Graph）手寫成 UI shader 版本，計算方式完全一樣。
// 差別：Shader Graph 的 URP Unlit 在 Screen Space - Overlay 的 Canvas 上畫不出來，這支可以，
// 所以「回主選單」轉場能放在最上層 Overlay，蓋過設定畫面與齒輪 HUD。
Shader "UI/EasyTransition/CircleOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Cutoff ("Cutoff", Range(0, 1)) = 1
        _Invert ("Invert", Float) = 0
        _Smoothness ("Smoothness", Float) = 0.001
        _RectSize ("Rect Size", Vector) = (1, 1, 0, 0)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            float _Cutoff;
            float _Invert;
            float _Smoothness;
            float4 _RectSize;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float aspect = _RectSize.x / max(_RectSize.y, 0.0001);
                float2 p = i.uv - 0.5;
                float d = length(float2(p.x * aspect, p.y));
                float maxD = length(float2(aspect * 0.5, 0.5));
                float n = d / maxD;

                float lo = lerp(-_Smoothness, 1.0, _Cutoff);
                float hi = lerp(0.0, _Smoothness + 1.0, _Cutoff);
                float a = smoothstep(lo, hi, n);
                a = lerp(a, 1.0 - a, _Invert);
                return fixed4(0, 0, 0, a);
            }
            ENDCG
        }
    }
}
