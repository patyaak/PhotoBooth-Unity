using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using ZXing;
using NativeWebSocket;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
#endif

public class LoginManager : MonoBehaviour
{
    public static LoginManager Instance;

    [Header("API Config")]
    public int ttlSeconds = 160;
    public string boothKey = "boothkey123";

    [Header("WebSocket Config")]
    public bool useSecureWebSocket = true;

    [Header("UI References")]
    public GameObject qrPanel;
    public Button generateQRButton;
    public Button GuestButton;
    public RawImage qrImage;
    public GameObject frameSelectionPanel;
    public GameObject paymentPanel;
    public GameObject blockImg;

    [Header("QR Print References")]
    public Button printQRButton;
    public GameObject qrPrintPanel;
    public RawImage qrPrintImage;
    public Button qrPrintBackButton;

    [Header("Timeout Settings")]
    public float framePanelTimeoutSeconds = 60f;

    private PhotoBoothFrameManager frameManager;
    private string boothId;
    private string currentToken;
    private Coroutine autoRefreshRoutine;
    private Coroutine panelTimeoutRoutine;
    private WebSocket ws;
    private bool isWebSocketConnected = false;
    private float lastActivityTime = 0f;


    private void Start()
    {
#if UNITY_STANDALONE || UNITY_EDITOR
        ServicePointManager.ServerCertificateValidationCallback =
            (sender, cert, chain, sslErrors) => true;
        ServicePointManager.SecurityProtocol =
            SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
#endif

        frameManager = FindObjectOfType<PhotoBoothFrameManager>();
        boothId = PlayerPrefs.GetString("booth_id", "test_booth_001");

        if (generateQRButton) generateQRButton.onClick.AddListener(OnGenerateQRClicked);
        if (GuestButton) GuestButton.onClick.AddListener(OnGuestBtnClick);
        if (printQRButton) printQRButton.onClick.AddListener(OnPrintQRClicked);
        if (qrPrintBackButton) qrPrintBackButton.onClick.AddListener(OnQRPrintBackClicked);

        // Keep QR panel inactive on app start - user clicks button to activate
        if (qrPanel != null) qrPanel.SetActive(false);
        if (qrPrintPanel != null) qrPrintPanel.SetActive(false);
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Update()
    {
        ws?.DispatchMessageQueue();

        if (frameSelectionPanel != null && frameSelectionPanel.activeSelf)
        {
            if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.touchCount > 0 || Input.GetAxis("Mouse ScrollWheel") != 0f)
            {
                lastActivityTime = Time.time;
            }
        }
    }

    public void ResetToLoginPanel()
    {
        Debug.Log("🔄 Resetting to login panel...");

        // Close all other panels
        if (frameSelectionPanel != null) frameSelectionPanel.SetActive(false);
        if (paymentPanel != null) paymentPanel.SetActive(false);
        if (blockImg != null) blockImg.SetActive(false);
        if (qrPrintPanel != null) qrPrintPanel.SetActive(false);

        //show back button
        if (frameManager != null && frameManager.backButton != null)
        {
            frameManager.backButton.gameObject.SetActive(true);
        }

        // Show QR panel
        if (qrPanel != null) qrPanel.SetActive(false);

        // Stop any ongoing timeout routines
        if (panelTimeoutRoutine != null)
        {
            StopCoroutine(panelTimeoutRoutine);
            panelTimeoutRoutine = null;
        }

        // Stop auto-refresh if running
        if (autoRefreshRoutine != null)
        {
            StopCoroutine(autoRefreshRoutine);
            autoRefreshRoutine = null;
        }

        // Close websocket if open
        CloseWebSocket();

        // Clear current token
        currentToken = null;

        // **NEW: Reset frame manager to default category**
        if (frameManager != null)
        {
            frameManager.ResetToDefaultCategory();
        }

        // **NEW: Clear gacha state if exists**
        if (GatchaManager.Instance != null)
        {
            GatchaManager.Instance.ClearSpawnedFramesInstant();
            GatchaManager.Instance.celebration.SetActive(false);
            GatchaManager.Instance.ResetGachaSession();
        }

        Debug.Log("✅ Ready for next customer!");
    }

    // QR PRINT FUNCTIONALITY
    private void OnPrintQRClicked()
    {
        AudioManager.Instance?.PlayClick();

        string currentBooth = PlayerPrefs.GetString("booth_id", "test_booth_001");
        string deviceId = SystemInfo.deviceUniqueIdentifier;
        string qrContent = $"device_id={deviceId}&booth_id={currentBooth}";

        if (qrPrintPanel != null)
        {
            qrPrintPanel.SetActive(true);
        }

        GenerateQRPrintCode(qrContent);
    }

