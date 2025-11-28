using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using ZXing;
using Newtonsoft.Json;

public class PaymentManager : MonoBehaviour
{
    public static PaymentManager Instance;

    [Header("UI References")]
    public GameObject paymentPanel;
    public TMP_Text priceText;
    public RawImage qrCodeImage;
    public Button cancelButton;
    public GameObject loadingIndicator;

    [Header("Payment Settings")]
    public string apiBaseURL = "http://photo-stg-api.chvps3.aozora-okinawa.com/";
    public float paymentCheckInterval = 2f;
    public float paymentTimeout = 300f;

    [Header("Mock Payment")]
    public bool useMockPayment = false;
    public float mockPaymentDelay = 3f;

    [Header("References")]
    public PhotoBoothFrameManager frameManager;
    public GatchaManager gatchaManager;

    private string currentPaymentId;
    private string currentBoothId;
    private float currentPrice;
    private PaymentType currentPaymentType;
    private Coroutine paymentCheckCoroutine;
    private int pendingGachaButtonIndex = -1;

    private FrameItem frameAfterPayment;
    public enum PaymentType { Frame, Gacha }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        paymentPanel?.SetActive(false);
        cancelButton?.onClick.AddListener(OnCancelPayment);
        if (loadingIndicator != null) loadingIndicator.SetActive(false);
    }

    #region Public Methods
    public void InitiateFramePayment(string boothId, FrameItem selectedFrame, string price)
    {
        if (string.IsNullOrEmpty(boothId) || selectedFrame == null) return;

        currentPaymentType = PaymentType.Frame;
        currentBoothId = boothId;
        currentPrice = float.Parse(price);

        frameAfterPayment = selectedFrame;

        ShowPaymentPanel(currentPrice);
        StartCoroutine(InitiatePaymentRequest());
    }


    public void InitiateGachaPayment(string boothId, int buttonIndex, string price)
    {
        if (string.IsNullOrEmpty(boothId)) return;

        currentPaymentType = PaymentType.Gacha;
        currentBoothId = boothId;
        pendingGachaButtonIndex = buttonIndex;
        currentPrice = float.Parse(price);

        ShowPaymentPanel(currentPrice);
        StartCoroutine(InitiatePaymentRequest());
    }


    #endregion



    #region Payment Flow

    private void ShowPaymentPanel(float price)
    {
        paymentPanel.SetActive(true);
        priceText.text = $"¥{price:F0}";

        if (qrCodeImage != null) qrCodeImage.gameObject.SetActive(false);
        if (loadingIndicator != null) loadingIndicator.SetActive(true);
    }

    private IEnumerator InitiatePaymentRequest()
    {
        if (useMockPayment)
        {
            yield return new WaitForSeconds(mockPaymentDelay);
            OnPaymentSuccess();
            yield break;
        }

        string url = $"{apiBaseURL}api/booths/{currentBoothId}/payment/initiate";

        // Fetch session/user ID from PlayerPrefs
        string sessionId = PlayerPrefs.GetString("session_id", "");
        string userId = PlayerPrefs.GetString("user_id", "");
        string mode = string.IsNullOrEmpty(userId) ? "guest" : "user";

        var payload = new
        {
            provider = "paypay",
            amount = currentPrice,
            session_id = sessionId,
            user_id = userId,
            mode = mode
        };

        string jsonPayload = JsonConvert.SerializeObject(payload);
        Debug.Log("Payment Request Payload: " + jsonPayload);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            string responseText = request.downloadHandler.text;
            Debug.Log("Payment API Response: " + responseText);

            if (request.result == UnityWebRequest.Result.Success)
            {
                PaymentInitiateResponse res = null;
                try
                {
                    res = JsonConvert.DeserializeObject<PaymentInitiateResponse>(responseText);
                }
                catch (Exception e)
                {
                    Debug.LogError("JSON Parse Exception: " + e.Message);
                    OnPaymentFailed("Failed to parse payment response.");
                    yield break;
                }

                if (res == null)
                {
                    Debug.LogError("Payment response is null.");
                    OnPaymentFailed("Payment response is empty.");
                    yield break;
                }

                if (!res.success)
                {
                    Debug.LogWarning("Payment request failed on server.");
                    OnPaymentFailed("Payment request failed on server.");
                    yield break;
                }

                // Safe null handling for fields
                currentPaymentId = res.payment_id ?? "";
                string accessId = res.access_id ?? "";
                string token = res.token ?? "";
                string startUrl = res.start_url ?? "";

                if (string.IsNullOrEmpty(currentPaymentId) || string.IsNullOrEmpty(accessId) ||
                    string.IsNullOrEmpty(token) || string.IsNullOrEmpty(startUrl))
                {
                    Debug.LogWarning("Payment response missing some fields.");
                }

                string qrData = $"access_id={accessId}&token={token}&start_url={startUrl}";
                GenerateQRCode(qrData);

                if (paymentCheckCoroutine != null) StopCoroutine(paymentCheckCoroutine);
                paymentCheckCoroutine = StartCoroutine(CheckPaymentStatus());
            }
            else
            {
                Debug.LogError($"Payment Request Failed: {request.error}");
                OnPaymentFailed($"Payment request error: {request.error}");
            }

            if (loadingIndicator != null) loadingIndicator.SetActive(false);
        }
    }


    private IEnumerator CheckPaymentStatus()
    {
        float elapsed = 0f;
        while (elapsed < paymentTimeout)
        {
            yield return new WaitForSeconds(paymentCheckInterval);
            elapsed += paymentCheckInterval;

            string url = $"{apiBaseURL}api/photobooth/payments/{currentPaymentId}/status";
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var res = JsonConvert.DeserializeObject<PaymentStatusResponse>(request.downloadHandler.text);
                        if (res != null && res.success)
                        {
                            string status = res.data.status.ToLower();
                            if (status == "completed" || status == "success") { OnPaymentSuccess(); yield break; }
                            if (status == "failed" || status == "cancelled") { OnPaymentFailed("Payment was cancelled."); yield break; }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("Payment Status Parse Error: " + e.Message);
                    }
                }
            }
        }

        OnPaymentFailed("Payment timed out.");
    }

    #endregion

    #region QR Code

    private void GenerateQRCode(string data)
    {
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

        Texture2D tex = writer.Write(data);
        qrCodeImage.texture = tex;
        qrCodeImage.rectTransform.sizeDelta = new Vector2(512, 512);
        qrCodeImage.gameObject.SetActive(true);
    }

    #endregion

    #region Payment Handlers

    private void OnPaymentSuccess()
    {
        Debug.Log("✅ Payment successful!");
        paymentCheckCoroutine = null;

        StartCoroutine(HidePanelAndProceed());
    }

    private IEnumerator HidePanelAndProceed()
    {
        yield return new WaitForSeconds(1.5f);
        paymentPanel.SetActive(false);

        if (currentPaymentType == PaymentType.Frame)
        {
            if (frameAfterPayment != null)
                frameManager?.ContinueAfterPayment(frameAfterPayment);
            else
                Debug.LogWarning("No frame stored for ContinueAfterPayment!");
        }
        else if (currentPaymentType == PaymentType.Gacha)
        {
            gatchaManager?.ContinueGachaAfterPayment(pendingGachaButtonIndex);
        }

        ResetPaymentState();
    }


    private void OnPaymentFailed(string message)
    {
        Debug.LogWarning("❌ Payment Failed: " + message);
        paymentCheckCoroutine = null;
        StartCoroutine(AutoClosePanel());
    }

    private IEnumerator AutoClosePanel()
    {
        yield return new WaitForSeconds(3f);
        ClosePaymentPanel();
    }

    public void OnCancelPayment()
    {
        Debug.Log("❌ Payment cancelled by user");
        if (paymentCheckCoroutine != null) StopCoroutine(paymentCheckCoroutine);
        paymentCheckCoroutine = null;

        if (!string.IsNullOrEmpty(currentPaymentId)) StartCoroutine(CancelPaymentRequest());
        ClosePaymentPanel();
    }

    private IEnumerator CancelPaymentRequest()
    {
        string url = $"{apiBaseURL}api/photobooth/payments/{currentPaymentId}/cancel";
        using (UnityWebRequest request = UnityWebRequest.PostWwwForm(url, ""))
        {
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success) Debug.Log("Payment cancelled on server");
            else Debug.LogWarning($"Cancel payment failed: {request.error}");
        }
    }

    private void ClosePaymentPanel()
    {
        paymentPanel.SetActive(false);
        ResetPaymentState();
    }

    private void ResetPaymentState()
    {
        currentPaymentId = null;
        currentBoothId = null;
        currentPrice = 0f;
        pendingGachaButtonIndex = -1;
    }

    #endregion

    #region Data Classes

    [Serializable]
    private class PaymentInitiateResponse
    {
        public bool success;
        public string order_id;
        public string payment_id;
        public string access_id;
        public string token;
        public string start_url;
        public string startlimitduration;
    }

    [Serializable]
    private class PaymentStatusResponse
    {
        public bool success;
        public PaymentStatusData data;
    }

    [Serializable]
    private class PaymentStatusData
    {
        public string payment_id;
        public string status;
        public float amount;
    }

    #endregion
}
