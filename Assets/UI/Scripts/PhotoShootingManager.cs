using System.Collections;
using System.Collections.Generic;
using Mediapipe.Unity.Tutorial;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PhotoShootingManager : MonoBehaviour
{
    public static PhotoShootingManager Instance;

    public UiController uiController;

    [Header("Panels")]
    public GameObject photoShootPanel;
    public GameObject beautificationPanel;

    [Header("Countdown")]
    public TMP_Text timerText;

    [Header("Camera & Preview")]
    public RawImage cameraPreview;
    public Image capturePreview;
    public Button reshotButton;

    [Header("Frame Display")]
    public CapturedPhotosDisplayManager displayManager;

    [Header("Printing")]
    public Button printButton;
    public bool autoPrintAfterCapture = false;

    public enum AspectRatio { Ratio16x9, Ratio1x1, Ratio4x5 }
    public AspectRatio selectedAspect = AspectRatio.Ratio1x1;

    private WebCamTexture webCamTexture;
    private FrameItem currentFrameItem;
    private int totalShots;
    private int currentShotIndex = 0;
    public List<Texture2D> capturedPhotos = new List<Texture2D>();
    private List<FrameAsset> placeholders = new List<FrameAsset>();

    [Header("UI References")]
    public GameObject loadingPanel;

    private Texture2D finalComposedImageForPrint;
    private GameObject instantiatedFrameObject;

    private void Awake()
    {
        Instance = this;
        photoShootPanel.SetActive(false);
    }

    private void Update()
    {
        if (webCamTexture != null && webCamTexture.width > 100)
        {
            float phWidth = 800f;
            float phHeight = 800f;

            if (currentShotIndex < placeholders.Count)
            {
                var ph = placeholders[currentShotIndex];
                phWidth = float.Parse(ph.width);
                phHeight = float.Parse(ph.height);
            }

            ApplyCenterCropToRawImageWithPlaceholder(cameraPreview, webCamTexture.width, webCamTexture.height, phWidth, phHeight);
        }
    }

    public void StartShooting(FrameItem selectedFrame)
    {
        if (selectedFrame == null) return;

        currentFrameItem = selectedFrame;
        placeholders.Clear();
        foreach (var asset in selectedFrame.frameData.assets)
            if (asset.type == "placeholder")
                placeholders.Add(asset);

        placeholders.Sort((a, b) => (a.placeholder_index ?? 0).CompareTo(b.placeholder_index ?? 0));
        totalShots = placeholders.Count > 0 ? placeholders.Count : 1;
        currentShotIndex = 0;
        capturedPhotos.Clear();

        photoShootPanel.SetActive(true);

        reshotButton.onClick.RemoveAllListeners();
        reshotButton.onClick.AddListener(OnReshotClicked);

        StartWebcam();
        StartCoroutine(StartCountdownAndCapture());
    }

    private void StartWebcam()
    {
        if (WebCamTexture.devices.Length == 0) return;

        webCamTexture = new WebCamTexture();
        webCamTexture.Play();

        cameraPreview.texture = webCamTexture;
        cameraPreview.gameObject.SetActive(true);

        StartCoroutine(WaitForWebcamAndMatchSize());
    }

    private IEnumerator WaitForWebcamAndMatchSize()
    {
        while (webCamTexture.width < 100) yield return null;
        yield return new WaitForEndOfFrame();
        MatchPreviewSizes();
    }

    private IEnumerator StartCountdownAndCapture()
    {
        capturePreview.gameObject.SetActive(false);
        cameraPreview.gameObject.SetActive(true);

        if (currentShotIndex < placeholders.Count)
        {
            var ph = placeholders[currentShotIndex];
            float phWidth = float.Parse(ph.width);
            float phHeight = float.Parse(ph.height);
            float placeholderAspect = phWidth / phHeight;

            SetCameraPreviewAspect(placeholderAspect);
        }

        for (int i = 3; i > 0; i--)
        {
            timerText.text = i.ToString();
            yield return new WaitForSeconds(1f);
        }

        timerText.text = "笑顔";
        yield return new WaitForSeconds(0.5f);
        CapturePhoto();
    }

    private Texture2D finalCroppedTex;

    private void CapturePhoto()
    {
        if (webCamTexture == null) return;

        int width = webCamTexture.width;
        int height = webCamTexture.height;

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.SetPixels(webCamTexture.GetPixels());
        tex.Apply();

        float phWidth = 800f;
        float phHeight = 800f;

        if (currentShotIndex < placeholders.Count)
        {
            var ph = placeholders[currentShotIndex];
            phWidth = float.Parse(ph.width);
            phHeight = float.Parse(ph.height);
        }

        finalCroppedTex = GetCroppedTexture(tex, phWidth, phHeight);
        capturedPhotos.Add(finalCroppedTex);

        capturePreview.sprite = Sprite.Create(finalCroppedTex, new Rect(0, 0, finalCroppedTex.width, finalCroppedTex.height), new Vector2(0.5f, 0.5f));
        capturePreview.preserveAspect = false;

        MatchPreviewSizes();

        cameraPreview.gameObject.SetActive(false);
        capturePreview.gameObject.SetActive(true);

        OpenBeautificationForImage(finalCroppedTex);
    }

    public void OpenBeautificationForImage(Texture2D clickedImage)
    {
        if (clickedImage == null) return;

        float phWidth = 800f;
        float phHeight = 800f;

        if (currentShotIndex < placeholders.Count)
        {
            var ph = placeholders[currentShotIndex];
            phWidth = float.Parse(ph.width);
            phHeight = float.Parse(ph.height);
        }

        beautificationPanel.SetActive(true);
        uiController.OnLoadSingleCaptureImage(clickedImage, currentShotIndex, phWidth, phHeight);
    }

    public void OnBeautificationComplete()
    {
        beautificationPanel.SetActive(false);
        currentShotIndex++;

        if (currentShotIndex < totalShots)
        {
            cameraPreview.gameObject.SetActive(true);
            StartCoroutine(StartCountdownAndCapture());
        }
        else
        {
            FinishShooting();
        }
    }

    public void FinishShooting()
    {
        if (webCamTexture != null && webCamTexture.isPlaying)
        {
            webCamTexture.Stop();
            Destroy(webCamTexture);
            webCamTexture = null;
        }

        if (currentFrameItem != null)
            StartCoroutine(ApplyPhotosWithFrame());

        photoShootPanel.SetActive(false);
        Debug.Log("📸 Photo shooting finished!");
    }

    private void MatchPreviewSizes()
    {
        if (cameraPreview == null || capturePreview == null) return;

        RectTransform camRect = cameraPreview.rectTransform;
        RectTransform capRect = capturePreview.rectTransform;

        capRect.anchorMin = camRect.anchorMin;
        capRect.anchorMax = camRect.anchorMax;
        capRect.pivot = camRect.pivot;
        capRect.anchoredPosition = camRect.anchoredPosition;

        capRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, camRect.rect.width);
        capRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, camRect.rect.height);
    }

    public void OnReshotClicked()
    {
        if (capturedPhotos.Count > 0)
            capturedPhotos.RemoveAt(capturedPhotos.Count - 1);

        capturePreview.gameObject.SetActive(false);
        reshotButton.gameObject.SetActive(false);
        cameraPreview.gameObject.SetActive(true);

        StartCoroutine(StartCountdownAndCapture());
    }

    // ============================================================
    // MODIFIED: ApplyPhotosWithFrame - Now with Printing Integration
    // ============================================================
    private IEnumerator ApplyPhotosWithFrame()
    {
        if (loadingPanel != null)
            loadingPanel.SetActive(true);

        Transform frameParent = displayManager.frameDisplayParent;
        if (frameParent == null)
        {
            Debug.LogWarning("Frame parent not assigned!");
            yield break;
        }

        foreach (Transform child in frameParent)
            Destroy(child.gameObject);

        if (displayManager.frameDisplayPrefab == null)
        {
            Debug.LogError("Frame prefab missing!");
            yield break;
        }

        GameObject frameObj = Instantiate(displayManager.frameDisplayPrefab, frameParent);
        frameObj.SetActive(true);
        instantiatedFrameObject = frameObj; // Store reference for printing

        Texture2D frameTex = null;
        string frameURL = currentFrameItem.frameData.asset_path;

        if (!string.IsNullOrEmpty(frameURL))
        {
            yield return FrameCacheManager.DownloadAndCacheTexture(frameURL,
                tex => frameTex = tex
            );
        }

        if (frameTex == null)
            frameTex = currentFrameItem.offlineTexture ?? Texture2D.grayTexture;

        Transform frameImgChild = frameObj.transform.Find("frameImg");
        if (frameImgChild != null)
        {
            Image frameImg = frameImgChild.GetComponent<Image>();
            frameImg.sprite = Sprite.Create(frameTex, new Rect(0, 0, frameTex.width, frameTex.height), new Vector2(0.5f, 0.5f));
            frameImg.preserveAspect = false;

            RectTransform rt = frameImg.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(frameTex.width, frameTex.height);
            rt.anchoredPosition = Vector2.zero;
        }

        Transform capturedImagesParent = frameObj.transform.Find("capturedImages");
        if (capturedImagesParent == null)
        {
            GameObject fallback = new GameObject("capturedImages", typeof(RectTransform));
            fallback.transform.SetParent(frameObj.transform, false);
            capturedImagesParent = fallback.transform;
        }

        for (int i = 0; i < UiController.Instance.beautifiedImages.Count && i < placeholders.Count; i++)
        {
            var tex = UiController.Instance.beautifiedImages[i];
            var ph = placeholders[i];

            GameObject imgObj = new GameObject("CapturedPhoto_" + (i + 1), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imgObj.transform.SetParent(capturedImagesParent, false);

            Image img = imgObj.GetComponent<Image>();

            float w = float.Parse(ph.width);
            float h = float.Parse(ph.height);

            img.sprite = CreateCenterCroppedSprite(tex, w, h);
            img.preserveAspect = false;

            RectTransform rt = imgObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(ph.x, ph.y);
            rt.localRotation = Quaternion.Euler(0, 0, ph.rotation);
        }

        if (loadingPanel != null)
            loadingPanel.SetActive(false);

        // ============================================================
        // NEW: PRINTING INTEGRATION
        // ============================================================

        // Wait for UI to fully render
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame(); // Extra frame for safety

        // Capture the final composed frame as texture
        finalComposedImageForPrint = CaptureFrameAsTexture(frameObj.transform);

        if (finalComposedImageForPrint != null)
        {
            Debug.Log($"✅ Final composed image captured for printing: {finalComposedImageForPrint.width}x{finalComposedImageForPrint.height}");

            // Auto-print or show print button
            if (autoPrintAfterCapture && PrintingManager.Instance != null)
            {
                // Auto print immediately
                PrintingManager.Instance.PrintFinalImage(finalComposedImageForPrint);
            }
            else if (printButton != null && PrintingManager.Instance != null)
            {
                // Show print button
                printButton.gameObject.SetActive(true);
                printButton.onClick.RemoveAllListeners();
                printButton.onClick.AddListener(OnPrintButtonClicked);

                Debug.Log("🖨️ Print button activated");
            }
        }
        else
        {
            Debug.LogError("❌ Failed to capture final image for printing!");
        }
    }

    // ============================================================
    // NEW: Print Button Handler
    // ============================================================
    private void OnPrintButtonClicked()
    {
        if (finalComposedImageForPrint == null)
        {
            Debug.LogError("❌ No image to print!");
            return;
        }

        if (PrintingManager.Instance == null)
        {
            Debug.LogError("❌ PrintingManager not found in scene!");
            return;
        }

        Debug.Log("🖨️ Print button clicked - sending to PrintingManager");

        // Hide print button after clicking
        if (printButton != null)
            printButton.gameObject.SetActive(false);

        // Send to PrintingManager for printing
        PrintingManager.Instance.PrintFinalImage(finalComposedImageForPrint);
    }

    // ============================================================
    // NEW: Capture Frame as Texture for Printing
    // ============================================================
    private Texture2D CaptureFrameAsTexture(Transform frameTransform)
    {
        if (frameTransform == null)
        {
            Debug.LogError("❌ Frame transform is null!");
            return null;
        }

        RectTransform rectTransform = frameTransform.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            Debug.LogError("❌ RectTransform not found!");
            return null;
        }

        // Get frame dimensions
        float frameWidth = rectTransform.rect.width;
        float frameHeight = rectTransform.rect.height;

        Debug.Log($"📸 Capturing frame: {frameWidth}x{frameHeight}");

        // Create render texture with higher resolution for printing
        int renderWidth = Mathf.RoundToInt(frameWidth);
        int renderHeight = Mathf.RoundToInt(frameHeight);

        // Ensure minimum print quality
        if (renderWidth < 1200)
        {
            float scale = 1200f / renderWidth;
            renderWidth = 1200;
            renderHeight = Mathf.RoundToInt(renderHeight * scale);
        }

        RenderTexture renderTexture = new RenderTexture(renderWidth, renderHeight, 24);

        // Create temporary camera
        GameObject tempCamObj = new GameObject("TempPrintCamera");
        Camera printCamera = tempCamObj.AddComponent<Camera>();

        printCamera.targetTexture = renderTexture;
        printCamera.orthographic = true;
        printCamera.clearFlags = CameraClearFlags.SolidColor;
        printCamera.backgroundColor = Color.white;

        // Calculate orthographic size to fit the frame
        printCamera.orthographicSize = frameHeight / 2f;

        // Position camera in front of frame
        Vector3 frameWorldPos = frameTransform.position;
        tempCamObj.transform.position = new Vector3(frameWorldPos.x, frameWorldPos.y, frameWorldPos.z - 100);
        tempCamObj.transform.LookAt(frameTransform);

        // Set camera to only render UI layer
        printCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");

        // Render
        printCamera.Render();

        // Read pixels
        RenderTexture.active = renderTexture;
        Texture2D capturedTexture = new Texture2D(renderWidth, renderHeight, TextureFormat.RGB24, false);
        capturedTexture.ReadPixels(new Rect(0, 0, renderWidth, renderHeight), 0, 0);
        capturedTexture.Apply();

        // Cleanup
        printCamera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(renderTexture);
        Destroy(tempCamObj);

        Debug.Log($"✅ Frame captured for printing: {capturedTexture.width}x{capturedTexture.height}");
        return capturedTexture;
    }

    // ============================================================
    // ALTERNATIVE: Simple Screenshot Method (Backup)
    // ============================================================
    private Texture2D CaptureFrameAsTexture_Screenshot()
    {
        // Use Unity's built-in screenshot (simpler but captures whole screen)
        Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();

        // Optionally crop to just the frame area
        // For now, just return the screenshot
        return screenshot;
    }

    // -------------------------------
    // CROPPING HELPERS
    // -------------------------------

    private void SetCameraPreviewAspect(float targetAspect)
    {
        RectTransform camRect = cameraPreview.rectTransform;
        RectTransform capRect = capturePreview.rectTransform;

        float baseSize = 800f;
        float width, height;

        if (currentShotIndex < placeholders.Count)
        {
            var ph = placeholders[currentShotIndex];
            float phWidth = float.Parse(ph.width);
            float phHeight = float.Parse(ph.height);

            float aspect = phWidth / phHeight;

            if (aspect >= 1f)
            {
                width = baseSize;
                height = baseSize / aspect;
            }
            else
            {
                height = baseSize;
                width = baseSize * aspect;
            }

            Debug.Log($"📷 Setting preview to {width}x{height} to match placeholder {phWidth}x{phHeight} (aspect: {aspect:F2})");
        }
        else
        {
            width = height = baseSize;
        }

        camRect.sizeDelta = new Vector2(width, height);
        capRect.sizeDelta = new Vector2(width, height);
    }

    private void ApplyCenterCropToRawImageWithPlaceholder(RawImage raw, int texW, int texH, float phWidth, float phHeight)
    {
        if (raw == null || texW <= 0 || texH <= 0) return;

        float targetAspect = phWidth / phHeight;
        float texAspect = (float)texW / texH;

        if (texAspect > targetAspect)
        {
            float scale = targetAspect / texAspect;
            raw.uvRect = new Rect((1f - scale) / 2f, 0f, scale, 1f);
        }
        else
        {
            float scale = texAspect / targetAspect;
            raw.uvRect = new Rect(0f, (1f - scale) / 2f, 1f, scale);
        }
    }

    private void ApplyCenterCropToRawImage(RawImage raw, int texW, int texH)
    {
        if (raw == null || texW <= 0 || texH <= 0) return;

        RectTransform rt = raw.rectTransform;
        float texAspect = (float)texW / texH;
        float uiAspect = rt.rect.width / rt.rect.height;

        if (texAspect > uiAspect)
        {
            float scale = uiAspect / texAspect;
            raw.uvRect = new Rect((1f - scale) / 2f, 0f, scale, 1f);
        }
        else
        {
            float scale = texAspect / uiAspect;
            raw.uvRect = new Rect(0f, (1f - scale) / 2f, 1f, scale);
        }
    }

    private Sprite CreateCenterCroppedSprite(Texture2D texture, float targetWidth, float targetHeight)
    {
        float imgAspect = (float)texture.width / texture.height;
        float targetAspect = targetWidth / targetHeight;

        int cropWidth = texture.width;
        int cropHeight = texture.height;

        if (imgAspect > targetAspect)
        {
            cropWidth = Mathf.RoundToInt(texture.height * targetAspect);
        }
        else
        {
            cropHeight = Mathf.RoundToInt(texture.width / targetAspect);
        }

        int x = (texture.width - cropWidth) / 2;
        int y = (texture.height - cropHeight) / 2;

        Color[] pixels = texture.GetPixels(x, y, cropWidth, cropHeight);
        Texture2D croppedTex = new Texture2D(cropWidth, cropHeight);
        croppedTex.SetPixels(pixels);
        croppedTex.Apply();

        return Sprite.Create(croppedTex, new Rect(0, 0, cropWidth, cropHeight), new Vector2(0.5f, 0.5f));
    }

    public Texture2D GetCroppedTexture(Texture2D texture, float targetWidth, float targetHeight)
    {
        float imgAspect = (float)texture.width / texture.height;
        float targetAspect = targetWidth / targetHeight;

        int cropWidth = texture.width;
        int cropHeight = texture.height;

        if (imgAspect > targetAspect)
            cropWidth = Mathf.RoundToInt(texture.height * targetAspect);
        else
            cropHeight = Mathf.RoundToInt(texture.width / targetAspect);

        int x = (texture.width - cropWidth) / 2;
        int y = (texture.height - cropHeight) / 2;

        Color[] pixels = texture.GetPixels(x, y, cropWidth, cropHeight);
        Texture2D croppedTex = new Texture2D(cropWidth, cropHeight);
        croppedTex.SetPixels(pixels);
        croppedTex.Apply();
        return croppedTex;
    }
}