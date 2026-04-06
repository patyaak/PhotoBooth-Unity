using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

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
    public float pollIntervalSeconds = 60f;

    [Header("Thresholds (0-100)")]
    [Tooltip("Ink % at which we raise an INK_LOW warning.")]
    [Range(0, 100)] public int lowThreshold  = 20;
    [Tooltip("Ink % at which we raise an INK_EMPTY error.")]
    [Range(0, 100)] public int emptyThreshold = 5;

    [Header("Simulation (Editor / test)")]
    public SimulatedInkState simulatedState = SimulatedInkState.None;

    public enum SimulatedInkState { None, SimulateLow, SimulateEmpty, Random }

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

    /// <summary>
    /// Sets state to Random and forces a check. 
    /// Can be linked to a UI button for testing.
    /// </summary>
    public void RandomizeAndCheck()
    {
        simulatedState = SimulatedInkState.Random;
        CheckInkLevel();
    }

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
            bool simRand  = simulatedState == SimulatedInkState.Random;

            var sb = new System.Text.StringBuilder();
            var payload = new InkStatusPayload { inks = new System.Collections.Generic.List<InkEntry>() };

            bool anyLow   = simLow;
            bool anyEmpty = simEmpty;

            // Helper for simulation
            void AddSim(string color, string status, string colorHex)
            {
                sb.AppendLine($"{color}: <color={colorHex}>{status}</color>");
                payload.inks.Add(new InkEntry { color = color.ToLower().Replace(" ", ""), status = status.ToLower() });
            }

            string GetRandomStatus(out string hex, out bool isLow, out bool isEmpty)
            {
                int r = UnityEngine.Random.Range(0, 3);
                if (r == 0) { hex = "#00FF00"; isLow = false; isEmpty = false; return "OK"; }
                if (r == 1) { hex = "#FFFF00"; isLow = true;  isEmpty = false; return "Low"; }
                hex = "#FF0000"; isLow = true; isEmpty = true; return "Empty";
            }

            string[] colors = { "Cyan", "Magenta", "Yellow", "Black", "Light Cyan", "Light Magenta" };
            foreach (var c in colors)
            {
                string status;
                string hex;
                bool l, e;

                if (simRand)
                {
                    status = GetRandomStatus(out hex, out l, out e);
                    if (l) anyLow = true;
                    if (e) anyEmpty = true;
                }
                else
                {
                    // Legacy static simulation logic
                    if (c == "Yellow" && (simLow || simEmpty)) { status = "Low"; hex = "#FFFF00"; anyLow = true; }
                    else if (c == "Black" && simEmpty) { status = "Empty"; hex = "#FF0000"; anyEmpty = true; anyLow = true; }
                    else { status = "OK"; hex = "#00FF00"; }
                }
                AddSim(c, status, hex);
            }
            
            // Maintenance tank
            string mtStatus = "OK";
            string mtHex = "#00FF00";
            if (simRand)
            {
                bool l, e;
                mtStatus = GetRandomStatus(out mtHex, out l, out e);
                if (l) anyLow = true;
                if (e) anyEmpty = true;
            }
            sb.AppendLine($"Maint. Tank: <color={mtHex}>{mtStatus}</color>");
            payload.inks.Add(new InkEntry { color = "maintenance", status = mtStatus.ToLower() });

            FireIfChanged(anyLow, anyEmpty, sb.ToString().Trim());
            SendInkStatusToBackend(payload);
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
        var payload    = new InkStatusPayload { inks = new System.Collections.Generic.List<InkEntry>() };

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

            msgBuilder.AppendLine($"{name}: <color={colorTag}>{statusText}</color>");
            payload.inks.Add(new InkEntry { 
                color  = name.ToLower().Replace(" ", ""), 
                status = statusText.ToLower() 
            });
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
                string apiStatus;

                if (mtEmpty)
                {
                    anyEmpty = true;
                    statusText = "Empty/Full";
                    colorTag = "#FF0000";
                    apiStatus = "empty";
                }
                else if (mtLow)
                {
                    anyLow = true;
                    statusText = "Nearly Full";
                    colorTag = "#FFFF00";
                    apiStatus = "low";
                }
                else
                {
                    statusText = "OK";
                    colorTag = "#00FF00";
                    apiStatus = "ok";
                }

                msgBuilder.AppendLine($"Maint. Tank: <color={colorTag}>{statusText}</color>");
                payload.inks.Add(new InkEntry { color = "maintenance", status = apiStatus });
            }
        }

        string msg = msgBuilder.ToString().Trim();
        FireIfChanged(anyLow, anyEmpty, msg);
        SendInkStatusToBackend(payload);
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

    private void SendInkStatusToBackend(InkStatusPayload payload)
    {
        string boothId = PlayerPrefs.GetString("booth_id", string.Empty);
        if (string.IsNullOrEmpty(boothId))
        {
            Debug.LogWarning("[EpsonInkMonitor] Booth ID not found. Skipping backend report.");
            return;
        }

        string json = JsonUtility.ToJson(payload);
        string url = $"{API.BaseURL}/api/photobooth/booths/{boothId}/ink-status";

        StartCoroutine(PostInkStatus(url, json));
    }

    private IEnumerator PostInkStatus(string url, string json)
    {
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[EpsonInkMonitor] Backend report failed: {request.error} | {request.downloadHandler.text}");
            else
                Debug.Log("[EpsonInkMonitor] Ink status reported successfully.");
        }
    }

    // ──────────────────────────────────────────────
    //  Backend serialization
    // ──────────────────────────────────────────────

    [Serializable]
    public class InkStatusPayload
    {
        public System.Collections.Generic.List<InkEntry> inks;
    }

    [Serializable]
    public class InkEntry
    {
        public string color;
        public string status;
    }
}
