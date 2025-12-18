using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Monitors server connectivity and shows/hides WiFi error panel
/// Automatically returns to login panel when server goes down
/// Place this script on a persistent GameObject in your scene
/// </summary>
public class ServerConnectivityManager : MonoBehaviour
{
    public static ServerConnectivityManager Instance;

    [Header("UI References")]
    [Tooltip("GameObject to show when server is down (WiFi error panel)")]
    public GameObject wifiErrorPanel;

    [Header("Server Settings")]
    [Tooltip("Your API base URL (main URL without endpoint)")]
    public string serverBaseURL = "https://photo-stg-api.chvps3.aozora-okinawa.com";

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
    public bool debugMode = true; // Enable detailed logging

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

            if (debugMode)
                Debug.Log("✅ ServerConnectivityManager initialized");
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        // CRITICAL: Check if wifiErrorPanel is assigned
        if (wifiErrorPanel == null)
        {
            Debug.LogError("❌❌❌ CRITICAL: wifiErrorPanel is NOT ASSIGNED in Inspector! WiFi panel will not show!");
            Debug.LogError("⚠️ Please assign the WiFi error panel GameObject in the Inspector.");
        }
        else
        {
            Debug.Log($"✅ WiFi error panel assigned: {wifiErrorPanel.name}");
            wifiErrorPanel.SetActive(false);
        }

