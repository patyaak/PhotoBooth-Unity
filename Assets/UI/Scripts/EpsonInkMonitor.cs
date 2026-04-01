using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Monitors Epson SL-D500 ink levels via PSM_SDK.dll.
/// Fires OnInkStatusChanged whenever the ink state changes.
/// Subscribe from VendorLogin to update the InkLevel UI panel.
/// </summary>
public class EpsonInkMonitor : MonoBehaviour
{
    public static EpsonInkMonitor Instance { get; private set; }

    // ──────────────────────────────────────────────
    //  Inspector
    // ──────────────────────────────────────────────

    [Header("Polling")]
    [Tooltip("How often (in seconds) to poll the printer for ink state.")]
    public float pollIntervalSeconds = 30f;

    [Header("Thresholds (0-100)")]
    [Tooltip("Ink % at which we raise an INK_LOW warning.")]
    [Range(0, 100)] public int lowThreshold  = 20;
    [Tooltip("Ink % at which we raise an INK_EMPTY error.")]
    [Range(0, 100)] public int emptyThreshold = 5;

    [Header("Simulation (Editor / test)")]
    public SimulatedInkState simulatedState = SimulatedInkState.None;

    public enum SimulatedInkState { None, SimulateLow, SimulateEmpty }

    // ──────────────────────────────────────────────
    //  Public state
    // ──────────────────────────────────────────────

    public bool IsInkLow   { get; private set; }
    public bool IsInkEmpty { get; private set; }
    public string InkStatusMessage { get; private set; } = string.Empty;

    /// <summary>
    /// Fired on the main thread whenever ink status changes.
    /// arg1 = isLow, arg2 = isEmpty, arg3 = human-readable message.
    /// </summary>
    public static event Action<bool, bool, string> OnInkStatusChanged;

    // ──────────────────────────────────────────────
    //  PSM_SDK P/Invoke
    // ──────────────────────────────────────────────

    private const string PSM_DLL = "PSM_SDK";

