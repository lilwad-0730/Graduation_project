// ★1109d 綠洲棕櫚葉微風搖擺：用 _SwayMask 的 R 當權重、G 當每棵樹的相位，位移貼圖 UV
// 效果全為 0 時，顏色與 Sprite-Lit-Default 一樣 (貼圖 * 頂點色，透明混合、不受光、雙面)
Shader "Custom/OasisFoliageSway"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SwayMask ("Sway Mask (R=權重 G=相位)", 2D) = "black" {}
        // ★1109d 精靈在整張貼圖裡的位置 (x0,y0,w,h，皆為 0~1 的 UV)；遮罩只對應這個範圍
        _MaskUVRect ("Sprite Rect In Tex (x0,y0,w,h)", Vector) = (0.020335, 0.110520, 0.961722, 0.750266)
        _SwayStrength ("Sway 總強度 (0=停)", Range(0,2)) = 1
        _Amp ("Sway 振幅 (UV)", Float) = 0.006
        _Speed ("Sway 速度", Float) = 1.1
        _FlutterAmp ("Flutter 振幅 (UV)", Float) = 0.0015
        _FlutterSpeed ("Flutter 速度", Float) = 6

        [Header(Blend Into Desert)]
        // ★1109h 融入沙漠：_BlendMask R=調色權重 G=邊緣淡出 (UV 同 _MaskUVRect)；顏色參數皆為線性 RGB
        _BlendMask ("Blend Mask (R=調色 G=邊緣淡出)", 2D) = "white" {}
        _GradeAmount ("調色強度", Range(0,1)) = 1
        _SrcMean ("原圖沙/岩 平均 (線性)", Vector) = (0, 0, 0, 0)
        _SrcStd ("原圖沙/岩 標準差 (線性)", Vector) = (1, 1, 1, 0)
        _DstMean ("目標沙 平均 (線性)", Vector) = (0, 0, 0, 0)
        _DstStd ("目標沙 標準差 (線性)", Vector) = (1, 1, 1, 0)
        _EdgeFade ("邊緣淡出", Range(0,1)) = 1
        // ★1109h 淡出處把沙色染向底色 (B 通道=下緣權重：1 染土色、0 染沙色)，避免半透明沙在暗土上變成一圈蒼白光暈
        _EdgeSoil ("下緣淡出染色 (土色)", Color) = (0.69, 0.26, 0.094, 1)
        _EdgeTintAmount ("淡出染色比例", Range(0,1)) = 0.8
        // ★1109i 切口：_BlendMask A 通道 = 接觸陰影(切口外)／暗邊(切口內)權重
        _CutShadow ("切口接觸陰影", Range(0,2)) = 1
        _CutShadowColor ("切口陰影色 (深暖褐)", Color) = (0.14, 0.055, 0.03, 1)
        _CutRim ("切口暗邊", Range(0,2)) = 1
        _CutRimColor ("切口暗邊乘色", Color) = (0.55, 0.36, 0.28, 1)
        _WarmTint ("暖色光 (沙漠霧光)", Color) = (1,1,1,1)
        _WarmAmount ("暖色比例", Range(0,0.5)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
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

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_SwayMask);  SAMPLER(sampler_SwayMask);
            TEXTURE2D(_BlendMask); SAMPLER(sampler_BlendMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float4 _MaskUVRect;
                float _SwayStrength;
                float _Amp;
                float _Speed;
                float _FlutterAmp;
                float _FlutterSpeed;
                float _GradeAmount;
                float4 _SrcMean;
                float4 _SrcStd;
                float4 _DstMean;
                float4 _DstStd;
                float _EdgeFade;
                half4 _EdgeSoil;
                float _CutShadow;
                half4 _CutShadowColor;
                float _CutRim;
                half4 _CutRimColor;
                float _EdgeTintAmount;
                half4 _WarmTint;
                float _WarmAmount;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // 貼圖 UV 換成遮罩 UV (遮罩只涵蓋精靈那一塊)
                float2 mUV = (i.uv - _MaskUVRect.xy) / _MaskUVRect.zw;
                half3 m = SAMPLE_TEXTURE2D(_SwayMask, sampler_SwayMask, mUV).rgb;
                float w = m.r * _SwayStrength;
                float ph = m.g * 6.2831853;
                float t = _Time.y;
                // x 慢擺 + 小顫動，y 取約 30% 並用 cos 錯開
                float ox = _Amp * sin(t * _Speed + ph) + _FlutterAmp * sin(t * _FlutterSpeed + mUV.x * 40.0 + ph);
                float oy = 0.3 * (_Amp * cos(t * _Speed + ph) + _FlutterAmp * cos(t * _FlutterSpeed + mUV.x * 40.0 + ph));
                float2 uv2 = i.uv + w * float2(ox, oy) * _MaskUVRect.zw;
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv2);

                // ★1109h 調色 + 邊緣淡出：遮罩取未位移的 UV，邊緣不跟著搖
                half4 bm = SAMPLE_TEXTURE2D(_BlendMask, sampler_BlendMask, mUV);
                float3 ratio = clamp(_DstStd.rgb / max(_SrcStd.rgb, 1e-4), 0.75, 1.3);   // 限制對比變化量
                float3 graded = saturate((c.rgb - _SrcMean.rgb) * ratio + _DstMean.rgb);
                c.rgb = lerp(c.rgb, graded, bm.r * _GradeAmount);
                // 暖色光：乘上亮度正規化的霧光色，變暖但不變暗
                float3 warm = _WarmTint.rgb / max(dot(_WarmTint.rgb, float3(0.2126, 0.7152, 0.0722)), 1e-4);
                c.rgb = saturate(lerp(c.rgb, c.rgb * warm, _WarmAmount));
                float fade = lerp(1.0, bm.g, _EdgeFade);
                float3 edgeCol = lerp(_DstMean.rgb, _EdgeSoil.rgb, bm.b);
                c.rgb = lerp(c.rgb, edgeCol, (1.0 - fade) * _EdgeTintAmount);
                // 切口內側暗邊 (A 在 G>0 處當暗邊權重)
                c.rgb = lerp(c.rgb, c.rgb * _CutRimColor.rgb, saturate(bm.a * fade * _CutRim));
                float srcA = c.a;
                float sandA = srcA * fade;
                // 切口外側接觸陰影 (A 在 G<1 處當陰影不透明度)，只畫在原圖有像素的地方
                float shA = saturate(bm.a * (1.0 - fade) * _CutShadow * _CutShadowColor.a) * srcA;
                float outA = sandA + shA * (1.0 - sandA);
                float3 outRGB = (c.rgb * sandA + _CutShadowColor.rgb * shA * (1.0 - sandA)) / max(outA, 1e-4);
                c = half4(outRGB, outA);
                return c * i.color;
            }
            ENDHLSL
        }
    }
}
