using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Mediapipe.Unity.Tutorial;

public class UiController : MonoBehaviour
{
    public static UiController Instance { get; private set; }

    private List<Texture2D> capturedImages = new List<Texture2D>();
    public List<Texture2D> beautifiedImages = new List<Texture2D>();

    [SerializeField] private Slider brightnessSlider;
    [SerializeField] private Slider faceBrightnessSlider;
    [SerializeField] private Slider smoothenSlider;
    [SerializeField] private Slider eyeEnlargementSlider;

    [SerializeField] private Button doneButton;
    [SerializeField] private Button retakeButton;

    private bool blockCallbacks = false;

    private FaceEffectsController faceController;

    private float currentBrightness;
    private float currentFaceBrightness;
    private float currentSmoothness;
    private float currentEnlarge;

    private bool isSingleImageMode = false;
    private Texture2D currentEditingImage;

    private int currentEditingIndex = -1;

    // Filter Selection
    private FilterType currentFilter = FilterType.Original;

    // Store placeholder dimensions for accurate preview
    private float placeholderWidth;
    private float placeholderHeight;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        faceController = FindObjectOfType<FaceEffectsController>();
        if (faceController == null)
            Debug.LogError("FaceEffectsController not found in scene!");

        doneButton.onClick.AddListener(OnDone);
        retakeButton.onClick.AddListener(OnRetakeClicked);

