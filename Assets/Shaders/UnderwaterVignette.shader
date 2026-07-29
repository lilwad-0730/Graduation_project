Shader "Custom/UnderwaterVignette"
{
    Properties
    {
        // Unity UI Canvas 的 RawImage 元件必須有 _MainTex，否則會噴 SendWillRenderCanvases 警告
        [HideInInspector] _MainTex ("Base (RGB)", 2D) = "white" {}
        _Color ("Vignette Dark Color", Color) = (0,0,0,1)
        // 圓心透明半徑 (1=完全無效, 0=全黑全模糊)
        _Radius ("Clear Radius", Range(0, 1)) = 0.8
        // 邊緣柔化過渡寬度
        _Softness ("Edge Softness", Range(0, 1)) = 0.3
        // 最大不透明度
        _Intensity ("Max Darkness Intensity", Range(0, 1)) = 1.0
        // 模糊強度 (像素偏移量，0=不模糊，20=超強模糊)
        _BlurAmount ("Blur Strength", Range(0, 30)) = 8.0
        // 暗色與模糊畫面的混合比 (1=純黑, 0=只模糊不暗)
        _BlurDarkMix ("Blur Dark Mix", Range(0, 1)) = 0.7
    }

    SubShader
    {
        Tags { "Queue"="Overlay+1" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200

        // GrabPass：在這個物件渲染前，先抓取目前螢幕畫面
        GrabPass { "_GrabTex" }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            // 使用預乘 Alpha 讓半透明邊緣與模糊畫面自然混合
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float2 uv      : TEXCOORD0;
                float4 grabPos : TEXCOORD1;
            };

            sampler2D _GrabTex;
            float4    _GrabTex_TexelSize;
            fixed4    _Color;
            float     _Radius;
            float     _Softness;
            float     _Intensity;
            float     _BlurAmount;
            float     _BlurDarkMix;

            // ----------------------------------------
            // 9-tap Gaussian Blur（對 GrabPass 抓到的螢幕取樣）
            // blurRadius：像素偏移量（越大越模糊）
            // ----------------------------------------
            fixed4 GaussianBlur(float4 grabPos, float blurRadius)
            {
                float2 px = _GrabTex_TexelSize.xy * blurRadius;

                fixed4 col = fixed4(0, 0, 0, 0);
                // 3x3 Gaussian 權重：[1,2,1 / 2,4,2 / 1,2,1] / 16
                col += tex2Dproj(_GrabTex, grabPos + float4(-px.x, -px.y, 0, 0)) * 0.0625;
                col += tex2Dproj(_GrabTex, grabPos + float4(    0, -px.y, 0, 0)) * 0.1250;
                col += tex2Dproj(_GrabTex, grabPos + float4( px.x, -px.y, 0, 0)) * 0.0625;
                col += tex2Dproj(_GrabTex, grabPos + float4(-px.x,     0, 0, 0)) * 0.1250;
                col += tex2Dproj(_GrabTex, grabPos                             ) * 0.2500;
                col += tex2Dproj(_GrabTex, grabPos + float4( px.x,     0, 0, 0)) * 0.1250;
                col += tex2Dproj(_GrabTex, grabPos + float4(-px.x,  px.y, 0, 0)) * 0.0625;
                col += tex2Dproj(_GrabTex, grabPos + float4(    0,  px.y, 0, 0)) * 0.1250;
                col += tex2Dproj(_GrabTex, grabPos + float4( px.x,  px.y, 0, 0)) * 0.0625;
                return col;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos     = UnityObjectToClipPos(v.vertex);
                o.uv      = v.uv;
                o.grabPos = ComputeGrabScreenPos(o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 以螢幕中心計算距離（補正長寬比保持正圓）
                float2 uv = i.uv - float2(0.5, 0.5);
                uv.x *= _ScreenParams.x / _ScreenParams.y;
                float dist = length(uv) * 2.0;

                // vignetteStrength：0 = 圓心（完全無效）, 1 = 外圍（全暗全模糊）
                float vignetteStrength = smoothstep(_Radius, _Radius + _Softness, dist);
                vignetteStrength = saturate(vignetteStrength) * _Intensity;

                // 完全透明區域直接跳過，不消耗效能
                if (vignetteStrength < 0.001)
                    discard;

                // 根據距離動態調整模糊強度（越外圍越模糊）
                float dynamicBlur = _BlurAmount * vignetteStrength;
                fixed4 blurred = GaussianBlur(i.grabPos, dynamicBlur);

                // 模糊畫面 + 暗化顏色 混合
                // _BlurDarkMix = 1 → 純暗色；_BlurDarkMix = 0 → 只模糊不暗
                fixed3 blendedRGB = lerp(blurred.rgb, _Color.rgb, vignetteStrength * _BlurDarkMix);

                // 使用預乘 Alpha 輸出，讓混合效果更正確
                return fixed4(blendedRGB * vignetteStrength, vignetteStrength);
            }
            ENDCG
        }
    }

    // Fallback：若裝置不支援 GrabPass，退回純黑半透明
    Fallback "Unlit/Transparent"
}
