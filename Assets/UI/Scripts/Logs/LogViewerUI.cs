using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LogViewerUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject logPanel;
    public Transform logContentParent;
    public GameObject logEntryPrefab;
    public TMP_Text statsText;
    public TMP_Text lastSyncText;

    [Header("Filter Buttons")]
    public Button allLogsButton;
    public Button clickLogsButton;
    public Button paymentLogsButton;
    public Button crashLogsButton;
    public Button connectionLogsButton;
    public Button systemLogsButton;

    [Header("Severity Filter")]
    public Toggle infoToggle;
    public Toggle warningToggle;
    public Toggle errorToggle;
    public Toggle criticalToggle;

    [Header("Action Buttons")]
    public Button refreshButton;
    public Button syncButton;
    public Button clearSyncedButton;
    public Button clearAllButton;
    public Button closeButton;

    [Header("Settings")]
    public int maxDisplayedLogs = 100;
    public Color infoColor = Color.white;
    public Color warningColor = Color.yellow;
    public Color errorColor = new Color(1f, 0.5f, 0f);
    public Color criticalColor = Color.red;

    private LogType? currentTypeFilter = null;
    private HashSet<LogSeverity> severityFilters = new HashSet<LogSeverity>();

    // ============================================================
    // INITIALIZATION
    // ============================================================
    private void Start()
    {
        SetupButtons();

        // Initialize severity filters (all enabled by default)
        severityFilters.Add(LogSeverity.Info);
        severityFilters.Add(LogSeverity.Warning);
        severityFilters.Add(LogSeverity.Error);
        severityFilters.Add(LogSeverity.Critical);

        logPanel?.SetActive(false);
    }

    private void SetupButtons()
    {
        if (allLogsButton) allLogsButton.onClick.AddListener(() => FilterByType(null));
        if (clickLogsButton) clickLogsButton.onClick.AddListener(() => FilterByType(LogType.CustomerClick));
        if (paymentLogsButton) paymentLogsButton.onClick.AddListener(() => FilterByType(LogType.Payment));
        if (crashLogsButton) crashLogsButton.onClick.AddListener(() => FilterByType(LogType.Crash));
        if (connectionLogsButton) connectionLogsButton.onClick.AddListener(() => FilterByType(LogType.Connection));
        if (systemLogsButton) systemLogsButton.onClick.AddListener(() => FilterByType(LogType.System));

        if (infoToggle) infoToggle.onValueChanged.AddListener(val => ToggleSeverity(LogSeverity.Info, val));
        if (warningToggle) warningToggle.onValueChanged.AddListener(val => ToggleSeverity(LogSeverity.Warning, val));
        if (errorToggle) errorToggle.onValueChanged.AddListener(val => ToggleSeverity(LogSeverity.Error, val));
        if (criticalToggle) criticalToggle.onValueChanged.AddListener(val => ToggleSeverity(LogSeverity.Critical, val));

        if (refreshButton) refreshButton.onClick.AddListener(RefreshLogs);
        if (syncButton) syncButton.onClick.AddListener(SyncLogs);
        if (clearSyncedButton) clearSyncedButton.onClick.AddListener(ClearSyncedLogs);
        if (clearAllButton) clearAllButton.onClick.AddListener(ClearAllLogs);
        if (closeButton) closeButton.onClick.AddListener(CloseLogViewer);
    }

    // ============================================================
    // SHOW/HIDE
    // ============================================================
    public void ShowLogViewer()
    {
        if (logPanel != null)
        {
            logPanel.SetActive(true);
            RefreshLogs();
        }
    }

    public void CloseLogViewer()
    {
        if (logPanel != null)
            logPanel.SetActive(false);
    }

    // ============================================================
    // FILTERING
    // ============================================================
    private void FilterByType(LogType? type)
    {
        currentTypeFilter = type;
        RefreshLogs();
    }

    private void ToggleSeverity(LogSeverity severity, bool enabled)
    {
        if (enabled)
            severityFilters.Add(severity);
        else
            severityFilters.Remove(severity);

        RefreshLogs();
    }

    // ============================================================
    // DISPLAY LOGS
    // ============================================================
    public void RefreshLogs()
    {
        if (LoggingManager.Instance == null)
        {
            Debug.LogError("LoggingManager not found!");
            return;
        }

        // Clear existing log entries
        foreach (Transform child in logContentParent)
            Destroy(child.gameObject);

        // Get logs
        var logs = LoggingManager.Instance.GetRecentLogs(maxDisplayedLogs);

        // Apply type filter
        if (currentTypeFilter.HasValue)
            logs = logs.Where(l => l.log_type == currentTypeFilter.Value).ToList();

        // Apply severity filter
        logs = logs.Where(l => severityFilters.Contains(l.severity)).ToList();

        // Display logs
        foreach (var log in logs)
            CreateLogEntry(log);

        // Update stats
        UpdateStats();
    }

    private void CreateLogEntry(LogEntry log)
    {
        if (logEntryPrefab == null || logContentParent == null)
            return;

        GameObject entry = Instantiate(logEntryPrefab, logContentParent);

        // Find text components (assuming prefab has these)
        TMP_Text timeText = entry.transform.Find("TimeText")?.GetComponent<TMP_Text>();
        TMP_Text typeText = entry.transform.Find("TypeText")?.GetComponent<TMP_Text>();
        TMP_Text severityText = entry.transform.Find("SeverityText")?.GetComponent<TMP_Text>();
        TMP_Text messageText = entry.transform.Find("MessageText")?.GetComponent<TMP_Text>();
        TMP_Text detailsText = entry.transform.Find("DetailsText")?.GetComponent<TMP_Text>();
        Image background = entry.GetComponent<Image>();

        // Set time
        if (timeText != null)
        {
            System.DateTime dt = System.DateTime.Parse(log.timestamp);
            timeText.text = dt.ToLocalTime().ToString("HH:mm:ss");
        }

        // Set type
        if (typeText != null)
            typeText.text = log.log_type.ToString();

        // Set severity with color
        if (severityText != null)
        {
            severityText.text = log.severity.ToString();
            severityText.color = GetSeverityColor(log.severity);
        }

        // Set message
        if (messageText != null)
            messageText.text = log.message;

        // Set details (collapsed by default)
        if (detailsText != null)
        {
            if (!string.IsNullOrEmpty(log.details))
            {
                detailsText.text = FormatDetails(log.details);
                detailsText.gameObject.SetActive(false); // Hidden by default
            }
            else
            {
                detailsText.gameObject.SetActive(false);
            }
        }

        // Set background color based on severity
        if (background != null)
        {
            Color bgColor = GetSeverityColor(log.severity);
            bgColor.a = 0.1f;
            background.color = bgColor;
        }

        // Add click listener to expand/collapse details
        Button entryButton = entry.GetComponent<Button>();
        if (entryButton == null)
            entryButton = entry.AddComponent<Button>();

        entryButton.onClick.AddListener(() =>
        {
            if (detailsText != null && !string.IsNullOrEmpty(log.details))
                detailsText.gameObject.SetActive(!detailsText.gameObject.activeSelf);
        });

        // Mark synced logs differently
        if (log.synced)
        {
            if (typeText != null)
                typeText.text += " ✓";
        }
    }

    private string FormatDetails(string details)
    {
        // Pretty print JSON if possible
        try
        {
            // Add line breaks for better readability
            return details.Replace(",", ",\n").Replace("{", "{\n").Replace("}", "\n}");
        }
        catch
        {
            return details;
        }
    }

    private Color GetSeverityColor(LogSeverity severity)
    {
        switch (severity)
        {
            case LogSeverity.Info: return infoColor;
            case LogSeverity.Warning: return warningColor;
            case LogSeverity.Error: return errorColor;
            case LogSeverity.Critical: return criticalColor;
            default: return Color.white;
        }
    }

    // ============================================================
    // STATS
    // ============================================================
    private void UpdateStats()
    {
        if (LoggingManager.Instance == null)
            return;

        var allLogs = LoggingManager.Instance.GetRecentLogs(10000);
        int totalLogs = allLogs.Count;
        int unsyncedLogs = LoggingManager.Instance.GetUnsyncedLogCount();

        int clickLogs = allLogs.Count(l => l.log_type == LogType.CustomerClick);
        int paymentLogs = allLogs.Count(l => l.log_type == LogType.Payment);
        int crashLogs = allLogs.Count(l => l.log_type == LogType.Crash);
        int connectionLogs = allLogs.Count(l => l.log_type == LogType.Connection);

        int errors = allLogs.Count(l => l.severity == LogSeverity.Error);
        int criticals = allLogs.Count(l => l.severity == LogSeverity.Critical);

        if (statsText != null)
        {
            statsText.text = $"Total Logs: {totalLogs} | Unsynced: {unsyncedLogs}\n" +
                           $"Clicks: {clickLogs} | Payments: {paymentLogs} | Crashes: {crashLogs} | Connections: {connectionLogs}\n" +
                           $"Errors: {errors} | Critical: {criticals}";
        }

        if (lastSyncText != null)
        {
            string lastSync = LoggingManager.Instance.GetLastSyncTime();
            lastSyncText.text = $"Last Sync: {lastSync}";
        }
    }

    // ============================================================
    // ACTIONS
    // ============================================================
    private void SyncLogs()
    {
        if (LoggingManager.Instance != null)
        {
            LoggingManager.Instance.ManualSyncLogs();

            // Refresh after a delay to show updated sync status
            Invoke(nameof(RefreshLogs), 2f);
        }
    }

    private void ClearSyncedLogs()
    {
        if (LoggingManager.Instance != null)
        {
            LoggingManager.Instance.ClearSyncedLogs();
            RefreshLogs();
        }
    }

    private void ClearAllLogs()
    {
        if (LoggingManager.Instance != null)
        {
            // Show confirmation dialog (you can implement this)
            LoggingManager.Instance.ClearAllLogs();
            RefreshLogs();
        }
    }
}