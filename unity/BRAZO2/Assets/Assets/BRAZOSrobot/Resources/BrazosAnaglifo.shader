Shader "Hidden/BrazosAnaglifo"
{
    // Anaglifo rojo-cian "optimizado": el rojo del ojo izquierdo se arma con su verde y azul
    // (menos rivalidad entre ojos); verde y azul vienen del ojo derecho.
    Properties { _MainTex ("Ojo izquierdo", 2D) = "white" {} _Der ("Ojo derecho", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_TexelSize;
            sampler2D _Der;
            fixed4 frag (v2f_img i) : SV_Target
            {
                float2 uv2 = i.uv;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0) uv2.y = 1 - uv2.y;
                #endif
                fixed3 l = tex2D(_MainTex, i.uv).rgb;
                fixed3 r = tex2D(_Der, uv2).rgb;
                return fixed4(saturate(0.7 * l.g + 0.3 * l.b + 0.15 * l.r), r.g, r.b, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
