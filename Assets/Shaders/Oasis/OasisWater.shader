// ★1109d 綠洲水面：波紋、流動光帶、閃點、棕櫚倒影、中間較深
// 水岸 alpha 取未扭曲的遮罩（岸線不晃）；效果全關且 _SurfaceAlpha=1 時，顏色與原圖一樣
Shader "Custom/OasisWater"
{
    Properties
    {
        _MainTex ("Water Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SurfaceAlpha ("整體不透明度", Range(0,1)) = 0.9

        [Header(Ripple)]
        _RippleAmp ("波紋扭曲 (UV)", Float) = 0.0015
        _RippleSpeed ("波紋速度", Float) = 1.0

        [Header(Light Bands)]
        _BandColor ("光帶顏色", Color) = (0.55, 1.0, 0.95, 1)
        _BandIntensity ("光帶強度", Range(0,1)) = 0.14
        _BandSpeed ("光帶流速", Float) = 0.35

        [Header(Sparkle)]
        _SparkleIntensity ("閃點強度", Range(0,2)) = 1.0
        _SparkleDensity ("閃點密度 (0~1)", Range(0,1)) = 0.2
        _SparkleSpeed ("閃點速度", Float) = 1.6
        _SparkleCell ("閃點格子大小 (像素)", Float) = 16

        [Header(Reflection)]
        _ReflTex ("倒影來源 (原綠洲貼圖)", 2D) = "black" {}
        _ReflStrength ("倒影強度", Range(0,1)) = 0.25
        _ReflLineV ("倒影鏡射線 v (水層 UV)", Float) = 0.255
        _ReflK ("倒影壓縮 k", Float) = 3.5
        _ReflWave ("倒影左右晃 (UV)", Float) = 0.004
        _ReflTint ("倒影染色", Color) = (0.25, 0.75, 0.72, 1)
        _ReflTintAmount ("倒影染色比例", Range(0,1)) = 0.5
        // 水層矩形在原貼圖裡的位置 (x0,y0,w,h)：(34,104,1608,706) / (1672,941)
        _ReflRect ("水層在原圖的 UV 矩形", Vector) = (0.020335, 0.110520, 0.961722, 0.750266)

        [Header(Depth)]
        _DeepDarken ("中間變深 (0=不變)", Range(0,0.5)) = 0.12
        _EdgeLighten ("岸邊變亮", Range(0,0.5)) = 0.06
        _TexelSize ("水層像素 (w,h)", Vector) = (1608, 706, 0, 0)

        [Header(Warm Light)]
        // ★1109h 與綠洲陸地同一道暖色光 (水用較低比例，保持清涼)
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

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_ReflTex);  SAMPLER(sampler_ReflTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _SurfaceAlpha;
                float _RippleAmp;
                float _RippleSpeed;
                half4 _BandColor;
                float _BandIntensity;
                float _BandSpeed;
                float _SparkleIntensity;
                float _SparkleDensity;
                float _SparkleSpeed;
                float _SparkleCell;
                float _ReflStrength;
                float _ReflLineV;
                float _ReflK;
                float _ReflWave;
                half4 _ReflTint;
                float _ReflTintAmount;
                float4 _ReflRect;
                float _DeepDarken;
                float _EdgeLighten;
                float4 _TexelSize;
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

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 frag (Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.uv;
                half4 c0 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                float a0 = c0.a;                              // 岸線用未扭曲的遮罩

                // (a) 波紋：兩組交叉的正弦扭曲水色
                float ts = t * _RippleSpeed;
                float2 d;
                d.x = sin(uv.y * 95.0 + ts * 1.3) * 0.6 + sin((uv.x + uv.y) * 62.0 - ts * 0.9) * 0.4;
                d.y = cos(uv.x * 72.0 + ts * 1.1) * 0.6 + cos((uv.x - uv.y) * 48.0 + ts * 0.7) * 0.4;
                half4 cd = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d * _RippleAmp);
                half3 col = lerp(c0.rgb, cd.rgb, cd.a);       // 取樣落到水外就退回原色，避免岸邊沙色混進來

                // (e) 離岸程度：用遮罩在兩圈半徑上取樣，近似模糊
                float2 px = 1.0 / _TexelSize.xy;
                float inner = 0.0;
                inner += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( 14, 0) * px).a;
                inner += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-14, 0) * px).a;
                inner += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0,  14) * px).a;
                inner += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, -14) * px).a;
                inner += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( 30, 0) * px).a;
                inner += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-30, 0) * px).a;
                inner += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0,  30) * px).a;
                inner += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, -30) * px).a;
                inner *= 0.125;
                float deep = inner * inner;
                col *= 1.0 - _DeepDarken * deep;
                col += _EdgeLighten * (1.0 - inner);

                // (d) 倒影：以鏡射線為軸，上方的景物上下翻轉、縱向壓縮 k 倍，再左右晃
                float srcV = _ReflLineV + (_ReflLineV - uv.y) * _ReflK;
                float wob = sin(uv.y * 160.0 + t * 1.2) * 0.6 + sin(uv.y * 70.0 - t * 0.8) * 0.4;
                float srcU = uv.x + wob * _ReflWave;
                float2 ruv = float2(_ReflRect.x + srcU * _ReflRect.z, _ReflRect.y + srcV * _ReflRect.w);
                half4 rc = SAMPLE_TEXTURE2D(_ReflTex, sampler_ReflTex, ruv);
                float inside = step(uv.y, _ReflLineV) * step(srcV, 1.0);   // 只在鏡射線下方 (水面前半) 有倒影
                float rA = rc.a * inside;
                half3 rcol = lerp(rc.rgb, _ReflTint.rgb, _ReflTintAmount);
                col = lerp(col, rcol, _ReflStrength * rA);

                // (b) 緩慢飄移的柔光帶
                float b = sin(uv.x * 14.0 + uv.y * 30.0 + t * _BandSpeed) * sin(uv.x * 7.0 - uv.y * 18.0 - t * _BandSpeed * 0.63);
                b = smoothstep(0.25, 1.0, b);
                col += _BandColor.rgb * (b * _BandIntensity);

                // (c) 閃點：格子隨機放小亮點，亮度隨時間明滅
                float2 g = uv * _TexelSize.xy / _SparkleCell;
                float2 id = floor(g);
                float2 f = frac(g);
                float r1 = Hash21(id);
                float r2 = Hash21(id + 17.3);
                float r3 = Hash21(id + 41.7);
                float2 ctr = float2(0.2 + 0.6 * r2, 0.2 + 0.6 * r3);
                float2 dd = (f - ctr) * float2(0.55, 1.5);                  // 橫向略扁的亮點
                float pt = saturate(1.0 - length(dd) / 0.16);
                float tw = pow(saturate(sin(t * _SparkleSpeed * (0.6 + r2) + r1 * 40.0) * 0.5 + 0.5), 6.0);
                float on = step(1.0 - _SparkleDensity, r1);
                col += float3(1.0, 1.0, 0.95) * (pt * pt * tw * on * _SparkleIntensity * 0.9);

                // ★1109h 暖色光：亮度正規化後相乘
                float3 warm = _WarmTint.rgb / max(dot(_WarmTint.rgb, float3(0.2126, 0.7152, 0.0722)), 1e-4);
                col = lerp(col, col * warm, _WarmAmount);

                half alpha = a0 * _SurfaceAlpha * i.color.a;
                return half4(saturate(col) * i.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
