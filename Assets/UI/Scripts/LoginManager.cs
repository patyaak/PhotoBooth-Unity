using System;
using System.Collections;
using System.Text;
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
    [Header("API Config")]
    public string baseUrl = "http://photo-stg-api.chvps3.aozora-okinawa.com";
    public int ttlSeconds = 160;
    public string boothKey = "boothkey123";

    [Header("WebSocket Config")]
    [Tooltip("Use wss:// for secure or ws:// for testing")]
    public bool useSecureWebSocket = true; // Toggle this to false for testing

    [Header("UI References")]
    public GameObject qrPanel;
    public Button generateQRButton;
    public Button GuestButton;
    public RawImage qrImage;
    public GameObject frameSelectionPanel;

    [Header("Timeout Settings")]
    public float framePanelTimeoutSeconds = 60f;

    private PhotoBoothFrameManager frameManager;
    private string boothId;
    private string currentToken;
    private Coroutine autoRefreshRoutine;
    private Coroutine panelTimeoutRoutine;
    private float lastActivityTime;

    private NativeWebSocket.WebSocket ws;
    private bool isWebSocketConnected = false;

    private void Start()
    {
        // CRITICAL FIX: Bypass SSL certificate validation
        // This is needed for staging servers with self-signed certificates
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        System.Net.ServicePointManager.ServerCertificateValidationCallback =
            (sender, certificate, chain, sslPolicyErrors) =>
            {
                // In production, you should validate the certificate properly
                // For now, accept all certificates
                Debug.Log("⚙️ SSL certificate validation bypassed");
                return true;
            };

        // Also set security protocol
        System.Net.ServicePointManager.SecurityProtocol =
            System.Net.SecurityProtocolType.Tls12 |
            System.Net.SecurityProtocolType.Tls11 |
            System.Net.SecurityProtocolType.Tls;

        Debug.Log("⚙️ SSL configuration applied");
#endif

        boothId = PlayerPrefs.GetString("booth_id", "");

        if (string.IsNullOrEmpty(boothId))
        {
            Debug.LogError("⚠️ Booth ID is not set! Please configure it in PlayerPrefs.");
        }

        frameManager = FindObjectOfType<PhotoBoothFrameManager>();

        if (frameManager == null)
        {
            Debug.LogWarning("⚠️ PhotoBoothFrameManager not found in scene!");
        }

        if (generateQRButton != null)
            generateQRButton.onClick.AddListener(OnGenerateQRClicked);
        else
            Debug.LogWarning("⚠️ Generate QR Button not assigned!");

        if (GuestButton != null)
            GuestButton.onClick.AddListener(OnGuestBtnClick);
        else
            Debug.LogWarning("⚠️ Guest Button not assigned!");

        if (qrImage == null)
            Debug.LogWarning("⚠️ QR Image not assigned!");

        if (frameSelectionPanel == null)
            Debug.LogWarning("⚠️ Frame Selection Panel not assigned!");
    }

    // ----------------------------------------------------------------------
    // QR GENERATION
    // ----------------------------------------------------------------------
    void OnGenerateQRClicked()
    {
        if (string.IsNullOrEmpty(boothId))
        {
            Debug.LogError("❌ Booth ID is missing! Cannot generate QR code.");
            return;
        }

        Debug.Log("🔄 Generating new QR code...");
        StartCoroutine(RequestQRToken());

        // Stop existing auto-refresh if running
        if (autoRefreshRoutine != null)
            StopCoroutine(autoRefreshRoutine);

        // Start auto-refresh cycle
        autoRefreshRoutine = StartCoroutine(AutoRefreshQR());
    }

    IEnumerator RequestQRToken()
    {
        string url = $"{baseUrl}/api/qr-login/issue";
        string jsonData = JsonUtility.ToJson(new QRRequestData(boothId, ttlSeconds));

        Debug.Log($"📤 Requesting QR token from: {url}");
        Debug.Log($"📦 Payload: {jsonData}");

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"✅ API Response: {request.downloadHandler.text}");

                QRResponse response = JsonUtility.FromJson<QRResponse>(request.downloadHandler.text);

                if (response.success && response.data != null)
                {
                    currentToken = response.data.token;
                    GenerateQRCode(currentToken);

                    Debug.Log($"🎫 QR Token: {currentToken}");
                    Debug.Log($"⏰ Expires: {response.data.expires_at}");

                    // Close existing WebSocket if any
                    if (ws != null && isWebSocketConnected)
                    {
                        Debug.Log("🔄 Closing previous WebSocket connection...");
                        CloseWebSocket();
                    }

                    // Connect to WebSocket for this token
                    ConnectWebSocket(currentToken);
                }
                else
                {
                    Debug.LogError($"❌ API returned success=false or null data");
                }
            }
            else
            {
                Debug.LogError($"❌ Network error: {request.error}");
                Debug.LogError($"❌ Response Code: {request.responseCode}");

                if (!string.IsNullOrEmpty(request.downloadHandler?.text))
                {
                    Debug.LogError($"❌ Response Body: {request.downloadHandler.text}");
                }
            }
        }
    }

    IEnumerator AutoRefreshQR()
    {
        // Calculate refresh delay: refresh 20 seconds before expiry
        float delay = Mathf.Max(10f, ttlSeconds - 20f);
        Debug.Log($"⏱️ Auto-refresh will occur every {delay} seconds");

        while (true)
        {
            yield return new WaitForSeconds(delay);
            Debug.Log("🔄 Auto-refreshing QR code...");
            yield return RequestQRToken();
        }
    }

    void GenerateQRCode(string token)
    {
        if (qrImage == null)
        {
            Debug.LogError("❌ QR Image reference is null!");
            return;
        }

        try
        {
            var writer = new BarcodeWriter<Texture2D>
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new ZXing.Common.EncodingOptions
                {
                    Height = 512,
                    Width = 512,
                    Margin = 2
                },
                Renderer = new ZXing.Rendering.Texture2DRenderer()
            };

            Texture2D qrTexture = writer.Write(token);
            qrImage.texture = qrTexture;

            Debug.Log("✅ QR Code image generated successfully");
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Failed to generate QR code: {e.Message}");
        }
    }

    // ----------------------------------------------------------------------
    // FRAME SELECTION
    // ----------------------------------------------------------------------
    public void OnGuestBtnClick()
    {
        Debug.Log("👤 Guest login selected");
        ActivateFrameSelection(isGuest: true);
    }

    void ActivateFrameSelection(bool isGuest)
    {
        if (frameSelectionPanel == null)
        {
            Debug.LogError("❌ Frame Selection Panel is null!");
            return;
        }

        frameSelectionPanel.SetActive(true);
        lastActivityTime = Time.time;

        Debug.Log($"🎨 Frame selection activated (Guest: {isGuest})");

        // Disable "My Frames" button for guests
        if (frameManager != null && frameManager.myFrameButton != null)
        {
            frameManager.myFrameButton.interactable = !isGuest;
            Debug.Log($"🖼️ My Frames button: {(isGuest ? "Disabled" : "Enabled")}");
        }

        // Stop any existing timeout routine
        if (panelTimeoutRoutine != null)
            StopCoroutine(panelTimeoutRoutine);

        // Start inactivity timeout
        panelTimeoutRoutine = StartCoroutine(FramePanelAutoClose());
    }

    IEnumerator FramePanelAutoClose()
    {
        Debug.Log($"⏱️ Frame panel will auto-close after {framePanelTimeoutSeconds} seconds of inactivity");

        while (frameSelectionPanel.activeSelf)
        {
            float inactiveTime = Time.time - lastActivityTime;

            if (inactiveTime >= framePanelTimeoutSeconds)
            {
                frameSelectionPanel.SetActive(false);
                Debug.Log("⏰ Frame Selection Panel closed due to inactivity");
                yield break;
            }

            yield return null;
        }
    }

    private void Update()
    {
        // Track user activity for inactivity timeout
        if (frameSelectionPanel != null && frameSelectionPanel.activeSelf)
        {
            if (Input.anyKeyDown ||
                Input.GetMouseButtonDown(0) ||
                Input.GetMouseButtonDown(1) ||
                Input.GetMouseButtonDown(2) ||
                Input.GetAxis("Mouse ScrollWheel") != 0f ||
                Input.touchCount > 0)
            {
                lastActivityTime = Time.time;
            }
        }

        // Dispatch WebSocket messages (required for non-WebGL builds)
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
    }

    // ----------------------------------------------------------------------
    // WEBSOCKET CONNECTION
    // ----------------------------------------------------------------------
    private async void ConnectWebSocket(string token)
    {
        string protocol = useSecureWebSocket ? "wss" : "ws";
        string wsUrl = $"{protocol}://photo-stg-api.chvps3.aozora-okinawa.com/app/{boothKey}";

        Debug.Log($"🔌 Connecting to WebSocket: {wsUrl}");
        Debug.Log($"🔒 Secure Connection: {useSecureWebSocket}");

        // For Windows, try creating with options
        try
        {
            ws = new WebSocket(wsUrl);
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Failed to create WebSocket: {e.Message}");
            return;
        }

        ws.OnOpen += async () =>
        {
            isWebSocketConnected = true;
            Debug.Log("🟢 WebSocket Connected!");

            // Subscribe to the QR login channel
            var subscribeMsg = new PusherSubscribeEvent
            {
                @event = "pusher:subscribe",
                data = new SubscribeData
                {
                    channel = $"qr-login.{token}"
                }
            };

            string json = JsonUtility.ToJson(subscribeMsg);

            Debug.Log($"📡 Subscribing to channel: qr-login.{token}");
            Debug.Log($"📤 Subscribe message: {json}");

            await ws.SendText(json);
        };

        ws.OnError += (e) =>
        {
            Debug.LogError($"❌ WebSocket Error: {e}");
            isWebSocketConnected = false;
        };

        ws.OnClose += (e) =>
        {
            Debug.Log($"🔴 WebSocket Closed (Code: {e})");
            isWebSocketConnected = false;
        };

        ws.OnMessage += (bytes) =>
        {
            string message = Encoding.UTF8.GetString(bytes);
            Debug.Log($"📩 WebSocket Message Received: {message}");

            HandleWebSocketMessage(message);
        };

        try
        {
            await ws.Connect();
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Failed to connect WebSocket: {e.Message}");
            isWebSocketConnected = false;
        }
    }

    // ----------------------------------------------------------------------
    // WEBSOCKET MESSAGE HANDLER
    // ----------------------------------------------------------------------
    private void HandleWebSocketMessage(string json)
    {
        try
        {
            // Parse the base message structure
            PusherMessage baseMsg = JsonUtility.FromJson<PusherMessage>(json);

            if (baseMsg == null || string.IsNullOrEmpty(baseMsg.@event))
            {
                Debug.LogWarning("⚠️ Received invalid or empty message");
                return;
            }

            Debug.Log($"📨 Event Type: {baseMsg.@event}");

            // Handle subscription confirmation
            if (baseMsg.@event == "pusher_internal:subscription_succeeded")
            {
                Debug.Log("✅ Successfully subscribed to channel! Waiting for user login...");
                return;
            }

            // Handle user login event
            if (baseMsg.@event == "user-logged-in")
            {
                Debug.Log("🎉 USER-LOGGED-IN EVENT RECEIVED!");

                if (string.IsNullOrEmpty(baseMsg.data))
                {
                    Debug.LogError("❌ Event data is null or empty");
                    return;
                }

                Debug.Log($"📦 Raw data field: {baseMsg.data}");

                // CRITICAL: The 'data' field is a JSON string, not an object
                // We need to parse it again to get the actual user session data
                UserSessionWrapper wrapper = JsonUtility.FromJson<UserSessionWrapper>(baseMsg.data);

                if (wrapper == null)
                {
                    Debug.LogError("❌ Failed to parse UserSessionWrapper from data field");
                    return;
                }

                if (wrapper.session == null)
                {
                    Debug.LogError("❌ Session object is null in wrapper");
                    return;
                }

                // Successfully parsed user session!
                UserSession session = wrapper.session;

                Debug.Log("=====================================");
                Debug.Log("✅ USER LOGGED IN SUCCESSFULLY!");
                Debug.Log("=====================================");
                Debug.Log($"👤 Name       : {session.user_name ?? "N/A"}");
                Debug.Log($"📧 Email      : {session.user_email ?? "N/A"}");
                Debug.Log($"🆔 User ID    : {session.user_id ?? "N/A"}");
                Debug.Log($"🎫 Session ID : {session.session_id ?? "N/A"}");
                Debug.Log($"🏢 Booth ID   : {session.booth_id ?? "N/A"}");
                Debug.Log($"⏰ Started At : {session.started_at ?? "N/A"}");
                Debug.Log("=====================================");

                // Activate frame selection for logged-in user
                ActivateFrameSelection(isGuest: false);

                // Optional: Close WebSocket after successful login
                CloseWebSocket();
            }
            else if (baseMsg.@event.StartsWith("pusher"))
            {
                // Other Pusher internal events (can be ignored or logged)
                Debug.Log($"ℹ️ Pusher internal event: {baseMsg.@event}");
            }
            else
            {
                // Unknown event
                Debug.LogWarning($"⚠️ Unknown event type: {baseMsg.@event}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Error parsing WebSocket message: {e.Message}");
            Debug.LogError($"❌ Stack trace: {e.StackTrace}");
        }
    }

    // ----------------------------------------------------------------------
    // CLEANUP
    // ----------------------------------------------------------------------
    private async void CloseWebSocket()
    {
        if (ws != null && isWebSocketConnected)
        {
            try
            {
                await ws.Close();
                Debug.Log("🔌 WebSocket closed");
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error closing WebSocket: {e.Message}");
            }
            finally
            {
                isWebSocketConnected = false;
            }
        }
    }

    private async void OnDestroy()
    {
        // Stop all coroutines
        if (autoRefreshRoutine != null)
            StopCoroutine(autoRefreshRoutine);

        if (panelTimeoutRoutine != null)
            StopCoroutine(panelTimeoutRoutine);

        // Close WebSocket connection
        CloseWebSocket();
    }

    private void OnApplicationQuit()
    {
        CloseWebSocket();
    }

    // ----------------------------------------------------------------------
    // DATA MODELS
    // ----------------------------------------------------------------------

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

    // Pusher/Reverb WebSocket message structure
    [Serializable]
    public class PusherMessage
    {
        public string @event;
        public string data;  // This is a JSON string, not an object!
    }

    [Serializable]
    public class PusherSubscribeEvent
    {
        public string @event;
        public SubscribeData data;
    }

    [Serializable]
    public class SubscribeData
    {
        public string channel;
    }

    // User login event payload
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