Shader "Custom/EyeEnlargementMultiFace"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _EyeRadius ("Eye Radius", Float) = 0.1
        _EnlargementStrength ("Enlargement Strength", Range(0, 2)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
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
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            float2 _LeftEyeCenters[10];   // Maximum 10 faces supported
            float2 _RightEyeCenters[10];
            int _FaceCount;
            float _EyeRadius;
            float _EnlargementStrength;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float2 ApplyEyeEnlargement(float2 uv, float2 eyeCenter, float radius, float strength)
            {
                float2 delta = uv - eyeCenter;
                float dist = length(delta);

                if (dist < radius && dist > 0.001)
                {
                    float normalizedDist = dist / radius;
                    float weight = 1.0 - smoothstep(0.0, 1.0, normalizedDist);
                    float newDist = dist * (1.0 - strength * weight * 0.5);
                    return eyeCenter + normalize(delta) * newDist;
                }

                return uv;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;

                for (int f = 0; f < _FaceCount; f++)
                {
                    uv = ApplyEyeEnlargement(uv, _LeftEyeCenters[f], _EyeRadius, _EnlargementStrength);
                    uv = ApplyEyeEnlargement(uv, _RightEyeCenters[f], _EyeRadius, _EnlargementStrength);
                }

                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }
}
