using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Mediapipe.Tasks.Vision.ImageSegmenter;
using Mediapipe.Unity.CoordinateSystem;

namespace Mediapipe.Unity.Tutorial
{
    /// <summary>
    /// Provides person/background segmentation mask using MediaPipe ImageSegmenter
    /// Acts as a Stage 3 filter for face brightening effects
    /// </summary>
    public class FaceSegmentationProvider : MonoBehaviour
    {
        [Header("Segmentation Model")]
        [SerializeField] private TextAsset segmentationModelAsset;

        [Header("Settings")]
        [SerializeField] private bool enableSegmentation = true;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;
        [SerializeField] private bool showDebugMaskDisplay = false; // Toggle to show/hide debug mask
        [SerializeField] private RawImage debugMaskDisplay; // Optional: to visualize mask

        private ImageSegmenter imageSegmenter;
        private Texture2D segmentationMaskTexture;
        private bool isInitialized = false;

        // Public property to get the mask texture
        public Texture2D SegmentationMask => segmentationMaskTexture;
        public bool IsSegmentationEnabled => enableSegmentation && isInitialized;

        private void Awake()
        {
            // Initialize in Awake so it's ready before other components
            if (segmentationModelAsset == null)
            {
                Debug.LogWarning("⚠️ Segmentation model not assigned. Segmentation will be disabled.");
                return;
            }

            InitializeSegmenter();
        }

        private void InitializeSegmenter()
        {
            try
            {
                var options = new ImageSegmenterOptions(
                    baseOptions: new Tasks.Core.BaseOptions(
                        Tasks.Core.BaseOptions.Delegate.CPU,
                        modelAssetBuffer: segmentationModelAsset.bytes
                    ),
                    runningMode: Tasks.Vision.Core.RunningMode.IMAGE,
                    outputConfidenceMasks: false,
                    outputCategoryMask: true
                );

                imageSegmenter = ImageSegmenter.CreateFromOptions(options);
                isInitialized = true;

                Debug.Log("✅ FaceSegmentationProvider initialized successfully");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Failed to initialize segmentation: {e.Message}");
                isInitialized = false;
            }
        }

        /// <summary>
        /// Process an image and generate segmentation mask
        /// </summary>
        public void ProcessImage(Texture2D inputImage)
        {
            if (!enableSegmentation)
            {
                if (showDebugInfo)
                    Debug.LogWarning("⚠️ Segmentation DISABLED (checkbox is OFF)");
                return;
            }
            
            if (!isInitialized || imageSegmenter == null)
            {
                if (showDebugInfo)
                    Debug.LogWarning($"⚠️ Segmentation NOT INITIALIZED");
                return;
            }

            if (showDebugInfo)
                Debug.Log($"🎬 Processing segmentation: {inputImage.width}x{inputImage.height}");

            try
            {
                // Create texture frame from input image
                using var textureFrame = new Experimental.TextureFrame(
                    inputImage.width, 
                    inputImage.height, 
                    TextureFormat.RGBA32
                );
                
                textureFrame.ReadTextureOnCPU(inputImage, flipHorizontally: false, flipVertically: true);
                
                using var image = textureFrame.BuildCPUImage();

                // Run segmentation
                var result = imageSegmenter.Segment(image, imageProcessingOptions: null);

                if (result.categoryMask != null)
                {
                    // Convert mask to texture
                    UpdateMaskTexture(result.categoryMask, inputImage.width, inputImage.height);

                    if (showDebugInfo)
                        Debug.Log($"✅ Segmentation mask generated: {inputImage.width}x{inputImage.height}");
                }
                else
                {
                    Debug.LogWarning("⚠️ Segmentation returned null mask");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Segmentation error: {e.Message}");
            }
        }

        private void UpdateMaskTexture(Mediapipe.Image mask, int width, int height)
        {
            // Create or resize texture if needed
            if (segmentationMaskTexture == null || 
                segmentationMaskTexture.width != width || 
                segmentationMaskTexture.height != height)
            {
                if (segmentationMaskTexture != null)
                    Destroy(segmentationMaskTexture);

                segmentationMaskTexture = new Texture2D(width, height, TextureFormat.R8, false);
                segmentationMaskTexture.filterMode = FilterMode.Bilinear;
                segmentationMaskTexture.wrapMode = TextureWrapMode.Clamp;
            }

            // Copy mask data to texture
            var maskData = new byte[width * height];
            
            // Ensure mask is on CPU
            mask.ConvertToCpu();
            
            // Get pixel data using PixelWriteLock
            using (var pixelLock = new Mediapipe.PixelWriteLock(mask))
            {
                var pixelPtr = pixelLock.Pixels();
                System.Runtime.InteropServices.Marshal.Copy(pixelPtr, maskData, 0, maskData.Length);
            }

            // Debug: Log some sample values
            if (showDebugInfo && maskData.Length > 0)
            {
                int centerIndex = (height / 2) * width + (width / 2);
                int cornerIndex = 0;
                int personCount = 0;
                for (int i = 0; i < maskData.Length; i++)
                {
                    if (maskData[i] > 0) personCount++;
                }
                Debug.Log($"🎭 Mask values - Corner(BG):{maskData[cornerIndex]}, Center(Person):{maskData[centerIndex]}, PersonPixels:{personCount}/{maskData.Length}");
            }

            // Flip vertically and convert to binary mask
            // MediaPipe output is upside down relative to Unity
            var flippedData = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIndex = y * width + x;
                    int dstIndex = (height - 1 - y) * width + x;
                    
                    // Convert category to 0-255 range
                    // MediaPipe selfie_segmentation: Category 0 = PERSON, 1 = BACKGROUND
                    // We want: Person = 255 (white), Background = 0 (black)
                    flippedData[dstIndex] = maskData[srcIndex] == 0 ? (byte)255 : (byte)0;
                }
            }

            segmentationMaskTexture.LoadRawTextureData(flippedData);
            segmentationMaskTexture.Apply();

            // Update debug display visibility
            UpdateDebugMaskDisplay();
        }

        /// <summary>
        /// Update debug mask display visibility based on showDebugMaskDisplay toggle
        /// </summary>
        private void UpdateDebugMaskDisplay()
        {
            if (debugMaskDisplay != null)
            {
                if (showDebugMaskDisplay)
                {
                    debugMaskDisplay.texture = segmentationMaskTexture;
                    debugMaskDisplay.gameObject.SetActive(true);
                }
                else
                {
                    debugMaskDisplay.gameObject.SetActive(false);
                }
            }
        }

        private void OnValidate()
        {
            // Update debug display when inspector values change in play mode
            if (Application.isPlaying)
            {
                UpdateDebugMaskDisplay();
            }
        }

        private void OnDestroy()
        {
            if (imageSegmenter != null)
            {
                imageSegmenter.Close();
                imageSegmenter = null;
            }

            if (segmentationMaskTexture != null)
            {
                Destroy(segmentationMaskTexture);
                segmentationMaskTexture = null;
            }
        }

        // Public properties
        public bool EnableSegmentation
        {
            get => enableSegmentation;
            set => enableSegmentation = value;
        }
    }
}
