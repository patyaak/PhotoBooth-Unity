using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Monitors server connectivity and shows/hides WiFi error panel
/// Handles offline mode based on booth configuration
/// </summary>
public class ServerConnectivityManager : MonoBehaviour
{
    public static ServerConnectivityManager Instance;

    [Header("UI References")]
    [Tooltip("GameObject to show when server is down (WiFi error panel)")]
    public GameObject wifiErrorPanel;

    [Tooltip("Simple endpoint to check - can include {boothId} placeholder")]
    public string healthCheckEndpoint = "/";

    [Tooltip("Use booth ID in health check URL (for endpoints that require it)")]
    public bool useBoothIdInCheck = false;

    [Tooltip("How often to check server status when online (seconds)")]
    public float normalCheckInterval = 10f;

    [Tooltip("How often to retry when server is down (seconds)")]
    public float retryInterval = 3f;

    [Tooltip("Request timeout in seconds")]
    public int requestTimeout = 5;

    [Header("Debug Settings")]
    public bool debugMode = true;

    // Internal state
    private bool isServerOnline = true;
    private bool wasServerOnline = true;
    private Coroutine healthCheckCoroutine;
    private bool isMonitoring = false;
    private bool hasShownOfflineUI = false;

    // Events that other scripts can subscribe to
    public event Action OnServerOnline;
    public event Action OnServerOffline;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        if (wifiErrorPanel != null)
            wifiErrorPanel.SetActive(false);

