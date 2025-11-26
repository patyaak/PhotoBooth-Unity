Shader "Unlit/VerticalGradient2D"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}        // 👈 Added to stop Unity UI warnings
        _TopColor ("Top Color", Color) = (0, 1, 1, 1)
        _BottomColor ("Bottom Color", Color) = (1, 0, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _TopColor;
            float4 _BottomColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Interpolate color vertically (1 = top, 0 = bottom)
                float t = 1.0 - i.uv.y;
                fixed4 col = lerp(_BottomColor, _TopColor, t);
                return col;
            }
            ENDHLSL
        }
    }
}
