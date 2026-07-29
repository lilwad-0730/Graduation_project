// URP 相容版本 - 完全不使用 GrabPass
Shader "Custom/UnderwaterVignette"
{
    Properties
    {
        // Unity UI Canvas RawImage 必要屬性
        [HideInInspector] _MainTex ("Base (RGB)", 2D) = "white" {}
        _Color    ("Vignette Color",              Color)        = (0,0,0,1)
        _Radius   ("Clear Radius (1=透明 0=全黑)", Range(0,1))  = 0.8
        _Softness ("Edge Softness",               Range(0,1))   = 0.35
        _Intensity("Max Opacity",                 Range(0,1))   = 1.0
    }

    SubShader
    {
        // 同時相容 Built-In RP 與 URP
        Tags
        {
            "Queue"          = "Overlay"
            "RenderType"     = "Transparent"
            "IgnoreProjector"= "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest  Always
        Cull   Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos    : SV_POSITION; float2 uv : TEXCOORD0; };

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
                // 以螢幕中心為圓心，修正長寬比使暗區保持正圓
                float2 uv = i.uv - float2(0.5, 0.5);
                uv.x *= _ScreenParams.x / _ScreenParams.y;
                float dist = length(uv) * 2.0;

                // 平滑漸變：從 _Radius 到 _Radius+_Softness 過渡
                float alpha = smoothstep(_Radius, _Radius + _Softness, dist);
                alpha = saturate(alpha) * _Intensity;

                return fixed4(_Color.rgb, alpha);
            }
            ENDCG
        }
    }
}