    private void OnQRPrintBackClicked()
    {
        AudioManager.Instance?.PlayClick();
        if (qrPrintPanel != null)
        {
            qrPrintPanel.SetActive(false);
        }
    }

    private void GenerateQRPrintCode(string qrContent)
    {
        try
        {
            Debug.Log($"📱 Generating print QR code for content: {qrContent}");

            var writer = new BarcodeWriter<Texture2D>
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new ZXing.Common.EncodingOptions
                {
                    Width = 400,
                    Height = 400,
                    Margin = 0
                },
                Renderer = new ZXing.Rendering.Texture2DRenderer()
            };

            Texture2D tex = writer.Write(qrContent);
            if (qrPrintImage != null)
            {
                qrPrintImage.texture = tex;
                qrPrintImage.rectTransform.sizeDelta = new Vector2(400, 400);
            }

            Debug.Log("✅ Print QR code generated successfully");
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ Failed to generate print QR code: {ex.Message}");
            ShowErrorMessage("Failed to generate QR code");
        }
    }

    // QR GENERATION
    private void OnGenerateQRClicked()
    {
        AudioManager.Instance?.PlayClick();
        qrPanel.SetActive(true);
        StartCoroutine(RequestQRToken());

        if (autoRefreshRoutine != null) StopCoroutine(autoRefreshRoutine);
        autoRefreshRoutine = StartCoroutine(AutoRefreshQR());
    }

    // ✅ UPDATED: Use ServerAwareWebRequest for connectivity handling
    IEnumerator RequestQRToken()
    {
        string url = $"{API.BaseURL}/api/qr-login/issue";
        QRRequestData data = new QRRequestData(boothId, ttlSeconds);
        string json = JsonUtility.ToJson(data);

        Debug.Log($"🔵 Requesting QR Token from: {url}");
        Debug.Log($"📤 Request payload: {json}");

        // ✅ CHANGED: Use ServerAwareWebRequest instead of UnityWebRequest
        yield return ServerAwareWebRequest.Post(url, json, (request) =>
        {
            // ✅ CHANGED: Check for connectivity errors
            if (ServerAwareWebRequest.IsConnectivityError(request))
            {
                Debug.LogError("❌ Server connectivity issue detected during QR request");
                ShowErrorMessage("Server connection failed. Please check your network.");
                return;
            }

            if (ServerAwareWebRequest.IsSuccess(request))
            {
                string responseText = request.downloadHandler.text;
                Debug.Log($"✅ QR Token Response (Raw): {responseText}");

                try
                {
                    // Validate response is not empty
                    if (string.IsNullOrEmpty(responseText))
                    {
                        Debug.LogError("❌ Server returned empty response");
                        ShowErrorMessage("Server returned empty response");
                        return;
                    }

                    // Try to parse the response
                    QRResponse res = JsonUtility.FromJson<QRResponse>(responseText);

                    // Validate parsed data
                    if (res == null)
                    {
                        Debug.LogError("❌ Failed to parse QR response - result is null");
                        ShowErrorMessage("Invalid server response format");
                        return;
                    }

                    if (res.data == null)
                    {
                        Debug.LogError("❌ QR response data is null");
                        Debug.LogError($"Response object: success={res.success}, data=null");
                        ShowErrorMessage("Invalid server response data");
                        return;
                    }

                    if (string.IsNullOrEmpty(res.data.token))
                    {
                        Debug.LogError("❌ Token is empty in response");
                        Debug.LogError($"Response data: token_id={res.data.token_id}, booth_id={res.data.booth_id}");
                        ShowErrorMessage("No token received from server");
                        return;
                    }

                    // Success - process the token
                    currentToken = res.data.token;
                    Debug.Log($"✅ Token received: {currentToken}");
                    Debug.Log($"📋 Token ID: {res.data.token_id}");
                    Debug.Log($"⏰ Expires at: {res.data.expires_at}");

                    GenerateQRCode(currentToken);
                    ConnectWebSocket();
                }
                catch (ArgumentException ex)
                {
                    Debug.LogError($"❌ JSON Parse Error: {ex.Message}");
                    Debug.LogError($"📄 Response that failed to parse: {responseText}");
                    Debug.LogError($"🔍 Response length: {responseText.Length} characters");

                    // Check if response looks like HTML
                    if (responseText.TrimStart().StartsWith("<"))
                    {
                        Debug.LogError("⚠️ Server returned HTML instead of JSON (possibly an error page)");
                    }

                    ShowErrorMessage("Failed to parse server response");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"❌ Unexpected error processing QR response: {ex.Message}");
                    Debug.LogError($"📄 Stack trace: {ex.StackTrace}");
                    ShowErrorMessage("Unexpected error occurred");
                }
            }
            else
            {
                Debug.LogError($"❌ QR Token Request Failed: {request.error}");
                Debug.LogError($"📄 Response Code: {request.responseCode}");
                Debug.LogError($"📄 Response: {request.downloadHandler.text}");
                ShowErrorMessage($"Request failed: {request.error}");
            }
        });
    }

    IEnumerator AutoRefreshQR()
    {
        float delay = Mathf.Max(10f, ttlSeconds - 20f);
        Debug.Log($"🔄 Auto-refresh QR every {delay} seconds");

        while (true)
        {
            yield return new WaitForSeconds(delay);
            if (!string.IsNullOrEmpty(currentToken))
            {
                Debug.Log("🔄 Auto-refreshing QR token...");
                yield return RequestQRToken();
            }
        }
    }

    private void GenerateQRCode(string token)
    {
        try
        {
            Debug.Log($"📱 Generating QR code for token: {token}");

            var writer = new BarcodeWriter<Texture2D>
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new ZXing.Common.EncodingOptions
                {
                    Width = 512,
                    Height = 512,
                    Margin = 0
                },
                Renderer = new ZXing.Rendering.Texture2DRenderer()
            };

            Texture2D tex = writer.Write(token);
            qrImage.texture = tex;
            qrImage.rectTransform.sizeDelta = new Vector2(400, 400);

            Debug.Log("✅ QR code generated successfully");
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ Failed to generate QR code: {ex.Message}");
            ShowErrorMessage("Failed to generate QR code");
        }
    }

    // WEBSOCKET

    private async void ConnectWebSocket()
    {
        if (string.IsNullOrEmpty(boothKey))
        {
            Debug.LogError("❌ boothKey is EMPTY!");
            return;
        }

        string wsUrl = API.GetWebSocketURL(useSecureWebSocket, boothKey);

        Debug.Log($"🔌 Connecting to WebSocket: {wsUrl}");

        await CloseWebSocketAsync();

        ws = new WebSocket(wsUrl);

        ws.OnOpen += () =>
        {
            isWebSocketConnected = true;
            Debug.Log("✅ WebSocket Connected!");
            if (!string.IsNullOrEmpty(currentToken))
                SendSubscription(currentToken);
        };

        ws.OnError += (e) =>
        {
            Debug.LogError($"❌ WS Error: {e}");
            isWebSocketConnected = false;
        };

        ws.OnClose += (code) =>
        {
            Debug.LogWarning($"⚠️ WS Closed with code: {code}");
            isWebSocketConnected = false;
        };

        ws.OnMessage += (bytes) =>
        {
            string message = Encoding.UTF8.GetString(bytes);
            HandleWebSocketMessage(message);
        };

        try
        {
            await ws.Connect();
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ WS Connect Failed: {ex.Message}");
        }
    }

    private async void SendSubscription(string token)
    {
        var sub = new PusherSubscribeEvent
        {
            @event = "pusher:subscribe",
            data = new SubscribeData { channel = $"qr-login.{token}" }
        };

        string json = JsonUtility.ToJson(sub);
        Debug.Log($"📡 Subscribing to channel: qr-login.{token}");
        await ws.SendText(json);
    }

    private void HandleWebSocketMessage(string json)
    {
        Debug.Log($"📨 WS Message received: {json}");

        try
        {
            var envelope = JsonUtility.FromJson<PusherEnvelope>(json);

            if (envelope.@event == "pusher_internal:subscription_succeeded")
            {
                Debug.Log("✅ Subscribed to channel successfully!");
                return;
            }

            if (envelope.@event == "user-logged-in")
            {
                AudioManager.Instance?.PlayLoginSuccess();
                Debug.Log("🎉 USER LOGGED IN VIA QR SCAN!");

                try
                {
                    UserSessionWrapper wrapper = JsonUtility.FromJson<UserSessionWrapper>(envelope.data);

                    if (wrapper?.session == null)
                    {
                        Debug.LogError($"❌ Failed to parse session data: {envelope.data}");
                        return;
                    }

                    var s = wrapper.session;
                    Debug.Log($"👤 Welcome {s.user_name} ({s.user_email})");

                    PlayerPrefs.SetString("user_id", s.user_id);
                    PlayerPrefs.SetString("user_name", s.user_name);
                    PlayerPrefs.SetString("user_email", s.user_email);
                    PlayerPrefs.SetString("session_id", s.session_id);
                    PlayerPrefs.SetString("booth_id", s.booth_id);
                    PlayerPrefs.Save();

                    // LOG: User login
                    if (LoggingManager.Instance != null)
                    {
                        LoggingManager.Instance.LogSystemEvent(
                            message: $"User logged in: {s.user_name}",
                            severity: LogSeverity.Info,
                            details: JsonUtility.ToJson(s)
                        );
                    }

                    ActivateFrameSelection(isGuest: false);
                    CloseWebSocketAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"❌ Error parsing user session: {ex.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ WS Parse Error: {e.Message}\n📄 JSON: {json}");
        }
    }

    private async Task CloseWebSocketAsync()
    {
        if (ws == null) return;

        if (ws.State == WebSocketState.Open || ws.State == WebSocketState.Connecting)
        {
            try
            {
                Debug.Log("🔌 Closing WebSocket...");
                await ws.Close();
                Debug.Log("✅ WebSocket closed");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"⚠️ WS close warning: {ex.Message}");
            }
        }

        ws = null;
        isWebSocketConnected = false;
    }

    private async void CloseWebSocket()
    {
        await CloseWebSocketAsync();
    }

    // UI & FRAME SELECTION
    public void OnGuestBtnClick()
    {
        AudioManager.Instance?.PlayLoginSuccess();
        Debug.Log("👤 Guest mode button clicked");

        PlayerPrefs.DeleteKey("user_id");
        PlayerPrefs.DeleteKey("user_name");
        PlayerPrefs.DeleteKey("session_id");

        // LOG: Guest mode
        if (LoggingManager.Instance != null)
        {
            LoggingManager.Instance.LogSystemEvent(
                message: "Guest mode activated",
                severity: LogSeverity.Info
            );
        }

        ActivateFrameSelection(isGuest: true);
    }

    private void ActivateFrameSelection(bool isGuest)
    {
        Debug.Log($"🖼️ Activating frame selection (Guest: {isGuest})");

        // **FIX: Ensure boothID is synchronized before opening selection panel**
        boothId = PlayerPrefs.GetString("booth_id", "test_booth_001");
        if (frameManager != null)
        {
            frameManager.SetBoothID(boothId);
        }

        qrPanel.SetActive(false);
        frameSelectionPanel.SetActive(true);
        lastActivityTime = Time.time;

        // **NEW: Always reset to default category when opening frame selection**
        if (frameManager != null)
        {
            frameManager.ResetToDefaultCategory();
        }

        // **NEW: Ensure button visibility based on guest status**
        if (frameManager != null && frameManager.myFrameButton != null)
        {
            frameManager.myFrameButton.gameObject.SetActive(!isGuest);
        }

        if (panelTimeoutRoutine != null) StopCoroutine(panelTimeoutRoutine);
        panelTimeoutRoutine = StartCoroutine(FramePanelAutoClose());
    }

    IEnumerator FramePanelAutoClose()
    {
        Debug.Log($"⏱️ Frame panel timeout started ({framePanelTimeoutSeconds}s)");

        while (frameSelectionPanel.activeSelf)
        {
            if (Time.time - lastActivityTime >= framePanelTimeoutSeconds)
            {
                Debug.Log("⏱️ Frame selection timed out - returning to login");

                // Close frame panel and return to login
                frameSelectionPanel.SetActive(false);
                paymentPanel.SetActive(false);

                // Clear user session data on timeout
                PlayerPrefs.DeleteKey("user_id");
                PlayerPrefs.DeleteKey("user_name");
                PlayerPrefs.DeleteKey("session_id");
                PlayerPrefs.Save();

                // Return to login panel
                ResetToLoginPanel();

                yield break;
            }
            yield return null;
        }
    }

    private void ShowErrorMessage(string message)
    {
        AudioManager.Instance?.PlayError();
        Debug.LogWarning($"⚠️ Showing error to user: {message}");
        // You can implement a UI popup here to show the error to the user
        // For example: errorMessageText.text = message; errorPanel.SetActive(true);
    }

    // APPLICATION QUIT
    public async void OnApplicationQuit()
    {
        Debug.Log("🛑 Application quitting...");
        await CloseWebSocketAsync();
        await Task.Delay(100);
    }


    // SERIALIZABLE CLASSES
    [Serializable]
    public class QRRequestData
    {
        public string booth_id;
        public int ttl_seconds;

        public QRRequestData(string id, int ttl)
        {
            booth_id = id;
            ttl_seconds = ttl;
        }
    }

    [Serializable]
    public class QRResponse
    {
        public bool success;
        public QRData data;
    }

    [Serializable]
    public class QRData
    {
        public string token;
        public string token_id;
        public string expires_at;
        public string booth_id;
    }

    [Serializable]
    private class PusherEnvelope
    {
        public string @event;
        public string data;
        public string channel;
    }

    [Serializable]
    private class PusherSubscribeEvent
    {
        public string @event;
        public SubscribeData data;
    }

    [Serializable]
    private class SubscribeData
    {
        public string channel;
    }

    [Serializable]
    public class UserSessionWrapper
    {
        public UserSession session;
    }

    [Serializable]
    public class UserSession
    {
        public string user_name;
        public string user_email;
        public string user_id;
        public string session_id;
        public string booth_id;
        public string started_at;
    }
}