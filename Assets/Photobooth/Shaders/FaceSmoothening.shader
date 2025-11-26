// ============================================
// FIXED FaceSmoothening.shader
// ============================================
// Replace your existing shader with this one

Shader "Custom/FaceSmoothening"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _SmoothStrength ("Smooth Strength", Range(0, 10)) = 5.0
        _BlurRadius ("Blur Radius", Range(1, 5)) = 2.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

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
            float4 _MainTex_TexelSize;

            float _SmoothStrength;
            float _BlurRadius;

            // Face oval points
            float4 _FacePoints[36];
            int _FacePointCount;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // Check if point is inside face region
            float GetFaceWeight(float2 uv)
            {
                if (_FacePointCount < 3)
                    return 0.0;

                float minDist = 999.0;

                for (int i = 0; i < _FacePointCount; i++)
                {
                    float2 facePoint = _FacePoints[i].xy;
                    float dist = distance(uv, facePoint);
                    minDist = min(minDist, dist);
                }

                // Adjust threshold for face region
                float threshold = 0.2;
                float smoothness = 0.05;
                float weight = 1.0 - smoothstep(0.0, threshold, minDist);

                return weight;
            }

            // Simple box blur
            fixed4 BoxBlur(float2 uv, float radius)
            {
                fixed4 sum = fixed4(0, 0, 0, 0);
                float count = 0.0;

                int samples = (int)clamp(radius, 1, 5);

                for (int x = -samples; x <= samples; x++)
                {
                    for (int y = -samples; y <= samples; y++)
                    {
                        float2 offset = float2(x, y) * _MainTex_TexelSize.xy * 2.0;
                        sum += tex2D(_MainTex, uv + offset);
                        count += 1.0;
                    }
                }

                return sum / count;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample original texture
                fixed4 original = tex2D(_MainTex, i.uv);

                // Get face weight
                float faceWeight = GetFaceWeight(i.uv);

                // If not in face region, return original
                if (faceWeight < 0.01 || _SmoothStrength < 0.01)
                {
                    return original;
                }

                // Apply blur
                fixed4 blurred = BoxBlur(i.uv, _BlurRadius);

                // Calculate blend factor
                float blendFactor = faceWeight * (_SmoothStrength / 10.0);

                // Mix original and blurred
                fixed4 result = lerp(original, blurred, blendFactor);

                return result;
            }
            ENDCG
        }
    }

    Fallback "Unlit/Texture"
}