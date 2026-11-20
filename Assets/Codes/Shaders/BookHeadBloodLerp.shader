// ★1118c 書頭怪逐步染血：把「原本貼圖」和「染血貼圖」依 _T 混合後 Blit 進 RenderTexture。
// 由 Assets/Resources/MonsterBlood/BookHeadBloodLerp.mat 參照（放在 Resources 裡，打包時一定會包進去）。
Shader "Hidden/BookHeadBloodLerp"
{
    Properties
    {
        _MainTex ("Normal Albedo", 2D) = "white" {}
        _BloodTex ("Blood Albedo", 2D) = "white" {}
        _T ("Blend", Range(0,1)) = 0
        _Mode ("0=albedo blend, 1=blood glow mask", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _BloodTex;
            float _T;
            float _Mode;

            float4 frag(v2f_img i) : SV_Target
            {
                // 兩張都是 sRGB 貼圖：取樣時硬體轉線性，混完寫進 sRGB 的 RT 再轉回去
                float4 a = tex2D(_MainTex, i.uv);
                float4 b = tex2D(_BloodTex, i.uv);
                if (_Mode > 0.5)
                {
                    // 血的發光遮罩：染血貼圖比原貼圖暗的地方＝有血。場景幾乎沒光，沒有這個血就看不見。
                    float la = dot(a.rgb, float3(0.299, 0.587, 0.114));
                    float lb = dot(b.rgb, float3(0.299, 0.587, 0.114));
                    float m = saturate((la - lb) * 4.0) * _T;
                    return float4(m, m * 0.04, m * 0.04, 1);
                }
                return lerp(a, b, _T);
            }
            ENDCG
        }
    }
    Fallback Off
}
