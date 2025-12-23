using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NativeWebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using ZXing;

public class PaymentManager : MonoBehaviour
{
    public static PaymentManager Instance;

    [Header("UI References")]
    public GameObject paymentPanel;
    public TMP_Text priceText;
    public RawImage qrCodeImage;
    public Button cancelButton;

    [Header("Payment Settings")]
    public float mockPaymentDelay = 3f;
    public bool useMockPayment = false;

    [Header("References")]
    public PhotoBoothFrameManager frameManager;
    public GatchaManager gatchaManager;

    private string currentBoothId;
    private float currentPrice;

    private string currentFrameType = "default";
    public string currentFrameId;

    public bool paymentActive = false;

    public PaymentType currentPaymentType { get; private set; } = PaymentType.None;
    private int pendingGachaButtonIndex = -1;
    private FrameItem frameAfterPayment;
    public string currentOrderId;

    private WebSocket ws;
    private bool isWebSocketConnected = false;

    private bool isInGachaFlow = false;
    public bool IsInGachaFlow() => isInGachaFlow;

    public enum PaymentType { None = 0, Frame = 1, Gacha = 2 }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        paymentPanel?.SetActive(false);
        if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelPayment);
    }

    private void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
    }

    #region Public Methods
    public void InitiateFramePayment(string boothId, FrameItem selectedFrame, string price, string frameType = "default")
    {
        if (gatchaManager != null && gatchaManager.gatchaWin != null && gatchaManager.gatchaWin.activeSelf)
        {
            Debug.Log("⚠️ Ignoring frame payment request - gacha reveal in progress");
            return;
        }

        if (frameType == "myframe")
        {
            Debug.LogWarning("⚠️ Payment attempted for myframe - this should be handled before reaching PaymentManager");
            return;
        }

        if (string.IsNullOrEmpty(boothId) || selectedFrame == null) return;

        currentFrameId = selectedFrame.frameData.frame_id;

        // Check if we're in offline mode
        if (IsOfflineMode())
        {
            Debug.Log("📵 Offline mode detected - proceeding without payment/API call");

            currentPaymentType = PaymentType.Frame;
            frameAfterPayment = selectedFrame;
            currentFrameType = frameType;

            // Generate offline order ID
            currentOrderId = $"offline_{System.Guid.NewGuid().ToString()}";

            // Skip payment panel and proceed directly
            frameManager?.ContinueAfterPayment(frameAfterPayment);
            return;
        }

        LoggingManager.Instance?.LogPayment(
            orderId: System.Guid.NewGuid().ToString(),
            paymentType: "frame",
            provider: "paypay",
            amount: float.Parse(price),
            status: "initiated",
            frameId: selectedFrame.frameData.frame_id
        );

        int paymentsEnabledInt = PlayerPrefs.GetInt("payments_enabled", 0);
        paymentActive = paymentsEnabledInt == 1;

        currentPaymentType = PaymentType.Frame;
        currentBoothId = boothId;
        currentPrice = float.Parse(price);
        frameAfterPayment = selectedFrame;
        currentFrameType = frameType;

        ShowPaymentPanel(currentPrice);
        StartCoroutine(InitiatePaymentRequest());
    }

    public void InitiateGachaPayment(string boothId, int buttonIndex, string price)
    {
        if (string.IsNullOrEmpty(boothId)) return;

        // Check if we're in offline mode
        if (IsOfflineMode())
        {
            Debug.Log("📵 Offline mode detected - proceeding with gacha without payment/API call");

            currentPaymentType = PaymentType.Gacha;
            pendingGachaButtonIndex = buttonIndex;

            // Generate offline order ID
            currentOrderId = $"offline_{System.Guid.NewGuid().ToString()}";

            // Set gacha flow flag
            isInGachaFlow = true;

            // Proceed directly to gacha
            gatchaManager?.SetBoothID(boothId);
            gatchaManager?.PlayGatchaAnimationAfterPayment();
            return;
        }

        int paymentsEnabledInt = PlayerPrefs.GetInt("payments_enabled", 0);
        paymentActive = paymentsEnabledInt == 1;

        currentPaymentType = PaymentType.Gacha;
        currentBoothId = boothId;
        pendingGachaButtonIndex = buttonIndex;
        currentPrice = float.Parse(price);
        currentFrameType = "gacha";

        ShowPaymentPanel(currentPrice);
        StartCoroutine(InitiatePaymentRequest());
    }

    public void OnGachaRevealComplete()
    {
        Debug.Log("[PaymentManager] OnGachaRevealComplete - clearing payment state for gacha");

        pendingGachaButtonIndex = -1;
        currentPaymentType = PaymentType.None;

        if (paymentPanel != null && paymentPanel.activeSelf)
            paymentPanel.SetActive(false);

        _ = CloseWebSocketAsync();

        Debug.Log($"✅ Payment state cleared. Gacha flow flag: {isInGachaFlow}");
    }
    #endregion

    public void ClearGachaFlowFlag()
    {
        Debug.Log("[PaymentManager] Clearing gacha flow flag - shooting started");
        isInGachaFlow = false;
    }

    /// <summary>
    /// Check if we're in offline mode (no internet + payments/login disabled)
    /// </summary>
    private bool IsOfflineMode()
    {
        // Check if server is offline
        bool serverOffline = ServerConnectivityManager.Instance != null &&
                            !ServerConnectivityManager.Instance.IsServerOnline();

        if (!serverOffline)
            return false;

        // Check if offline mode is allowed
        int paymentsEnabled = PlayerPrefs.GetInt("payments_enabled", 1);
        int loginRequired = PlayerPrefs.GetInt("login_required", 1);

        bool offlineAllowed = (paymentsEnabled == 0) && (loginRequired == 0);

        Debug.Log($"🔍 Offline mode check: serverOffline={serverOffline}, paymentsEnabled={paymentsEnabled}, loginRequired={loginRequired}, allowed={offlineAllowed}");

        return offlineAllowed;
    }

    #region Payment Flow
    private void ShowPaymentPanel(float price)
    {
        if (paymentPanel == null) return;
        paymentPanel.SetActive(true);
        priceText.text = $"¥{price:F0}";
        qrCodeImage?.gameObject.SetActive(false);
    }

    private IEnumerator InitiatePaymentRequest()
    {
        if (useMockPayment)
        {
            yield return new WaitForSeconds(mockPaymentDelay);
            OnPaymentSuccess();
            yield break;
        }

        string url = $"{API.BaseURL}/api/booths/{currentBoothId}/payment/initiate";
        string sessionId = PlayerPrefs.GetString("session_id", "");
        string userId = PlayerPrefs.GetString("user_id", "");
        string mode = string.IsNullOrEmpty(userId) ? "guest" : "user";

        var payload = new
        {
            provider = "paypay",
            amount = currentPrice,
            session_id = sessionId,
            user_id = userId,
            mode = mode,
            frametype = currentFrameType,
            frame_id = currentFrameId,
            payment_active = paymentActive
        };

        string jsonPayload = JsonConvert.SerializeObject(payload);
        Debug.Log("Payment Request Payload: " + jsonPayload);

        yield return LoggedWebRequest.Post(url, jsonPayload, (request) =>
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                OnPaymentFailed($"Payment request error: {request.error}");
                return;
            }

            PaymentInitiateResponse res;
            try { res = JsonConvert.DeserializeObject<PaymentInitiateResponse>(request.downloadHandler.text); }
            catch (Exception e) { OnPaymentFailed("Failed to parse payment response: " + e.Message); return; }

            if (res == null || !res.success || string.IsNullOrEmpty(res.order_id))
            {
                OnPaymentFailed("Payment initiation failed.");
                return;
            }

            currentOrderId = res.order_id;
            Debug.Log($"✅ Order ID received: {currentOrderId}");

            if (paymentActive && !string.IsNullOrEmpty(res.start_url))
            {
                GenerateQRCode(res.start_url);
                ConnectWebSocketForPayment(currentOrderId);
            }
            else
            {
                Debug.Log("💡 Payment OFF → skipping QR/WS, continuing after order_id generation");

                // ✅ For gacha without payment, set the flag immediately
                if (currentPaymentType == PaymentType.Gacha)
                {
                    isInGachaFlow = true;
                    string boothIdToUse = currentBoothId;
                    ResetPaymentState();

                    if (!string.IsNullOrEmpty(boothIdToUse))
                    {
                        gatchaManager?.SetBoothID(boothIdToUse);
                        gatchaManager?.PlayGatchaAnimationAfterPayment();
                    }
                }
                else
                {
                    frameManager?.ContinueAfterPayment(frameAfterPayment);
                }
            }
        });
    }
    #endregion

    #region QR Code
    private void GenerateQRCode(string data)
    {
        var writer = new BarcodeWriter<Texture2D>
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new ZXing.Common.EncodingOptions { Width = 400, Height = 400, Margin = 0 },
            Renderer = new ZXing.Rendering.Texture2DRenderer()
        };

        qrCodeImage.texture = writer.Write(data);
        qrCodeImage.rectTransform.sizeDelta = new Vector2(400, 400);
        qrCodeImage.gameObject.SetActive(true);
    }
    #endregion

    #region WebSocket Payment
    private async void ConnectWebSocketForPayment(string orderId)
    {
        if (string.IsNullOrEmpty(orderId)) return;

        await CloseWebSocketAsync();

        string wsUrl = $"wss://photo-stg-api.chvps3.aozora-okinawa.com/app/{LoginManager.Instance.boothKey}";
        ws = new WebSocket(wsUrl);

        ws.OnOpen += () => { isWebSocketConnected = true; Debug.Log("Payment WS Connected!"); SendPaymentSubscription(orderId); };
        ws.OnError += (e) => { isWebSocketConnected = false; Debug.LogError("Payment WS Error: " + e); };
        ws.OnClose += (code) => { isWebSocketConnected = false; Debug.LogWarning("Payment WS Closed: " + code); };
        ws.OnMessage += (bytes) => { HandlePaymentWebSocketMessage(Encoding.UTF8.GetString(bytes)); };

        try { await ws.Connect(); }
        catch (Exception ex) { Debug.LogError("Payment WS Connect Failed: " + ex.Message); }
    }

    private async void SendPaymentSubscription(string orderId)
    {
        if (!isWebSocketConnected || ws == null) return;

        var sub = new PusherSubscribeEvent { Event = "pusher:subscribe", data = new SubscribeData { channel = $"payment_status.{orderId}" } };
        try { await ws.SendText(JsonConvert.SerializeObject(sub)); }
        catch (Exception ex) { Debug.LogError("Failed to send subscribe: " + ex.Message); }
    }

    private void HandlePaymentWebSocketMessage(string json)
    {
        Debug.Log("WS RAW MESSAGE: " + json);

        try
        {
            var j = JObject.Parse(json);
            Debug.Log("Parsed WS JSON: " + j.ToString());

            string evt = (string)(j["event"] ?? j["@event"]);
            if (!string.IsNullOrEmpty(evt))
            {
                if (evt == "payment-updated" && j["data"] != null)
                {
                    JObject dataObj = null;

                    if (j["data"].Type == JTokenType.String)
                        dataObj = JObject.Parse(j["data"].ToString());
                    else if (j["data"].Type == JTokenType.Object)
                        dataObj = (JObject)j["data"];

                    if (dataObj != null)
                    {
                        string orderId = dataObj["order_id"]?.ToString() ?? dataObj["orderId"]?.ToString();
                        string status = dataObj["status"]?.ToString()?.ToLower();

                        Debug.Log($"Payment Updated: orderId={orderId}, status={status}");

                        if (orderId == currentOrderId)
                        {
                            if (status == "succeeded" || status == "success") OnPaymentSuccess();
                            else if (status == "failed") OnPaymentFailed("Payment failed via backend");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("Unhandled WS event: " + evt);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("WS Parse Error: " + ex.Message);
        }
    }

    private async Task CloseWebSocketAsync()
    {
        if (ws == null) return;
        if (ws.State == WebSocketState.Open || ws.State == WebSocketState.Connecting)
        {
            try { await ws.Close(); }
            catch (Exception ex) { Debug.LogWarning("WS close warning: " + ex.Message); }
        }
        ws = null;
        isWebSocketConnected = false;
    }
    #endregion

    #region Payment Handlers
    private void OnPaymentSuccess()
    {
        LoggingManager.Instance?.LogPayment(
            orderId: currentOrderId,
            paymentType: currentPaymentType == PaymentType.Frame ? "frame" : "gacha",
            provider: "paypay",
            amount: currentPrice,
            status: "success",
            frameId: frameAfterPayment?.frameData.frame_id
        );

        Debug.Log("✅ Payment successful!");
        StartCoroutine(HidePanelAndProceed());
    }

    private IEnumerator HidePanelAndProceed()
    {
        yield return new WaitForSeconds(1f);

        if (paymentPanel != null) paymentPanel.SetActive(false);

        if (currentPaymentType == PaymentType.Frame && frameAfterPayment != null)
        {
            var frameToContinue = frameAfterPayment;
            ResetPaymentState();
            frameManager?.ContinueAfterPayment(frameToContinue);
        }
        else if (currentPaymentType == PaymentType.Gacha)
        {
            // ✅ Set the gacha flow flag BEFORE clearing payment state
            isInGachaFlow = true;

            string boothIdToUse = currentBoothId;
            ResetPaymentState();

            Debug.Log("✅ Payment complete for gacha - gacha flow flag SET");

            if (!string.IsNullOrEmpty(boothIdToUse))
            {
                gatchaManager?.SetBoothID(boothIdToUse);
                gatchaManager?.PlayGatchaAnimationAfterPayment();
            }
        }
        else
        {
            ResetPaymentState();
        }

        _ = CloseWebSocketAsync();
    }

    private void OnPaymentFailed(string message)
    {
        LoggingManager.Instance?.LogPayment(
            orderId: currentOrderId,
            paymentType: currentPaymentType == PaymentType.Frame ? "frame" : "gacha",
            provider: "paypay",
            amount: currentPrice,
            status: "failed",
            frameId: frameAfterPayment?.frameData.frame_id,
            errorMessage: message
        );

        Debug.LogWarning("❌ Payment Failed: " + message);
        StartCoroutine(AutoClosePanel());

        _ = CloseWebSocketAsync();
    }

    private IEnumerator AutoClosePanel()
    {
        yield return new WaitForSeconds(3f);
        if (paymentPanel != null) paymentPanel.SetActive(false);
        ResetPaymentState();
    }

    public void OnCancelPayment()
    {
        Debug.Log("❌ Payment cancelled by user");
        if (paymentPanel != null) paymentPanel.SetActive(false);
        ResetPaymentState();
        _ = CloseWebSocketAsync();
    }

    public void ResetPaymentState()
    {
        currentBoothId = null;
        currentPrice = 0f;
        pendingGachaButtonIndex = -1;
        frameAfterPayment = null;
        currentPaymentType = PaymentType.None;
        currentFrameType = "default";
        currentFrameId = null;

        Debug.Log($"ℹ️ Payment state reset (order_id preserved: {currentOrderId})");
    }

    public void InitiateFramePaymentForDecide(string boothId, FrameItem selectedFrame, string price, string frameType = "default")
    {
        if (string.IsNullOrEmpty(boothId) || selectedFrame == null) return;

        currentFrameId = selectedFrame.frameData.frame_id;
        currentFrameType = frameType;
        frameAfterPayment = selectedFrame;

        currentPaymentType = PaymentType.Frame;
        currentBoothId = boothId;
        currentPrice = float.Parse(price);

        // Check if we're in offline mode
        if (IsOfflineMode())
        {
            Debug.Log("📵 Offline mode detected - proceeding without payment/API call");

            // Generate offline order ID
            currentOrderId = $"offline_{System.Guid.NewGuid().ToString()}";

            // Skip payment panel and proceed directly
            frameManager?.ContinueAfterPayment(frameAfterPayment);
            return;
        }

        int paymentsEnabledInt = PlayerPrefs.GetInt("payments_enabled", 0);
        bool paymentsEnabled = paymentsEnabledInt == 1;

        if (frameType == "myframe")
        {
            paymentActive = false;
            Debug.Log("💡 MyFrame selected - skipping payment panel");
        }
        else if (paymentsEnabled)
        {
            paymentActive = true;
            ShowPaymentPanel(currentPrice);
        }
        else
        {
            paymentActive = false;
            Debug.Log("💡 Payment is OFF - initiating order_id generation only");
        }

        StartCoroutine(InitiatePaymentRequest());
    }
    #endregion

    #region Data Classes
    [Serializable] private class PaymentInitiateResponse { public bool success; public string order_id; public string payment_id; public string start_url; }
    [Serializable] private class CallbackResponse { public bool success; public string message; public string order_id; }
    #endregion

    #region Pusher helper classes
    private class PusherSubscribeEvent { [JsonProperty("event")] public string Event { get; set; } public SubscribeData data; }
    private class SubscribeData { public string channel; }
    #endregion

    private async void OnApplicationQuit() => await CloseWebSocketAsync();
}