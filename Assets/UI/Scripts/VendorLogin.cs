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

    [Header("API Endpoint")]
    public string apiBaseURL = "http://photo-stg-api.chvps3.aozora-okinawa.com";

    // Default sprites to restore on switch vendor
    private Sprite defaultBackground;
    private Sprite defaultLogo;
    private Sprite defaultQRMobile;
    private Sprite defaultCamera;

    void Start()
    {
        submitButton.onClick.AddListener(OnSubmitClicked);

        mainAppPanel.SetActive(false);

        // Store default sprites for reset
        defaultBackground = backgroundImage.sprite;
        defaultLogo = logoImage.sprite;
        defaultQRMobile = qrMobileImage.sprite;
        defaultCamera = cameraImage.sprite;

        // Auto-load last saved booth ID
        if (PlayerPrefs.HasKey("booth_id"))
        {
            string savedBoothID = PlayerPrefs.GetString("booth_id");
            Debug.Log($"Found saved booth ID: {savedBoothID}. Auto-loading...");
            boothIDInput.text = savedBoothID;
            StartCoroutine(LoadBoothData(savedBoothID));
        }

        SetupSecretTrigger();
    }

    void OnSubmitClicked()
    {
        string boothID = boothIDInput.text.Trim();
        if (!string.IsNullOrEmpty(boothID))
        {
            StartCoroutine(LoadBoothData(boothID));
        }
        else
        {
            Debug.LogWarning("Please enter a valid Booth ID.");
        }
    }

    IEnumerator LoadBoothData(string boothID)
    {
        string fullURL = $"{apiBaseURL}/api/photobooth/booths/{boothID}";
        Debug.Log($"Fetching booth data from: {fullURL}");

        yield return LoggedWebRequest.Get(fullURL, (request) =>
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                if (wifiErrorGO != null)
                    wifiErrorGO.SetActive(false);

                string json = request.downloadHandler.text;
                json = json.Replace(": null", ": \"\"");

                Debug.Log($"Raw JSON Response:\n{json}");

                BoothListResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<BoothListResponse>(json);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"JSON parse error: {ex.Message}");
                    return;
                }

                if (response != null && response.success && response.data != null && response.data.booth != null)
                {
                    Booth booth = response.data.booth;
                    Theme theme = response.data.theme;

                    // Reset visuals first to avoid old theme showing
                    ResetThemeVisuals();

                    if (theme != null)
                    {
                        Debug.Log("Applying theme...");
                        ApplyTheme(theme);
                    }
                    else
                    {
                        Debug.LogWarning("No theme assigned to this booth.");
                    }

                    if (boothPrice != null)
                        boothPrice.text = booth.price;

                    PlayerPrefs.SetString("booth_id", booth.booth_id);
                    PlayerPrefs.SetString("booth_price", booth.price);
                    PlayerPrefs.SetString("gacha_price", booth.gacha_price);
                    PlayerPrefs.SetInt("payments_enabled", booth.payments_enabled ? 1 : 0);
                    PlayerPrefs.Save();

                    Debug.Log($"💾 Booth settings saved: ID={booth.booth_id}, Price={booth.price}, Gacha={booth.gacha_price}, Payments={booth.payments_enabled}");

                    // LOG: Booth logged in
                    LoggingManager.Instance?.LogSystemEvent(
                        message: $"Booth logged in: {booth.booth_id}",
                        severity: LogSeverity.Info,
                        details: JsonUtility.ToJson(booth)
                    );

                    mainAppPanel.SetActive(true);

                    var frameManager = FindAnyObjectByType<PhotoBoothFrameManager>();
                    if (frameManager != null)
                    {
                        frameManager.ClearFrames();
                        frameManager.SetBoothID(booth.booth_id);
                        StartCoroutine(frameManager.FetchFramesFromServer());
                    }
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
        });
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
            logoClickCount = 0; // reset after triggering
        }
    }


    public void SwitchVendor()
    {
        Debug.Log("🔄 Switching vendor and resetting all data...");

        PlayerPrefs.DeleteKey("booth_id");
        PlayerPrefs.DeleteKey("gacha_price");
        PlayerPrefs.DeleteKey("payments_enabled");
        PlayerPrefs.Save();

        var deviceReg = FindAnyObjectByType<DeviceRegistration>();
        if (deviceReg != null)
            boothIDInput.text = deviceReg.GetSavedBoothID();
        else
            boothIDInput.text = "";

        boothPrice.text = "";

        // Restore default images instead of clearing
        ResetThemeVisuals();

        mainAppPanel.SetActive(false);

        var frameManager = FindAnyObjectByType<PhotoBoothFrameManager>();
        if (frameManager != null)
        {
            frameManager.ClearFrames();
        }

        Debug.Log("All data cleared. Ready for a new booth ID.");
    }

    void ResetThemeVisuals()
    {
        if (backgroundImage) backgroundImage.sprite = defaultBackground;
        if (logoImage) logoImage.sprite = defaultLogo;
        if (qrMobileImage) qrMobileImage.sprite = defaultQRMobile;
        if (cameraImage) cameraImage.sprite = defaultCamera;
    }
}