// ★1109j 荒原時間感（DesertTimeOfDay）用的疊色 shader：一支 shader、三種混合模式，由腳本設 _SrcBlend/_DstBlend
//   ‧ 全場乘色   ：Blend DstColor Zero            （輸出 RGB 當乘數，白＝不變）
//   ‧ 天空漸層   ：Blend SrcAlpha OneMinusSrcAlpha（頂點色＋貼圖）
//   ‧ 太陽光暈   ：Blend SrcAlpha One             （加色）
// 不吃光、不寫深度；全場乘色 ZTest Always，天空／光暈 ZTest LEqual（才不會蓋在不透明的主角、鳥身上）；是否被前景遮住由 sortingOrder／z 控制（見 DesertTimeOfDay.cs）
Shader "Custom/DesertTodOverlay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest [_ZTest]
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
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
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
            }
            ENDHLSL
        }
    }
}
