using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

    [Header("Panels")]
    public GameObject photoShootPanel;
    public GameObject beautificationPanel;
    public GameObject editPanel; // NEW
    public GameObject photoPreviewPanel;
    
    [Header("Countdown")]
    // public TMP_Text timerText; // Replaced
    public GameObject timerRoot;
    public GameObject count3;
    public GameObject count2;
    public GameObject count1;
    public Image flashPanel;

    [Header("Camera & Preview")]
    public RawImage cameraPreview;
    public Image capturePreview;
    public Button reshotButton;

    [Header("Frame Display")]
    public CapturedPhotosDisplayManager displayManager;

    [Header("Preview Display")]
    public Image finalPhotoPreview;

    [Header("Webcam Selection")]
    public TMP_Dropdown webcamDropdown;
    private string currentWebcamName;

    [Header("Printing")]
    public Button printButton;
    public bool autoPrintAfterCapture = true;
    public GameObject printingPanel;
    public GameObject printingInProgress;
    public GameObject printingDone;

    [Header("UI References")]
    public GameObject loadingPanel;
    public TMP_Text remainingShotCount;
    private int totalAllowedShots;

    [Header("Preview Settings")]
    public float previewDurationSeconds = 2f;

    public enum AspectRatio { Ratio16x9, Ratio1x1, Ratio4x5 }
    public AspectRatio selectedAspect = AspectRatio.Ratio1x1;

    [Header("Retake Logic")]
    private int currentRetakeCount = 0;
    public int maxRetakes = 2; // Default 2 retakes per shot

    private WebCamTexture webCamTexture;
    private FrameItem currentFrameItem;
    private int totalShots;
    private int currentShotIndex = 0;
    public List<Texture2D> capturedPhotos = new List<Texture2D>();
    private List<FrameAsset> placeholders = new List<FrameAsset>();
    private List<Vector2> cachedPlaceholderSizes = new List<Vector2>();

    private Dictionary<int, Texture2D> photoByIndex = new Dictionary<int, Texture2D>();
    private List<int> uniqueIndices = new List<int>();

    private Texture2D finalComposedImageForPrint;
    private GameObject instantiatedFrameObject;


    private void UpdateRemainingShots(bool isBeautificationActive)
    {
        if (remainingShotCount != null)
        {
            int shotsTaken = currentShotIndex;
            int remaining = totalAllowedShots - shotsTaken;
            if (isBeautificationActive) remaining--;
            
            remaining = Mathf.Max(0, remaining);
            remainingShotCount.text = remaining.ToString();
        }
    }

    private void Start()
    {
        autoPrintAfterCapture = true;

        if (photoPreviewPanel != null)
            photoPreviewPanel.SetActive(false);

        InitializeWebcamDropdown();

        // -------------------------------------------------------------
        // AUTO-DISCOVERY FOR FACE EFFECTS (User Convenience)
        // -------------------------------------------------------------
        if (liveCameraFaceEffects == null && cameraPreview != null)
        {
            // Try explicit component on the preview object logic
            liveCameraFaceEffects = cameraPreview.GetComponent<FaceEffectsController>();
            
            // Try looking in children (common if preview has a wrapper)
            if (liveCameraFaceEffects == null)
                liveCameraFaceEffects = cameraPreview.GetComponentInChildren<FaceEffectsController>();

            // Try looking in parent (common if preview is child of a manager)
            if (liveCameraFaceEffects == null)
                liveCameraFaceEffects = cameraPreview.GetComponentInParent<FaceEffectsController>();

            if (liveCameraFaceEffects != null)
                Debug.Log($"📷 Auto-connected Live Camera Face Effects found on: {liveCameraFaceEffects.name}");
            else
                Debug.LogWarning("⚠️ Could not auto-detect FaceEffectsController on CameraPreview. Please assign 'Live Camera Face Effects' manually in Inspector.");
        }
        // -------------------------------------------------------------

        Debug.Log($"🖨️ Auto-print is: {(autoPrintAfterCapture ? "ENABLED ✅" : "DISABLED ⏸️")}");
    }

    private void InitializeWebcamDropdown()
    {
        if (webcamDropdown == null) return;

        webcamDropdown.ClearOptions();
        WebCamDevice[] devices = WebCamTexture.devices;
        List<string> options = new List<string>();

        if (devices.Length == 0)
        {
            options.Add("No Camera Found");
            webcamDropdown.AddOptions(options);
            return;
        }

        for (int i = 0; i < devices.Length; i++)
        {
            options.Add(devices[i].name);
        }

        webcamDropdown.AddOptions(options);

        // Default to first camera or previously saved preference if you wanted to implement persistence
        webcamDropdown.value = 0;
        currentWebcamName = devices[0].name;

        webcamDropdown.onValueChanged.AddListener(OnWebcamChanged);
    }

    public void OnWebcamChanged(int index)
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length > index && index >= 0)
        {
            currentWebcamName = devices[index].name;
            Debug.Log($"📷 Selected Webcam: {currentWebcamName}");
        }
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

            if (currentShotIndex < cachedPlaceholderSizes.Count)
            {
                var size = cachedPlaceholderSizes[currentShotIndex];
                phWidth = size.x;
                phHeight = size.y;
            }

            ApplyCenterCropToRawImageWithPlaceholder(cameraPreview, webCamTexture.width, webCamTexture.height, phWidth, phHeight);
        }
    }

    [Header("Face Effects (Live Preview)")]
    public FaceEffectsController liveCameraFaceEffects;

    public void StartShooting(FrameItem selectedFrame, string orderID = null)
    {
        if (selectedFrame == null) return;

        photoShootPanel.SetActive(true); // Activate early to ensure child objects (CameraPreview) are findable

        currentFrameItem = selectedFrame;
        totalAllowedShots = selectedFrame.frameData.number_of_shots;
        placeholders.Clear();
        photoByIndex.Clear();
        uniqueIndices.Clear();
        
        currentRetakeCount = 0; // Reset for first shot

        // Apply automatic filter to Live Camera Preview
        if (liveCameraFaceEffects != null)
        {
            // FIX: Check if we are accidentally controlling the STATIC controller instead of the LIVE one
            if (liveCameraFaceEffects.gameObject.name.Contains("FaceLandmarkerRunner"))
            {
                Debug.LogWarning("⚠️ DETECTED WRONG CONTROLLER: 'FaceLandmarkerRunner' is for static editing! Attempting to find 'CameraPreview'...");
                var camPreviewObj = GameObject.Find("CameraPreview");
                if (camPreviewObj != null)
                {
                    var liveController = camPreviewObj.GetComponent<FaceEffectsController>();
                    if (liveController != null)
                    {
                        liveCameraFaceEffects = liveController;
                        Debug.Log("✅ AUTO-CORRECTED [Fixed]: Switched to 'CameraPreview' controller.");
                    }
                }
            }

            string autoFilter = selectedFrame.frameData.filter;
            liveCameraFaceEffects.SetFilter(autoFilter);
            Debug.Log($"📷 Live Camera Filter applied: {autoFilter} on Object: '{liveCameraFaceEffects.gameObject.name}'");
        }
        else
        {
            // Emergency fallback: Try to find it by name "CameraPreview" if strictly needed
            var camPreviewObj = GameObject.Find("CameraPreview");
            if (camPreviewObj != null)
            {
                 liveCameraFaceEffects = camPreviewObj.GetComponent<FaceEffectsController>();
                 if (liveCameraFaceEffects != null)
                 {
                     string autoFilter = selectedFrame.frameData.filter;
                     liveCameraFaceEffects.SetFilter(autoFilter);
                     Debug.Log($"📷 EMERGENCY FOUND & APPLIED: {autoFilter} on '{liveCameraFaceEffects.gameObject.name}'");
                     return; 
                 }
            }
            
            Debug.LogWarning("⚠️ liveCameraFaceEffects is NULL! Cannot apply live filter.");
        }

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

        // Cache placeholder sizes to avoid float.Parse in Update
        cachedPlaceholderSizes.Clear();
        foreach (var ph in placeholders)
        {
            if (float.TryParse(ph.width, out float w) && float.TryParse(ph.height, out float h))
            {
                cachedPlaceholderSizes.Add(new Vector2(w, h));
            }
            else
            {
                cachedPlaceholderSizes.Add(new Vector2(800f, 800f)); // Default
            }
        }

        uniqueIndices = placeholders
            .Select(p => p.placeholder_index)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        Debug.Log($"Found {placeholders.Count} placeholders with {uniqueIndices.Count} unique indices: [{string.Join(", ", uniqueIndices)}]");

        currentShotIndex = 0;


        // Clear previous session's beautified images for a fresh start (Fix for 2nd customer issue)
        if (UiController.Instance != null)
        {
            UiController.Instance.beautifiedImages.Clear();
        }

        StartWebcam();
        StartCoroutine(StartCountdownAndCapture());
    }

    private void StartWebcam()
    {
        if (WebCamTexture.devices.Length == 0) return;

        if (!string.IsNullOrEmpty(currentWebcamName))
        {
            webCamTexture = new WebCamTexture(currentWebcamName);
        }
        else
        {
            webCamTexture = new WebCamTexture();
        }
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
        UpdateRemainingShots(false); // Update counter for camera mode

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

        // --- NEW TIMER LOGIC ---
        if (timerRoot != null) timerRoot.SetActive(true);
        
        if (count3 != null) count3.SetActive(true);
        if (count2 != null) count2.SetActive(false);
        if (count1 != null) count1.SetActive(false);
        yield return new WaitForSeconds(1f);

        if (count3 != null) count3.SetActive(false);
        if (count2 != null) count2.SetActive(true);
        yield return new WaitForSeconds(1f);

        if (count2 != null) count2.SetActive(false);
        if (count1 != null) count1.SetActive(true);
        yield return new WaitForSeconds(1f);

        if (count1 != null) count1.SetActive(false);
        if (timerRoot != null) timerRoot.SetActive(false);
        // -----------------------

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
        if (reshotButton != null) reshotButton.gameObject.SetActive(true); // Ensure reshot button is active
        cameraPreview.gameObject.SetActive(false);

        MatchPreviewSizes();

        photoByIndex[placeholderIndex] = cropped;
        
        UpdateRemainingShots(true); // Decrement counter for beautification mode
        OpenBeautificationForImage(cropped, placeholderIndex, targetWidth, targetHeight);
    }

    public void OpenBeautificationForImage(Texture2D image, int placeholderIndex, float w, float h)
    {
        beautificationPanel.SetActive(true);

        // Control EditPanel visibility based on decoration_enabled flag
        if (editPanel != null)
        {
            bool isDecorationEnabled = PlayerPrefs.GetInt("decoration_enabled", 1) == 1;
            editPanel.SetActive(isDecorationEnabled);
            Debug.Log($"🎨 EditPanel active state set to: {isDecorationEnabled}");
        }

        string autoFilter = "";
        if (currentFrameItem != null && currentFrameItem.frameData != null)
        {
            autoFilter = currentFrameItem.frameData.filter;
        }

        uiController.OnLoadSingleCaptureImage(image, placeholderIndex, w, h, autoFilter);
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
        currentRetakeCount = 0; // Reset for next shot

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
        // Increment retake count
        currentRetakeCount++;
        Debug.Log($"🔄 Retake clicked. Count: {currentRetakeCount}/{maxRetakes}");

        // Safety: Ensure panel is deactivated
        if (beautificationPanel != null) beautificationPanel.SetActive(false);
        if (capturedPhotos.Count > 0)
            capturedPhotos.RemoveAt(capturedPhotos.Count - 1);

        capturePreview.gameObject.SetActive(false);
        reshotButton.gameObject.SetActive(false);
        cameraPreview.gameObject.SetActive(true);

        StartCoroutine(StartCountdownAndCapture());
    }

    public bool CanRetake()
    {
        return currentRetakeCount < maxRetakes;
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

        string url = $"{API.BaseURL}/api/order/upload-photo";

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
    /// <summary>
    /// Saves texture to persistent data path (works in both Editor and Build)
    /// </summary>
    private void SaveTextureToFile(Texture2D texture, string filename)
    {
        try
        {
            // Create debug folder if it doesn't exist
            string debugFolder = Path.Combine(Application.persistentDataPath, "DebugPhotos");

            if (!Directory.Exists(debugFolder))
            {
                Directory.CreateDirectory(debugFolder);
                Debug.Log($"📁 Created debug folder: {debugFolder}");
            }

            // Cleanup old photos if we exceed 4 (so the new one makes it 5)
            try
            {
                var dirInfo = new DirectoryInfo(debugFolder);
                var files = dirInfo.GetFiles("*.png")
                    .OrderBy(f => f.CreationTime)
                    .ToList();

                while (files.Count >= 5)
                {
                    var fileToDelete = files[0];
                    try
                    {
                        fileToDelete.Delete();
                        Debug.Log($"🗑️ Deleted old debug photo: {fileToDelete.Name}");
                        files.RemoveAt(0);
                    }
                    catch (Exception deleteEx)
                    {
                        Debug.LogWarning($"⚠️ Could not delete old photo {fileToDelete.Name}: {deleteEx.Message}");
                        break; // Stop if we can't delete to avoid infinite loop if file is locked
                    }
                }
            }
            catch (Exception cleanupEx)
            {
                Debug.LogWarning($"⚠️ Failed to cleanup old photos: {cleanupEx.Message}");
            }

            // Full path
            string fullPath = Path.Combine(debugFolder, filename);

            // Encode and save
            byte[] bytes = texture.EncodeToPNG();
            if (bytes != null && bytes.Length > 0)
            {
                File.WriteAllBytes(fullPath, bytes);
                Debug.Log($"✅ Photo saved to: {fullPath}");
                Debug.Log($"📊 File size: {bytes.Length / 1024}KB ({texture.width}x{texture.height}px)");
            }
            else
            {
                Debug.LogError("❌ Failed to encode texture to PNG!");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Failed to save texture: {e.Message}");
        }
    }

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

        // --- NEW LOGIC: Resizing Bg based on Scene ---
        string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        bool isPortraitScene = currentSceneName.IndexOf("Portrait", System.StringComparison.OrdinalIgnoreCase) >= 0;
        bool isLandscapeScene = currentSceneName.IndexOf("Landscape", System.StringComparison.OrdinalIgnoreCase) >= 0;

        Transform bgTransform = frameObj.transform.Find("Bg");
        if (bgTransform != null)
        {
            RectTransform bgRect = bgTransform.GetComponent<RectTransform>();
            if (bgRect != null)
            {
                if (isPortraitScene)
                {
                    bgRect.sizeDelta = new Vector2(2000, 2000);
                  
                }
                else if (isLandscapeScene)
                {
                    bgRect.sizeDelta = new Vector2(1920, 1080);
                }
            }
        }
        // ---------------------------------------------


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
            RectTransform frt = frameContainer.GetComponent<RectTransform>();
            frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0.5f);
            frt.pivot = new Vector2(0.5f, 0.5f);
            frt.anchoredPosition = Vector2.zero;
            frt.sizeDelta = new Vector2(frameTex.width, frameTex.height);
        }

        // --- NEW LOGIC: Scaling frame container for Landscape Frame in Portrait Scene ---
        if (isPortraitScene && string.Equals(currentFrameItem.frameData.type, "landscape", System.StringComparison.OrdinalIgnoreCase))
        {
             frameContainer.localScale = new Vector3(0.5f, 0.5f, 1f);
             Debug.Log("✅ [PSM] Scaled 'frame' container to 0.5 (Portrait Scene + Landscape Frame)");
        }
        else
        {
             frameContainer.localScale = Vector3.one; // Ensure reset if reused
        }
        // -------------------------------------------------------------------------------

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

        RectTransform frameRT = frameImgChild.GetComponent<RectTransform>();
        frameRT.anchorMin = frameRT.anchorMax = new Vector2(0.5f, 0.5f);
        frameRT.pivot = new Vector2(0.5f, 0.5f);
        frameRT.anchoredPosition = Vector2.zero;
        frameRT.sizeDelta = new Vector2(frameTex.width, frameTex.height);

        // === CRITICAL: Force exact size on frameContainer ===
        RectTransform containerRT = frameContainer.GetComponent<RectTransform>();
        containerRT.sizeDelta = new Vector2(frameTex.width, frameTex.height);
        containerRT.anchoredPosition = Vector2.zero;

        // === FINAL RENDER & CAPTURE ===
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(containerRT);
        yield return new WaitForEndOfFrame();

        // Hide loading panel BEFORE capture
        if (loadingPanel != null) loadingPanel.SetActive(false);

        yield return new WaitForEndOfFrame();

        // 🔥 RENDER FRAME TO TEXTURE (No screen capture - pure rendering)
        finalComposedImageForPrint = RenderFrameToTexture(frameContainer, frameTex.width, frameTex.height);

        if (finalComposedImageForPrint == null)
        {
            Debug.LogError("❌ Failed to render final image!");
            yield break;
        }

        // 💾 SAVE DEBUG PHOTO (Works in both Editor and Build)
        if (finalComposedImageForPrint != null)
        {
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
           // string userId = PlayerPrefs.GetString("user_id", "guest");
            string filename = $"PHOTO_{timestamp}.png";

            SaveTextureToFile(finalComposedImageForPrint, filename);
        }

        // STEP 1: SHOW FRAME DISPLAY FOR PREVIEW
        Debug.Log($"🖼️ Displaying frame with photos for {previewDurationSeconds} seconds...");

        if (instantiatedFrameObject != null)
            instantiatedFrameObject.SetActive(true);

        yield return new WaitForSeconds(previewDurationSeconds);

        // STEP 2: START PRINTING PROCESS
        if (finalComposedImageForPrint != null && autoPrintAfterCapture)
        {
            Debug.Log($"🖨️ Starting print process for {frameType} frame...");

            if (PrintingManager.Instance != null)
            {
                // Show Printing Panel
                if (printingPanel != null) printingPanel.SetActive(true);
                if (printingInProgress != null) printingInProgress.SetActive(true);
                if (printingDone != null) printingDone.SetActive(false);

                PrintingManager.Instance.PrintFinalImage(finalComposedImageForPrint, frameType);
                
                // --- NEW WAITING LOGIC ---
                // 1. Wait a moment for the print job to be registered by the spooler (important!)
                yield return new WaitForSeconds(2.0f);

                // 2. Wait while PrintingManager reports "IsPrinting"
                // This covers "Status_Printing" or "Status_Busy" returned by the driver
                while (PrintingManager.Instance.IsPrinting)
                {
                    Debug.Log("🖨️ Printer is busy... waiting.");
                    yield return new WaitForSeconds(0.5f);
                }

                // Show Done
                if (printingInProgress != null) printingInProgress.SetActive(false);
                if (printingDone != null) printingDone.SetActive(true);
                
                Debug.Log("✅ Printing workflow completed!");

                // --- REPORT STATUS TO BACKEND ---
                string lastStatus = PrintingManager.Instance.LastStatus;
                string orderId = PaymentManager.Instance?.currentOrderId ?? "";
                string paymentId = PaymentManager.Instance?.currentPaymentId ?? "";
                
                // Determine if successful (Ready means job completed and no errors detected)
                bool isSuccess = (lastStatus == "Ready");
                string condition = "success";

                if (!isSuccess)
                {
                    if (lastStatus.Contains("PAPER_JAM")) condition = "paper jam";
                    else if (lastStatus.Contains("PAPER_OUT")) condition = "no print out";
                    else if (lastStatus.Contains("OFFLINE")) condition = "printer offline";
                    else condition = lastStatus; // Fallback to raw status
                }

                Debug.Log($"📊 Reporting Final Print Status: {isSuccess} | Reason: {condition}");
                yield return StartCoroutine(PrintingManager.Instance.SendPrintStatusToBackend(orderId, isSuccess, condition));
              //  yield return StartCoroutine(PrintingManager.Instance.SendPrintStatusToBackend(orderId, paymentId, isSuccess, condition));
                // --------------------------------
                
                // Wait for user to see "Done"
                yield return new WaitForSeconds(4f);
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


    private Texture2D RenderFrameToTexture(Transform frameTransform, float width, float height)
    {
        int targetWidth = Mathf.RoundToInt(width);
        int targetHeight = Mathf.RoundToInt(height);

        Debug.Log($"🎨 Manually compositing frame: {targetWidth}x{targetHeight}px");

        // Create base texture with white background
        Texture2D finalTexture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
        Color[] pixels = new Color[targetWidth * targetHeight];

        // Fill with white
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.white;

        finalTexture.SetPixels(pixels);
        finalTexture.Apply();

        // Find all Image components in the frame hierarchy
        Transform capturedImagesParent = frameTransform.Find("capturedImages");
        if (capturedImagesParent == null)
        {
            Debug.LogError("❌ capturedImages not found!");
            return finalTexture;
        }

        List<ImageData> imagesToComposite = new List<ImageData>();

        // Collect all photos first
        foreach (Transform child in capturedImagesParent)
        {
            if (child.name == "frameImg") continue; // Skip frame, add it last

            Image img = child.GetComponent<Image>();
            RectTransform rt = child.GetComponent<RectTransform>();

            if (img != null && img.sprite != null && rt != null)
            {
                imagesToComposite.Add(new ImageData
                {
                    texture = img.sprite.texture,
                    rectTransform = rt,
                    sortOrder = child.GetSiblingIndex()
                });
            }
        }

        // Add frame image last (on top)
        Transform frameImg = capturedImagesParent.Find("frameImg");
        if (frameImg != null)
        {
            Image img = frameImg.GetComponent<Image>();
            RectTransform rt = frameImg.GetComponent<RectTransform>();

            if (img != null && img.sprite != null && rt != null)
            {
                imagesToComposite.Add(new ImageData
                {
                    texture = img.sprite.texture,
                    rectTransform = rt,
                    sortOrder = 9999 // Always on top
                });
            }
        }

        // Sort by order
        imagesToComposite.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));

        // Composite all images onto base texture
        foreach (var data in imagesToComposite)
        {
            BlitImageOntoTexture(finalTexture, data.texture, data.rectTransform, targetWidth, targetHeight);
        }

        finalTexture.Apply();

        Debug.Log($"✅ Composited {imagesToComposite.Count} images into final texture");

        return finalTexture;
    }

    private class ImageData
    {
        public Texture2D texture;
        public RectTransform rectTransform;
        public int sortOrder;
    }

    /// <summary>
    /// Blit one texture onto another at the correct position
    /// </summary>
    private void BlitImageOntoTexture(Texture2D target, Texture2D source, RectTransform rt, int frameWidth, int frameHeight)
    {
        if (source == null || rt == null) return;

        // Get image dimensions and position relative to center
        float imgWidth = rt.sizeDelta.x;
        float imgHeight = rt.sizeDelta.y;
        Vector2 imgPos = rt.anchoredPosition;

        // Convert to pixel coordinates (canvas center is 0,0)
        int centerX = frameWidth / 2;
        int centerY = frameHeight / 2;

        int destX = centerX + Mathf.RoundToInt(imgPos.x - imgWidth / 2f);
        int destY = centerY + Mathf.RoundToInt(imgPos.y - imgHeight / 2f);

        int destWidth = Mathf.RoundToInt(imgWidth);
        int destHeight = Mathf.RoundToInt(imgHeight);

        Debug.Log($"   Blitting {source.width}x{source.height} to ({destX},{destY}) size:{destWidth}x{destHeight}");

        // Scale source if needed
        Texture2D scaledSource = source;
        if (source.width != destWidth || source.height != destHeight)
        {
            scaledSource = ResizeTexture(source, destWidth, destHeight);
        }

        // Copy pixels with bounds checking
        for (int y = 0; y < destHeight; y++)
        {
            for (int x = 0; x < destWidth; x++)
            {
                int targetX = destX + x;
                int targetY = destY + y;

                // Skip if out of bounds
                if (targetX < 0 || targetX >= frameWidth || targetY < 0 || targetY >= frameHeight)
                    continue;

                Color sourcePixel = scaledSource.GetPixel(x, y);

                // Alpha blend
                if (sourcePixel.a > 0.01f)
                {
                    Color targetPixel = target.GetPixel(targetX, targetY);
                    Color blended = Color.Lerp(targetPixel, sourcePixel, sourcePixel.a);
                    target.SetPixel(targetX, targetY, blended);
                }
            }
        }

        if (scaledSource != source)
            Destroy(scaledSource);
    }

    private Texture2D ResizeTexture(Texture2D src, int w, int h)
    {
        RenderTexture rt = RenderTexture.GetTemporary(w, h);
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();

        RenderTexture.ReleaseTemporary(rt);
        RenderTexture.active = null;
        return tex;
    }
    /// <summary>
    /// Determines if frame is portrait or landscape based on JSON type or dimensions
    /// </summary>
    private string DetermineFrameOrientation(Texture2D frameTexture)
    {
        if (frameTexture == null)
            return "portrait";

        // PRIORITY 1: Check frameData.type from JSON
        if (currentFrameItem?.frameData != null && !string.IsNullOrEmpty(currentFrameItem.frameData.type))
        {
            string type = currentFrameItem.frameData.type.ToLower();
            Debug.Log($"📋 Frame orientation from JSON type: {type}");
            return type;
        }

        // FALLBACK: Analyze dimensions
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

        foreach (Vector3 corner in corners)
        {
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, corner);
            minX = Mathf.Min(minX, screenPoint.x);
            minY = Mathf.Min(minY, screenPoint.y);
            maxX = Mathf.Max(maxX, screenPoint.x);
            maxY = Mathf.Max(maxY, screenPoint.y);
        }

        int width = Mathf.RoundToInt(maxX - minX);
        int height = Mathf.RoundToInt(maxY - minY);

        if (width <= 0 || height <= 0)
        {
            Debug.LogError($"❌ Invalid capture dimensions: {width}x{height}");
            return null;
        }

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(minX, minY, width, height), 0, 0);
        tex.Apply();

        return tex;
    }

    public void ResetToLoginScreen()
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
        if (printingPanel != null) printingPanel.SetActive(false);

        // Activate login panel through LoginManager
        if (LoginManager.Instance != null)
        {
            LoginManager.Instance.ResetToLoginPanel();
        }

        Debug.Log("✅ System reset complete - Ready for next customer!");
    }
}