        StartMonitoring();
    }

    /// <summary>
    /// Start continuous server health monitoring
    /// </summary>
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

    /// <summary>
    /// Stop server health monitoring
    /// </summary>
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

    /// <summary>
    /// Main monitoring coroutine
    /// </summary>
    private IEnumerator MonitorServerHealth()
    {
        // Do an immediate check on startup
        yield return CheckServerStatus();

        while (isMonitoring)
        {
            // Use different intervals based on server state
            float waitTime = isServerOnline ? normalCheckInterval : retryInterval;

            if (debugMode)
                Debug.Log($"⏱️ Next check in {waitTime}s (Server {(isServerOnline ? "ONLINE" : "OFFLINE")})");

            yield return new WaitForSeconds(waitTime);

            yield return CheckServerStatus();
        }
    }

    /// <summary>
    /// Check if server is responding
    /// </summary>
    private IEnumerator CheckServerStatus()
    {
        bool serverResponded = false;

        // Build the full URL
        string checkURL = BuildHealthCheckURL();

        if (debugMode)
            Debug.Log($"🔍 Checking server: {checkURL}");

        // Try to connect to server
        yield return CheckEndpoint(checkURL, (success) => serverResponded = success);

        // Update server status
        wasServerOnline = isServerOnline;
        isServerOnline = serverResponded;

        if (debugMode)
            Debug.Log($"📊 Server Status: {(isServerOnline ? "✅ ONLINE" : "❌ OFFLINE")} (was: {(wasServerOnline ? "ONLINE" : "OFFLINE")})");

        // Handle state changes
        if (wasServerOnline && !isServerOnline)
        {
            // Server just went down
            Debug.LogError("🚨🚨🚨 SERVER WENT OFFLINE - TRIGGERING WIFI PANEL");
            OnServerWentOffline();
        }
        else if (!wasServerOnline && isServerOnline)
        {
            // Server just came back online
            Debug.Log("🎉🎉🎉 SERVER CAME BACK ONLINE - HIDING WIFI PANEL");
            OnServerCameOnline();
        }
    }

    /// <summary>
    /// Build health check URL with optional booth ID replacement
    /// </summary>
    private string BuildHealthCheckURL()
    {
        string baseUrl = serverBaseURL.TrimEnd('/');
        string endpoint = healthCheckEndpoint;

        // If we need to use booth ID and it contains placeholder
        if (useBoothIdInCheck && endpoint.Contains("{boothId}"))
        {
            string boothId = GetBoothId();
            if (!string.IsNullOrEmpty(boothId))
            {
                endpoint = endpoint.Replace("{boothId}", boothId);
            }
            else
            {
                // If no booth ID available, fall back to root
                Debug.LogWarning("⚠️ Booth ID not available, using root endpoint for health check");
                endpoint = "/";
            }
        }

        return baseUrl + endpoint;
    }

    /// <summary>
    /// Get booth ID from PlayerPrefs or other managers
    /// </summary>
    private string GetBoothId()
    {
        // Try to get from PlayerPrefs first
        string boothId = PlayerPrefs.GetString("booth_id", "");

        // If not in PlayerPrefs, try LoginManager
        if (string.IsNullOrEmpty(boothId) && LoginManager.Instance != null)
        {
            boothId = LoginManager.Instance.boothKey;
        }

        return boothId;
    }

    /// <summary>
    /// Check a specific endpoint
    /// </summary>
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


    /// <summary>
    /// Called when server goes offline
    /// </summary>
    private void OnServerWentOffline()
    {
        Debug.LogError("❌❌❌ SERVER OFFLINE - EXECUTING OFFLINE PROCEDURE");

        // CRITICAL: Check if panel exists before trying to activate
        if (wifiErrorPanel == null)
        {
            Debug.LogError("❌❌❌ CRITICAL ERROR: wifiErrorPanel is NULL! Cannot show WiFi panel!");
            Debug.LogError("⚠️ Please assign the WiFi error panel in the Inspector!");
            return;
        }

        // Show WiFi error panel and keep it visible
        Debug.Log($"📱 Activating WiFi panel: {wifiErrorPanel.name}");
        wifiErrorPanel.SetActive(true);

        // Verify it's actually active
        if (wifiErrorPanel.activeSelf)
        {
            Debug.Log("✅ WiFi panel successfully activated");
        }
        else
        {
            Debug.LogError("❌ WiFi panel failed to activate! Check if parent is active.");
        }

        // Log the event
        if (LoggingManager.Instance != null)
        {
            LoggingManager.Instance.LogSystemEvent(
                message: "Server connectivity lost",
                severity: LogSeverity.Error
            );
        }

        // Return to login panel and clear session (only once)
        if (!hasShownOfflineUI)
        {
            hasShownOfflineUI = true;
            Debug.Log("🔄 Resetting to login panel...");
            ResetToLoginPanel();
        }
        else
        {
            Debug.Log("⚠️ Already shown offline UI, skipping reset");
        }

        // Trigger event for other systems
        OnServerOffline?.Invoke();
    }

    /// <summary>
    /// Called when server comes back online
    /// </summary>
    private void OnServerCameOnline()
    {
        Debug.Log("✅✅✅ SERVER ONLINE - EXECUTING ONLINE PROCEDURE");

        // Hide WiFi error panel
        if (wifiErrorPanel != null)
        {
            Debug.Log($"📱 Deactivating WiFi panel: {wifiErrorPanel.name}");
            wifiErrorPanel.SetActive(false);

            // Verify it's actually inactive
            if (!wifiErrorPanel.activeSelf)
            {
                Debug.Log("✅ WiFi panel successfully deactivated");
            }
        }
        else
        {
            Debug.LogWarning("⚠️ wifiErrorPanel is NULL during online procedure");
        }

        // Reset the flag so we can show offline UI again if needed
        hasShownOfflineUI = false;

        // Log the event
        if (LoggingManager.Instance != null)
        {
            LoggingManager.Instance.LogSystemEvent(
                message: "Server connectivity restored",
                severity: LogSeverity.Info
            );
        }

        // Trigger event for other systems
        OnServerOnline?.Invoke();
    }

    /// <summary>
    /// Reset application to login panel while keeping WiFi panel active
    /// </summary>
    private void ResetToLoginPanel()
    {
        Debug.Log("🔄 Resetting to login panel due to server offline...");

        // Stop any ongoing operations
        StopAllManagerOperations();

        // Clear user session
        PlayerPrefs.DeleteKey("user_id");
        PlayerPrefs.DeleteKey("user_name");
        PlayerPrefs.DeleteKey("user_email");
        PlayerPrefs.DeleteKey("session_id");
        PlayerPrefs.Save();

        // Reset to login via LoginManager (WiFi panel stays active)
        if (LoginManager.Instance != null)
        {
            Debug.Log("✅ Calling LoginManager.ResetToLoginPanel()");
            LoginManager.Instance.ResetToLoginPanel();

            // Make sure QR panel is hidden
            if (LoginManager.Instance.qrPanel != null)
                LoginManager.Instance.qrPanel.SetActive(false);
        }
        else
        {
            Debug.LogWarning("⚠️ LoginManager.Instance is NULL!");
        }

        Debug.Log("🌐 WiFi error panel remains active until server is back online");
    }

    /// <summary>
    /// Stop operations in all managers
    /// </summary>
    private void StopAllManagerOperations()
    {
        Debug.Log("🛑 Stopping all manager operations...");

        // Stop payment operations
        if (PaymentManager.Instance != null)
        {
            if (PaymentManager.Instance.paymentPanel != null)
                PaymentManager.Instance.paymentPanel.SetActive(false);

            PaymentManager.Instance.ResetPaymentState();
            Debug.Log("✅ PaymentManager stopped");
        }

        // Stop photo shooting
        if (PhotoShootingManager.Instance != null)
        {
            if (PhotoShootingManager.Instance.photoShootPanel != null)
                PhotoShootingManager.Instance.photoShootPanel.SetActive(false);

            if (PhotoShootingManager.Instance.beautificationPanel != null)
                PhotoShootingManager.Instance.beautificationPanel.SetActive(false);

            Debug.Log("✅ PhotoShootingManager stopped");
        }

        // Clear gacha state
        if (GatchaManager.Instance != null)
        {
            GatchaManager.Instance.ClearSpawnedFramesInstant();

            if (GatchaManager.Instance.celebration != null)
                GatchaManager.Instance.celebration.SetActive(false);

            if (GatchaManager.Instance.gatchaWin != null)
                GatchaManager.Instance.gatchaWin.SetActive(false);

            Debug.Log("✅ GatchaManager stopped");
        }

        // Stop frame manager operations
        if (PhotoBoothFrameManager.Instance != null)
        {
            PhotoBoothFrameManager.Instance.StopAllCoroutines();
            Debug.Log("✅ PhotoBoothFrameManager stopped");
        }
    }

    /// <summary>
    /// Public method to manually check server status
    /// </summary>
    public void ManualServerCheck()
    {
        Debug.Log("🔄 Manual server check triggered");
        StartCoroutine(CheckServerStatus());
    }

    /// <summary>
    /// Get current server status
    /// </summary>
    public bool IsServerOnline()
    {
        return isServerOnline;
    }

    /// <summary>
    /// Called when API request fails - can be called from other managers
    /// </summary>
    public void OnAPIRequestFailed(string endpoint, string error)
    {
        Debug.LogWarning($"⚠️ API Request Failed: {endpoint} - {error}");
        Debug.LogWarning("🔄 Triggering immediate server check...");

        // Trigger immediate server check
        StartCoroutine(CheckServerStatus());
    }

    /// <summary>
    /// Force show WiFi panel (for testing)
    /// </summary>
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

    /// <summary>
    /// Force hide WiFi panel (for testing)
    /// </summary>
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