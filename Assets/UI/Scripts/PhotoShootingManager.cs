using System.Collections;
using System.Collections.Generic;
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

    [Header("Countdown")]
    public TMP_Text timerText;
    public Image flashPanel;

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

        // Set aspect ratio if placeholder exists
        if (currentShotIndex < placeholders.Count)
        {
            var ph = placeholders[currentShotIndex];
            float phWidth = float.Parse(ph.width);
            float phHeight = float.Parse(ph.height);
            float placeholderAspect = phWidth / phHeight;

            SetCameraPreviewAspect(placeholderAspect);
        }

        // ====== NEW COUNTDOWN ======
        timerText.gameObject.SetActive(true);

        for (int i = 3; i > 0; i--)
        {
            timerText.text = i.ToString();
            yield return new WaitForSeconds(1f);
        }

        timerText.text = "";
        timerText.gameObject.SetActive(false);

        // ====== FLASH EFFECT ======
        yield return StartCoroutine(FlashEffect());

        // Capture
        CapturePhoto();
    }

    private IEnumerator FlashEffect()
    {
        if (flashPanel == null)
            yield break;

        flashPanel.gameObject.SetActive(true);

        // fade-in (quick)
        for (float a = 0; a <= 1; a += Time.deltaTime * 8f)
        {
            flashPanel.color = new Color(1, 1, 1, a);
            yield return null;
        }

        // fade-out (slower)
        for (float a = 1; a >= 0; a -= Time.deltaTime * 4f)
        {
            flashPanel.color = new Color(1, 1, 1, a);
            yield return null;
        }

        flashPanel.gameObject.SetActive(false);
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
        // LOG: Photo captured
        LoggingManager.Instance?.LogCustomerClick(
            buttonName: "PhotoCapture",
            screenName: "ShootingManager",
            frameId: currentFrameItem?.frameData.frame_id
        );
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
    // NEW: Print Button Handler
    // ============================================================
    private void OnPrintButtonClicked()
    {
        if (finalComposedImageForPrint == null)
        {
            Debug.LogError("❌ No image to print!");
            return;
        }

        // Debug.Log("🖨️ Print button clicked - sending to PrintingManager");

        // Hide print button after clicking
        if (printButton != null)
            printButton.gameObject.SetActive(false);

        // Send to PrintingManager for printing
        // PrintingManager.Instance.PrintFinalImage(finalComposedImageForPrint);
    }

    // ============================================================
    // NEW: Capture Frame as Texture for Printing
    // ============================================================
  
  



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

        // Convert texture to PNG bytes
        byte[] photoBytes = photoTexture.EncodeToPNG();

        Debug.Log($"📤 Uploading photo to {url}");
        Debug.Log($"   - order_id: '{orderId}' (IsNull: {orderId == null}, IsEmpty: {string.IsNullOrEmpty(orderId)}, Length: {orderId?.Length ?? 0})");
        Debug.Log($"   - frame_id: {frameId}");
        Debug.Log($"   - payment_active: {paymentActive}");
        Debug.Log($"   - photo size: {photoBytes.Length} bytes ({photoTexture.width}x{photoTexture.height})");

        // Create multipart form data using WWWForm
        WWWForm formData = new WWWForm();

        // ALWAYS add order_id field (send empty string if not available)
        string orderIdToSend = string.IsNullOrEmpty(orderId) ? "" : orderId;
        formData.AddField("order_id", orderIdToSend);
        Debug.Log($"   - Adding order_id to form: '{orderIdToSend}' (isEmpty: {string.IsNullOrEmpty(orderIdToSend)})");

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

        // Force UI to update correctly
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

        // Get corners
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        Camera cam = Camera.main;
        if (cam == null)
        {
            Canvas canvas = frameTransform.GetComponentInParent<Canvas>();
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

        // Clamp BEFORE texture creation
        width = Mathf.Clamp(width, 1, Screen.width);
        height = Mathf.Clamp(height, 1, Screen.height);

        x = Mathf.Clamp(x, 0, Screen.width - width);
        y = Mathf.Clamp(y, 0, Screen.height - height);

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);

        try
        {
            tex.ReadPixels(new Rect(x, y, width, height), 0, 0);
            tex.Apply();
            Debug.Log($"✅ Captured {width}x{height}");
            return tex;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Capture failed: {e.Message}");
            Destroy(tex);
            return null;
        }
    }


    // Helper method for debugging (optional)
    private void SaveTextureToFile(Texture2D texture, string filename)
    {
        byte[] bytes = texture.EncodeToPNG();
        string path = System.IO.Path.Combine(Application.persistentDataPath, filename);
        System.IO.File.WriteAllBytes(path, bytes);
        Debug.Log($"💾 Debug: Saved texture to: {path}");
    }

    // Helper method to get full transform path
    private string GetFullPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }

 
    // REPLACE the entire ApplyPhotosWithFrame method with this updated version:

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
        instantiatedFrameObject = frameObj;

        Debug.Log($"✅ Instantiated frame prefab: {frameObj.name}");

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

        // =================================================================
        // UPDATED: Find "frame" container (no (Clone) suffix)
        // =================================================================
        Transform frameContainer = frameObj.transform.Find("frame");

        if (frameContainer == null)
        {
            Debug.LogWarning("⚠️ 'frame' container not found, creating it...");
            GameObject frameGO = new GameObject("frame", typeof(RectTransform));
            frameGO.transform.SetParent(frameObj.transform, false);
            frameContainer = frameGO.transform;

            RectTransform frameRect = frameContainer.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0.5f, 0.5f);
            frameRect.anchorMax = new Vector2(0.5f, 0.5f);
            frameRect.pivot = new Vector2(0.5f, 0.5f);
            frameRect.anchoredPosition = Vector2.zero;

            if (frameTex != null)
            {
                frameRect.sizeDelta = new Vector2(frameTex.width, frameTex.height);
            }
        }
        else
        {
            Debug.Log($"✅ Found frame container at: {GetFullPath(frameContainer)}");

            // Ensure frame has correct size
            RectTransform frameRect = frameContainer.GetComponent<RectTransform>();
            if (frameRect != null && frameTex != null)
            {
                frameRect.sizeDelta = new Vector2(frameTex.width, frameTex.height);
                frameRect.anchoredPosition = Vector2.zero;
                Debug.Log($"✅ Set frame size to: {frameTex.width}x{frameTex.height}");
            }
        }

        // =================================================================
        // Find or create "capturedImages" inside "frame"
        // =================================================================
        Transform capturedImagesParent = frameContainer.Find("capturedImages");

        if (capturedImagesParent == null)
        {
            Debug.LogWarning("⚠️ capturedImages not found inside frame, creating it...");
            GameObject capturedImagesGO = new GameObject("capturedImages", typeof(RectTransform));
            capturedImagesGO.transform.SetParent(frameContainer, false);
            capturedImagesParent = capturedImagesGO.transform;

            // Set capturedImages to match frame size
            RectTransform capturedImagesRect = capturedImagesGO.GetComponent<RectTransform>();
            capturedImagesRect.anchorMin = new Vector2(0.5f, 0.5f);
            capturedImagesRect.anchorMax = new Vector2(0.5f, 0.5f);
            capturedImagesRect.pivot = new Vector2(0.5f, 0.5f);

            if (frameTex != null)
            {
                capturedImagesRect.sizeDelta = new Vector2(frameTex.width, frameTex.height);
                capturedImagesRect.anchoredPosition = Vector2.zero;
                Debug.Log($"✅ Created capturedImages with size: {frameTex.width}x{frameTex.height}");
            }
        }
        else
        {
            Debug.Log($"✅ Found capturedImages at: {GetFullPath(capturedImagesParent)}");

            // Ensure capturedImages has correct size
            RectTransform capturedImagesRect = capturedImagesParent.GetComponent<RectTransform>();
            if (capturedImagesRect != null && frameTex != null)
            {
                capturedImagesRect.sizeDelta = new Vector2(frameTex.width, frameTex.height);
                capturedImagesRect.anchoredPosition = Vector2.zero;
                Debug.Log($"✅ Updated capturedImages size to: {frameTex.width}x{frameTex.height}");
            }
        }

        // =================================================================
        // Add captured photos FIRST to capturedImages (behind frameImg)
        // =================================================================
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
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(ph.x, ph.y);
            rt.localRotation = Quaternion.Euler(0, 0, ph.rotation);

            Debug.Log($"✅ Added CapturedPhoto_{i + 1}: {w}x{h} at ({ph.x}, {ph.y})");
        }

        // =================================================================
        // Add frameImg as LAST child inside capturedImages (in front of photos)
        // =================================================================
        Transform frameImgChild = capturedImagesParent.Find("frameImg");
        if (frameImgChild == null)
        {
            GameObject frameImgGO = new GameObject("frameImg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            frameImgGO.transform.SetParent(capturedImagesParent, false);
            frameImgChild = frameImgGO.transform;
            Debug.Log("✅ Created frameImg child");
        }

        // Set frameImg as last sibling (in front of all photos)
        frameImgChild.SetAsLastSibling();

        if (frameImgChild != null)
        {
            Debug.Log($"✅ Setting up frameImg at: {GetFullPath(frameImgChild)}");

            Image frameImg = frameImgChild.GetComponent<Image>();
            frameImg.sprite = Sprite.Create(frameTex, new Rect(0, 0, frameTex.width, frameTex.height), new Vector2(0.5f, 0.5f));
            frameImg.preserveAspect = false;

            RectTransform rt = frameImg.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(frameTex.width, frameTex.height);
            rt.anchoredPosition = Vector2.zero;
        }

        if (loadingPanel != null)
            loadingPanel.SetActive(false);

        // =================================================================
        // Wait for UI to fully render
        // =================================================================
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();

        // Force canvas update
        Canvas.ForceUpdateCanvases();

        // Wait one more frame after force update
        yield return new WaitForEndOfFrame();

        Debug.Log("🎬 About to capture frame...");

        // =================================================================
        // CAPTURE "capturedImages" - this contains both frame and photos
        // =================================================================
        if (capturedImagesParent == null)
        {
            Debug.LogError("❌ capturedImages is null!");
            yield break;
        }

        Debug.Log($"📍 Capturing from: {GetFullPath(capturedImagesParent)}");
        Debug.Log($"📍 Object active: {capturedImagesParent.gameObject.activeInHierarchy}");
        Debug.Log($"📍 Position: {capturedImagesParent.position}");

        RectTransform capturedRect = capturedImagesParent.GetComponent<RectTransform>();
        if (capturedRect != null)
        {
            Debug.Log($"📍 Size: {capturedRect.sizeDelta}");
        }

        // Capture the capturedImages container (has frame + photos)
        finalComposedImageForPrint = CaptureFrameAsTexture(capturedImagesParent);

        if (finalComposedImageForPrint != null)
        {
            Debug.Log($"✅ Final composed image captured: {finalComposedImageForPrint.width}x{finalComposedImageForPrint.height}");

            // =================================================================
            // Photo upload section
            // =================================================================
            string userId = PlayerPrefs.GetString("user_id", "");
            bool isLoggedIn = !string.IsNullOrEmpty(userId);

            if (isLoggedIn)
            {
                string orderId = PaymentManager.Instance?.currentOrderId ?? "";
                string orderIdForUpload = orderId;
                string frameId = currentFrameItem?.frameData?.frame_id;
                bool paymentActive = PlayerPrefs.GetInt("payments_enabled", 0) == 1;

                if (!string.IsNullOrEmpty(frameId))
                {
                    Debug.Log($"🚀 Initiating photo upload:");
                    Debug.Log($"   - User ID: {userId}");
                    Debug.Log($"   - Order ID: {(string.IsNullOrEmpty(orderIdForUpload) ? "NONE" : orderIdForUpload)}");
                    Debug.Log($"   - Frame ID: {frameId}");
                    Debug.Log($"   - Payment Active: {paymentActive}");

                    yield return StartCoroutine(UploadFinalPhoto(
                        finalComposedImageForPrint,
                        orderIdForUpload,
                        frameId,
                        paymentActive
                    ));

                    if (PaymentManager.Instance != null)
                    {
                        PaymentManager.Instance.currentOrderId = null;
                        Debug.Log("✅ Cleared order_id after photo upload");
                    }
                }
                else
                {
                    Debug.LogWarning($"⚠️ Skipping photo upload - no frame_id available");
                }
            }
            else
            {
                Debug.Log($"ℹ️ Skipping photo upload - user is in GUEST mode");
            }
        }
        else
        {
            Debug.LogError("❌ Failed to capture final image!");
        }
    }

}