    // -- Instance lifecycle --
    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Winapi)]
    private static extern int PSM_InitInstance();

    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Winapi)]
    private static extern int PSM_ExitInstance();

    // -- Printer handle --
    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private static extern int PSM_OpenPrinter(string printerName, out IntPtr phPrinter);

    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Winapi)]
    private static extern int PSM_ClosePrinter(IntPtr hPrinter);

    // -- Status query --
    [DllImport(PSM_DLL, CallingConvention = CallingConvention.StdCall)]
    private static extern int PSM_GetPrinterStatus(IntPtr hPrinter, IntPtr pStatus, int statusSize);

    // PSM_GetPrinterInformation – 4 arguments in modern PSM_SDK
    // nInfoId = 1 for Ink levels
    [DllImport(PSM_DLL, CallingConvention = CallingConvention.StdCall)]
    private static extern int PSM_GetPrinterInformation(IntPtr hPrinter, int nInfoId, IntPtr pInfo, int infoSize);

    // ──────────────────────────────────────────────
    //  Epson PSM status / info structures
    // ──────────────────────────────────────────────

    // Ink-level data from PSM_GetPrinterInformation.
    // The structure varies by firmware/SDK version; we read the raw buffer and
    // interpret the first section which holds ink data.
    //
    //  Offset 0   : DWORD  dwSize  (size of the struct)
    //  Offset 4   : DWORD  dwInkCount  (number of ink cartridges)
    //  Offset 8   : INK_INFO[8]  (each INK_INFO = 12 bytes)
    //               INK_INFO { DWORD color, DWORD level (0-100), DWORD status }
    //  After inks : DWORD  maintenanceLevel  (0-100)

    private const int INK_INFO_ELEMENT_SIZE = 12;   // bytes per cartridge entry
    private const int MAX_INK_SLOTS         = 8;
    private const int INFO_HEADER_SIZE      = 8;    // dwSize + dwInkCount
    private const int INFO_BUF_SIZE         = INFO_HEADER_SIZE + (MAX_INK_SLOTS * INK_INFO_ELEMENT_SIZE) + 4 + 512;

    // Ink status flags (from PSM SDK docs)
    private const uint INK_STATUS_EMPTY    = 0x01;
    private const uint INK_STATUS_LOW      = 0x02;

    // Cartridge color IDs for SL-D500 (6-color system)
    private static string ColorName(uint colorId) => colorId switch
    {
        0 => "Cyan",
        1 => "Magenta",
        2 => "Yellow",
        3 => "Black",
        4 => "Light Cyan",
        5 => "Light Magenta",
        _ => $"Ink#{colorId}"
    };

    // ──────────────────────────────────────────────
    //  Private state
    // ──────────────────────────────────────────────

    private bool   _sdkInitialized = false;
    private bool   _lastLow        = false;
    private bool   _lastEmpty      = false;
    private string _lastMsg        = string.Empty;
    private Coroutine _pollRoutine;

    // ──────────────────────────────────────────────
    //  Unity lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        InitSDK();
        _pollRoutine = StartCoroutine(PollRoutine());
    }

    private void OnDestroy()
    {
        if (_pollRoutine != null) StopCoroutine(_pollRoutine);
        ShutdownSDK();
    }

    // ──────────────────────────────────────────────
    //  SDK init / shutdown
    // ──────────────────────────────────────────────

    private void InitSDK()
    {
        try
        {
            int ret = PSM_InitInstance();
            _sdkInitialized = (ret == 0);
            Debug.Log($"[EpsonInkMonitor] PSM_InitInstance → {ret} (0 = OK). Initialized: {_sdkInitialized}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[EpsonInkMonitor] PSM_InitInstance failed: {ex.Message}");
            _sdkInitialized = false;
        }
    }

    private void ShutdownSDK()
    {
        if (!_sdkInitialized) return;
        try { PSM_ExitInstance(); }
        catch (Exception ex) { Debug.LogWarning($"[EpsonInkMonitor] PSM_ExitInstance: {ex.Message}"); }
        _sdkInitialized = false;
    }

    // ──────────────────────────────────────────────
    //  Poll coroutine
    // ──────────────────────────────────────────────

    private IEnumerator PollRoutine()
    {
        // Small initial delay so PrintingManager loads its printer first.
        yield return new WaitForSeconds(3f);

        while (true)
        {
            CheckInkLevel();
            yield return new WaitForSeconds(pollIntervalSeconds);
        }
    }

    /// <summary>Call this to force an immediate re-check (e.g. after a print job).</summary>
    public void ForceCheck() => CheckInkLevel();

    // ──────────────────────────────────────────────
    //  Core check
    // ──────────────────────────────────────────────

    private void CheckInkLevel()
    {
        // ── Simulation override ──
        if (simulatedState != SimulatedInkState.None)
        {
            bool simLow   = simulatedState == SimulatedInkState.SimulateLow;
            bool simEmpty = simulatedState == SimulatedInkState.SimulateEmpty;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Cyan: <color=#00FF00>OK</color> (85%)");
            sb.AppendLine("Magenta: <color=#00FF00>OK</color> (72%)");
            
            if (simEmpty)
            {
                sb.AppendLine("Yellow: <color=#FFFF00>Low</color> (12%)");
                sb.AppendLine("Black: <color=#FF0000>Empty</color> (0%)");
            }
            else if (simLow)
            {
                sb.AppendLine("Yellow: <color=#FFFF00>Low</color> (18%)");
                sb.AppendLine("Black: <color=#00FF00>OK</color> (60%)");
            }
            else
            {
                sb.AppendLine("Yellow: <color=#00FF00>OK</color> (90%)");
                sb.AppendLine("Black: <color=#00FF00>OK</color> (60%)");
            }

            sb.AppendLine("Light Cyan: <color=#00FF00>OK</color> (95%)");
            sb.AppendLine("Light Magenta: <color=#00FF00>OK</color> (88%)");
            sb.AppendLine("Maint. Tank: <color=#00FF00>OK</color> (80%)");


            FireIfChanged(simLow, simEmpty, sb.ToString().Trim());
            return;
        }

        // ── Real hardware path ──
        if (!_sdkInitialized)
        {
            InitSDK(); // retry init in case it failed at startup
            if (!_sdkInitialized) return;
        }

        // Get printer name from PrintingManager
        string printerName = PrintingManager.Instance != null
            ? PrintingManager.Instance.selectedPrinter
            : string.Empty;

        if (string.IsNullOrWhiteSpace(printerName))
        {
            Debug.LogWarning("[EpsonInkMonitor] No printer selected — skipping ink check.");
            return;
        }

        IntPtr hPrinter = IntPtr.Zero;
        IntPtr pInfoBuf = IntPtr.Zero;

        try
        {
            // Open printer
            int openRet = PSM_OpenPrinter(printerName, out hPrinter);
            if (openRet != 0 || hPrinter == IntPtr.Zero)
            {
                Debug.LogWarning($"[EpsonInkMonitor] PSM_OpenPrinter failed (ret={openRet}) for '{printerName}'");
                return;
            }

            // Allocate buffer and query printer information
            pInfoBuf = Marshal.AllocHGlobal(INFO_BUF_SIZE);
            // Zero-fill to avoid garbage reads
            for (int i = 0; i < INFO_BUF_SIZE; i++)
                Marshal.WriteByte(pInfoBuf, i, 0);

            int infoRet = PSM_GetPrinterInformation(hPrinter, 1, pInfoBuf, INFO_BUF_SIZE);
            if (infoRet != 0)
            {
                Debug.LogWarning($"[EpsonInkMonitor] PSM_GetPrinterInformation failed (ret={infoRet})");
                return;
            }

            ParseInkInfo(pInfoBuf);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[EpsonInkMonitor] CheckInkLevel error: {ex.Message}");
        }
        finally
        {
            if (pInfoBuf != IntPtr.Zero) Marshal.FreeHGlobal(pInfoBuf);
            if (hPrinter != IntPtr.Zero)
            {
                try { PSM_ClosePrinter(hPrinter); }
                catch { /* ignore */ }
            }
        }
    }

    // ──────────────────────────────────────────────
    //  Buffer parsing
    // ──────────────────────────────────────────────

    private void ParseInkInfo(IntPtr pInfo)
    {
        // Layout:
        //   [0]  uint dwSize
        //   [4]  uint dwInkCount
        //   [8]  INK_INFO[dwInkCount]  (each 12 bytes: colorId, level, statusFlags)
        //   [8 + dwInkCount*12]  uint maintenanceLevel

        uint dwSize     = (uint)Marshal.ReadInt32(pInfo, 0);
        uint dwInkCount = (uint)Marshal.ReadInt32(pInfo, 4);

        if (dwInkCount == 0 || dwInkCount > MAX_INK_SLOTS)
        {
            Debug.LogWarning($"[EpsonInkMonitor] Unexpected ink count: {dwInkCount}");
            return;
        }

        bool anyLow   = false;
        bool anyEmpty = false;
        
        var msgBuilder = new System.Text.StringBuilder();

        for (int i = 0; i < (int)dwInkCount; i++)
        {
            int offset    = INFO_HEADER_SIZE + i * INK_INFO_ELEMENT_SIZE;
            uint colorId  = (uint)Marshal.ReadInt32(pInfo, offset);
            int  level    = Marshal.ReadInt32(pInfo, offset + 4);   // 0-100
            uint flags    = (uint)Marshal.ReadInt32(pInfo, offset + 8);

            string name   = ColorName(colorId);

            bool isEmpty  = (flags & INK_STATUS_EMPTY) != 0 || level <= emptyThreshold;
            bool isLow    = (flags & INK_STATUS_LOW)   != 0 || level <= lowThreshold;

            string statusText;
            string colorTag;

            if (isEmpty)
            {
                anyEmpty = true;
                statusText = "Empty";
                colorTag = "#FF0000"; // Red
            }
            else if (isLow)
            {
                anyLow = true;
                statusText = "Low";
                colorTag = "#FFFF00"; // Yellow
            }
            else
            {
                statusText = "OK";
                colorTag = "#00FF00"; // Green
            }

            msgBuilder.AppendLine($"{name}: <color={colorTag}>{statusText}</color> ({level}%)");
        }

        // Maintenance tank (optional)
        int mtOffset = INFO_HEADER_SIZE + (int)dwInkCount * INK_INFO_ELEMENT_SIZE;
        if (mtOffset + 4 <= INFO_BUF_SIZE)
        {
            int mtLevel = Marshal.ReadInt32(pInfo, mtOffset);
            if (mtLevel >= 0 && mtLevel <= 100)
            {
                bool mtEmpty = mtLevel <= emptyThreshold;
                bool mtLow   = mtLevel <= lowThreshold;

                string statusText;
                string colorTag;

                if (mtEmpty)
                {
                    anyEmpty = true;
                    statusText = "Empty/Full";
                    colorTag = "#FF0000";
                }
                else if (mtLow)
                {
                    anyLow = true;
                    statusText = "Nearly Full";
                    colorTag = "#FFFF00";
                }
                else
                {
                    statusText = "OK";
                    colorTag = "#00FF00";
                }

                msgBuilder.AppendLine($"Maint. Tank: <color={colorTag}>{statusText}</color> ({mtLevel}%)");
            }
        }

        string msg = msgBuilder.ToString().Trim();
        FireIfChanged(anyLow, anyEmpty, msg);
    }


    // ──────────────────────────────────────────────
    //  Event dispatch
    // ──────────────────────────────────────────────

    private void FireIfChanged(bool isLow, bool isEmpty, string msg)
    {
        // Normalise: empty implies low too
        if (isEmpty) isLow = true;

        bool changed = (isLow != _lastLow) || (isEmpty != _lastEmpty) || (msg != _lastMsg);
        if (!changed) return;

        _lastLow   = isLow;
        _lastEmpty = isEmpty;
        _lastMsg   = msg;

        IsInkLow        = isLow;
        IsInkEmpty      = isEmpty;
        InkStatusMessage = msg;

        Debug.Log($"[EpsonInkMonitor] Status changed → low={isLow} empty={isEmpty} | {msg}");
        OnInkStatusChanged?.Invoke(isLow, isEmpty, msg);
    }
}
