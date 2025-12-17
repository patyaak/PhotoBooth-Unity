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

    // Internal state
    private bool isServerOnline = true;
    private bool wasServerOnline = true;
    private Coroutine healthCheckCoroutine;
    private bool isMonitoring = false;

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

    /// <summary>
    /// Start continuous server health monitoring
    /// </summary>
    public void StartMonitoring()
    {
        if (isMonitoring) return;

        isMonitoring = true;

        if (healthCheckCoroutine != null)
            StopCoroutine(healthCheckCoroutine);

        healthCheckCoroutine = StartCoroutine(MonitorServerHealth());

        Debug.Log("🔍 Server monitoring started");
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
        while (isMonitoring)
        {
            yield return CheckServerStatus();

            // Use different intervals based on server state
            float waitTime = isServerOnline ? normalCheckInterval : retryInterval;
            yield return new WaitForSeconds(waitTime);
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

        Debug.Log($"🔍 Checking server: {checkURL}");

        // Try to connect to server
        yield return CheckEndpoint(checkURL, (success) => serverResponded = success);

        // Update server status
        wasServerOnline = isServerOnline;
        isServerOnline = serverResponded;

        // Handle state changes
        if (wasServerOnline && !isServerOnline)
        {
            // Server just went down
            OnServerWentOffline();
        }
        else if (!wasServerOnline && isServerOnline)
        {
            // Server just came back online
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

            // Set headers to make it look like a real request
            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            // Check if server responded (even with 404 or other errors, it means server is up)
            // Only connection errors mean server is truly down
            bool success = request.result != UnityWebRequest.Result.ConnectionError;

            if (success)
            {
                Debug.Log($"✅ Server responding: {url} (Code: {request.responseCode})");
            }
            else
            {
                Debug.LogWarning($"❌ Server not responding: {url} - {request.error}");
            }

            callback?.Invoke(success);
        }
    }

    /// <summary>
    /// Called when server goes offline
    /// </summary>
    private void OnServerWentOffline()
    {
        Debug.LogError("❌ SERVER OFFLINE - Showing WiFi error panel");

        // Show WiFi error panel
        if (wifiErrorPanel != null)
            wifiErrorPanel.SetActive(true);

        // Log the event
        if (LoggingManager.Instance != null)
        {
            LoggingManager.Instance.LogSystemEvent(
                message: "Server connectivity lost",
                severity: LogSeverity.Error
            );
        }

        // Return to login panel and clear session
        ResetToLoginPanel();

        // Trigger event for other systems
        OnServerOffline?.Invoke();
    }

    /// <summary>
    /// Called when server comes back online
    /// </summary>
    private void OnServerCameOnline()
    {
        Debug.Log("✅ SERVER ONLINE - Hiding WiFi error panel");

        // Hide WiFi error panel
        if (wifiErrorPanel != null)
            wifiErrorPanel.SetActive(false);

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
    /// Reset application to login panel
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

        // Reset to login via LoginManager
        if (LoginManager.Instance != null)
        {
            LoginManager.Instance.ResetToLoginPanel();
        }
    }

    /// <summary>
    /// Stop operations in all managers
    /// </summary>
    private void StopAllManagerOperations()
    {
        // Stop payment operations
        if (PaymentManager.Instance != null)
        {
            if (PaymentManager.Instance.paymentPanel != null)
                PaymentManager.Instance.paymentPanel.SetActive(false);

            PaymentManager.Instance.ResetPaymentState();
        }

        // Stop photo shooting
        if (PhotoShootingManager.Instance != null)
        {
            if (PhotoShootingManager.Instance.photoShootPanel != null)
                PhotoShootingManager.Instance.photoShootPanel.SetActive(false);

            if (PhotoShootingManager.Instance.beautificationPanel != null)
                PhotoShootingManager.Instance.beautificationPanel.SetActive(false);
        }

        // Clear gacha state
        if (GatchaManager.Instance != null)
        {
            GatchaManager.Instance.ClearSpawnedFramesInstant();

            if (GatchaManager.Instance.celebration != null)
                GatchaManager.Instance.celebration.SetActive(false);

            if (GatchaManager.Instance.gatchaWin != null)
                GatchaManager.Instance.gatchaWin.SetActive(false);
        }

        // Stop frame manager operations
        if (PhotoBoothFrameManager.Instance != null)
        {
            PhotoBoothFrameManager.Instance.StopAllCoroutines();
        }
    }

    /// <summary>
    /// Public method to manually check server status
    /// </summary>
    public void ManualServerCheck()
    {
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

        // Trigger immediate server check
        StartCoroutine(CheckServerStatus());
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