// ★1109e 海市蜃樓：地平線上一條銀藍色、會扭動閃爍的「假水面」光帶
// 做在一張橫向長方形上 (UV 0~1)；實際 alpha 由 _Fade (腳本依玩家 x 控制) 與 _MaxAlpha 決定
Shader "Custom/OasisMirage"
{
    Properties
    {
        _Color ("主色 (銀藍)", Color) = (0.72, 0.87, 1.0, 1)
        _HiColor ("亮紋顏色", Color) = (0.95, 0.98, 1.0, 1)
        _Size ("長方形世界尺寸 (寬,高)", Vector) = (43, 2.2, 0, 0)
        _Fade ("整體淡入淡出 (腳本控制)", Range(0,1)) = 1
        _MaxAlpha ("最大 alpha", Range(0,1)) = 0.28
        _HorizonV ("地平線高度 (0~1)", Range(0,1)) = 0.38
        _WaveSpeed ("波動速度", Float) = 0.7
        _EndFade ("左右兩端淡出 (米)", Float) = 5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _HiColor;
                float4 _Size;
                float _Fade;
                float _MaxAlpha;
                float _HorizonV;
                float _WaveSpeed;
                float _EndFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float hash21 (float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise (float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 frag (Varyings i) : SV_Target
            {
                float t = _Time.y * _WaveSpeed;
                float wx = i.uv.x * _Size.x;          // 世界 x (米)
                float wy = (i.uv.y - _HorizonV) * _Size.y; // 離地平線的高度 (米)

                // 光帶中心跟著 x 輕輕上下扭動
                float wob = sin(wx * 0.9 + t * 1.7) * 0.07 + sin(wx * 0.23 - t * 1.1) * 0.12;
                float dy = wy - wob;

                // 垂直輪廓：地平線最強，上方衰減較慢、下方較快
                float s = dy > 0 ? 0.7 : 0.45;
                float prof = exp(-(dy * dy) / (s * s));

                // 橫向細長亮紋 (x 拉長、y 壓密)，會左右流動並上下抖
                float sk = dy * 11.0 + sin(wx * 0.6 + t * 2.0) * 0.8;
                float n1 = vnoise(float2(wx * 0.22 + t * 0.9, sk));
                float n2 = vnoise(float2(wx * 0.55 - t * 1.3, sk * 1.7 + 3.1));
                float streak = smoothstep(0.4, 0.8, n1 * 0.6 + n2 * 0.4);

                // 整體閃爍 (慢) + 局部閃爍 (快)
                float flick = 0.8 + 0.2 * sin(t * 2.3 + wx * 0.4) + 0.1 * sin(t * 7.0 + wx * 1.7);

                // 左右兩端淡出，避免出現硬邊
                float ex = saturate(min(wx, _Size.x - wx) / max(_EndFade, 0.01));
                ex = ex * ex * (3.0 - 2.0 * ex);

                float a = prof * (0.35 + 0.65 * streak) * flick * ex * _Fade * _MaxAlpha;
                half3 col = lerp(_Color.rgb, _HiColor.rgb, streak);
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
}
