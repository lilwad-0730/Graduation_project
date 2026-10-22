// 水下光束：加算混合的柔和光帶，不需要貼圖。
// 網格 UV：x 0~1 = 橫向，y 1 = 上端（水面那頭）、0 = 下端。
Shader "Underwater/LightRay"
{
    Properties
    {
        _Color ("Color", Color) = (0.7, 0.9, 1, 1)
        _Intensity ("Intensity", Float) = 0.35
        _Seed ("Seed", Float) = 0
        _StreakScale ("Streak Scale", Float) = 9
        _StreakSpeed ("Streak Speed", Float) = 0.25
        _FlickerSpeed ("Flicker Speed", Float) = 0.6
        _BottomFade ("Bottom Fade (0~1)", Range(0.05, 1)) = 0.75
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "LightRay"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _Seed;
                float _StreakScale;
                float _StreakSpeed;
                float _FlickerSpeed;
                float _BottomFade;
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

            half4 frag (Varyings i) : SV_Target
            {
                float t = _Time.y;
                // 兩側柔邊
                float edge = smoothstep(0.0, 0.5, i.uv.x) * smoothstep(1.0, 0.5, i.uv.x);
                edge *= edge;
                // 上亮下淡，頂端也稍微柔一下避免切邊
                float along = smoothstep(0.0, _BottomFade, i.uv.y) * smoothstep(1.0, 0.92, i.uv.y);
                // 光束裡細細的條紋，慢慢橫移
                float s1 = sin(i.uv.x * _StreakScale + t * _StreakSpeed + _Seed);
                float s2 = sin(i.uv.x * _StreakScale * 2.3 - t * _StreakSpeed * 1.7 + _Seed * 3.1);
                float streak = 0.65 + 0.2 * s1 + 0.15 * s2;
                // 整條忽明忽暗
                float f = t * _FlickerSpeed + _Seed * 7.0;
                float flicker = 0.7 + 0.3 * (0.5 + 0.5 * sin(f) * sin(f * 0.37 + 1.3));

                float a = edge * along * streak * flicker * _Intensity;
                return half4(_Color.rgb * a, 1.0);
            }
            ENDHLSL
        }
    }
}
