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
    [SerializeField] private Slider smoothenSlider;
    [SerializeField] private Slider eyeEnlargementSlider;

    [SerializeField] private Button doneButton;
    [SerializeField] private Button retakeButton;

    private bool blockCallbacks = false;

    private FaceEffectsController faceController;

    private float currentBrightness;
    private float currentSmoothness;
    private float currentEnlarge;

    private bool isSingleImageMode = false;
    private Texture2D currentEditingImage;

    private int currentEditingIndex = -1;

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
        smoothenSlider.onValueChanged.AddListener(OnSmoothenChanged);
        eyeEnlargementSlider.onValueChanged.AddListener(OnEyeEnlargeChanged);
    }

    private void OnRetakeClicked()
    {
        PhotoShootingManager.Instance?.OnReshotClicked();

        // Remove the last edited image correctly
        if (currentEditingIndex >= 0 && currentEditingIndex < beautifiedImages.Count)
        {
            beautifiedImages.RemoveAt(currentEditingIndex);
        }

        if (PhotoShootingManager.Instance.beautificationPanel != null)
            PhotoShootingManager.Instance.beautificationPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Load image for beautification with placeholder dimensions for accurate preview
    /// </summary>
    public void OnLoadSingleCaptureImage(Texture2D image, int shotIndex, float phWidth, float phHeight)
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
        float baseWidth = 800f;
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

        // Load current effect values
        currentBrightness = faceController.BrightenStrength;
        currentSmoothness = faceController.SmoothingStrength;
        currentEnlarge = faceController.eyeEnlargementStrength;

        blockCallbacks = true;
        brightnessSlider.value = currentBrightness;
        smoothenSlider.value = currentSmoothness;
        eyeEnlargementSlider.value = currentEnlarge;
        blockCallbacks = false;

        if (retakeButton != null)
            retakeButton.gameObject.SetActive(true);

        StaticFaceDetection.Instance.inputImage = currentEditingImage;
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
        faceController.SmoothingStrength = currentSmoothness;
        faceController.UpdateEyeEnlargementStrength(currentEnlarge);
    }

    private void OnDone()
    {
        if (isSingleImageMode)
        {
            StartCoroutine(ProcessAndSaveSingleImage());
        }
    }

    private IEnumerator ProcessAndSaveSingleImage()
    {
        StaticFaceDetection.Instance.inputImage = currentEditingImage;
        yield return StartCoroutine(StaticFaceDetection.Instance.OnDetectImage());

        faceController.BrightenStrength = currentBrightness;
        faceController.SmoothingStrength = currentSmoothness;
        faceController.UpdateEyeEnlargementStrength(currentEnlarge);

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