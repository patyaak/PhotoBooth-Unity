using System;
using System.Collections.Generic;

// ============================================================
// LOG ENUMS
// ============================================================
public enum LogType
{
    CustomerClick,
    Payment,
    Crash,
    Connection,
    System,
    Error
}

public enum LogSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

// ============================================================
// BASE LOG ENTRY
// ============================================================
[Serializable]
public class LogEntry
{
    public string log_id;              // Unique ID for this log
    public string booth_id;            // Which booth generated this log
    public string device_id;           // Device identifier
    public string session_id;          // Current session (if any)
    public string user_id;             // User ID (if logged in)
    public LogType log_type;           // Type of log
    public LogSeverity severity;       // Severity level
    public string timestamp;           // When it happened (ISO 8601)
    public string message;             // Human-readable message
    public string details;             // JSON string of additional data
    public string app_version;         // Application version
    public bool synced;                // Has this been sent to server?

    public LogEntry()
    {
        log_id = Guid.NewGuid().ToString();
        timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        synced = false;
        app_version = UnityEngine.Application.version;
    }
}

// ============================================================
// CUSTOMER CLICK LOG
// ============================================================
[Serializable]
public class CustomerClickLog
{
    public string button_name;         // Which button was clicked
    public string screen_name;         // Which screen/pane
    public string frame_id;            // Frame ID if applicable
    public float x_position;           // Click position X
    public float y_position;           // Click position Y
    public int click_count;            // How many times clicked in session
}

// ============================================================
// PAYMENT LOG
// ============================================================
[Serializable]
public class PaymentLog
{
    public string order_id;            // Payment order ID
    public string payment_type;        // "frame" or "gacha"
    public string payment_provider;    // "paypay", etc.
    public float amount;               // Payment amount
    public string status;              // "initiated", "pending", "success", "failed", "cancelled"
    public string frame_id;            // Frame ID if frame payment
    public string error_message;       // Error message if failed
    public long duration_ms;           // How long payment took
}

// ============================================================
// CRASH LOG
// ============================================================
[Serializable]
public class CrashLog
{
    public string exception_type;     // Type of exception
    public string stack_trace;        // Full stack trace
    public string scene_name;         // Which scene crashed
    public string last_action;        // Last user action before crash
    public Dictionary<string, string> system_info; // Device specs
}

// ============================================================
// CONNECTION LOG
// ============================================================
[Serializable]
public class ConnectionLog
{
    public string connection_type;    // "wifi", "mobile", "none"
    public string event_type;         // "connected", "disconnected", "reconnected", "api_call", "websocket"
    public string endpoint;           // API endpoint or WebSocket URL
    public int response_code;         // HTTP response code
    public long latency_ms;           // Request latency
    public bool success;              // Was connection successful?
    public string error_message;      // Error if failed
}

// ============================================================
// SERVER SYNC PAYLOAD
// ============================================================
[Serializable]
public class LogSyncRequest
{
    public string booth_id;
    public string device_id;
    public int log_count;
    public List<LogEntry> logs;
}

[Serializable]
public class LogSyncResponse
{
    public bool success;
    public string message;
    public int logs_received;
    public int logs_processed;
    public List<string> failed_log_ids;
}

// ============================================================
// LOG STORAGE (Local Save Format)
// ============================================================
[Serializable]
public class LogStorage
{
    public List<LogEntry> logs = new List<LogEntry>();
    public string last_sync_time;
    public int total_logs_created;
    public int total_logs_synced;
}