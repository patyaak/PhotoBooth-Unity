using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mediapipe.Unity.Tutorial;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class PhotoShootingManager : MonoBehaviour
{
    public static PhotoShootingManager Instance;

    public UiController uiController;

    [Header("API")]
    public string apiBaseURL = "http://photo-stg-api.chvps3.aozora-okinawa.com/";

    [Header("Panels")]
    public GameObject photoShootPanel;
    public GameObject beautificationPanel;
    public GameObject photoPreviewPanel;

    [Header("Countdown")]
    public TMP_Text timerText;
    public Image flashPanel;

    [Header("Camera & Preview")]
    public RawImage cameraPreview;
    public Image capturePreview;
    public Button reshotButton;

    [Header("Frame Display")]
    public CapturedPhotosDisplayManager displayManager;

    [Header("Preview Display")]
    public Image finalPhotoPreview;

    [Header("Printing")]
    public Button printButton;
    public bool autoPrintAfterCapture = true;

    [Header("UI References")]
    public GameObject loadingPanel;

    [Header("Preview Settings")]
    public float previewDurationSeconds = 2f;

    public enum AspectRatio { Ratio16x9, Ratio1x1, Ratio4x5 }
    public AspectRatio selectedAspect = AspectRatio.Ratio1x1;

    private WebCamTexture webCamTexture;
    private FrameItem currentFrameItem;
    private int totalShots;
    private int currentShotIndex = 0;
    public List<Texture2D> capturedPhotos = new List<Texture2D>();
    private List<FrameAsset> placeholders = new List<FrameAsset>();

    private Dictionary<int, Texture2D> photoByIndex = new Dictionary<int, Texture2D>();
    private List<int> uniqueIndices = new List<int>();

    private Texture2D finalComposedImageForPrint;
    private GameObject instantiatedFrameObject;


    private void Start()
    {
        autoPrintAfterCapture = true;

        if (photoPreviewPanel != null)
            photoPreviewPanel.SetActive(false);

        Debug.Log($"🖨️ Auto-print is: {(autoPrintAfterCapture ? "ENABLED ✅" : "DISABLED ⏸️")}");
    }

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

    public void StartShooting(FrameItem selectedFrame, string orderID = null)
    {
        if (selectedFrame == null) return;

        currentFrameItem = selectedFrame;
        placeholders.Clear();
        photoByIndex.Clear();
        uniqueIndices.Clear();

        foreach (var asset in selectedFrame.frameData.assets)
        {
            if (asset.type == "placeholder" && asset.placeholder_index > 0)
            {
                placeholders.Add(asset);
            }
        }

        if (placeholders.Count == 0)
        {
            Debug.LogError("No placeholders found! Check if placeholder_index is being deserialized correctly.");
            return;
        }

        uniqueIndices = placeholders
            .Select(p => p.placeholder_index)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        Debug.Log($"Found {placeholders.Count} placeholders with {uniqueIndices.Count} unique indices: [{string.Join(", ", uniqueIndices)}]");

        currentShotIndex = 0;
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

        int currentIndex = uniqueIndices[currentShotIndex];
        var repPlaceholder = placeholders.FirstOrDefault(p => p.placeholder_index == currentIndex);
        if (repPlaceholder == null)
        {
            Debug.LogError($"No placeholder found with index {currentIndex}");
            yield break;
        }

        float phWidth = float.Parse(repPlaceholder.width);
        float phHeight = float.Parse(repPlaceholder.height);
        float aspect = phWidth / phHeight;

        SetCameraPreviewAspect(aspect);

        timerText.gameObject.SetActive(true);
        for (int i = 3; i > 0; i--)
        {
            timerText.text = i.ToString();
            yield return new WaitForSeconds(1f);
        }
        timerText.gameObject.SetActive(false);

        yield return StartCoroutine(FlashEffect());

        CapturePhoto(currentIndex, phWidth, phHeight);
    }

    private IEnumerator FlashEffect()
    {
        if (flashPanel == null)
            yield break;

        flashPanel.gameObject.SetActive(true);

        for (float a = 0; a <= 1; a += Time.deltaTime * 8f)
        {
            flashPanel.color = new Color(1, 1, 1, a);
            yield return null;
        }

        for (float a = 1; a >= 0; a -= Time.deltaTime * 4f)
        {
            flashPanel.color = new Color(1, 1, 1, a);
            yield return null;
        }

        flashPanel.gameObject.SetActive(false);
    }

    private Texture2D finalCroppedTex;

    private void CapturePhoto(int placeholderIndex, float targetWidth, float targetHeight)
    {
        if (webCamTexture == null) return;

        Texture2D raw = new Texture2D(webCamTexture.width, webCamTexture.height, TextureFormat.RGB24, false);
        raw.SetPixels(webCamTexture.GetPixels());
        raw.Apply();

        Texture2D cropped = GetCroppedTexture(raw, targetWidth, targetHeight);
        Destroy(raw);

        capturePreview.sprite = Sprite.Create(cropped, new Rect(0, 0, cropped.width, cropped.height), Vector2.one * 0.5f);
        capturePreview.preserveAspect = false;
        capturePreview.gameObject.SetActive(true);
        cameraPreview.gameObject.SetActive(false);

        MatchPreviewSizes();

        photoByIndex[placeholderIndex] = cropped;

        OpenBeautificationForImage(cropped, placeholderIndex, targetWidth, targetHeight);
    }

    public void OpenBeautificationForImage(Texture2D image, int placeholderIndex, float w, float h)
    {
        beautificationPanel.SetActive(true);
        uiController.OnLoadSingleCaptureImage(image, placeholderIndex, w, h);
    }

    public void OnBeautificationComplete()
    {
        beautificationPanel.SetActive(false);

        int currentIndex = uniqueIndices[currentShotIndex];
        if (UiController.Instance.beautifiedImages.Count > currentShotIndex)
        {
            photoByIndex[currentIndex] = UiController.Instance.beautifiedImages[currentShotIndex];
        }

        currentShotIndex++;

        if (currentShotIndex < uniqueIndices.Count)
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

        StartCoroutine(ApplyPhotosWithFrame());
        photoShootPanel.SetActive(false);
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

    private IEnumerator UploadFinalPhoto(Texture2D photoTexture, string orderId, string frameId, bool paymentActive)
    {
        if (photoTexture == null)
        {
            Debug.LogError("❌ No photo texture to upload!");
            yield break;
        }

        if (string.IsNullOrEmpty(frameId))
        {
            Debug.LogError("❌ No frame_id available!");
            yield break;
        }

        string url = $"{apiBaseURL}api/order/upload-photo";

        byte[] photoBytes = photoTexture.EncodeToPNG();

        Debug.Log($"📤 Uploading photo to {url}");
        Debug.Log($"   - order_id: '{orderId}' (Length: {orderId?.Length ?? 0})");
        Debug.Log($"   - frame_id: {frameId}");
        Debug.Log($"   - payment_active: {paymentActive}");
        Debug.Log($"   - photo size: {photoBytes.Length} bytes ({photoTexture.width}x{photoTexture.height})");

        WWWForm formData = new WWWForm();

        string orderIdToSend = string.IsNullOrEmpty(orderId) ? "" : orderId;
        formData.AddField("order_id", orderIdToSend);
        formData.AddField("frame_id", frameId);
        formData.AddField("payment_active", paymentActive.ToString().ToLower());
        formData.AddBinaryData("photo", photoBytes, "photo.png", "image/png");

        using (UnityWebRequest request = UnityWebRequest.Post(url, formData))
        {
            request.timeout = 30;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("✅ Photo uploaded successfully!");
                Debug.Log($"Response: {request.downloadHandler.text}");

                LoggingManager.Instance?.LogCustomerClick(
                    buttonName: "PhotoUploadSuccess",
                    screenName: "ShootingManager",
                    frameId: frameId
                );
            }
            else
            {
                Debug.LogError($"❌ Photo upload failed: {request.error}");
                Debug.LogError($"Response Code: {request.responseCode}");
                if (!string.IsNullOrEmpty(request.downloadHandler?.text))
                {
                    Debug.LogError($"Response Body: {request.downloadHandler.text}");
                }

                LoggingManager.Instance?.LogCustomerClick(
                    buttonName: "PhotoUploadFailed",
                    screenName: "ShootingManager",
                    frameId: frameId
                );
            }
        }
    }

    private void SaveTextureToFile(Texture2D texture, string filename)
    {
        byte[] bytes = texture.EncodeToPNG();
        string path = System.IO.Path.Combine(Application.persistentDataPath, filename);
        System.IO.File.WriteAllBytes(path, bytes);
        Debug.Log($"💾 Debug: Saved texture to: {path}");
    }

    // ============================================================
    // UPDATED MAIN COMPOSITION WITH DISPLAY → PRINT → RESET FLOW
    // ============================================================
    private IEnumerator ApplyPhotosWithFrame()
    {
        // Show loading at the beginning
        if (loadingPanel != null) loadingPanel.SetActive(true);

        Transform frameParent = displayManager.frameDisplayParent;
        if (frameParent == null)
        {
            Debug.LogError("Frame parent not assigned!");
            if (loadingPanel != null) loadingPanel.SetActive(false);
            yield break;
        }

        // Clear previous frame
        foreach (Transform child in frameParent)
            Destroy(child.gameObject);

        if (displayManager.frameDisplayPrefab == null)
        {
            Debug.LogError("Frame prefab missing!");
            if (loadingPanel != null) loadingPanel.SetActive(false);
            yield break;
        }

        GameObject frameObj = Instantiate(displayManager.frameDisplayPrefab, frameParent);
        frameObj.SetActive(true);
        instantiatedFrameObject = frameObj;

        // Load frame texture
        Texture2D frameTex = null;
        string frameURL = PhotoBoothFrameManager.Instance.ResolveUrl(currentFrameItem.frameData.asset_path);
        if (!string.IsNullOrEmpty(frameURL))
        {
            if (PhotoBoothFrameManager.Instance.assetCache.TryGetValue(frameURL, out var cached))
                frameTex = cached;
            else
                yield return FrameCacheManager.DownloadAndCacheTexture(frameURL, tex => frameTex = tex);
        }
        if (frameTex == null) frameTex = Texture2D.grayTexture;

        // Determine frame orientation
        string frameType = DetermineFrameOrientation(frameTex);
        Debug.Log($"🖼️ Frame orientation: {frameType} ({frameTex.width}x{frameTex.height})");

        // Setup container hierarchy
        Transform frameContainer = frameObj.transform.Find("frame");
        if (frameContainer == null)
        {
            GameObject go = new GameObject("frame", typeof(RectTransform));
            go.transform.SetParent(frameObj.transform, false);
            frameContainer = go.transform;
            frameContainer.GetComponent<RectTransform>().sizeDelta = new Vector2(frameTex.width, frameTex.height);
        }

        Transform capturedImagesParent = frameContainer.Find("capturedImages");
        if (capturedImagesParent == null)
        {
            GameObject go = new GameObject("capturedImages", typeof(RectTransform));
            go.transform.SetParent(frameContainer, false);
            capturedImagesParent = go.transform;
            var rt = capturedImagesParent.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(frameTex.width, frameTex.height);
        }

        // Map beautified images correctly using uniqueIndices
        var photoByIndexLocal = new Dictionary<int, Texture2D>();
        for (int i = 0; i < UiController.Instance.beautifiedImages.Count; i++)
        {
            int index = uniqueIndices[i];
            photoByIndexLocal[index] = UiController.Instance.beautifiedImages[i];
        }

        // Place all photos (including duplicates)
        foreach (var ph in placeholders)
        {
            if (ph.placeholder_index <= 0) continue;
            if (!photoByIndexLocal.TryGetValue(ph.placeholder_index, out Texture2D photo)) continue;

            float w = float.Parse(ph.width);
            float h = float.Parse(ph.height);

            GameObject imgObj = new GameObject($"Photo_Index{ph.placeholder_index}", typeof(Image));
            imgObj.transform.SetParent(capturedImagesParent, false);

            Image img = imgObj.GetComponent<Image>();
            img.sprite = Sprite.Create(photo, new Rect(0, 0, photo.width, photo.height), Vector2.one * 0.5f);
            img.preserveAspect = false;

            RectTransform rt = imgObj.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(ph.x, ph.y);
            rt.localRotation = Quaternion.Euler(0, 0, ph.rotation);
        }

        // Add frame image on top
        Transform frameImgChild = capturedImagesParent.Find("frameImg");
        if (frameImgChild == null)
        {
            GameObject go = new GameObject("frameImg", typeof(Image));
            go.transform.SetParent(capturedImagesParent, false);
            frameImgChild = go.transform;
        }
        frameImgChild.SetAsLastSibling();

        Image frameImage = frameImgChild.GetComponent<Image>();
        frameImage.sprite = Sprite.Create(frameTex, new Rect(0, 0, frameTex.width, frameTex.height), Vector2.one * 0.5f);
        frameImage.preserveAspect = false;

        RectTransform frt = frameImgChild.GetComponent<RectTransform>();
        frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0.5f);
        frt.pivot = new Vector2(0.5f, 0.5f);
        frt.anchoredPosition = Vector2.zero;
        frt.sizeDelta = new Vector2(frameTex.width, frameTex.height);

        // === FINAL RENDER & CAPTURE ===
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();

        // Hide loading panel BEFORE capture
        if (loadingPanel != null) loadingPanel.SetActive(false);

        yield return new WaitForEndOfFrame();

        // Capture the final composed image
        finalComposedImageForPrint = CaptureTransformContent(capturedImagesParent);

#if UNITY_EDITOR
        if (finalComposedImageForPrint != null)
            SaveTextureToFile(finalComposedImageForPrint, "DEBUG_FINAL_PHOTO_WITH_FRAME.png");
#endif

        // STEP 1: SHOW FRAME DISPLAY FOR 5 SECONDS
        Debug.Log($"🖼️ Displaying frame with photos for {previewDurationSeconds} seconds...");

        // Keep the instantiated frame visible
        if (instantiatedFrameObject != null)
            instantiatedFrameObject.SetActive(true);

        // Wait for preview duration
        yield return new WaitForSeconds(previewDurationSeconds);

        // STEP 2: START PRINTING PROCESS
        if (finalComposedImageForPrint != null && autoPrintAfterCapture)
        {
            Debug.Log($"🖨️ Starting print process for {frameType} frame...");

            if (PrintingManager.Instance != null)
            {
                // This will handle the printing panel states internally
                PrintingManager.Instance.PrintFinalImage(finalComposedImageForPrint, frameType);

                // Wait for printing to complete
                yield return new WaitUntil(() => PrintingManager.Instance.IsPrintingComplete());

                Debug.Log("✅ Printing workflow completed!");
            }
            else
            {
                Debug.LogError("❌ PrintingManager.Instance is null! Cannot auto-print.");
            }
        }

        // Show loading during upload
        if (loadingPanel != null) loadingPanel.SetActive(true);

        // Upload the image
        string userId = PlayerPrefs.GetString("user_id", "");
        if (!string.IsNullOrEmpty(userId))
        {
            string orderId = PaymentManager.Instance?.currentOrderId ?? "";
            string frameId = currentFrameItem.frameData.frame_id;
            bool paymentActive = PlayerPrefs.GetInt("payments_enabled", 0) == 1;

            yield return StartCoroutine(UploadFinalPhoto(finalComposedImageForPrint, orderId, frameId, paymentActive));

            if (PaymentManager.Instance != null)
                PaymentManager.Instance.currentOrderId = null;
        }

        // Hide loading
        if (loadingPanel != null) loadingPanel.SetActive(false);

        Debug.Log($"✅ Complete workflow finished!");

        // STEP 3: RETURN TO LOGIN SCREEN
        yield return new WaitForSeconds(0.5f);
        ResetToLoginScreen();
    }

    /// <summary>
    /// Determines if frame is portrait or landscape based on JSON type or dimensions
    /// </summary>
    private string DetermineFrameOrientation(Texture2D frameTexture)
    {
        if (frameTexture == null)
            return "portrait";

        // Check from JSON if available
        if (currentFrameItem?.frameData != null)
        {
            string type = currentFrameItem.frameData.type;
            if (!string.IsNullOrEmpty(type))
            {
                Debug.Log($"📋 Using frame type from JSON: {type}");
                return type.ToLower();
            }
        }

        // Fallback: analyze dimensions
        float aspectRatio = (float)frameTexture.width / frameTexture.height;

        if (aspectRatio > 1.1f)
        {
            Debug.Log($"📐 Detected LANDSCAPE from aspect ratio: {aspectRatio:F2}");
            return "landscape";
        }
        else
        {
            Debug.Log($"📐 Detected PORTRAIT from aspect ratio: {aspectRatio:F2}");
            return "portrait";
        }
    }

    /// <summary>
    /// Captures ONLY the content of capturedImages transform
    /// </summary>
    private Texture2D CaptureTransformContent(Transform target)
    {
        if (target == null)
        {
            Debug.LogError("❌ Target transform is null!");
            return null;
        }

        RectTransform rectTransform = target.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            Debug.LogError("❌ RectTransform not found!");
            return null;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        Camera cam = Camera.main;
        if (cam == null)
        {
            Canvas canvas = target.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.worldCamera != null)
                cam = canvas.worldCamera;
        }

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for (int i = 0; i < 4; i++)
        {
            Vector3 screenPoint = cam ? cam.WorldToScreenPoint(corners[i]) : corners[i];
            minX = Mathf.Min(minX, screenPoint.x);
            minY = Mathf.Min(minY, screenPoint.y);
            maxX = Mathf.Max(maxX, screenPoint.x);
            maxY = Mathf.Max(maxY, screenPoint.y);
        }

        int x = Mathf.RoundToInt(minX);
        int y = Mathf.RoundToInt(minY);
        int width = Mathf.RoundToInt(maxX - minX);
        int height = Mathf.RoundToInt(maxY - minY);

        width = Mathf.Clamp(width, 1, Screen.width);
        height = Mathf.Clamp(height, 1, Screen.height);
        x = Mathf.Clamp(x, 0, Screen.width - width);
        y = Mathf.Clamp(y, 0, Screen.height - height);

        Debug.Log($"📸 Capturing capturedImages: {width}x{height}px at screen position ({x},{y})");

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);

        try
        {
            tex.ReadPixels(new Rect(x, y, width, height), 0, 0);
            tex.Apply();
            Debug.Log($"✅ Successfully captured frame content: {width}x{height}");
            return tex;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Capture failed: {e.Message}");
            Destroy(tex);
            return null;
        }
    }

    /// <summary>
    /// Resets the entire system to login screen for next customer
    /// </summary>
    private void ResetToLoginScreen()
    {
        Debug.Log("🔄 Resetting to login screen for next customer...");

        // Clear all captured data
        capturedPhotos.Clear();
        photoByIndex.Clear();
        uniqueIndices.Clear();
        placeholders.Clear();
        currentShotIndex = 0;
        currentFrameItem = null;

        // Clear beautified images
        if (UiController.Instance != null)
            UiController.Instance.beautifiedImages.Clear();

        // Destroy instantiated frame object
        if (instantiatedFrameObject != null)
            Destroy(instantiatedFrameObject);

        // Clean up textures
        if (finalComposedImageForPrint != null)
        {
            Destroy(finalComposedImageForPrint);
            finalComposedImageForPrint = null;
        }

        // Clear user session
        PlayerPrefs.DeleteKey("user_id");
        PlayerPrefs.DeleteKey("user_name");
        PlayerPrefs.DeleteKey("user_email");
        PlayerPrefs.DeleteKey("session_id");
        PlayerPrefs.Save();

        // Close all panels except QR/Login
        if (photoShootPanel != null) photoShootPanel.SetActive(false);
        if (beautificationPanel != null) beautificationPanel.SetActive(false);
        if (photoPreviewPanel != null) photoPreviewPanel.SetActive(false);
        if (loadingPanel != null) loadingPanel.SetActive(false);

        // Activate login panel through LoginManager
        if (LoginManager.Instance != null)
        {
            LoginManager.Instance.ResetToLoginPanel();
        }

        Debug.Log("✅ System reset complete - Ready for next customer!");
    }
}