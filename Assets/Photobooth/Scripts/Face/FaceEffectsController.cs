using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Mediapipe.Unity.Tutorial
{
    public class FaceEffectsController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] public RawImage targetImage;

        [SerializeField] private Shader faceEffectsShader;

        [Header("Brightening Settings")]
        [SerializeField, Range(0f, 0.4f)]
        private float brightenStrength = 0.2f;

        [SerializeField, Range(0.02f, 0.15f)]
        private float regionExpansion = 0.12f;

        [SerializeField, Range(0.5f, 2.0f)]
        private float foreheadExpansionMultiplier = 1.5f;

        [SerializeField]
        private bool enableBrightening = true;

        [Header("Eye Enlargement Settings")]
        [SerializeField, Range(0.5f, 3f)]
        [Tooltip("Multiplier for eye radius. Higher = larger affected area. Default 1.5")]
        private float eyeRadiusMultiplier = 1.5f;

        [SerializeField, Range(0f, 2f)]
        public float eyeEnlargementStrength = 0.4f;

        [SerializeField]
        private bool enableEyeEnlargement = true;

        [Header("Smoothing Settings")]
        [SerializeField, Range(0f, 1f)]
        private float smoothingStrength = 0.5f;

        [SerializeField, Range(1, 10)]
        private int smoothingRadius = 5;

        [SerializeField, Range(0.01f, 1f)]
        private float colorSigma = 0.2f;

        [SerializeField]
        private bool enableSmoothing = true;

        [SerializeField]
        private bool excludeHairFromSmoothing = true;

        [SerializeField, Range(0f, 1f)]
        private float hairDetectionSensitivity = 0.5f;

        [Header("Skin Detection")]
        [SerializeField]
        private bool enableSkinDetection = true;

        [SerializeField, Range(0f, 10f)]
        private float skinTolerance = 5f;

        [SerializeField]
        private bool useAdaptiveSkinColor = true;

        [SerializeField]
        private bool autoAdjustToleranceByBrightness = true;

        [Header("Segmentation")]
        [SerializeField]
        private bool enableSegmentationMask = false;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;

        [SerializeField] private bool debugSegmentationMask = false;

        private Material brighteningMaterial;
        private Texture2D segmentationMaskTexture;

        // Complete face oval landmarks - defines the entire face boundary
        private int[] faceOvalIndices = { 10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288,
                                          397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136,
                                          172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109 };

        // Support for multiple faces (max 10 faces)
        private const int MAX_FACES = 10;

        private const int POINTS_PER_FACE = 36;
        private Vector4[] faceOvalPoints = new Vector4[MAX_FACES * POINTS_PER_FACE]; // 360 points total
        private int currentFaceCount = 0;
        private bool hasValidFaceData = false;

        // Eye enlargement data (received from EyeEnlargement controller)
        // Note: leftEyeCenters and rightEyeCenters store (x, y, radius, 0) in their components
        private Vector4[] leftEyeCenters = new Vector4[MAX_FACES];

        private Vector4[] rightEyeCenters = new Vector4[MAX_FACES];

        // Eye landmark indices for internal calculation
        private int[] leftEyeIndices = { 33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246 };

        private int[] rightEyeIndices = { 362, 382, 381, 380, 374, 373, 390, 249, 263, 466, 388, 387, 386, 385, 384, 398 };

        // Eye and eyebrow exclusion data for smoothing
        private const int EYE_POINTS = 16;

        private const int EYEBROW_POINTS = 10;
        private const int MOUTH_POINTS = 40;  // Outer and inner lip contours
        private Vector4[] leftEyePoints = new Vector4[MAX_FACES * EYE_POINTS];
        private Vector4[] rightEyePoints = new Vector4[MAX_FACES * EYE_POINTS];
        private Vector4[] leftEyebrowPoints = new Vector4[MAX_FACES * EYEBROW_POINTS];
        private Vector4[] rightEyebrowPoints = new Vector4[MAX_FACES * EYEBROW_POINTS];
        private Vector4[] mouthPoints = new Vector4[MAX_FACES * MOUTH_POINTS];

        private void Awake()
        {
            // Create material from shader
            if (faceEffectsShader != null)
            {
                brighteningMaterial = new Material(faceEffectsShader);
            }
            else
            {
                Debug.LogError("❌ Face Effects Shader is not assigned!");
            }
        }

        private void Start()
        {
            if (faceEffectsShader == null)
            {
                Debug.LogError("❌ Face Effects Shader is not assigned!");
                return;
            }

            if (targetImage != null)
            {
                targetImage.material = brighteningMaterial;
                if (showDebugInfo)
                    Debug.Log("✅ Brightening material applied to target image");
            }
            else
            {
                Debug.LogError("❌ Target RawImage is not assigned!");
            }

            // Initialize face points array ONLY if not already set
            if (faceOvalPoints == null || faceOvalPoints.Length != (MAX_FACES * POINTS_PER_FACE))
            {
                faceOvalPoints = new Vector4[MAX_FACES * POINTS_PER_FACE];
            }

            // Only zero out if we don't have valid data yet
            if (!hasValidFaceData)
            {
                for (int i = 0; i < faceOvalPoints.Length; i++)
                {
                    faceOvalPoints[i] = Vector4.zero;
                }
                currentFaceCount = 0;
            }

            if (showDebugInfo)
                Debug.Log($"✅ FaceEffectsController initialized - hasValidFaceData: {hasValidFaceData}, currentFaceCount: {currentFaceCount}");
        }

        /// <summary>
        /// Update face position from detected landmarks (single face - for backward compatibility)
        /// </summary>
        public void UpdateFacePosition(List<Vector3> landmarks, RectTransform imageRect)
        {
            if (landmarks == null || landmarks.Count == 0)
            {
                Debug.LogWarning("⚠️ No landmarks provided");
                return;
            }

            // Convert to list of lists for multi-face method
            var multipleLandmarks = new List<List<Vector3>> { landmarks };
            UpdateMultipleFacePositions(multipleLandmarks, imageRect);
        }

        /// <summary>
        /// Update positions for multiple detected faces
        /// </summary>
        public void UpdateMultipleFacePositions(List<List<Vector3>> allFaceLandmarks, RectTransform imageRect)
        {
            if (brighteningMaterial == null)
            {
                Debug.LogError("❌ Brightening material is null!");
                return;
            }

            if (allFaceLandmarks == null || allFaceLandmarks.Count == 0)
            {
                Debug.LogWarning("⚠️ No face landmarks provided");
                currentFaceCount = 0;
                hasValidFaceData = false;
                UpdateShaderProperties();
                return;
            }

            // Limit to MAX_FACES
            int faceCount = Mathf.Min(allFaceLandmarks.Count, MAX_FACES);
            currentFaceCount = faceCount;

            UnityEngine.Rect rect = imageRect.rect;
            float rectWidth = rect.width;
            float rectHeight = rect.height;

            // Process each face
            for (int faceIdx = 0; faceIdx < faceCount; faceIdx++)
            {
                var landmarks = allFaceLandmarks[faceIdx];
                int baseIndex = faceIdx * POINTS_PER_FACE;

                // Convert face oval landmarks to normalized UV coordinates
                for (int i = 0; i < faceOvalIndices.Length; i++)
                {
                    int landmarkIndex = faceOvalIndices[i];

                    if (landmarkIndex < landmarks.Count)
                    {
                        Vector3 localPos = landmarks[landmarkIndex];

                        // Convert from local RectTransform position to UV coordinates (0-1)
                        float uvX = Mathf.Clamp01((localPos.x - rect.xMin) / rectWidth);
                        float uvY = Mathf.Clamp01((localPos.y - rect.yMin) / rectHeight);

                        faceOvalPoints[baseIndex + i] = new Vector4(uvX, uvY, 0, 0);

                        if (showDebugInfo && faceIdx == 0 && i == 0)
                        {
                            Debug.Log($"🎯 Face[{faceIdx}] Landmark[0]: localPos=({localPos.x:F2},{localPos.y:F2}) -> UV=({uvX:F3},{uvY:F3})");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"⚠️ Face[{faceIdx}] Landmark index {landmarkIndex} out of range (count: {landmarks.Count})");
                    }
                }
            }

            hasValidFaceData = true;

            if (showDebugInfo)
            {
                Debug.Log($"✅ Updated {faceCount} face(s) with {POINTS_PER_FACE} points each");
                Debug.Log($"   Face[0] First point: {faceOvalPoints[0]}, Last point: {faceOvalPoints[35]}");
            }

            // Calculate eye enlargement data
            UpdateEyeEnlargementData(allFaceLandmarks, imageRect);

            // Auto-adjust skin tolerance based on skin brightness if enabled
            if (autoAdjustToleranceByBrightness && targetImage != null && targetImage.texture != null)
            {
                AutoAdjustSkinTolerance();
            }

            // Update shader with new face data
            UpdateShaderProperties();
        }

        /// <summary>
        /// Automatically adjust skin tolerance based on detected skin brightness
        /// Dark skin = lower tolerance (4.0f), Light skin = higher tolerance (10.0f)
        /// </summary>
        private void AutoAdjustSkinTolerance()
        {
            if (currentFaceCount == 0 || targetImage == null || targetImage.texture == null)
                return;

            try
            {
                // Calculate center of first face
                Vector2 faceCenter = Vector2.zero;
                for (int i = 0; i < POINTS_PER_FACE; i++)
                {
                    faceCenter += new Vector2(faceOvalPoints[i].x, faceOvalPoints[i].y);
                }
                faceCenter /= POINTS_PER_FACE;

                // Sample the texture at face center
                Texture2D mainTexture = targetImage.texture as Texture2D;
                if (mainTexture != null && mainTexture.isReadable)
                {
                    // Convert UV (0-1) to pixel coordinates
                    int x = Mathf.Clamp((int)(faceCenter.x * mainTexture.width), 0, mainTexture.width - 1);
                    int y = Mathf.Clamp((int)(faceCenter.y * mainTexture.height), 0, mainTexture.height - 1);

                    UnityEngine.Color skinColor = mainTexture.GetPixel(x, y);

                    // Calculate perceived brightness (luminance)
                    // Using standard RGB to luminance conversion
                    float brightness = 0.299f * skinColor.r + 0.587f * skinColor.g + 0.114f * skinColor.b;

                    // Adjust tolerance based on brightness
                    // Dark skin (brightness < 0.4): tolerance = 4.0f
                    // Light skin (brightness > 0.6): tolerance = 10.0f
                    // Medium skin (0.4 - 0.6): interpolate between 4.0f and 10.0f
                    if (brightness < 0.4f)
                    {
                        skinTolerance = 4.0f;
                        if (showDebugInfo)
                            Debug.Log($"🎨 Dark skin detected (brightness: {brightness:F2}) - Setting tolerance to 4.0");
                    }
                    else if (brightness > 0.6f)
                    {
                        skinTolerance = 10.0f;
                        if (showDebugInfo)
                            Debug.Log($"🎨 Light skin detected (brightness: {brightness:F2}) - Setting tolerance to 10.0");
                    }
                    else
                    {
                        // Interpolate for medium skin tones
                        skinTolerance = Mathf.Lerp(4.0f, 10.0f, (brightness - 0.4f) / 0.2f);
                        if (showDebugInfo)
                            Debug.Log($"🎨 Medium skin detected (brightness: {brightness:F2}) - Setting tolerance to {skinTolerance:F1}");
                    }
                }
                else if (mainTexture != null && !mainTexture.isReadable)
                {
                    // Texture not readable, try using RenderTexture approach
                    RenderTexture currentRT = RenderTexture.active;
                    RenderTexture renderTex = RenderTexture.GetTemporary(mainTexture.width, mainTexture.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(mainTexture, renderTex);
                    RenderTexture.active = renderTex;

                    Texture2D readableTex = new Texture2D(1, 1, TextureFormat.RGB24, false);
                    int x = Mathf.Clamp((int)(faceCenter.x * mainTexture.width), 0, mainTexture.width - 1);
                    int y = Mathf.Clamp((int)(faceCenter.y * mainTexture.height), 0, mainTexture.height - 1);

                    readableTex.ReadPixels(new UnityEngine.Rect(x, y, 1, 1), 0, 0);
                    readableTex.Apply();

                    UnityEngine.Color skinColor = readableTex.GetPixel(0, 0);
                    float brightness = 0.299f * skinColor.r + 0.587f * skinColor.g + 0.114f * skinColor.b;

                    if (brightness < 0.4f)
                    {
                        skinTolerance = 4.0f;
                        if (showDebugInfo)
                            Debug.Log($"🎨 Dark skin detected (brightness: {brightness:F2}) - Setting tolerance to 4.0");
                    }
                    else if (brightness > 0.6f)
                    {
                        skinTolerance = 10.0f;
                        if (showDebugInfo)
                            Debug.Log($"🎨 Light skin detected (brightness: {brightness:F2}) - Setting tolerance to 10.0");
                    }
                    else
                    {
                        skinTolerance = Mathf.Lerp(4.0f, 10.0f, (brightness - 0.4f) / 0.2f);
                        if (showDebugInfo)
                            Debug.Log($"🎨 Medium skin detected (brightness: {brightness:F2}) - Setting tolerance to {skinTolerance:F1}");
                    }

                    RenderTexture.active = currentRT;
                    RenderTexture.ReleaseTemporary(renderTex);
                    Destroy(readableTex);
                }
            }
            catch (System.Exception e)
            {
                if (showDebugInfo)
                    Debug.LogWarning($"⚠️ Could not auto-adjust skin tolerance: {e.Message}");
            }
        }

        /// <summary>
        /// Update shader properties with current settings
        /// </summary>
        public void UpdateShaderProperties()
        {
            if (brighteningMaterial == null)
            {
                Debug.LogWarning("⚠️ Cannot update shader - material is null");
                return;
            }

            if (showDebugInfo)
            {
                int pointsToSend = hasValidFaceData ? POINTS_PER_FACE : 0;
                int facesToSend = hasValidFaceData ? currentFaceCount : 0;
                Debug.Log($"📤 Sending to shader: _FaceCount={facesToSend}, _FaceOvalCount={pointsToSend}, faceOvalPoints[0]={faceOvalPoints[0]}");
            }

            // Send face oval points to shader
            brighteningMaterial.SetInt("_FaceOvalCount", hasValidFaceData ? POINTS_PER_FACE : 0);
            brighteningMaterial.SetInt("_FaceCount", hasValidFaceData ? currentFaceCount : 0);
            brighteningMaterial.SetVectorArray("_FaceOvalPoints", faceOvalPoints);

            // Set brightening parameters
            brighteningMaterial.SetFloat("_BrightenStrength", enableBrightening ? brightenStrength : 0f);
            brighteningMaterial.SetFloat("_RegionExpansion", regionExpansion);
            brighteningMaterial.SetFloat("_ForeheadExpansionMultiplier", foreheadExpansionMultiplier);

            // Set skin detection parameters
            brighteningMaterial.SetFloat("_EnableSkinDetection", enableSkinDetection ? 1.0f : 0.0f);
            brighteningMaterial.SetFloat("_SkinTolerance", skinTolerance);
            brighteningMaterial.SetFloat("_UseAdaptiveSkinColor", useAdaptiveSkinColor ? 1.0f : 0.0f);

            // Set segmentation mask
            if (segmentationMaskTexture != null && enableSegmentationMask)
            {
                brighteningMaterial.SetTexture("_SegmentationMask", segmentationMaskTexture);
                brighteningMaterial.SetFloat("_EnableSegmentationMask", 1.0f);
                if (showDebugInfo)
                    Debug.Log($"✅ Segmentation mask ENABLED: {segmentationMaskTexture.width}x{segmentationMaskTexture.height}");
            }
            else
            {
                brighteningMaterial.SetFloat("_EnableSegmentationMask", 0.0f);
            }

            // Set debug mode
            brighteningMaterial.SetFloat("_DebugSegmentationMask", debugSegmentationMask ? 1.0f : 0.0f);

            // Set eye enlargement parameters
            brighteningMaterial.SetVectorArray("_LeftEyeCenters", leftEyeCenters);
            brighteningMaterial.SetVectorArray("_RightEyeCenters", rightEyeCenters);
            float eyeStrengthToUse = enableEyeEnlargement ? eyeEnlargementStrength : 0f;
            brighteningMaterial.SetFloat("_EnlargementStrength", eyeStrengthToUse);

            // Set smoothing parameters
            brighteningMaterial.SetFloat("_SmoothingStrength", enableSmoothing ? smoothingStrength : 0f);
            brighteningMaterial.SetInt("_SmoothingRadius", smoothingRadius);
            brighteningMaterial.SetFloat("_ColorSigma", colorSigma);
            brighteningMaterial.SetFloat("_ExcludeHairFromSmoothing", excludeHairFromSmoothing ? 1.0f : 0.0f);
            brighteningMaterial.SetFloat("_HairDetectionSensitivity", hairDetectionSensitivity);

            if (showDebugInfo)
            {
                Debug.Log($"🎨 Shader updated - Brightening: {brightenStrength}, Enlargement: {eyeEnlargementStrength}, Smoothing: {smoothingStrength}, Points: {(hasValidFaceData ? faceOvalIndices.Length : 0)}");
            }
        }

        /// <summary>
        /// Update eye enlargement data (DEPRECATED - kept for backward compatibility with old EyeEnlargement script)
        /// Eye enlargement is now calculated automatically in UpdateMultipleFacePositions
        /// </summary>
        [System.Obsolete("Use UpdateMultipleFacePositions instead - eye enlargement is now integrated")]
        public void SetEyeEnlargementData(List<Vector4> leftCenters, List<Vector4> rightCenters, float strength)
        {
            if (leftCenters == null || rightCenters == null)
            {
                Debug.LogWarning("⚠️ Eye centers are null!");
                return;
            }

            int faceCount = Mathf.Min(leftCenters.Count, MAX_FACES);

            // Copy eye centers (with radius in .z component)
            for (int i = 0; i < faceCount; i++)
            {
                leftEyeCenters[i] = leftCenters[i];
                rightEyeCenters[i] = rightCenters[i];
            }

            // Clear remaining slots to avoid old data
            for (int i = faceCount; i < MAX_FACES; i++)
            {
                leftEyeCenters[i] = Vector4.zero;
                rightEyeCenters[i] = Vector4.zero;
            }

            eyeEnlargementStrength = strength;

            Debug.Log($"👁️ Eye enlargement data received (DEPRECATED): {faceCount} faces, strength={strength:F2}");
            for (int i = 0; i < faceCount; i++)
            {
                Debug.Log($"   Face[{i}] - Left eye: ({leftCenters[i].x:F3}, {leftCenters[i].y:F3}, r={leftCenters[i].z:F3}), Right eye: ({rightCenters[i].x:F3}, {rightCenters[i].y:F3}, r={rightCenters[i].z:F3})");
            }

            // Update shader immediately
            UpdateEyeShaderProperties();
        }

        /// <summary>
        /// Update only the eye enlargement properties in the shader
        /// </summary>
        public void UpdateEyeShaderProperties()
        {
            if (brighteningMaterial != null)
            {
                float strengthToUse = enableEyeEnlargement ? eyeEnlargementStrength : 0f;

                brighteningMaterial.SetVectorArray("_LeftEyeCenters", leftEyeCenters);
                brighteningMaterial.SetVectorArray("_RightEyeCenters", rightEyeCenters);
                brighteningMaterial.SetFloat("_EnlargementStrength", strengthToUse);

                // IMPORTANT: Set face count so shader knows how many faces to process for eye enlargement
                // Count non-zero eye centers
                int eyeFaceCount = 0;
                for (int i = 0; i < MAX_FACES; i++)
                {
                    // Check if either x or y is non-zero (valid eye center)
                    if (leftEyeCenters[i].x > 0.001f || leftEyeCenters[i].y > 0.001f)
                        eyeFaceCount = i + 1;
                }

                // Use the maximum of current face count or eye face count
                int faceCountToSet = Mathf.Max(currentFaceCount, eyeFaceCount);
                brighteningMaterial.SetInt("_FaceCount", faceCountToSet);

                Debug.Log($"✅ Shader updated with eye data - strength: {strengthToUse}, eyeFaceCount: {eyeFaceCount}, currentFaceCount: {currentFaceCount}, final _FaceCount: {faceCountToSet}");

                // Debug: Show all eye centers being sent to shader
                for (int i = 0; i < faceCountToSet; i++)
                {
                    Debug.Log($"   Shader Face[{i}] - LeftEye: {leftEyeCenters[i]}, RightEye: {rightEyeCenters[i]}");
                }
            }
            else
            {
                Debug.LogError("❌ Brightening material is null!");
            }
        }

        /// <summary>
        /// Update just the eye enlargement strength (for slider changes)
        /// </summary>
        public void UpdateEyeEnlargementStrength(float strength)
        {
            eyeEnlargementStrength = strength;

            if (brighteningMaterial != null)
            {
                float strengthToUse = enableEyeEnlargement ? eyeEnlargementStrength : 0f;
                brighteningMaterial.SetFloat("_EnlargementStrength", strengthToUse);
                Debug.Log($"✅ Eye strength updated to: {strengthToUse}");
            }
        }

        /// <summary>
        /// Calculate and update eye enlargement data from landmarks
        /// </summary>
        private void UpdateEyeEnlargementData(List<List<Vector3>> allFaceLandmarks, RectTransform imageRect)
        {
            if (allFaceLandmarks == null || allFaceLandmarks.Count == 0)
                return;

            int faceCount = Mathf.Min(allFaceLandmarks.Count, MAX_FACES);

            // Calculate per-eye centers and radii
            for (int f = 0; f < faceCount; f++)
            {
                var landmarks = allFaceLandmarks[f];

                Vector2 leftCenter = CalculateEyeCenterNormalized(landmarks, leftEyeIndices, imageRect);
                Vector2 rightCenter = CalculateEyeCenterNormalized(landmarks, rightEyeIndices, imageRect);

                float leftRadius = CalculateEyeRadiusNormalized(landmarks, leftEyeIndices, imageRect);
                float rightRadius = CalculateEyeRadiusNormalized(landmarks, rightEyeIndices, imageRect);

                // Store eye center in .xy and radius in .z component
                leftEyeCenters[f] = new Vector4(leftCenter.x, leftCenter.y, leftRadius, 0f);
                rightEyeCenters[f] = new Vector4(rightCenter.x, rightCenter.y, rightRadius, 0f);

                if (showDebugInfo)
                {
                    Debug.Log($"👁️ Face[{f}] eye UV - Left: ({leftCenter.x:F3}, {leftCenter.y:F3}, r={leftRadius:F4}), Right: ({rightCenter.x:F3}, {rightCenter.y:F3}, r={rightRadius:F4})");
                }
            }

            // Clear remaining slots
            for (int i = faceCount; i < MAX_FACES; i++)
            {
                leftEyeCenters[i] = Vector4.zero;
                rightEyeCenters[i] = Vector4.zero;
            }

            // Update shader
            UpdateEyeShaderProperties();
        }

        /// <summary>
        /// Calculate normalized eye center from landmarks
        /// </summary>
        private Vector2 CalculateEyeCenterNormalized(List<Vector3> landmarks, int[] eyeIndices, RectTransform imageRect)
        {
            Vector3 center = Vector3.zero;
            int count = 0;

            foreach (int index in eyeIndices)
            {
                if (index < landmarks.Count)
                {
                    center += landmarks[index];
                    count++;
                }
            }

            if (count == 0) return new Vector2(0.5f, 0.5f);

            center /= count;

            UnityEngine.Rect rect = imageRect.rect;
            float normalizedX = Mathf.Clamp01((center.x - rect.xMin) / rect.width);
            float normalizedY = Mathf.Clamp01((center.y - rect.yMin) / rect.height);

            return new Vector2(normalizedX, normalizedY);
        }

        /// <summary>
        /// Calculate normalized eye radius from landmarks
        /// </summary>
        private float CalculateEyeRadiusNormalized(List<Vector3> landmarks, int[] eyeIndices, RectTransform imageRect)
        {
            if (eyeIndices.Length < 2) return 0.07f; // Fallback

            Vector3 center = Vector3.zero;
            foreach (int index in eyeIndices)
            {
                if (index < landmarks.Count)
                    center += landmarks[index];
            }
            center /= eyeIndices.Length;

            float maxDist = 0f;
            foreach (int index in eyeIndices)
            {
                if (index < landmarks.Count)
                {
                    float dist = Vector3.Distance(landmarks[index], center);
                    if (dist > maxDist) maxDist = dist;
                }
            }

            UnityEngine.Rect rect = imageRect.rect;
            float normalizedRadius = maxDist / Mathf.Max(rect.width, rect.height);
            return normalizedRadius * eyeRadiusMultiplier;
        }

        /// <summary>
        /// Set eye and eyebrow exclusion data for smoothing (called by detection scripts)
        /// </summary>
        public void SetEyeAndEyebrowExclusionData(List<List<Vector3>> allFaceLandmarks, RectTransform imageRect)
        {
            if (allFaceLandmarks == null || allFaceLandmarks.Count == 0)
                return;

            UnityEngine.Rect rect = imageRect.rect;
            float rectWidth = rect.width;
            float rectHeight = rect.height;

            // Eye, eyebrow, and mouth landmark indices
            int[] leftEyeIndices = { 33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246 };
            int[] rightEyeIndices = { 362, 382, 381, 380, 374, 373, 390, 249, 263, 466, 388, 387, 386, 385, 384, 398 };
            int[] leftEyebrowIndices = { 70, 63, 105, 66, 107, 55, 65, 52, 53, 46 };
            int[] rightEyebrowIndices = { 300, 293, 334, 296, 336, 285, 295, 282, 283, 276 };
            int[] mouthIndices = {
                61, 185, 40, 39, 37, 0, 267, 269, 270, 409, 291,  // Upper outer lip
                375, 321, 405, 314, 17, 84, 181, 91, 146,         // Lower outer lip
                78, 191, 80, 81, 82, 13, 312, 311, 310, 415, 308, // Upper inner lip
                324, 318, 402, 317, 14, 87, 178, 88, 95           // Lower inner lip
            };

            int faceCount = Mathf.Min(allFaceLandmarks.Count, MAX_FACES);

            for (int faceIdx = 0; faceIdx < faceCount; faceIdx++)
            {
                var landmarks = allFaceLandmarks[faceIdx];

                // Convert left eye landmarks
                for (int i = 0; i < leftEyeIndices.Length && i < EYE_POINTS; i++)
                {
                    if (leftEyeIndices[i] < landmarks.Count)
                    {
                        Vector3 localPos = landmarks[leftEyeIndices[i]];
                        float uvX = Mathf.Clamp01((localPos.x - rect.xMin) / rectWidth);
                        float uvY = Mathf.Clamp01((localPos.y - rect.yMin) / rectHeight);
                        leftEyePoints[faceIdx * EYE_POINTS + i] = new Vector4(uvX, uvY, 0, 0);
                    }
                }

                // Convert right eye landmarks
                for (int i = 0; i < rightEyeIndices.Length && i < EYE_POINTS; i++)
                {
                    if (rightEyeIndices[i] < landmarks.Count)
                    {
                        Vector3 localPos = landmarks[rightEyeIndices[i]];
                        float uvX = Mathf.Clamp01((localPos.x - rect.xMin) / rectWidth);
                        float uvY = Mathf.Clamp01((localPos.y - rect.yMin) / rectHeight);
                        rightEyePoints[faceIdx * EYE_POINTS + i] = new Vector4(uvX, uvY, 0, 0);
                    }
                }

                // Convert left eyebrow landmarks
                for (int i = 0; i < leftEyebrowIndices.Length && i < EYEBROW_POINTS; i++)
                {
                    if (leftEyebrowIndices[i] < landmarks.Count)
                    {
                        Vector3 localPos = landmarks[leftEyebrowIndices[i]];
                        float uvX = Mathf.Clamp01((localPos.x - rect.xMin) / rectWidth);
                        float uvY = Mathf.Clamp01((localPos.y - rect.yMin) / rectHeight);
                        leftEyebrowPoints[faceIdx * EYEBROW_POINTS + i] = new Vector4(uvX, uvY, 0, 0);
                    }
                }

                // Convert right eyebrow landmarks
                for (int i = 0; i < rightEyebrowIndices.Length && i < EYEBROW_POINTS; i++)
                {
                    if (rightEyebrowIndices[i] < landmarks.Count)
                    {
                        Vector3 localPos = landmarks[rightEyebrowIndices[i]];
                        float uvX = Mathf.Clamp01((localPos.x - rect.xMin) / rectWidth);
                        float uvY = Mathf.Clamp01((localPos.y - rect.yMin) / rectHeight);
                        rightEyebrowPoints[faceIdx * EYEBROW_POINTS + i] = new Vector4(uvX, uvY, 0, 0);
                    }
                }

                // Convert mouth landmarks
                for (int i = 0; i < mouthIndices.Length && i < MOUTH_POINTS; i++)
                {
                    if (mouthIndices[i] < landmarks.Count)
                    {
                        Vector3 localPos = landmarks[mouthIndices[i]];
                        float uvX = Mathf.Clamp01((localPos.x - rect.xMin) / rectWidth);
                        float uvY = Mathf.Clamp01((localPos.y - rect.yMin) / rectHeight);
                        mouthPoints[faceIdx * MOUTH_POINTS + i] = new Vector4(uvX, uvY, 0, 0);
                    }
                }
            }

            // Update shader with exclusion data
            UpdateEyeExclusionShaderProperties();
        }

        /// <summary>
        /// Update eye and eyebrow exclusion properties in shader
        /// </summary>
        private void UpdateEyeExclusionShaderProperties()
        {
            if (brighteningMaterial != null)
            {
                brighteningMaterial.SetVectorArray("_LeftEyePoints", leftEyePoints);
                brighteningMaterial.SetVectorArray("_RightEyePoints", rightEyePoints);
                brighteningMaterial.SetVectorArray("_LeftEyebrowPoints", leftEyebrowPoints);
                brighteningMaterial.SetVectorArray("_RightEyebrowPoints", rightEyebrowPoints);
                brighteningMaterial.SetVectorArray("_MouthPoints", mouthPoints);

                if (showDebugInfo)
                    Debug.Log($"✅ Eye, eyebrow, and mouth exclusion data updated for {currentFaceCount} faces");
            }
        }

        private void OnValidate()
        {
            if (Application.isPlaying && brighteningMaterial != null)
            {
                if (showDebugInfo)
                {
                    Debug.Log($"🔄 OnValidate called: hasValidFaceData={hasValidFaceData}, faceOvalPoints[0]={faceOvalPoints[0]}");
                }

                // DON'T re-convert landmarks, just update the shader parameters
                // The face points should already be set from UpdateFacePosition
                UpdateShaderProperties();
            }
        }

        private void OnDestroy()
        {
            if (brighteningMaterial != null)
            {
                Destroy(brighteningMaterial);
            }
        }

        // Public properties for external control
        public float BrightenStrength
        {
            get => brightenStrength;
            set
            {
                brightenStrength = Mathf.Clamp(value, 0f, 2f);
                UpdateShaderProperties();
            }
        }

        public float RegionExpansion
        {
            get => regionExpansion;
            set
            {
                regionExpansion = Mathf.Clamp(value, 0.02f, 0.15f);
                UpdateShaderProperties();
            }
        }

        public bool EnableBrightening
        {
            get => enableBrightening;
            set
            {
                enableBrightening = value;
                UpdateShaderProperties();
            }
        }

        public bool EnableSkinDetection
        {
            get => enableSkinDetection;
            set
            {
                enableSkinDetection = value;
                UpdateShaderProperties();
            }
        }

        public float SkinTolerance
        {
            get => skinTolerance;
            set
            {
                skinTolerance = Mathf.Clamp(value, 0f, 1f);
                UpdateShaderProperties();
            }
        }

        public bool UseAdaptiveSkinColor
        {
            get => useAdaptiveSkinColor;
            set
            {
                useAdaptiveSkinColor = value;
                UpdateShaderProperties();
            }
        }

        public bool EnableSegmentationMask
        {
            get => enableSegmentationMask;
            set
            {
                enableSegmentationMask = value;
                UpdateShaderProperties();
            }
        }

        public float SmoothingStrength
        {
            get => smoothingStrength;
            set
            {
                smoothingStrength = Mathf.Clamp(value, 0f, 1f);
                UpdateShaderProperties();
            }
        }

        public bool EnableSmoothing
        {
            get => enableSmoothing;
            set
            {
                enableSmoothing = value;
                UpdateShaderProperties();
            }
        }

        public bool ExcludeHairFromSmoothing
        {
            get => excludeHairFromSmoothing;
            set
            {
                excludeHairFromSmoothing = value;
                UpdateShaderProperties();
            }
        }

        public float HairDetectionSensitivity
        {
            get => hairDetectionSensitivity;
            set
            {
                hairDetectionSensitivity = Mathf.Clamp(value, 0f, 1f);
                UpdateShaderProperties();
            }
        }

        /// <summary>
        /// Set the segmentation mask texture (called by StaticFaceDetection)
        /// </summary>
        public void SetSegmentationMask(Texture2D maskTexture)
        {
            segmentationMaskTexture = maskTexture;
            UpdateShaderProperties();

            if (showDebugInfo && maskTexture != null)
                Debug.Log($"✅ Segmentation mask set: {maskTexture.width}x{maskTexture.height}");
        }

        /// <summary>
        /// Get the brightening material (for EyeEnlargement to update directly)
        /// </summary>
        public Material GetBrighteningMaterial()
        {
            return brighteningMaterial;
        }

        public bool HasValidFaceData => hasValidFaceData;
    }
}