        StartMonitoring();
    }

    public void StartMonitoring()
    {
        if (isMonitoring)
        {
            if (debugMode)
                Debug.LogWarning("⚠️ Monitoring already running");
            return;
        }

        isMonitoring = true;

        if (healthCheckCoroutine != null)
            StopCoroutine(healthCheckCoroutine);

        healthCheckCoroutine = StartCoroutine(MonitorServerHealth());

        Debug.Log("🔍 Server monitoring started");
        Debug.Log($"📊 Check intervals: Online={normalCheckInterval}s, Retry={retryInterval}s");
    }

    public void StopMonitoring()
    {
        isMonitoring = false;

        if (healthCheckCoroutine != null)
        {
            StopCoroutine(healthCheckCoroutine);
            healthCheckCoroutine = null;
        }

        Debug.Log("⏸️ Server monitoring stopped");
    }

    private IEnumerator MonitorServerHealth()
    {
        yield return CheckServerStatus();

        while (isMonitoring)
        {
            float waitTime = isServerOnline ? normalCheckInterval : retryInterval;

            if (debugMode)
                Debug.Log($"⏱️ Next check in {waitTime}s (Server {(isServerOnline ? "ONLINE" : "OFFLINE")})");

            yield return new WaitForSeconds(waitTime);
            yield return CheckServerStatus();
        }
    }

    private IEnumerator CheckServerStatus()
    {
        bool serverResponded = false;
        string checkURL = BuildHealthCheckURL();

        if (debugMode)
            Debug.Log($"🔍 Checking server: {checkURL}");

        yield return CheckEndpoint(checkURL, (success) => serverResponded = success);

        wasServerOnline = isServerOnline;
        isServerOnline = serverResponded;

        if (debugMode)
            Debug.Log($"📊 Server Status: {(isServerOnline ? "✅ ONLINE" : "❌ OFFLINE")} (was: {(wasServerOnline ? "ONLINE" : "OFFLINE")})");

        if (wasServerOnline && !isServerOnline)
        {
            Debug.LogError("🚨 SERVER WENT OFFLINE");
            OnServerWentOffline();
        }
        else if (!wasServerOnline && isServerOnline)
        {
            Debug.Log("🎉 SERVER CAME BACK ONLINE");
            OnServerCameOnline();
        }
    }

    private string BuildHealthCheckURL()
    {
        string baseUrl = API.BaseURL.TrimEnd('/');
        string endpoint = healthCheckEndpoint;

        if (useBoothIdInCheck && endpoint.Contains("{boothId}"))
        {
            string boothId = GetBoothId();
            if (!string.IsNullOrEmpty(boothId))
            {
                endpoint = endpoint.Replace("{boothId}", boothId);
            }
            else
            {
                Debug.LogWarning("⚠️ Booth ID not available, using root endpoint for health check");
                endpoint = "/";
            }
        }

        return baseUrl + endpoint;
    }

    private string GetBoothId()
    {
        string boothId = PlayerPrefs.GetString("booth_id", "");

        if (string.IsNullOrEmpty(boothId) && LoginManager.Instance != null)
        {
            boothId = LoginManager.Instance.boothKey;
        }

        return boothId;
    }

    private IEnumerator CheckEndpoint(string url, Action<bool> callback)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = requestTimeout;
            request.SetRequestHeader("Accept", "application/json");

            if (debugMode)
                Debug.Log($"📤 Sending request to: {url}");

            yield return request.SendWebRequest();

            bool success =
                request.result == UnityWebRequest.Result.Success &&
                request.responseCode == 200;

            if (success)
            {
                Debug.Log($"✅ Server ONLINE (200 OK): {url}");
            }
            else
            {
                Debug.LogError($"❌ Server OFFLINE");
                Debug.LogError($"↳ URL: {url}");
                Debug.LogError($"↳ Result: {request.result}");
                Debug.LogError($"↳ Code: {request.responseCode}");
                Debug.LogError($"↳ Error: {request.error}");
            }

            callback?.Invoke(success);
        }
    }

    private void OnServerWentOffline()
    {
        Debug.LogError("❌ SERVER OFFLINE TRIGGERED");

        // Check if offline mode is allowed
        bool offlineModeAllowed = CanUseOfflineMode();

        if (offlineModeAllowed)
        {
            Debug.Log("🟡 Offline mode ALLOWED — using cached booth data");

            // DON'T show WiFi error panel in offline mode - keep app running smoothly
            if (wifiErrorPanel != null)
                wifiErrorPanel.SetActive(false);

            // Trigger offline mode in VendorLogin
            var vendorLogin = FindAnyObjectByType<VendorLogin>();
            if (vendorLogin != null)
            {
                vendorLogin.EnterOfflineMode();
            }

            Debug.Log("✅ App continues to run smoothly with cached data (no WiFi panel)");
        }
        else
        {
            Debug.Log("🔴 Offline mode BLOCKED — resetting to login");

            // Show WiFi error panel
            if (wifiErrorPanel != null)
                wifiErrorPanel.SetActive(true);

            // Reset to login panel
            ResetToLoginPanel();
        }

        hasShownOfflineUI = true;
        OnServerOffline?.Invoke();
    }

    /// <summary>
    /// Check if offline mode is allowed based on saved booth configuration
    /// </summary>
    private bool CanUseOfflineMode()
    {
        // Must have a saved booth ID
        if (!PlayerPrefs.HasKey("booth_id"))
        {
            Debug.Log("❌ No saved booth_id — offline mode not allowed");
            return false;
        }

        // Get saved settings (default to requiring payments/login if not set)
        int paymentsEnabled = PlayerPrefs.GetInt("payments_enabled", 1);
        int loginRequired = PlayerPrefs.GetInt("login_required", 1);

        Debug.Log($"📋 Booth Config: payments_enabled={paymentsEnabled}, login_required={loginRequired}");

        // Offline mode is ONLY allowed if BOTH are disabled
        bool offlineAllowed = (paymentsEnabled == 0) && (loginRequired == 0);

        Debug.Log($"🔍 Offline mode allowed: {offlineAllowed}");

        return offlineAllowed;
    }

    private void OnServerCameOnline()
    {
        Debug.Log("✅ SERVER ONLINE - EXECUTING ONLINE PROCEDURE");

        // Hide WiFi error panel
        if (wifiErrorPanel != null)
        {
            Debug.Log($"📱 Deactivating WiFi panel: {wifiErrorPanel.name}");
            wifiErrorPanel.SetActive(false);

            if (!wifiErrorPanel.activeSelf)
            {
                Debug.Log("✅ WiFi panel successfully deactivated");
            }
        }
        else
        {
            Debug.LogWarning("⚠️ wifiErrorPanel is NULL during online procedure");
        }

        hasShownOfflineUI = false;

        if (LoggingManager.Instance != null)
        {
            LoggingManager.Instance.LogSystemEvent(
                message: "Server connectivity restored",
                severity: LogSeverity.Info
            );
        }

        OnServerOnline?.Invoke();
    }

    private void ResetToLoginPanel()
    {
        Debug.Log("🔄 Resetting to login panel due to server offline...");

        StopAllManagerOperations();

        // Clear user session
        PlayerPrefs.DeleteKey("user_id");
        PlayerPrefs.DeleteKey("user_name");
        PlayerPrefs.DeleteKey("user_email");
        PlayerPrefs.DeleteKey("session_id");
        PlayerPrefs.Save();

        if (LoginManager.Instance != null)
        {
            Debug.Log("✅ Calling LoginManager.ResetToLoginPanel()");
            LoginManager.Instance.ResetToLoginPanel();

            if (LoginManager.Instance.qrPanel != null)
                LoginManager.Instance.qrPanel.SetActive(false);
        }
        else
        {
            Debug.LogWarning("⚠️ LoginManager.Instance is NULL!");
        }

        Debug.Log("🌐 WiFi error panel remains active until server is back online");
    }

    private void StopAllManagerOperations()
    {
        Debug.Log("🛑 Stopping all manager operations...");

        if (PaymentManager.Instance != null)
        {
            if (PaymentManager.Instance.paymentPanel != null)
                PaymentManager.Instance.paymentPanel.SetActive(false);

            PaymentManager.Instance.ResetPaymentState();
            Debug.Log("✅ PaymentManager stopped");
        }

        if (PhotoShootingManager.Instance != null)
        {
            if (PhotoShootingManager.Instance.photoShootPanel != null)
                PhotoShootingManager.Instance.photoShootPanel.SetActive(false);

            if (PhotoShootingManager.Instance.beautificationPanel != null)
                PhotoShootingManager.Instance.beautificationPanel.SetActive(false);

            Debug.Log("✅ PhotoShootingManager stopped");
        }

        if (GatchaManager.Instance != null)
        {
            GatchaManager.Instance.ClearSpawnedFramesInstant();

            if (GatchaManager.Instance.celebration != null)
                GatchaManager.Instance.celebration.SetActive(false);

            if (GatchaManager.Instance.gatchaWin != null)
                GatchaManager.Instance.gatchaWin.SetActive(false);

            Debug.Log("✅ GatchaManager stopped");
        }

        if (PhotoBoothFrameManager.Instance != null)
        {
            PhotoBoothFrameManager.Instance.StopAllCoroutines();
            Debug.Log("✅ PhotoBoothFrameManager stopped");
        }
    }

    public void ManualServerCheck()
    {
        Debug.Log("🔄 Manual server check triggered");
        StartCoroutine(CheckServerStatus());
    }

    public bool IsServerOnline()
    {
        return isServerOnline;
    }

    public void OnAPIRequestFailed(string endpoint, string error)
    {
        Debug.LogWarning($"⚠️ API Request Failed: {endpoint} - {error}");
        Debug.LogWarning("🔄 Triggering immediate server check...");
        StartCoroutine(CheckServerStatus());
    }

    public void ForceShowWiFiPanel()
    {
        Debug.Log("🧪 TEST: Forcing WiFi panel to show");
        if (wifiErrorPanel != null)
        {
            wifiErrorPanel.SetActive(true);
            Debug.Log($"✅ WiFi panel forced active: {wifiErrorPanel.activeSelf}");
        }
        else
        {
            Debug.LogError("❌ Cannot force show - wifiErrorPanel is NULL!");
        }
    }

    public void ForceHideWiFiPanel()
    {
        Debug.Log("🧪 TEST: Forcing WiFi panel to hide");
        if (wifiErrorPanel != null)
        {
            wifiErrorPanel.SetActive(false);
            Debug.Log($"✅ WiFi panel forced inactive: {!wifiErrorPanel.activeSelf}");
        }
    }

    private void OnDestroy()
    {
        StopMonitoring();
    }

    private void OnApplicationQuit()
    {
        StopMonitoring();
    }
}