        brightnessSlider.onValueChanged.AddListener(OnBrightnessChanged);
        faceBrightnessSlider.onValueChanged.AddListener(OnFaceBrightnessChanged);
        smoothenSlider.onValueChanged.AddListener(OnSmoothenChanged);
        eyeEnlargementSlider.onValueChanged.AddListener(OnEyeEnlargeChanged);
    }

    private void OnRetakeClicked()
    {
        AudioManager.Instance?.PlayClick();
        // 1. Hide the panel immediately so user sees feedback
        if (PhotoShootingManager.Instance.beautificationPanel != null)
            PhotoShootingManager.Instance.beautificationPanel.SetActive(false);

        // 2. Remove the last edited image logic
        if (currentEditingIndex >= 0 && currentEditingIndex < beautifiedImages.Count)
        {
            beautifiedImages.RemoveAt(currentEditingIndex);
        }

        // 3. Trigger reshot logic (restarts camera/countdown)
        PhotoShootingManager.Instance?.OnReshotClicked();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Load image for beautification with placeholder dimensions for accurate preview
    /// </summary>
    public void OnLoadSingleCaptureImage(Texture2D image, int shotIndex, float phWidth, float phHeight, string filterName = "")
    {
        if (image == null) return;

        isSingleImageMode = true;
        currentEditingImage = image;
        currentEditingIndex = shotIndex;
        placeholderWidth = phWidth;
        placeholderHeight = phHeight;

        var screen = StaticFaceDetection.Instance.screen;
        screen.texture = image;

        // Calculate target aspect ratio from placeholder
        float targetAspect = placeholderWidth / placeholderHeight;

        // Set screen size to match placeholder aspect ratio (using consistent base width)
        float baseWidth = 1000f;
        float adjustedHeight = baseWidth / targetAspect;

        screen.rectTransform.sizeDelta = new Vector2(baseWidth, adjustedHeight);

        // Apply center-crop UV rect to match final output
        float texAspect = (float)image.width / image.height;

        if (texAspect > targetAspect)
        {
            // Texture is wider - crop sides
            float scale = targetAspect / texAspect;
            screen.uvRect = new Rect((1f - scale) / 2f, 0f, scale, 1f);
        }
        else
        {
            // Texture is taller - crop top/bottom
            float scale = texAspect / targetAspect;
            screen.uvRect = new Rect(0f, (1f - scale) / 2f, 1f, scale);
        }

        // Reset effect values for fresh start (User Request)
        currentBrightness = 0f;
        currentFaceBrightness = 0f;
        currentSmoothness = 0f;
        currentEnlarge = 0f;

        blockCallbacks = true;
        brightnessSlider.value = currentBrightness;
        faceBrightnessSlider.value = currentFaceBrightness;
        smoothenSlider.value = currentSmoothness;
        eyeEnlargementSlider.value = currentEnlarge;
        
        // Reset or Apply Filter
        currentFilter = FilterType.Original;
        
        if (!string.IsNullOrEmpty(filterName))
        {
             if (System.Enum.TryParse(filterName, true, out FilterType parsedFilter))
             {
                 currentFilter = parsedFilter;
                 // Don't log if it's Original (default), only log if a specific filter is applied
                 if (currentFilter != FilterType.Original)
                    Debug.Log($"🎨 Auto-applying filter: {currentFilter}");
             }
             else
             {
                 Debug.LogWarning($"⚠️ Could not parse filter name: '{filterName}' - Defaulting to Original");
                 currentFilter = FilterType.Original;
             }
        }
        
        blockCallbacks = false;

        if (retakeButton != null)
        {
            // Only show Retake if allowed
            bool canRetake = PhotoShootingManager.Instance.CanRetake();
            retakeButton.gameObject.SetActive(canRetake);
            Debug.Log($"Displaying Retake Button: {canRetake}");
        }

        StaticFaceDetection.Instance.inputImage = currentEditingImage;
        
        // Disable auto-resize in StaticFaceDetection so it doesn't override our layout
        StaticFaceDetection.Instance.autoResizeScreen = false;
        
        StartCoroutine(StaticFaceDetection.Instance.OnDetectImage());

        ApplySettingsToFaceController();

        Debug.Log($"✅ Preview set to {baseWidth}x{adjustedHeight} (aspect: {targetAspect:F2}) matching placeholder {placeholderWidth}x{placeholderHeight}");
    }

    private void OnBrightnessChanged(float value)
    {
        if (blockCallbacks) return;
        if (isSingleImageMode) currentBrightness = value;
        ApplySettingsToFaceController();
    }

    private void OnFaceBrightnessChanged(float value)
    {
        if (blockCallbacks) return;
        if (isSingleImageMode) currentFaceBrightness = value;
        ApplySettingsToFaceController();
    }

    private void OnSmoothenChanged(float value)
    {
        if (blockCallbacks) return;
        if (isSingleImageMode) currentSmoothness = value;
        ApplySettingsToFaceController();
    }

    private void OnEyeEnlargeChanged(float value)
    {
        if (blockCallbacks) return;
        if (isSingleImageMode) currentEnlarge = value;
        ApplySettingsToFaceController();
    }

    private void ApplySettingsToFaceController()
    {
        if (faceController == null) return;
        faceController.BrightenStrength = currentBrightness;
        faceController.FaceBrightenStrength = currentFaceBrightness;
        faceController.SmoothingStrength = currentSmoothness;
        faceController.UpdateEyeEnlargementStrength(currentEnlarge);
        faceController.CurrentFilter = currentFilter;
    }

    private void OnDone()
    {
        AudioManager.Instance?.PlayClick();
        if (isSingleImageMode)
        {
            StartCoroutine(ProcessAndSaveSingleImage());
        }
    }

    private IEnumerator ProcessAndSaveSingleImage()
    {
        StaticFaceDetection.Instance.inputImage = currentEditingImage;
        // Ensure auto-resize is disabled here too
        StaticFaceDetection.Instance.autoResizeScreen = false;
        yield return StartCoroutine(StaticFaceDetection.Instance.OnDetectImage());

        faceController.BrightenStrength = currentBrightness;
        faceController.FaceBrightenStrength = currentFaceBrightness;
        faceController.SmoothingStrength = currentSmoothness;
        faceController.UpdateEyeEnlargementStrength(currentEnlarge);
        faceController.CurrentFilter = currentFilter;

        yield return new WaitForEndOfFrame();

        Texture2D beautifiedImage = CaptureBeautifiedImage(faceController.targetImage);

        // Crop to placeholder dimensions to match final output exactly
        Texture2D finalSavedImage = PhotoShootingManager.Instance.GetCroppedTexture(
            beautifiedImage,
            placeholderWidth,
            placeholderHeight
        );

        if (currentEditingIndex >= 0 && currentEditingIndex < beautifiedImages.Count)
            beautifiedImages[currentEditingIndex] = finalSavedImage;
        else
            beautifiedImages.Add(finalSavedImage);

        Debug.Log($"✅ Beautified image saved at index {currentEditingIndex} with dimensions {placeholderWidth}x{placeholderHeight}. Total: {beautifiedImages.Count}");

        // Notify PhotoShootingManager to move to next shot
        PhotoShootingManager.Instance.OnBeautificationComplete();
    }

    private Texture2D CaptureBeautifiedImage(RawImage rawImage)
    {
        if (rawImage == null || rawImage.texture == null)
            return null;

        RectTransform rt = rawImage.rectTransform;
        int width = (int)rt.rect.width;
        int height = (int)rt.rect.height;

        RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previousRT = RenderTexture.active;

        try
        {
            RenderTexture.active = renderTexture;
            Graphics.Blit(rawImage.texture, renderTexture, rawImage.material);
            Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            return result;
        }
        finally
        {
            RenderTexture.active = previousRT;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }
}