using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class LoggingManager : MonoBehaviour
{
    public static LoggingManager Instance;

    [Header("Settings")]
    public string apiBaseURL = "http://photo-stg-api.chvps3.aozora-okinawa.com/";
    public float syncIntervalHours = 24f;      // Sync every 24 hours
    public int maxLogsBeforeForceSync = 1000;   // Force sync if too many logs
    public int maxLogsToKeep = 5000;            // Delete oldest logs if exceeded
    public bool enableDebugLogging = true;      // Show logs in Unity console

    [Header("UI References (Optional)")]
    public GameObject logViewerPanel;           // Panel to show logs in-app

    private LogStorage logStorage;
    private string logFilePath;
    private Coroutine syncCoroutine;
    private string boothId;
    private string deviceId;
    private int sessionClickCounts = new Dictionary<string, int>().Count;
    private Dictionary<string, int> clickCounters = new Dictionary<string, int>();

    // ============================================================
    // INITIALIZATION
    // ============================================================
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

        logFilePath = Path.Combine(Application.persistentDataPath, "photobooth_logs.json");
        deviceId = SystemInfo.deviceUniqueIdentifier;

        LoadLogsFromDisk();
        SetupCrashHandler();
    }

    private void Start()
    {
        boothId = PlayerPrefs.GetString("booth_id", "unknown");

        // Start periodic sync
        if (syncCoroutine == null)
            syncCoroutine = StartCoroutine(PeriodicSyncRoutine());

        // Log app startup
        LogSystemEvent("Application Started", LogSeverity.Info);
    }

    // ============================================================
    // CRASH HANDLING
    // ============================================================
    private void SetupCrashHandler()
    {
        Application.logMessageReceived += HandleUnityLog;
    }

    private void HandleUnityLog(string logString, string stackTrace, UnityEngine.LogType type)
    {
        // Only log exceptions and errors, not warnings
        if (type == UnityEngine.LogType.Exception || type == UnityEngine.LogType.Error)
        {
            var crashData = new CrashLog
            {
                exception_type = type.ToString(),
                stack_trace = stackTrace,
                scene_name = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                last_action = GetLastUserAction(),
                system_info = GetSystemInfo()
            };

            LogCrash(logString, crashData, LogSeverity.Error);
        }
    }

    private Dictionary<string, string> GetSystemInfo()
    {
        return new Dictionary<string, string>
        {
            { "device_model", SystemInfo.deviceModel },
            { "device_type", SystemInfo.deviceType.ToString() },
            { "os", SystemInfo.operatingSystem },
            { "processor", SystemInfo.processorType },
            { "memory", SystemInfo.systemMemorySize + " MB" },
            { "graphics", SystemInfo.graphicsDeviceName },
            { "screen_resolution", Screen.width + "x" + Screen.height }
        };
    }

    private string lastUserAction = "None";

    private string GetLastUserAction()
    {
        return lastUserAction;
    }

    public void SetLastUserAction(string action)
    {
        lastUserAction = action;
    }

    // ============================================================
    // LOGGING METHODS
    // ============================================================

    /// <summary>
    /// Log a customer click event
    /// </summary>
    public void LogCustomerClick(string buttonName, string screenName, string frameId = null, float x = 0, float y = 0)
    {
        string key = $"{screenName}_{buttonName}";
        if (!clickCounters.ContainsKey(key))
            clickCounters[key] = 0;
        clickCounters[key]++;

        var clickData = new CustomerClickLog
        {
            button_name = buttonName,
            screen_name = screenName,
            frame_id = frameId,
            x_position = x,
            y_position = y,
            click_count = clickCounters[key]
        };

        CreateLog(
            LogType.CustomerClick,
            LogSeverity.Info,
            $"Button clicked: {buttonName} on {screenName}",
            JsonUtility.ToJson(clickData)
        );

        SetLastUserAction($"Clicked {buttonName} on {screenName}");
    }

    /// <summary>
    /// Log a payment event
    /// </summary>
    public void LogPayment(string orderId, string paymentType, string provider, float amount,
                          string status, string frameId = null, string errorMessage = null, long durationMs = 0)
    {
        var paymentData = new PaymentLog
        {
            order_id = orderId,
            payment_type = paymentType,
            payment_provider = provider,
            amount = amount,
            status = status,
            frame_id = frameId,
            error_message = errorMessage,
            duration_ms = durationMs
        };

        LogSeverity severity = status == "success" ? LogSeverity.Info :
                              status == "failed" ? LogSeverity.Error : LogSeverity.Warning;

        CreateLog(
            LogType.Payment,
            severity,
            $"Payment {status}: {paymentType} - ¥{amount}",
            JsonUtility.ToJson(paymentData)
        );
    }

    /// <summary>
    /// Log a crash or exception
    /// </summary>
    public void LogCrash(string message, CrashLog crashData, LogSeverity severity = LogSeverity.Critical)
    {
        CreateLog(
            LogType.Crash,
            severity,
            message,
            JsonUtility.ToJson(crashData)
        );

        // Force immediate sync for critical crashes
        if (severity == LogSeverity.Critical)
            StartCoroutine(SyncLogsToServer());
    }

    /// <summary>
    /// Log a connection event
    /// </summary>
    public void LogConnection(string connectionType, string eventType, string endpoint,
                             int responseCode = 0, long latencyMs = 0, bool success = true, string errorMessage = null)
    {
        var connectionData = new ConnectionLog
        {
            connection_type = connectionType,
            event_type = eventType,
            endpoint = endpoint,
            response_code = responseCode,
            latency_ms = latencyMs,
            success = success,
            error_message = errorMessage
        };

        LogSeverity severity = success ? LogSeverity.Info : LogSeverity.Warning;

        CreateLog(
            LogType.Connection,
            severity,
            $"Connection {eventType}: {endpoint}",
            JsonUtility.ToJson(connectionData)
        );
    }

    /// <summary>
    /// Log a system event
    /// </summary>
    public void LogSystemEvent(string message, LogSeverity severity = LogSeverity.Info, string details = null)
    {
        CreateLog(LogType.System, severity, message, details);
    }

    /// <summary>
    /// Core method to create and store a log entry
    /// </summary>
    private void CreateLog(LogType type, LogSeverity severity, string message, string details = null)
    {
        var log = new LogEntry
        {
            booth_id = boothId,
            device_id = deviceId,
            session_id = PlayerPrefs.GetString("session_id", "guest"),
            user_id = PlayerPrefs.GetString("user_id", "guest"),
            log_type = type,
            severity = severity,
            message = message,
            details = details
        };

        logStorage.logs.Add(log);
        logStorage.total_logs_created++;

        if (enableDebugLogging)
            Debug.Log($"[{type}] {severity}: {message}");

        SaveLogsToDisk();

        // Force sync if too many logs
        if (logStorage.logs.Count(l => !l.synced) >= maxLogsBeforeForceSync)
        {
            Debug.LogWarning("Too many unsynced logs - forcing sync");
            StartCoroutine(SyncLogsToServer());
        }
    }

    // ============================================================
    // LOCAL STORAGE
    // ============================================================
    private void LoadLogsFromDisk()
    {
        if (File.Exists(logFilePath))
        {
            try
            {
                string json = File.ReadAllText(logFilePath);
                logStorage = JsonUtility.FromJson<LogStorage>(json);
                Debug.Log($"Loaded {logStorage.logs.Count} logs from disk");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load logs: {ex.Message}");
                logStorage = new LogStorage();
            }
        }
        else
        {
            logStorage = new LogStorage();
        }

        // Clean old logs if too many
        if (logStorage.logs.Count > maxLogsToKeep)
        {
            int toRemove = logStorage.logs.Count - maxLogsToKeep;
            logStorage.logs = logStorage.logs.OrderByDescending(l => l.timestamp).Take(maxLogsToKeep).ToList();
            Debug.Log($"Cleaned {toRemove} old logs");
        }
    }

    private void SaveLogsToDisk()
    {
        try
        {
            string json = JsonUtility.ToJson(logStorage, true);
            File.WriteAllText(logFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to save logs: {ex.Message}");
        }
    }

    // ============================================================
    // SERVER SYNC
    // ============================================================
    private IEnumerator PeriodicSyncRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(syncIntervalHours * 3600f);

            if (Application.internetReachability != NetworkReachability.NotReachable)
            {
                yield return SyncLogsToServer();
            }
        }
    }

    public IEnumerator SyncLogsToServer()
    {
        var unsyncedLogs = logStorage.logs.Where(l => !l.synced).ToList();

        if (unsyncedLogs.Count == 0)
        {
            Debug.Log("No logs to sync");
            yield break;
        }

        Debug.Log($"Syncing {unsyncedLogs.Count} logs to server...");

        var syncRequest = new LogSyncRequest
        {
            booth_id = boothId,
            device_id = deviceId,
            log_count = unsyncedLogs.Count,
            logs = unsyncedLogs
        };

        string json = JsonUtility.ToJson(syncRequest);
        string url = apiBaseURL + "api/photobooth/logs/sync";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    LogSyncResponse response = JsonUtility.FromJson<LogSyncResponse>(request.downloadHandler.text);

                    if (response.success)
                    {
                        // Mark logs as synced
                        foreach (var log in unsyncedLogs)
                        {
                            if (!response.failed_log_ids.Contains(log.log_id))
                                log.synced = true;
                        }

                        logStorage.total_logs_synced += response.logs_processed;
                        logStorage.last_sync_time = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                        SaveLogsToDisk();

                        Debug.Log($"✅ Synced {response.logs_processed} logs successfully");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to parse sync response: {ex.Message}");
                }
            }
            else
            {
                Debug.LogError($"Log sync failed: {request.error}");
                LogConnection("http", "api_call_failed", url, (int)request.responseCode, 0, false, request.error);
            }
        }
    }

    /// <summary>
    /// Manual sync trigger (can be called from UI button)
    /// </summary>
    public void ManualSyncLogs()
    {
        StartCoroutine(SyncLogsToServer());
    }

    // ============================================================
    // LOG VIEWER (For In-App Display)
    // ============================================================
    public List<LogEntry> GetRecentLogs(int count = 100)
    {
        return logStorage.logs.OrderByDescending(l => l.timestamp).Take(count).ToList();
    }

    public List<LogEntry> GetLogsByType(LogType type, int count = 100)
    {
        return logStorage.logs.Where(l => l.log_type == type)
                              .OrderByDescending(l => l.timestamp)
                              .Take(count)
                              .ToList();
    }

    public List<LogEntry> GetLogsBySeverity(LogSeverity severity, int count = 100)
    {
        return logStorage.logs.Where(l => l.severity == severity)
                              .OrderByDescending(l => l.timestamp)
                              .Take(count)
                              .ToList();
    }

    public int GetUnsyncedLogCount()
    {
        return logStorage.logs.Count(l => !l.synced);
    }

    public string GetLastSyncTime()
    {
        return logStorage.last_sync_time ?? "Never";
    }

    // ============================================================
    // CLEANUP
    // ============================================================
    public void ClearSyncedLogs()
    {
        int removed = logStorage.logs.RemoveAll(l => l.synced);
        SaveLogsToDisk();
        Debug.Log($"Removed {removed} synced logs");
    }

    public void ClearAllLogs()
    {
        logStorage.logs.Clear();
        logStorage.total_logs_created = 0;
        logStorage.total_logs_synced = 0;
        SaveLogsToDisk();
        Debug.Log("All logs cleared");
    }

    private void OnApplicationQuit()
    {
        SaveLogsToDisk();
        LogSystemEvent("Application Quit", LogSeverity.Info);
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause)
        {
            SaveLogsToDisk();
            LogSystemEvent("Application Paused", LogSeverity.Info);
        }
        else
        {
            LogSystemEvent("Application Resumed", LogSeverity.Info);
        }
    }
}