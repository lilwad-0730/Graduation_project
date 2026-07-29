Shader "Custom/UnderwaterVignette"
{
    Properties
    {
        // Unity UI Canvas 的 RawImage 元件必須有 _MainTex，否則會噴 SendWillRenderCanvases 警告
        [HideInInspector] _MainTex ("Base (RGB)", 2D) = "white" {}
        _Color ("Vignette Color", Color) = (0,0,0,1)
        // _Radius: 透明圓心半徑 (0=全黑, 1=全透明, 0.5=半屏暗化)
        _Radius ("Clear Radius (0=fullBlack 1=fullClear)", Range(0, 1)) = 0.8
        // _Softness: 邊緣柔化過渡區域大小
        _Softness ("Edge Softness", Range(0, 1)) = 0.3
        // _Intensity: 最暗處的不透明度上限
        _Intensity ("Max Darkness Intensity", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
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
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            fixed4 _Color;
            float  _Radius;
            float  _Softness;
            float  _Intensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 以螢幕中心 (0.5, 0.5) 為圓心計算距離
                // 考慮螢幕長寬比，讓光暈保持正圓
                float2 screenUV = i.uv - float2(0.5, 0.5);
                // 補正長寬比 (Unity UI Canvas 預設 16:9)
                screenUV.x *= _ScreenParams.x / _ScreenParams.y;
                
                float dist = length(screenUV) * 2.0; // *2 讓 dist=1 對應螢幕邊緣

                // smoothstep: 在 _Radius 到 (_Radius + _Softness) 之間平滑過渡
                float alpha = smoothstep(_Radius, _Radius + _Softness, dist);
                alpha = saturate(alpha) * _Intensity;

                return fixed4(_Color.rgb, alpha);
            }
            ENDCG
        }
    }
}
