using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;

public class PaymentManager : MonoBehaviour
{
    public static PaymentManager Instance;

    [Header("Payment Panel UI")]
    public GameObject paymentPanel;
    public TMP_Text priceText;
    public TMP_Text statusText;
    public Image qrCodeImage;
    public Button cancelButton;
    public GameObject loadingIndicator;

    [Header("Payment Settings")]
    public string apiBaseURL = "http://photo-stg-api.chvps3.aozora-okinawa.com/";
    public float paymentCheckInterval = 2f;
    public float paymentTimeout = 300f;

    [Header("Mock Payment (For Testing)")]
    public bool useMockPayment = false;
    public float mockPaymentDelay = 3f;

    [Header("References")]
    public PhotoBoothFrameManager frameManager;
    public GatchaManager gatchaManager;
    public PhotoShootingManager shootingManager;

    // Payment state
    private string currentPaymentId;
    private string currentBoothId;
    private float currentPrice;
    private PaymentType currentPaymentType;
    private Coroutine paymentCheckCoroutine;
    private FrameItem selectedFrame;
    private int pendingGachaButtonIndex = -1;

    public enum PaymentType
    {
        Frame,
        Gacha
    }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        if (paymentPanel != null)
            paymentPanel.SetActive(false);

        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelPayment);

        if (loadingIndicator != null)
            loadingIndicator.SetActive(false);
    }

    /// <summary>
    /// Called when user clicks decide button on a frame (default/recommendation)
    /// </summary>
    public void InitiateFramePayment(FrameItem frame, string boothId, string priceStr)
    {
        Debug.Log($"🔵 InitiateFramePayment called - Frame: {frame?.frameData?.frame_id}, Booth: {boothId}, Price: {priceStr}");

        if (frame == null)
        {
            Debug.LogError("❌ Frame is NULL!");
            return;
        }

        if (string.IsNullOrEmpty(boothId))
        {
            Debug.LogError("❌ Booth ID is empty!");
            return;
        }

        selectedFrame = frame;
        currentBoothId = boothId;

        // Parse price from string (format: "¥1000" or "1000")
        priceStr = priceStr.Replace("¥", "").Replace(",", "").Trim();
        if (!float.TryParse(priceStr, out currentPrice))
        {
            Debug.LogWarning($"⚠️ Failed to parse price: {priceStr}, using default 1000");
            currentPrice = 1000f;
        }

        currentPaymentType = PaymentType.Frame;

        Debug.Log($"💳 Showing payment panel for ¥{currentPrice}");
        ShowPaymentPanel(currentPrice);
        StartCoroutine(CreatePaymentRequest());
    }

    /// <summary>
    /// Called when user clicks play button on gacha
    /// </summary>
    public void InitiateGachaPayment(string boothId, string gachaPriceStr, int buttonIndex)
    {
        if (string.IsNullOrEmpty(boothId))
        {
            Debug.LogError("Invalid booth ID for gacha payment");
            return;
        }

        currentBoothId = boothId;
        pendingGachaButtonIndex = buttonIndex;

        // Parse gacha price
        gachaPriceStr = gachaPriceStr.Replace("¥", "").Replace(",", "").Trim();
        float.TryParse(gachaPriceStr, out currentPrice);

        currentPaymentType = PaymentType.Gacha;

        ShowPaymentPanel(currentPrice);
        StartCoroutine(CreatePaymentRequest());
    }

    private void ShowPaymentPanel(float price)
    {
        Debug.Log($"🔵 ShowPaymentPanel called with price: ¥{price}");

        if (paymentPanel == null)
        {
            Debug.LogError("❌ Payment Panel is NULL! Assign it in Inspector!");
            return;
        }

        paymentPanel.SetActive(true);
        Debug.Log("✅ Payment panel activated");

        if (priceText != null)
        {
            priceText.text = $"¥{price:F0}";
            Debug.Log($"✅ Price text set to: {priceText.text}");
        }
        else
        {
            Debug.LogWarning("⚠️ Price Text is NULL!");
        }

        if (statusText != null)
        {
            statusText.text = "QRコードを読み取って支払いを完了してください";
        }
        else
        {
            Debug.LogWarning("⚠️ Status Text is NULL!");
        }

        if (qrCodeImage != null)
        {
            qrCodeImage.gameObject.SetActive(false);
        }

        if (loadingIndicator != null)
        {
            loadingIndicator.SetActive(true);
        }
    }

    private IEnumerator CreatePaymentRequest()
    {
        // MOCK PAYMENT FOR TESTING
        if (useMockPayment)
        {
            Debug.Log("🧪 Using mock payment (testing mode)");
            if (loadingIndicator != null)
                loadingIndicator.SetActive(false);

            if (statusText != null)
                statusText.text = "テスト支払い処理中...";

            yield return new WaitForSeconds(mockPaymentDelay);
            OnPaymentSuccess();
            yield break;
        }

        // REAL PAYMENT REQUEST
        string endpoint = currentPaymentType == PaymentType.Frame
            ? "api/photobooth/payments/frame"
            : "api/photobooth/payments/gacha";

        string url = $"{apiBaseURL}{endpoint}";

        PaymentRequest request = new PaymentRequest
        {
            booth_id = currentBoothId,
            amount = currentPrice,
            frame_id = currentPaymentType == PaymentType.Frame && selectedFrame != null
                ? selectedFrame.frameData.frame_id
                : null
        };

        string jsonData = JsonUtility.ToJson(request);
        Debug.Log($"💳 Payment request: {jsonData}");

        using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");

            yield return webRequest.SendWebRequest();

            if (loadingIndicator != null)
                loadingIndicator.SetActive(false);

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                string response = webRequest.downloadHandler.text;
                Debug.Log($"💳 Payment response: {response}");

                PaymentResponse paymentResponse = JsonUtility.FromJson<PaymentResponse>(response);

                if (paymentResponse != null && paymentResponse.success)
                {
                    currentPaymentId = paymentResponse.data.payment_id;

                    if (!string.IsNullOrEmpty(paymentResponse.data.qr_code_url))
                    {
                        StartCoroutine(LoadQRCode(paymentResponse.data.qr_code_url));
                    }

                    if (paymentCheckCoroutine != null)
                        StopCoroutine(paymentCheckCoroutine);

                    paymentCheckCoroutine = StartCoroutine(CheckPaymentStatus());
                }
                else
                {
                    OnPaymentFailed("支払いリクエストの作成に失敗しました");
                }
            }
            else
            {
                Debug.LogError($"Payment request failed: {webRequest.error}");
                OnPaymentFailed($"エラー: {webRequest.error}");
            }
        }
    }

    private IEnumerator LoadQRCode(string qrCodeUrl)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(qrCodeUrl))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f)
                );

                if (qrCodeImage != null)
                {
                    qrCodeImage.sprite = sprite;
                    qrCodeImage.gameObject.SetActive(true);
                }
            }
            else
            {
                Debug.LogWarning($"Failed to load QR code: {request.error}");
            }
        }
    }

    private IEnumerator CheckPaymentStatus()
    {
        float elapsedTime = 0f;

        while (elapsedTime < paymentTimeout)
        {
            yield return new WaitForSeconds(paymentCheckInterval);
            elapsedTime += paymentCheckInterval;

            string url = $"{apiBaseURL}api/photobooth/payments/{currentPaymentId}/status";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string response = request.downloadHandler.text;
                    PaymentStatusResponse statusResponse = JsonUtility.FromJson<PaymentStatusResponse>(response);

                    if (statusResponse != null && statusResponse.success)
                    {
                        string status = statusResponse.data.status.ToLower();
                        Debug.Log($"💳 Payment status: {status}");

                        switch (status)
                        {
                            case "completed":
                            case "success":
                                OnPaymentSuccess();
                                yield break;

                            case "failed":
                            case "cancelled":
                                OnPaymentFailed("支払いがキャンセルされました");
                                yield break;

                            case "pending":
                                if (statusText != null)
                                    statusText.text = "支払い処理中...";
                                break;
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"Payment status check failed: {request.error}");
                }
            }
        }

        OnPaymentFailed("支払いがタイムアウトしました");
    }

    private void OnPaymentSuccess()
    {
        Debug.Log("✅ Payment successful!");

        if (paymentCheckCoroutine != null)
        {
            StopCoroutine(paymentCheckCoroutine);
            paymentCheckCoroutine = null;
        }

        if (statusText != null)
            statusText.text = "支払い完了！";

        StartCoroutine(HidePaymentPanelAndProceed());
    }

    private IEnumerator HidePaymentPanelAndProceed()
    {
        yield return new WaitForSeconds(1.5f);

        if (paymentPanel != null)
            paymentPanel.SetActive(false);

        if (currentPaymentType == PaymentType.Frame)
        {
            // Continue to frame shooting
            if (frameManager != null)
            {
                frameManager.ContinueAfterPayment(selectedFrame);
            }
        }
        else if (currentPaymentType == PaymentType.Gacha)
        {
            // Continue gacha animation with the saved button index
            if (gatchaManager != null)
            {
                gatchaManager.ContinueGachaAfterPayment(pendingGachaButtonIndex);
            }
        }

        ResetPaymentState();
    }

    private void OnPaymentFailed(string message)
    {
        Debug.LogWarning($"❌ Payment failed: {message}");

        if (paymentCheckCoroutine != null)
        {
            StopCoroutine(paymentCheckCoroutine);
            paymentCheckCoroutine = null;
        }

        if (statusText != null)
            statusText.text = message;

        StartCoroutine(AutoClosePaymentPanel());
    }

    private IEnumerator AutoClosePaymentPanel()
    {
        yield return new WaitForSeconds(3f);
        ClosePaymentPanel();
    }

    public void OnCancelPayment()
    {
        Debug.Log("❌ Payment cancelled by user");

        if (paymentCheckCoroutine != null)
        {
            StopCoroutine(paymentCheckCoroutine);
            paymentCheckCoroutine = null;
        }

        if (!string.IsNullOrEmpty(currentPaymentId))
        {
            StartCoroutine(CancelPaymentRequest());
        }

        ClosePaymentPanel();
    }

    private IEnumerator CancelPaymentRequest()
    {
        string url = $"{apiBaseURL}api/photobooth/payments/{currentPaymentId}/cancel";

        using (UnityWebRequest request = UnityWebRequest.PostWwwForm(url, ""))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Payment cancelled on server");
            }
            else
            {
                Debug.LogWarning($"Failed to cancel payment on server: {request.error}");
            }
        }
    }

    private void ClosePaymentPanel()
    {
        if (paymentPanel != null)
            paymentPanel.SetActive(false);

        ResetPaymentState();
    }

    private void ResetPaymentState()
    {
        currentPaymentId = null;
        currentBoothId = null;
        currentPrice = 0f;
        selectedFrame = null;
        pendingGachaButtonIndex = -1;
    }

    private void OnDestroy()
    {
        if (paymentCheckCoroutine != null)
            StopCoroutine(paymentCheckCoroutine);
    }

    // ==================== DATA CLASSES ====================

    [System.Serializable]
    public class PaymentRequest
    {
        public string booth_id;
        public float amount;
        public string frame_id;
    }

    [System.Serializable]
    public class PaymentResponse
    {
        public bool success;
        public PaymentData data;
        public string message;
    }

    [System.Serializable]
    public class PaymentData
    {
        public string payment_id;
        public string qr_code_url;
        public string status;
    }

    [System.Serializable]
    public class PaymentStatusResponse
    {
        public bool success;
        public PaymentStatusData data;
    }

    [System.Serializable]
    public class PaymentStatusData
    {
        public string payment_id;
        public string status;
        public float amount;
    }
}