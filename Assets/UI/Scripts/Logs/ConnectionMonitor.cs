using UnityEngine;

/// <summary>
/// Monitors network connectivity changes and logs connection events
/// </summary>
public class ConnectionMonitor : MonoBehaviour
{
    [Header("Settings")]
    public float checkIntervalSeconds = 5f;
    public bool logConnectionChanges = true;

    private NetworkReachability lastReachability;
    private bool isFirstCheck = true;

    private void Start()
    {
        lastReachability = Application.internetReachability;

        // Log initial connection state
        LogConnectionState("initial_check", lastReachability);

        // Start periodic checking
        InvokeRepeating(nameof(CheckConnection), checkIntervalSeconds, checkIntervalSeconds);
    }

    private void CheckConnection()
    {
        NetworkReachability currentReachability = Application.internetReachability;

        // Only log if connection state changed
        if (currentReachability != lastReachability || isFirstCheck)
        {
            string eventType = DetermineEventType(lastReachability, currentReachability);
            LogConnectionState(eventType, currentReachability);

            lastReachability = currentReachability;
            isFirstCheck = false;
        }
    }

    private string DetermineEventType(NetworkReachability oldState, NetworkReachability newState)
    {
        // No connection
        if (newState == NetworkReachability.NotReachable)
        {
            return "disconnected";
        }

        // Gained connection
        if (oldState == NetworkReachability.NotReachable && newState != NetworkReachability.NotReachable)
        {
            return "connected";
        }

        // Changed connection type
        if (oldState != newState)
        {
            return "connection_changed";
        }

        return "connected";
    }

    private void LogConnectionState(string eventType, NetworkReachability reachability)
    {
        if (!logConnectionChanges || LoggingManager.Instance == null)
            return;

        string connectionType = GetConnectionTypeName(reachability);
        bool isConnected = reachability != NetworkReachability.NotReachable;

        LoggingManager.Instance.LogConnection(
            connectionType: connectionType,
            eventType: eventType,
            endpoint: "system_network_monitor",
            responseCode: isConnected ? 200 : 0,
            latencyMs: 0,
            success: isConnected,
            errorMessage: isConnected ? null : "No internet connection available"
        );

        if (logConnectionChanges)
        {
            Debug.Log($"🌐 Connection {eventType}: {connectionType} (Reachable: {isConnected})");
        }
    }

    private string GetConnectionTypeName(NetworkReachability reachability)
    {
        switch (reachability)
        {
            case NetworkReachability.NotReachable:
                return "none";
            case NetworkReachability.ReachableViaLocalAreaNetwork:
                return "wifi";
            case NetworkReachability.ReachableViaCarrierDataNetwork:
                return "mobile";
            default:
                return "unknown";
        }
    }

    private void OnDestroy()
    {
        CancelInvoke(nameof(CheckConnection));
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (!pauseStatus)
        {
            // App resumed - check connection immediately
            CheckConnection();
        }
    }

    // ============================================================
    // PUBLIC METHODS
    // ============================================================

    /// <summary>
    /// Manually trigger a connection check
    /// </summary>
    public void ForceConnectionCheck()
    {
        CheckConnection();
    }

    /// <summary>
    /// Get current connection status
    /// </summary>
    public bool IsConnected()
    {
        return Application.internetReachability != NetworkReachability.NotReachable;
    }

    /// <summary>
    /// Get current connection type as string
    /// </summary>
    public string GetCurrentConnectionType()
    {
        return GetConnectionTypeName(Application.internetReachability);
    }
}