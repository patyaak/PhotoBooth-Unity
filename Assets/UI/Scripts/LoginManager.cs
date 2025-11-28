using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;  // Required for Task
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
    public string baseUrl = "https://photo-stg-api.chvps3.aozora-okinawa.com";
    public int ttlSeconds = 160;
    public string boothKey = "boothkey123"; // Reverb / Pusher App Key

    [Header("WebSocket Config")]
    public bool useSecureWebSocket = true;

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
    private WebSocket ws;
    private bool isWebSocketConnected = false;
    private float lastActivityTime = 0f;

    // =======================================================================
    // INITIALIZATION
    // =======================================================================
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

    // =======================================================================
    // QR GENERATION
    // =======================================================================
    private void OnGenerateQRClicked()
    {
        qrPanel.SetActive(true);
        StartCoroutine(RequestQRToken());

        if (autoRefreshRoutine != null) StopCoroutine(autoRefreshRoutine);
        autoRefreshRoutine = StartCoroutine(AutoRefreshQR());
    }

    IEnumerator RequestQRToken()
    {
        string url = $"{baseUrl}/api/qr-login/issue";
        QRRequestData data = new QRRequestData(boothId, ttlSeconds);
        string json = JsonUtility.ToJson(data);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                QRResponse res = JsonUtility.FromJson<QRResponse>(request.downloadHandler.text);
                currentToken = res.data.token;
                GenerateQRCode(currentToken);
                ConnectWebSocket();
            }
            else
            {
                Debug.LogError("QR Token Request Failed: " + request.error);
                Debug.LogError("Response: " + request.downloadHandler.text);
            }
        }
    }

    IEnumerator AutoRefreshQR()
    {
        float delay = Mathf.Max(10f, ttlSeconds - 20f);
        while (true)
        {
            yield return new WaitForSeconds(delay);
            if (!string.IsNullOrEmpty(currentToken))
                yield return RequestQRToken();
        }
    }

    private void GenerateQRCode(string token)
    {
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
    }

    // =======================================================================
    // WEBSOCKET (FULLY FIXED + AWAITABLE CLOSE)
    // =======================================================================
    private async void ConnectWebSocket()
    {
        if (string.IsNullOrEmpty(boothKey))
        {
            Debug.LogError("boothKey is EMPTY!");
            return;
        }

        string protocol = useSecureWebSocket ? "wss" : "ws";
        string wsUrl = $"{protocol}://photo-stg-api.chvps3.aozora-okinawa.com/app/{boothKey}";

        await CloseWebSocketAsync(); // Ensure old connection is closed

        ws = new WebSocket(wsUrl);

        ws.OnOpen += () =>
        {
            isWebSocketConnected = true;
            Debug.Log("WebSocket Connected!");
            if (!string.IsNullOrEmpty(currentToken))
                SendSubscription(currentToken);
        };

        ws.OnError += (e) => { Debug.LogError("WS Error: " + e); isWebSocketConnected = false; };
        ws.OnClose += (code) => { Debug.LogWarning("WS Closed: " + code); isWebSocketConnected = false; };

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
            Debug.LogError("WS Connect Failed: " + ex.Message);
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
        Debug.Log("Subscribing to: qr-login." + token);
        await ws.SendText(json);
    }

    private void HandleWebSocketMessage(string json)
    {
        Debug.Log("WS Message: " + json);

        try
        {
            var envelope = JsonUtility.FromJson<PusherEnvelope>(json);

            if (envelope.@event == "pusher_internal:subscription_succeeded")
            {
                Debug.Log("Subscribed to channel successfully!");
                return;
            }

            if (envelope.@event == "user-logged-in")
            {
                Debug.Log("USER LOGGED IN VIA QR SCAN!");

                UserSessionWrapper wrapper = JsonUtility.FromJson<UserSessionWrapper>(envelope.data);

                if (wrapper?.session == null)
                {
                    Debug.LogError("Failed to parse session: " + envelope.data);
                    return;
                }

                var s = wrapper.session;
                Debug.Log($"Welcome {s.user_name} ({s.user_email})");

                PlayerPrefs.SetString("user_id", s.user_id);
                PlayerPrefs.SetString("user_name", s.user_name);
                PlayerPrefs.SetString("user_email", s.user_email);
                PlayerPrefs.SetString("session_id", s.session_id);
                PlayerPrefs.SetString("booth_id", s.booth_id);
                PlayerPrefs.Save();

                ActivateFrameSelection(isGuest: false);
                CloseWebSocketAsync(); // Fire and forget (safe)
            }
        }
        catch (Exception e)
        {
            Debug.LogError("WS Parse Error: " + e.Message + "\nJSON: " + json);
        }
    }

    // =======================================================================
    // CLEAN AWAITABLE WEBSOCKET CLOSE (THE FIX YOU NEEDED)
    // =======================================================================
    private async Task CloseWebSocketAsync()
    {
        if (ws == null) return;

        if (ws.State == WebSocketState.Open || ws.State == WebSocketState.Connecting)
        {
            try
            {
                Debug.Log("Closing WebSocket...");
                await ws.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("WS close warning: " + ex.Message);
            }
        }

        ws = null;
        isWebSocketConnected = false;
    }

    // Backward compatible method
    private async void CloseWebSocket()
    {
        await CloseWebSocketAsync();
    }

    // =======================================================================
    // UI & FRAME SELECTION
    // =======================================================================
    public void OnGuestBtnClick()
    {
        PlayerPrefs.DeleteKey("user_id");
        PlayerPrefs.DeleteKey("user_name");
        PlayerPrefs.DeleteKey("session_id");
        ActivateFrameSelection(isGuest: true);
    }

    private void ActivateFrameSelection(bool isGuest)
    {
        qrPanel.SetActive(false);
        frameSelectionPanel.SetActive(true);
        lastActivityTime = Time.time;

        if (frameManager != null && frameManager.myFrameButton != null)
            frameManager.myFrameButton.interactable = !isGuest;

        if (panelTimeoutRoutine != null) StopCoroutine(panelTimeoutRoutine);
        panelTimeoutRoutine = StartCoroutine(FramePanelAutoClose());
    }

    IEnumerator FramePanelAutoClose()
    {
        while (frameSelectionPanel.activeSelf)
        {
            if (Time.time - lastActivityTime >= framePanelTimeoutSeconds)
            {
                frameSelectionPanel.SetActive(false);
                qrPanel.SetActive(true);
                yield break;
            }
            yield return null;
        }
    }

    // =======================================================================
    // APPLICATION QUIT – NOW ACTUALLY WAITS FOR WEBSOCKET CLOSE
    // =======================================================================
    private async void OnApplicationQuit()
    {
        await CloseWebSocketAsync();
        await Task.Delay(100); // Let OS cleanup
    }

    // =======================================================================
    // SERIALIZABLE CLASSES
    // =======================================================================
    [Serializable] public class QRRequestData { public string booth_id; public int ttl_seconds; public QRRequestData(string id, int ttl) { booth_id = id; ttl_seconds = ttl; } }
    [Serializable] public class QRResponse { public bool success; public QRData data; }
    [Serializable] public class QRData { public string token; public string token_id; public string expires_at; public string booth_id; }

    [Serializable] private class PusherEnvelope { public string @event; public string data; public string channel; }
    [Serializable] private class PusherSubscribeEvent { public string @event; public SubscribeData data; }
    [Serializable] private class SubscribeData { public string channel; }

    [Serializable] public class UserSessionWrapper { public UserSession session; }

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