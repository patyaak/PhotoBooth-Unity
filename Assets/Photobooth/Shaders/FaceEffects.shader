Shader "Custom/FaceEffects"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BrightenStrength ("Brighten Strength", Range(0, 3)) = 0.3
        _Smoothness ("Edge Smoothness", Range(0.001, 0.1)) = 0.02
        _ForeheadExpansionMultiplier ("Forehead Expansion Multiplier", Range(0.5, 2.0)) = 1.5

        // Eye Enlargement Properties (radius is per-eye, passed via Vector4.z)
        _EnlargementStrength ("Enlargement Strength", Range(0, 2)) = 0.5

        // Smoothing Properties
        _SmoothingStrength ("Smoothing Strength", Range(0, 1)) = 0.5
        _SmoothingRadius ("Smoothing Radius", Int) = 5
        _ColorSigma ("Color Sigma", Range(0.01, 1)) = 0.2
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
            float _BrightenStrength;
            float _RegionExpansion;
            float _ForeheadExpansionMultiplier;
            float _EnableSkinDetection;
            float _SkinTolerance;
            float _UseAdaptiveSkinColor;
            sampler2D _SegmentationMask;
            float _EnableSegmentationMask;
            float _DebugSegmentationMask;

            // Face oval boundary points (36 points forming a closed polygon)
            // Support for multiple faces (max 10 faces, 36 points each)
            #define MAX_FACES 10
            #define POINTS_PER_FACE 36
            float4 _FaceOvalPoints[360];  // 10 faces * 36 points
            int _FaceOvalCount;  // Points per face (always 36)
            int _FaceCount;      // Number of detected faces

            // Eye Enlargement Parameters
            // Note: _LeftEyeCenters and _RightEyeCenters store (x, y, radius, 0) per eye
            float4 _LeftEyeCenters[10];   // Maximum 10 faces supported
            float4 _RightEyeCenters[10];
            float _EnlargementStrength;

            // Smoothing Parameters
            float _SmoothingStrength;
            int _SmoothingRadius;
            float _ColorSigma;
            float _ExcludeHairFromSmoothing;
            float _HairDetectionSensitivity;

            // Eye, Eyebrow, and Mouth Exclusion for Smoothing
            #define EYE_POINTS 16
            #define EYEBROW_POINTS 10
            #define MOUTH_POINTS 40
            float4 _LeftEyePoints[160];      // 10 faces * 16 points
            float4 _RightEyePoints[160];     // 10 faces * 16 points
            float4 _LeftEyebrowPoints[100];  // 10 faces * 10 points
            float4 _RightEyebrowPoints[100]; // 10 faces * 10 points
            float4 _MouthPoints[400];        // 10 faces * 40 points

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // ============================================
            // EYE ENLARGEMENT FUNCTIONS (ADDED)
            // ============================================

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

            float2 ApplyAllEyeEnlargements(float2 uv)
            {
                float2 result = uv;

                // Apply eye enlargement for all faces
                for (int f = 0; f < _FaceCount; f++)
                {
                    // Extract eye center (.xy) and radius (.z) for each eye
                    result = ApplyEyeEnlargement(result, _LeftEyeCenters[f].xy, _LeftEyeCenters[f].z, _EnlargementStrength);
                    result = ApplyEyeEnlargement(result, _RightEyeCenters[f].xy, _RightEyeCenters[f].z, _EnlargementStrength);
                }

                return result;
            }

            // Point-in-polygon test using ray casting for multiple faces
            // Returns 1.0 if inside any face, 0.0 if outside all faces
            float IsInsideAnyPolygon(float2 p)
            {
                if (_FaceOvalCount < 3 || _FaceCount < 1)
                    return 0.0;

                // Check each face
                for (int faceIdx = 0; faceIdx < _FaceCount; faceIdx++)
                {
                    int baseIndex = faceIdx * POINTS_PER_FACE;
                    int crossings = 0;

                    for (int i = 0; i < _FaceOvalCount; i++)
                    {
                        float2 v1 = _FaceOvalPoints[baseIndex + i].xy;
                        float2 v2 = _FaceOvalPoints[baseIndex + ((i + 1) % _FaceOvalCount)].xy;

                        // Check if ray crosses edge
                        if (((v1.y <= p.y) && (v2.y > p.y)) || ((v1.y > p.y) && (v2.y <= p.y)))
                        {
                            float vt = (p.y - v1.y) / (v2.y - v1.y);
                            if (p.x < v1.x + vt * (v2.x - v1.x))
                            {
                                crossings++;
                            }
                        }
                    }

                    // If inside this face, return immediately
                    if (crossings % 2 == 1)
                        return 1.0;
                }

                return 0.0;
            }

            // Calculate distance to nearest polygon edge for smooth blending (supports multiple faces)
            // Returns both minDist and the adaptive expansion multiplier
            float2 DistanceToEdgeWithExpansion(float2 p)
            {
                if (_FaceOvalCount < 3 || _FaceCount < 1)
                    return float2(100.0, 1.0);

                float minDist = 100.0;
                float closestFaceCenterY = 0.5;

                // Find the closest face and its center
                for (int faceIdx = 0; faceIdx < _FaceCount; faceIdx++)
                {
                    int baseIndex = faceIdx * POINTS_PER_FACE;

                    // Calculate this face's center Y coordinate
                    float centerY = 0.0;
                    for (int j = 0; j < _FaceOvalCount; j++)
                    {
                        centerY += _FaceOvalPoints[baseIndex + j].y;
                    }
                    centerY /= max(1.0, float(_FaceOvalCount));

                    // Calculate minimum distance to this face's edges
                    for (int i = 0; i < _FaceOvalCount; i++)
                    {
                        float2 v1 = _FaceOvalPoints[baseIndex + i].xy;
                        float2 v2 = _FaceOvalPoints[baseIndex + ((i + 1) % _FaceOvalCount)].xy;

                        // Distance from point to line segment
                        float2 pa = p - v1;
                        float2 ba = v2 - v1;
                        float h = saturate(dot(pa, ba) / dot(ba, ba));
                        float dist = length(pa - ba * h);

                        if (dist < minDist)
                        {
                            minDist = dist;
                            closestFaceCenterY = centerY;
                        }
                    }
                }

                // Calculate expansion multiplier based on vertical position relative to closest face
                // Above center (forehead) = larger expansion
                // Below center (chin) = smaller expansion
                float verticalOffset = p.y - closestFaceCenterY;
                float expansionMultiplier = lerp(0.7, _ForeheadExpansionMultiplier, saturate((verticalOffset + 0.2) / 0.4));

                return float2(minDist, expansionMultiplier);
            }

            // Calculate maximum brightness weight across all faces (for overlapping regions)
            // Returns the highest weight value from any face
            // Uses shape-aware expansion: more at forehead/top, less at chin/jaw
            float CalculateMaxWeightAcrossFaces(float2 p)
            {
                if (_FaceOvalCount < 3 || _FaceCount < 1)
                    return 0.0;

                float maxWeight = 0.0;

                // Check each face independently and take the maximum weight
                for (int faceIdx = 0; faceIdx < _FaceCount; faceIdx++)
                {
                    int baseIndex = faceIdx * POINTS_PER_FACE;

                    // Check if inside this specific face
                    int crossings = 0;
                    for (int i = 0; i < _FaceOvalCount; i++)
                    {
                        float2 v1 = _FaceOvalPoints[baseIndex + i].xy;
                        float2 v2 = _FaceOvalPoints[baseIndex + ((i + 1) % _FaceOvalCount)].xy;

                        if (((v1.y <= p.y) && (v2.y > p.y)) || ((v1.y > p.y) && (v2.y <= p.y)))
                        {
                            float vt = (p.y - v1.y) / (v2.y - v1.y);
                            if (p.x < v1.x + vt * (v2.x - v1.x))
                            {
                                crossings++;
                            }
                        }
                    }

                    bool isInside = (crossings % 2 == 1);

                    if (isInside)
                    {
                        // Inside face polygon - maximum weight
                        maxWeight = 1.0;
                        continue; // Can't get higher than 1.0
                    }

                    // Not inside - calculate distance to closest edge with shape-aware expansion
                    float minWeightedDist = 100.0;
                    float centerY = 0.0;
                    float2 faceCenter = float2(0, 0);

                    // Calculate face center
                    for (int j = 0; j < _FaceOvalCount; j++)
                    {
                        faceCenter += _FaceOvalPoints[baseIndex + j].xy;
                    }
                    faceCenter /= max(1.0, float(_FaceOvalCount));
                    centerY = faceCenter.y;

                    // Find minimum distance to edge WITH shape-aware expansion
                    // Each edge segment gets its own expansion multiplier based on its position
                    for (int k = 0; k < _FaceOvalCount; k++)
                    {
                        float2 v1 = _FaceOvalPoints[baseIndex + k].xy;
                        float2 v2 = _FaceOvalPoints[baseIndex + ((k + 1) % _FaceOvalCount)].xy;

                        // Calculate distance from point to this edge segment
                        float2 pa = p - v1;
                        float2 ba = v2 - v1;
                        float h = saturate(dot(pa, ba) / dot(ba, ba));
                        float2 closestPoint = v1 + ba * h;
                        float dist = length(p - closestPoint);

                        // Calculate expansion multiplier for this edge based on its position
                        // Use the closest point on the edge to determine position
                        float edgeY = closestPoint.y;
                        float edgeX = closestPoint.x;

                        // VERTICAL component: Top vs Bottom
                        float verticalOffset = edgeY - centerY;
                        float verticalMultiplier = lerp(0.5, _ForeheadExpansionMultiplier, saturate((verticalOffset + 0.2) / 0.4));

                        // HORIZONTAL component: Sides (ears) vs Center
                        float horizontalOffset = abs(edgeX - faceCenter.x);
                        // Calculate face width for normalization
                        float faceWidth = 0.0;
                        for (int w = 0; w < _FaceOvalCount; w++)
                        {
                            float xDist = abs(_FaceOvalPoints[baseIndex + w].x - faceCenter.x);
                            faceWidth = max(faceWidth, xDist);
                        }

                        // Normalize horizontal offset (0 = center, 1 = extreme side)
                        float normalizedHorizontalOffset = saturate(horizontalOffset / max(0.01, faceWidth));

                        // Reduce expansion on sides (ears)
                        // Center (0): full expansion (1.0), Sides (1): reduced expansion (0.7)
                        float horizontalMultiplier = lerp(1.0, 0.7, normalizedHorizontalOffset);

                        // Combine vertical and horizontal multipliers
                        // Use multiplication so both constraints apply
                        float expansionMultiplier = verticalMultiplier * horizontalMultiplier;

                        // Normalize distance by expansion multiplier
                        // This effectively creates larger expansion zones at top-center, smaller at bottom and sides
                        float weightedDist = dist / expansionMultiplier;

                        minWeightedDist = min(minWeightedDist, weightedDist);
                    }

                    // Calculate weight using the base region expansion
                    // The weighted distance already incorporates shape-aware expansion
                    // Use smoothstep for smooth falloff from face boundary
                    float faceWeight = 1.0 - smoothstep(0.0, _RegionExpansion, minWeightedDist);
                    maxWeight = max(maxWeight, faceWeight);
                }

                return maxWeight;
            }

            // Sample reference skin color from center of first detected face
            float3 GetReferenceSkinColor()
            {
                if (_FaceOvalCount < 3 || _FaceCount < 1)
                    return float3(0.5, 0.5, 0.5);

                // Use first face for reference (could average multiple faces if needed)
                int baseIndex = 0;

                // Calculate center point of first face polygon
                float2 center = float2(0, 0);
                for (int i = 0; i < _FaceOvalCount; i++)
                {
                    center += _FaceOvalPoints[baseIndex + i].xy;
                }
                center /= float(_FaceOvalCount);

                // Sample color from face center (should be skin)
                return tex2D(_MainTex, center).rgb;
            }

            // Adaptive skin detection: compare pixel against reference skin color
            // Returns 0-1 weight (1 = matches reference, 0 = different)
            float DetectSkinAdaptive(float3 rgb, float3 referenceSkin)
            {
                // Convert both to YCbCr
                float3 ycbcr = float3(
                    0.299 * rgb.r + 0.587 * rgb.g + 0.114 * rgb.b,
                    -0.169 * rgb.r - 0.331 * rgb.g + 0.500 * rgb.b + 0.5,
                    0.500 * rgb.r - 0.419 * rgb.g - 0.081 * rgb.b + 0.5
                );

                float3 refYCbCr = float3(
                    0.299 * referenceSkin.r + 0.587 * referenceSkin.g + 0.114 * referenceSkin.b,
                    -0.169 * referenceSkin.r - 0.331 * referenceSkin.g + 0.500 * referenceSkin.b + 0.5,
                    0.500 * referenceSkin.r - 0.419 * referenceSkin.g - 0.081 * referenceSkin.b + 0.5
                );

                // Calculate color difference in YCbCr space
                float CbDiff = abs(ycbcr.y - refYCbCr.y);
                float CrDiff = abs(ycbcr.z - refYCbCr.z);
                float YDiff = abs(ycbcr.x - refYCbCr.x);

                // Stricter tolerance to avoid matching hair
                float chromaTolerance = lerp(0.03, 0.10, _SkinTolerance);  // Tighter chroma tolerance
                float lumaTolerance = lerp(0.15, 0.35, _SkinTolerance);    // More permissive luma (brightness can vary)

                // Match based on similarity to reference
                float CbMatch = 1.0 - smoothstep(0.0, chromaTolerance, CbDiff);
                float CrMatch = 1.0 - smoothstep(0.0, chromaTolerance, CrDiff);
                float YMatch = 1.0 - smoothstep(0.0, lumaTolerance, YDiff);

                // Chrominance is MUCH more important than luminance for skin matching
                // This helps exclude blonde hair which has different chroma but similar luma
                return (CbMatch * CrMatch) * (0.3 + 0.7 * YMatch);  // 70% luma weight, 30% base
            }

            // Generic skin tone detection using YCbCr color space
            // Returns 0-1 weight (1 = definitely skin, 0 = not skin)
            float DetectSkinTone(float3 rgb)
            {
                // Convert RGB to YCbCr (used in skin detection algorithms)
                float Y  = 0.299 * rgb.r + 0.587 * rgb.g + 0.114 * rgb.b;
                float Cb = -0.169 * rgb.r - 0.331 * rgb.g + 0.500 * rgb.b + 0.5;
                float Cr = 0.500 * rgb.r - 0.419 * rgb.g - 0.081 * rgb.b + 0.5;

                // Tighter skin tone ranges to avoid blonde hair
                // Skin has specific red/orange chroma (Cr) that hair lacks
                float CbMin = 0.35;  // Blue chroma minimum
                float CbMax = 0.55;  // Blue chroma maximum
                float CrMin = 0.45;  // Red chroma minimum (higher to exclude blonde hair)
                float CrMax = 0.68;  // Red chroma maximum

                // Apply tolerance scaling - makes ranges even wider
                float toleranceScale = lerp(0.8, 1.5, _SkinTolerance);
                float CbRange = (CbMax - CbMin) * 0.5;
                float CrRange = (CrMax - CrMin) * 0.5;
                float CbCenter = (CbMax + CbMin) * 0.5;
                float CrCenter = (CrMax + CrMin) * 0.5;

                CbMin = CbCenter - CbRange * toleranceScale;
                CbMax = CbCenter + CbRange * toleranceScale;
                CrMin = CrCenter - CrRange * toleranceScale;
                CrMax = CrCenter + CrRange * toleranceScale;

                // Smoother transitions at boundaries (larger fade zones)
                float fadeZone = 0.08;  // Increased from 0.05
                float CbMatch = smoothstep(CbMin - fadeZone, CbMin, Cb) * (1.0 - smoothstep(CbMax, CbMax + fadeZone, Cb));
                float CrMatch = smoothstep(CrMin - fadeZone, CrMin, Cr) * (1.0 - smoothstep(CrMax, CrMax + fadeZone, Cr));

                // Much more permissive luminance check
                // Includes very dark skin (0.1) to very light skin (0.95)
                float YMatch = smoothstep(0.05, 0.15, Y) * (1.0 - smoothstep(0.85, 0.95, Y));

                // Weight luminance less heavily - focus on chrominance
                float chromaMatch = CbMatch * CrMatch;

                // Combine with softer luminance weighting
                return chromaMatch * (0.5 + 0.5 * YMatch);  // Luminance contributes 50% max penalty
            }

            // ============================================
            // DETECT HAIR (COLOR-BASED)
            // Returns true if color matches hair characteristics
            // Hair tends to have low saturation variance and darker tones
            // ============================================
            bool IsHairColor(float3 rgb)
            {
                // Convert to HSV for better hair detection
                float maxC = max(max(rgb.r, rgb.g), rgb.b);
                float minC = min(min(rgb.r, rgb.g), rgb.b);
                float delta = maxC - minC;

                // Value (brightness)
                float V = maxC;

                // Saturation
                float S = (maxC > 0.0001) ? (delta / maxC) : 0.0;

                // Hair characteristics:
                // 1. Low saturation (gray/brown/black hair: S < 0.3)
                // 2. OR dark colors (V < 0.4 for black/dark brown hair)
                // 3. OR very consistent RGB values (beard stubble, gray hair)

                float rgbVariance = delta;

                // Adjust thresholds based on sensitivity
                float satThreshold = lerp(0.15, 0.4, _HairDetectionSensitivity);
                float darkThreshold = lerp(0.2, 0.5, _HairDetectionSensitivity);
                float varianceThreshold = lerp(0.05, 0.15, _HairDetectionSensitivity);

                // Hair detection criteria
                bool isLowSaturation = (S < satThreshold);
                bool isDark = (V < darkThreshold);
                bool isLowVariance = (rgbVariance < varianceThreshold);

                // It's likely hair if:
                // - Low saturation AND (dark OR low variance)
                // - OR extremely dark (likely black hair or beard)
                return (isLowSaturation && (isDark || isLowVariance)) || (V < 0.15);
            }

            // ============================================
            // CHECK IF POINT IS INSIDE EYES, EYEBROWS, OR MOUTH
            // Returns true if point should be excluded from smoothing
            // ============================================
            bool IsInsideEyeOrEyebrow(float2 p)
            {
                if (_FaceCount < 1)
                    return false;

                // Check each face
                for (int faceIdx = 0; faceIdx < _FaceCount; faceIdx++)
                {
                    int eyeBaseIdx = faceIdx * EYE_POINTS;
                    int eyebrowBaseIdx = faceIdx * EYEBROW_POINTS;

                    // Check left eye using point-in-polygon test
                    int leftEyeCrossings = 0;
                    for (int i = 0; i < EYE_POINTS; i++)
                    {
                        float2 v1 = _LeftEyePoints[eyeBaseIdx + i].xy;
                        float2 v2 = _LeftEyePoints[eyeBaseIdx + ((i + 1) % EYE_POINTS)].xy;

                        if (((v1.y <= p.y) && (v2.y > p.y)) || ((v1.y > p.y) && (v2.y <= p.y)))
                        {
                            float vt = (p.y - v1.y) / (v2.y - v1.y);
                            if (p.x < v1.x + vt * (v2.x - v1.x))
                                leftEyeCrossings++;
                        }
                    }
                    if (leftEyeCrossings % 2 == 1) return true;

                    // Check right eye
                    int rightEyeCrossings = 0;
                    for (int i = 0; i < EYE_POINTS; i++)
                    {
                        float2 v1 = _RightEyePoints[eyeBaseIdx + i].xy;
                        float2 v2 = _RightEyePoints[eyeBaseIdx + ((i + 1) % EYE_POINTS)].xy;

                        if (((v1.y <= p.y) && (v2.y > p.y)) || ((v1.y > p.y) && (v2.y <= p.y)))
                        {
                            float vt = (p.y - v1.y) / (v2.y - v1.y);
                            if (p.x < v1.x + vt * (v2.x - v1.x))
                                rightEyeCrossings++;
                        }
                    }
                    if (rightEyeCrossings % 2 == 1) return true;

                    // Check left eyebrow
                    int leftBrowCrossings = 0;
                    for (int i = 0; i < EYEBROW_POINTS; i++)
                    {
                        float2 v1 = _LeftEyebrowPoints[eyebrowBaseIdx + i].xy;
                        float2 v2 = _LeftEyebrowPoints[eyebrowBaseIdx + ((i + 1) % EYEBROW_POINTS)].xy;

                        if (((v1.y <= p.y) && (v2.y > p.y)) || ((v1.y > p.y) && (v2.y <= p.y)))
                        {
                            float vt = (p.y - v1.y) / (v2.y - v1.y);
                            if (p.x < v1.x + vt * (v2.x - v1.x))
                                leftBrowCrossings++;
                        }
                    }
                    if (leftBrowCrossings % 2 == 1) return true;

                    // Check right eyebrow
                    int rightBrowCrossings = 0;
                    for (int i = 0; i < EYEBROW_POINTS; i++)
                    {
                        float2 v1 = _RightEyebrowPoints[eyebrowBaseIdx + i].xy;
                        float2 v2 = _RightEyebrowPoints[eyebrowBaseIdx + ((i + 1) % EYEBROW_POINTS)].xy;

                        if (((v1.y <= p.y) && (v2.y > p.y)) || ((v1.y > p.y) && (v2.y <= p.y)))
                        {
                            float vt = (p.y - v1.y) / (v2.y - v1.y);
                            if (p.x < v1.x + vt * (v2.x - v1.x))
                                rightBrowCrossings++;
                        }
                    }
                    if (rightBrowCrossings % 2 == 1) return true;

                    // Check mouth
                    int mouthBaseIdx = faceIdx * MOUTH_POINTS;
                    int mouthCrossings = 0;
                    for (int i = 0; i < MOUTH_POINTS; i++)
                    {
                        float2 v1 = _MouthPoints[mouthBaseIdx + i].xy;
                        float2 v2 = _MouthPoints[mouthBaseIdx + ((i + 1) % MOUTH_POINTS)].xy;

                        if (((v1.y <= p.y) && (v2.y > p.y)) || ((v1.y > p.y) && (v2.y <= p.y)))
                        {
                            float vt = (p.y - v1.y) / (v2.y - v1.y);
                            if (p.x < v1.x + vt * (v2.x - v1.x))
                                mouthCrossings++;
                        }
                    }
                    if (mouthCrossings % 2 == 1) return true;
                }

                return false;
            }

            // ============================================
            // SIMPLIFIED BILATERAL-STYLE SMOOTHING
            // Edge-preserving smoothing for skin (GPU optimized)
            // ============================================
            fixed4 ApplyBilateralFilter(float2 uv, float2 modifiedUV, float faceWeight)
            {
                // If no smoothing or not in face region, return original
                if (_SmoothingStrength <= 0.0 || faceWeight < 0.01)
                {
                    return tex2D(_MainTex, modifiedUV);
                }

                // Skip smoothing if inside eyes or eyebrows
                if (IsInsideEyeOrEyebrow(uv))
                {
                    return tex2D(_MainTex, modifiedUV);
                }

                // Get center pixel color
                fixed4 centerColor = tex2D(_MainTex, modifiedUV);

                // Skip smoothing if this pixel is hair (facial hair, head hair, beard, etc.)
                if (_ExcludeHairFromSmoothing > 0.5 && IsHairColor(centerColor.rgb))
                {
                    return centerColor;
                }

                // Bilateral filter accumulator
                fixed4 filteredColor = fixed4(0, 0, 0, 0);
                float totalWeight = 0.0;

                // Get texture dimensions for proper pixel size calculation
                float2 texelSize = float2(1.0 / 1920.0, 1.0 / 1080.0);

                // Fixed kernel size for GPU compatibility (5x5 = 25 samples)
                // Unrolled loop for better GPU performance
                [unroll]
                for (int dx = -2; dx <= 2; dx++)
                {
                    [unroll]
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        // Sample offset position
                        float2 offset = float2(dx, dy) * texelSize * _SmoothingRadius;
                        float2 sampleUV = modifiedUV + offset;

                        // Sample color at offset (LOD 0 to prevent gradient issues)
                        fixed4 sampleColor = tex2Dlod(_MainTex, float4(sampleUV, 0, 0));

                        // Spatial weight (Gaussian based on distance)
                        float spatialDist = length(float2(dx, dy));
                        float spatialWeight = exp(-(spatialDist * spatialDist) / 8.0);

                        // Color weight (Gaussian based on color difference)
                        float3 colorDiff = centerColor.rgb - sampleColor.rgb;
                        float colorDist = length(colorDiff);
                        float colorWeight = exp(-(colorDist * colorDist) / (2.0 * _ColorSigma * _ColorSigma));

                        // Combined weight
                        float weight = spatialWeight * colorWeight;

                        filteredColor += sampleColor * weight;
                        totalWeight += weight;
                    }
                }

                // Normalize
                filteredColor /= totalWeight;

                // Blend between original and filtered based on smoothing strength and face weight
                float blendFactor = _SmoothingStrength * faceWeight;
                return lerp(centerColor, filteredColor, blendFactor);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // ============================================
                // STEP 1: APPLY EYE ENLARGEMENT (UV WARPING)
                // Must happen FIRST before sampling texture
                // ============================================
                float2 modifiedUV = ApplyAllEyeEnlargements(i.uv);

                // Sample texture with modified UV coordinates (this gives us enlarged eyes)
                fixed4 col = tex2D(_MainTex, modifiedUV);

                // DEBUG MODE: Show segmentation mask as red overlay
                if (_DebugSegmentationMask > 0.5 && _EnableSegmentationMask > 0.5)
                {
                    float maskValue = tex2D(_SegmentationMask, i.uv).r;
                    return fixed4(maskValue, 0, 0, 1); // Red channel shows mask
                }

                // Calculate geometric weight for face detection (used by both brightening and smoothing)
                float geometricWeight = (_FaceOvalCount >= 3) ? CalculateMaxWeightAcrossFaces(i.uv) : 0.0;

                // ============================================
                // STAGE 3: Subject/Background Segmentation (Optional)
                // Check first - most efficient early exit
                // ============================================
                if (_EnableSegmentationMask > 0.5)
                {
                    float maskValue = tex2D(_SegmentationMask, i.uv).r;
                    // If background (maskValue ~= 0), skip immediately
                    // Mask: 1.0 (white) = person, 0.0 (black) = background
                    if (maskValue < 0.5)
                        return col;
                }

                // ============================================
                // STEP 2: FACE SMOOTHING (BILATERAL FILTER)
                // Apply edge-preserving smoothing to face regions FIRST
                // This smooths the texture before brightening
                // ============================================
                if (_SmoothingStrength > 0.0 && geometricWeight >= 0.01)
                {
                    // Apply smoothing using geometric weight
                    col = ApplyBilateralFilter(i.uv, modifiedUV, geometricWeight);
                }

                // ============================================
                // STEP 3: FACE BRIGHTENING
                // Uses ORIGINAL UV (i.uv) for position detection, not modified UV
                // Applies AFTER smoothing so brightening is preserved
                // ============================================

                float finalWeight = geometricWeight;

                // Apply brightening only if enabled and face detected
                if (_BrightenStrength > 0.0 && geometricWeight >= 0.01)
                {
                    // ============================================
                    // STAGE 2: Skin Detection (Optional refinement in expansion zones)
                    // Only apply skin filtering if we're in an expansion zone (not fully inside)
                    // ============================================

                    if (_EnableSkinDetection > 0.5 && geometricWeight < 1.0)
                    {
                        // We're in an expansion zone - apply skin detection
                        float skinWeight = 0.0;

                        if (_UseAdaptiveSkinColor > 0.5)
                        {
                            // ADAPTIVE MODE: Compare against actual face skin color
                            float3 referenceSkin = GetReferenceSkinColor();
                            skinWeight = DetectSkinAdaptive(col.rgb, referenceSkin);
                        }
                        else
                        {
                            // GENERIC MODE: Use universal skin tone ranges
                            skinWeight = DetectSkinTone(col.rgb);
                        }

                        // Only apply geometric weight if it's skin
                        if (skinWeight < 0.5)
                        {
                            // Not skin - reduce weight to zero
                            finalWeight = 0.0;
                        }
                        // If it is skin, keep the geometric weight as-is
                    }

                    // Apply brightening based on combined weight
                    col.rgb *= (1.0 + _BrightenStrength * finalWeight);

                    // Prevent overexposure
                    col.rgb = saturate(col.rgb);
                }

                return col;
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
}