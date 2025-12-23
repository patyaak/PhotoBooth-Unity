using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;

public class VendorLogin : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField boothIDInput;
    public Button submitButton;
    public GameObject mainAppPanel;
    public GameObject wifiErrorGO;

    [Header("Theme References (Image Components)")]
    public Image backgroundImage;
    public Image logoImage;
    public Image qrMobileImage;
    public Image cameraImage;
    public TMP_Text boothPrice;

    [Header("Hidden Buttons for switching vendor")]
    public Button logoBtn;
    public Button boothPriceBtn;
    private int logoClickCount = 0;
    private float lastClickTime = 0;
    private float resetDelay = 1f;

    // Default sprites to restore on switch vendor
    private Sprite defaultBackground;
    private Sprite defaultLogo;
    private Sprite defaultQRMobile;
    private Sprite defaultCamera;

    public GameObject errorPanel;

    // Currently logged-in booth ID
    private string currentBoothID = "";

    [Header("Booth Id Edit Unlock")]
    public Button boothIdTextButton;
    private int boothIdClickCount = 0;
    private float lastBoothIdClickTime = 0;
    private float clickResetTime = 1f;
    private int clicksRequiredToUnlock = 5;

    void Start()
    {
        submitButton.onClick.AddListener(OnSubmitClicked);
        mainAppPanel.SetActive(false);

        defaultBackground = backgroundImage.sprite;
        defaultLogo = logoImage.sprite;
        defaultQRMobile = qrMobileImage.sprite;
        defaultCamera = cameraImage.sprite;

        boothIDInput.readOnly = true;
        if (boothIdTextButton != null)
            boothIdTextButton.onClick.AddListener(OnBoothIdTextClicked);

        SetupSecretTrigger();

        // Auto-load last saved booth ID
        if (PlayerPrefs.HasKey("booth_id"))
        {
            string savedBoothID = PlayerPrefs.GetString("booth_id");
            Debug.Log($"Found saved booth ID: {savedBoothID}. Checking offline/online mode...");

            // Check internet connectivity
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                Debug.Log("📵 No internet connection detected at startup");
                HandleOfflineStartup(savedBoothID);
            }
            else
            {
                Debug.Log("🌐 Internet available — loading booth data from server");
                StartCoroutine(LoadBoothData(savedBoothID));
            }
        }
    }

    void HandleOfflineStartup(string savedBoothID)
    {
        // Get saved booth configuration
        int paymentsEnabled = PlayerPrefs.GetInt("payments_enabled", 1);
        int loginRequired = PlayerPrefs.GetInt("login_required", 1);

        Debug.Log($"📋 Saved Config: payments_enabled={paymentsEnabled}, login_required={loginRequired}");

        // Check if offline mode is allowed (both must be 0/false)
        if (paymentsEnabled == 0 && loginRequired == 0)
        {
            Debug.Log("✅ Offline mode allowed — entering offline mode");
            EnterOfflineMode();
        }
        else
        {
            Debug.Log("❌ Offline mode NOT allowed — showing WiFi error and resetting");
            if (wifiErrorGO != null)
                wifiErrorGO.SetActive(true);

            SwitchVendor(); // Reset to login panel
        }
    }

    public void EnterOfflineMode()
    {
        Debug.Log("🔄 Entering OFFLINE MODE...");

        // DON'T show WiFi error - keep app running smoothly
        if (wifiErrorGO != null)
            wifiErrorGO.SetActive(false);

        // Activate main app panel
        mainAppPanel.SetActive(true);

        // Load cached booth data
        currentBoothID = PlayerPrefs.GetString("booth_id");

        if (boothPrice != null)
            boothPrice.text = PlayerPrefs.GetString("booth_price", "");

        // Force-disable payments in offline mode
        PlayerPrefs.SetInt("payments_enabled", 0);
        PlayerPrefs.Save();

        // Hide QR generation button (no login in offline mode)
        var loginManager = FindAnyObjectByType<LoginManager>();
        if (loginManager != null && loginManager.generateQRButton != null)
            loginManager.generateQRButton.gameObject.SetActive(false);

        // Load cached frames
        var frameManager = FindAnyObjectByType<PhotoBoothFrameManager>();
        if (frameManager != null)
        {
            frameManager.ClearFrames();
            frameManager.SetBoothID(currentBoothID);
            frameManager.LoadFramesFromCache(currentBoothID);
            Debug.Log($"📦 Loaded cached frames for booth: {currentBoothID}");
        }

        Debug.Log("✅ Offline mode ready — app running smoothly with cached data (no WiFi panel)");

        // Log the offline mode activation
        LoggingManager.Instance?.LogSystemEvent(
            message: $"Entered offline mode for booth: {currentBoothID}",
            severity: LogSeverity.Warning
        );
    }

    void OnBoothIdTextClicked()
    {
        if (Time.time - lastBoothIdClickTime > clickResetTime)
            boothIdClickCount = 0;

        boothIdClickCount++;
        lastBoothIdClickTime = Time.time;

        if (boothIdClickCount >= clicksRequiredToUnlock)
        {
            boothIDInput.readOnly = false;
            Debug.Log("Booth ID input is now editable!");
            boothIdClickCount = 0;
        }
    }

    void OnSubmitClicked()
    {
        string boothID = boothIDInput.text.Trim();
        if (!string.IsNullOrEmpty(boothID))
        {
            StartCoroutine(LoadBoothData(boothID));
            boothIDInput.readOnly = true;
        }
        else
        {
            Debug.LogWarning("Please enter a valid Booth ID.");
        }
    }

    IEnumerator LoadBoothData(string boothID)
    {
        string fullURL = $"{API.BaseURL}/api/photobooth/booths/{boothID}";
        Debug.Log($"Fetching booth data from: {fullURL}");

        yield return StartCoroutine(FetchBoothData(fullURL));
    }

    IEnumerator FetchBoothData(string url)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                // Hide WiFi error since we successfully connected
                if (wifiErrorGO != null)
                    wifiErrorGO.SetActive(false);

                string json = request.downloadHandler.text.Replace(": null", ": \"\"");
                Debug.Log($"Raw JSON Response:\n{json}");

                BoothListResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<BoothListResponse>(json);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"JSON parse error: {ex.Message}");
                    yield break;
                }

                if (response != null && response.success && response.data != null && response.data.booth != null)
                {
                    Booth booth = response.data.booth;
                    Theme theme = response.data.theme;

                    // Check if booth is stopped
                    if (booth.status.ToLower() == "stop")
                    {
                        ShowErrorAndReset();
                        yield break;
                    }

                    // Update current booth ID
                    currentBoothID = booth.booth_id;

                    // Reset visuals first
                    ResetThemeVisuals();

                    // Apply theme
                    if (theme != null)
                        ApplyTheme(theme);
                    else
                        Debug.LogWarning("No theme assigned to this booth.");

                    if (boothPrice != null)
                        boothPrice.text = booth.price.ToString();

                    // Save booth settings to PlayerPrefs
                    PlayerPrefs.SetString("booth_id", booth.booth_id);
                    PlayerPrefs.SetString("booth_price", booth.price.ToString());
                    PlayerPrefs.SetString("gacha_price", booth.gacha_price.ToString());
                    PlayerPrefs.SetInt("payments_enabled", booth.payments_enabled ? 1 : 0);
                    PlayerPrefs.SetInt("login_required", booth.login_required ? 1 : 0);
                    PlayerPrefs.Save();

                    Debug.Log($"💾 Booth settings saved:");
                    Debug.Log($"   • ID: {booth.booth_id}");
                    Debug.Log($"   • Price: {booth.price}");
                    Debug.Log($"   • Gacha: {booth.gacha_price}");
                    Debug.Log($"   • Payments: {booth.payments_enabled}");
                    Debug.Log($"   • Login Required: {booth.login_required}");

                    // Show/hide QR generation button based on login_required
                    var loginManager = FindAnyObjectByType<LoginManager>();
                    if (loginManager != null && loginManager.generateQRButton != null)
                        loginManager.generateQRButton.gameObject.SetActive(booth.login_required);

                    // Log booth login
                    LoggingManager.Instance?.LogSystemEvent(
                        message: $"Booth logged in: {booth.booth_id}",
                        severity: LogSeverity.Info,
                        details: JsonUtility.ToJson(booth)
                    );

                    // Activate main app panel
                    mainAppPanel.SetActive(true);

                    // Fetch frames from server
                    var frameManager = FindAnyObjectByType<PhotoBoothFrameManager>();
                    if (frameManager != null)
                    {
                        frameManager.ClearFrames();
                        frameManager.SetBoothID(booth.booth_id);
                        StartCoroutine(frameManager.FetchFramesFromServer());
                    }

                    // Start checking booth status periodically
                    StartCoroutine(CheckBoothStatusRoutine());
                }
                else
                {
                    Debug.LogError($"Invalid or empty response.\nRaw JSON:\n{json}");
                    if (wifiErrorGO != null)
                        wifiErrorGO.SetActive(true);
                }
            }
            else
            {
                Debug.LogError($"Booth fetch failed: {request.error}");
                if (wifiErrorGO != null)
                    wifiErrorGO.SetActive(true);
            }
        }
    }

    void ApplyTheme(Theme theme)
    {
        if (theme == null) return;

        if (!string.IsNullOrEmpty(theme.backgroundImg))
            StartCoroutine(LoadImage(theme.backgroundImg, backgroundImage));

        if (!string.IsNullOrEmpty(theme.logo_path))
            StartCoroutine(LoadImage(theme.logo_path, logoImage));

        if (!string.IsNullOrEmpty(theme.QRmobileImg))
            StartCoroutine(LoadImage(theme.QRmobileImg, qrMobileImage));

        if (!string.IsNullOrEmpty(theme.CameraImg))
            StartCoroutine(LoadImage(theme.CameraImg, cameraImage));
    }

    IEnumerator LoadImage(string url, Image target)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D tex = DownloadHandlerTexture.GetContent(request);
                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                target.sprite = sprite;
            }
            else
            {
                Debug.LogWarning($"Failed to load image: {url} | {request.error}");
            }
        }
    }

    void SetupSecretTrigger()
    {
        if (logoBtn != null)
            logoBtn.onClick.AddListener(OnLogoClicked);

        if (boothPriceBtn != null)
            boothPriceBtn.onClick.AddListener(OnBoothPriceClicked);
    }

    void OnLogoClicked()
    {
        if (Time.time - lastClickTime > resetDelay)
            logoClickCount = 0;

        logoClickCount++;
        lastClickTime = Time.time;
    }

    void OnBoothPriceClicked()
    {
        if (logoClickCount >= 5)
        {
            SwitchVendor();
            logoClickCount = 0;
        }
    }

    public void SwitchVendor()
    {
        Debug.Log("🔄 Switching vendor and resetting all data...");

        PlayerPrefs.DeleteKey("booth_id");
        PlayerPrefs.DeleteKey("booth_price");
        PlayerPrefs.DeleteKey("gacha_price");
        PlayerPrefs.DeleteKey("payments_enabled");
        PlayerPrefs.DeleteKey("login_required");
        PlayerPrefs.Save();

        currentBoothID = "";

        var deviceReg = FindAnyObjectByType<DeviceRegistration>();
        boothIDInput.text = deviceReg != null ? deviceReg.GetSavedBoothID() : "";
        boothPrice.text = "";

        ResetThemeVisuals();
        mainAppPanel.SetActive(false);

        var frameManager = FindAnyObjectByType<PhotoBoothFrameManager>();
        if (frameManager != null)
            frameManager.ClearFrames();

        Debug.Log("All data cleared. Ready for a new booth ID.");
    }

    void ResetThemeVisuals()
    {
        if (backgroundImage) backgroundImage.sprite = defaultBackground;
        if (logoImage) logoImage.sprite = defaultLogo;
        if (qrMobileImage) qrMobileImage.sprite = defaultQRMobile;
        if (cameraImage) cameraImage.sprite = defaultCamera;
    }

    void ShowErrorAndReset()
    {
        if (errorPanel != null)
            errorPanel.SetActive(true);

        mainAppPanel.SetActive(false);
        SwitchVendor();
    }

    IEnumerator CheckBoothStatusRoutine()
    {
        while (!string.IsNullOrEmpty(currentBoothID))
        {
            string url = $"{API.BaseURL}/api/photobooth/booths/{currentBoothID}";
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string json = request.downloadHandler.text.Replace(": null", ": \"\"");
                    BoothListResponse response = JsonUtility.FromJson<BoothListResponse>(json);

                    if (response != null && response.success && response.data != null && response.data.booth != null)
                    {
                        Booth booth = response.data.booth;
                        if (booth.status.ToLower() == "stop")
                        {
                            Debug.LogWarning("Booth has been stopped! Returning to login page.");
                            ShowErrorAndReset();
                            yield break;
                        }
                    }
                }
            }

            yield return new WaitForSeconds(5f); // check every 5 seconds
        }
    }
}