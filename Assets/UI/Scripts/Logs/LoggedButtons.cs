using Mediapipe.Tasks.Components.Containers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Attach to any UI Button to automatically log clicks
/// This is OPTIONAL - only use if you want easy auto-logging on buttons
/// </summary>
[RequireComponent(typeof(Button))]
public class LoggedButton : MonoBehaviour, IPointerClickHandler
{
    [Header("Logging Settings")]
    [Tooltip("Custom name for this button (leave empty to use GameObject name)")]
    public string buttonName;

    [Tooltip("Screen/Panel name where this button exists")]
    public string screenName = "UnknownScreen";

    [Tooltip("Optional frame ID if this button is related to a frame")]
    public string frameId;

    [Tooltip("Enable to log this button's clicks")]
    public bool enableLogging = true;

    [Header("Debug")]
    public bool showDebugLog = false;

    private Button button;
    private int clickCount = 0;

    private void Awake()
    {
        button = GetComponent<Button>();

        // Auto-detect screen name from parent hierarchy if not set
        if (screenName == "UnknownScreen")
        {
            screenName = DetectScreenName();
        }
    }

    /// <summary>
    /// Called when button is clicked
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (!enableLogging || LoggingManager.Instance == null)
            return;

        // Don't log if button is not interactable
        if (button != null && !button.interactable)
            return;

        clickCount++;

        string finalButtonName = string.IsNullOrEmpty(buttonName) ? gameObject.name : buttonName;

        LoggingManager.Instance.LogCustomerClick(
            buttonName: finalButtonName,
            screenName: screenName,
            frameId: string.IsNullOrEmpty(frameId) ? null : frameId,
            x: eventData.position.x,
            y: eventData.position.y
        );

        if (showDebugLog)
        {
            Debug.Log($"🔘 Button Logged: {finalButtonName} on {screenName} (Click #{clickCount})");
        }
    }

    /// <summary>
    /// Try to auto-detect screen name from parent hierarchy
    /// </summary>
    private string DetectScreenName()
    {
        Transform current = transform.parent;

        while (current != null)
        {
            // Look for common panel/screen naming patterns
            string name = current.name;

            if (name.Contains("Panel") || name.Contains("Screen") ||
                name.Contains("Menu") || name.Contains("View"))
            {
                return name;
            }

            current = current.parent;
        }

        return "UnknownScreen";
    }

    /// <summary>
    /// Manually set the frame ID (useful for dynamic buttons)
    /// </summary>
    public void SetFrameId(string newFrameId)
    {
        frameId = newFrameId;
    }

    /// <summary>
    /// Manually set the screen name
    /// </summary>
    public void SetScreenName(string newScreenName)
    {
        screenName = newScreenName;
    }

    /// <summary>
    /// Get total clicks on this button in current session
    /// </summary>
    public int GetClickCount()
    {
        return clickCount;
    }

    /// <summary>
    /// Reset click counter
    /// </summary>
    public void ResetClickCount()
    {
        clickCount = 0;
    }

    // ============================================================
    // EDITOR HELPER (Optional - for easy setup)
    // ============================================================

#if UNITY_EDITOR
    [ContextMenu("Auto-Configure From Hierarchy")]
    private void AutoConfigure()
    {
        // Auto-set button name from GameObject
        if (string.IsNullOrEmpty(buttonName))
        {
            buttonName = gameObject.name;
            Debug.Log($"Set buttonName to: {buttonName}");
        }

        // Auto-detect screen name
        screenName = DetectScreenName();
        Debug.Log($"Detected screenName: {screenName}");

        // Find Text component and use it as button name if available
        var textComponent = GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (textComponent != null && !string.IsNullOrEmpty(textComponent.text))
        {
            buttonName = textComponent.text;
            Debug.Log($"Updated buttonName from Text component: {buttonName}");
        }
    }
#endif
}

/// <summary>
/// Helper extension methods for easy button logging setup
/// </summary>
public static class ButtonLoggingExtensions
{
    /// <summary>
    /// Quick setup for logged button
    /// </summary>
    public static LoggedButton SetupLogging(this Button button, string buttonName, string screenName)
    {
        LoggedButton loggedBtn = button.GetComponent<LoggedButton>();

        if (loggedBtn == null)
            loggedBtn = button.gameObject.AddComponent<LoggedButton>();

        loggedBtn.buttonName = buttonName;
        loggedBtn.screenName = screenName;
        loggedBtn.enableLogging = true;

        return loggedBtn;
    }
}