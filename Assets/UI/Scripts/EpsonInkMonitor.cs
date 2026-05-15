using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Epson SL-D500 Ink Monitor using PSM_SDK.dll.
/// Checks ink level only for the currently selected printer.
/// </summary>
public class EpsonInkMonitor : MonoBehaviour
{
    public static EpsonInkMonitor Instance { get; private set; }

    [Header("Polling")]
    public float pollIntervalSeconds = 10f;
    public bool autoStartPolling = true;

    [Header("Thresholds")]
    [Range(0, 100)] public int lowThreshold = 20;
    [Range(0, 100)] public int emptyThreshold = 5;

    [Header("Simulation")]
    public SimulatedInkState simulatedState = SimulatedInkState.None;

    public enum SimulatedInkState
    {
        None,
        SimulateLow,
        SimulateEmpty,
        Random
    }

    public bool IsInkLow { get; private set; }
    public bool IsInkEmpty { get; private set; }
    public string InkStatusMessage { get; private set; } = "";

    public static event Action<bool, bool, string> OnInkStatusChanged;

    private const string PSM_DLL = "PSM_SDK";

    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int PSM_InitInstance();

    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int PSM_ExitInstance();

    [DllImport(PSM_DLL, CharSet = CharSet.Unicode)]
    private static extern int PSM_OpenPrinterW(string printerName, out IntPtr phPrinter);

    [DllImport(PSM_DLL, CharSet = CharSet.Ansi)]
    private static extern int PSM_OpenPrinterA(string printerName, out IntPtr phPrinter);

    [DllImport(PSM_DLL)]
    private static extern int PSM_ClosePrinter(IntPtr hPrinter);

    [DllImport(PSM_DLL)]
    private static extern int PSM_GetPrinterInformation(
        IntPtr hPrinter,
        int nInfoId,
        IntPtr pInfo,
        int infoSize
    );

    // IMPORTANT:
    // Confirm this value from Epson PSM SDK documentation.
    // If wrong, SDK may return invalid data or crash.
    private const int INFO_ID_INK = 1;

    private const int INFO_HEADER_SIZE = 8;
    private const int INK_INFO_ELEMENT_SIZE = 12;
    private const int MAX_INK_SLOTS = 8;
    private const int INFO_BUF_SIZE = INFO_HEADER_SIZE + (MAX_INK_SLOTS * INK_INFO_ELEMENT_SIZE) + 512;

    private const uint INK_STATUS_EMPTY = 0x01;
    private const uint INK_STATUS_LOW = 0x02;

    private bool _sdkInitialized;
    private Coroutine _pollCoroutine;

    private bool _lastLow;
    private bool _lastEmpty;
    private string _lastMessage = "";

    private static string ColorName(uint colorId)
    {
        switch (colorId)
        {
            case 0: return "Cyan";
            case 1: return "Magenta";
            case 2: return "Yellow";
            case 3: return "Black";
            case 4: return "Light Cyan";
            case 5: return "Light Magenta";
            case 10: return "Maintenance Tank";
            default: return "Ink#" + colorId;
        }
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        InitSDK();

        if (autoStartPolling)
        {
            _pollCoroutine = StartCoroutine(PollRoutine());
        }
    }

    private void OnDestroy()
    {
        if (_pollCoroutine != null)
        {
            StopCoroutine(_pollCoroutine);
            _pollCoroutine = null;
        }

        ShutdownSDK();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void InitSDK()
    {
        if (_sdkInitialized) return;

        try
        {
            int ret = PSM_InitInstance();

            if (ret == 0)
            {
                _sdkInitialized = true;
                Debug.Log("[EpsonInkMonitor] Epson SDK initialized.");
                FireIfChanged(false, false, "Epson SDK initialized.\n<color=yellow>Searching for printer...</color>");
            }
            else
            {
                _sdkInitialized = false;
                Debug.LogError("[EpsonInkMonitor] Epson SDK init failed: " + ret);
                FireIfChanged(false, false, "<color=red>Epson SDK init failed: " + ret + "</color>");
            }
        }
        catch (Exception ex)
        {
            _sdkInitialized = false;
            Debug.LogError("[EpsonInkMonitor] SDK init exception: " + ex.Message);
            FireIfChanged(false, false, "<color=red>SDK init exception: " + ex.Message + "</color>");
        }
    }

    private void ShutdownSDK()
    {
        if (!_sdkInitialized) return;

        try
        {
            PSM_ExitInstance();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[EpsonInkMonitor] SDK shutdown exception: " + ex.Message);
        }

        _sdkInitialized = false;
    }

    private IEnumerator PollRoutine()
    {
        yield return new WaitForSeconds(3f);

        while (true)
        {
            CheckInkLevel();
            yield return new WaitForSeconds(pollIntervalSeconds);
        }
    }

    private void Update()
    {
        if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.I))
        {
            CheckInkLevel();
        }

        if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.S))
        {
            simulatedState = simulatedState == SimulatedInkState.None
                ? SimulatedInkState.Random
                : SimulatedInkState.None;

            CheckInkLevel();
        }
    }

    public void ForceCheck()
    {
        CheckInkLevel();
    }

    public void CheckInkLevel()
    {
        if (simulatedState != SimulatedInkState.None)
        {
            RunSimulation();
            return;
        }

        if (!_sdkInitialized)
        {
            FireIfChanged(false, false, "<color=red>Epson SDK is not initialized.</color>");
            return;
        }

        IntPtr hPrinter = IntPtr.Zero;
        IntPtr pBuffer = IntPtr.Zero;

        try
        {
            // 1. Build a list of potential printer names to try
            var printerVariations = new System.Collections.Generic.List<string>();
            
            // Priority: The printer selected in PrintingManager
            string selected = PrintingManager.Instance?.selectedPrinter?.Trim() ?? "";
            if (!string.IsNullOrEmpty(selected)) {
                printerVariations.Add(selected);
                // Try without "EPSON "
                string noEpson = selected.Replace("EPSON ", "").Trim();
                if (!printerVariations.Contains(noEpson)) printerVariations.Add(noEpson);
                // Try without " Series"
                string noSeries = selected.Replace(" Series", "").Trim();
                if (!printerVariations.Contains(noSeries)) printerVariations.Add(noSeries);
                // Try both removed
                string minimal = noEpson.Replace(" Series", "").Trim();
                if (!printerVariations.Contains(minimal)) printerVariations.Add(minimal);
            }

            // Fallback 1: Scan all installed system printers
            try {
                foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters) {
                    if (p.Contains("SL-D") || p.Contains("D500") || p.Contains("EPSON")) {
                        if (!printerVariations.Contains(p)) printerVariations.Add(p);
                        string clean = p.Replace("EPSON ", "").Replace(" Series", "").Trim();
                        if (!printerVariations.Contains(clean)) printerVariations.Add(clean);
                    }
                }
            } catch { }

            // Fallback 2: Common port names
            for (int i = 1; i <= 4; i++) printerVariations.Add($"USB00{i}");

            int lastErrorW = 0;
            int lastErrorA = 0;
            string lastAttemptedName = "";

            // 2. Loop through variations
            foreach (string name in printerVariations)
            {
                lastAttemptedName = name;
                FireIfChanged(false, false, $"<color=yellow>Checking printer: {name}</color>");
                
                int openRetW = -1;
                try { openRetW = PSM_OpenPrinterW(name, out hPrinter); } catch { }
                if (openRetW == 0 && hPrinter != IntPtr.Zero) break;
                lastErrorW = openRetW;

                int openRetA = -1;
                try { openRetA = PSM_OpenPrinterA(name, out hPrinter); } catch { }
                if (openRetA == 0 && hPrinter != IntPtr.Zero) break;
                lastErrorA = openRetA;

                if (hPrinter != IntPtr.Zero) { PSM_ClosePrinter(hPrinter); hPrinter = IntPtr.Zero; }
            }

            if (hPrinter == IntPtr.Zero)
            {
                string debugInfo = $"<color=red>Could not find printer.</color>\nName: {lastAttemptedName}\nErrW: {lastErrorW} | ErrA: {lastErrorA}\n\nList:\n";
                foreach (var p in printerVariations) debugInfo += $"- {p}\n";
                FireIfChanged(false, false, debugInfo);
                return;
            }

            // 3. Get Ink Information
            pBuffer = Marshal.AllocHGlobal(INFO_BUF_SIZE);
            ZeroMemory(pBuffer, INFO_BUF_SIZE);

            int infoRet = PSM_GetPrinterInformation(hPrinter, INFO_ID_INK, pBuffer, INFO_BUF_SIZE);

            if (infoRet != 0)
            {
                FireIfChanged(
                    false,
                    false,
                    $"<color=red>Failed to get ink data.</color>\nPrinter: {lastAttemptedName}\nError: {infoRet}"
                );
                return;
            }

            ParseInkInfo(pBuffer);
        }
        catch (Exception ex)
        {
            Debug.LogError("[EpsonInkMonitor] Check ink exception: " + ex.Message);
            FireIfChanged(false, false, "<color=red>Ink check exception: " + ex.Message + "</color>");
        }
        finally
        {
            if (pBuffer != IntPtr.Zero) Marshal.FreeHGlobal(pBuffer);
            if (hPrinter != IntPtr.Zero) PSM_ClosePrinter(hPrinter);
        }
    }


    private void ParseInkInfo(IntPtr pInfo)
    {
        int count = Marshal.ReadInt32(pInfo, 4);

        if (count <= 0 || count > MAX_INK_SLOTS)
        {
            FireIfChanged(false, false, "<color=yellow>Printer found, but ink data is empty or invalid.</color>");
            return;
        }

        bool low = false;
        bool empty = false;

        var message = new System.Text.StringBuilder();
        var payload = new InkStatusPayload
        {
            inks = new System.Collections.Generic.List<InkEntry>()
        };

        for (int i = 0; i < count; i++)
        {
            int offset = INFO_HEADER_SIZE + i * INK_INFO_ELEMENT_SIZE;

            uint colorId = (uint)Marshal.ReadInt32(pInfo, offset);
            int level = Marshal.ReadInt32(pInfo, offset + 4);
            uint flags = (uint)Marshal.ReadInt32(pInfo, offset + 8);

            level = Mathf.Clamp(level, 0, 100);

            string colorName = ColorName(colorId);

            bool isEmpty = (flags & INK_STATUS_EMPTY) != 0 || level <= emptyThreshold;
            bool isLow = (flags & INK_STATUS_LOW) != 0 || level <= lowThreshold;

            if (isEmpty)
            {
                empty = true;
                low = true;
            }
            else if (isLow)
            {
                low = true;
            }

            string status = isEmpty ? "empty" : isLow ? "low" : "ok";
            string displayStatus = isEmpty ? "Empty" : isLow ? "Low" : "OK";
            string color = isEmpty ? "#FF0000" : isLow ? "#FFFF00" : "#00FF00";

            message.AppendLine(colorName + ": <color=" + color + ">" + displayStatus + "</color> (" + level + "%)");

            payload.inks.Add(new InkEntry
            {
                color = colorName.ToLower().Replace(" ", ""),
                status = status,
                level = level
            });
        }

        string finalMessage = message.ToString().Trim();

        FireIfChanged(low, empty, finalMessage);
        SendInkStatusToBackend(payload);
    }

    private void RunSimulation()
    {
        bool low = false;
        bool empty = false;

        var message = new System.Text.StringBuilder();
        var payload = new InkStatusPayload
        {
            inks = new System.Collections.Generic.List<InkEntry>()
        };

        string[] colors = { "Cyan", "Magenta", "Yellow", "Black" };

        foreach (string colorName in colors)
        {
            int level;

            switch (simulatedState)
            {
                case SimulatedInkState.SimulateEmpty:
                    level = UnityEngine.Random.Range(0, emptyThreshold + 1);
                    break;

                case SimulatedInkState.SimulateLow:
                    level = UnityEngine.Random.Range(emptyThreshold + 1, lowThreshold + 1);
                    break;

                case SimulatedInkState.Random:
                    level = UnityEngine.Random.Range(0, 101);
                    break;

                default:
                    level = UnityEngine.Random.Range(50, 101);
                    break;
            }

            bool isEmpty = level <= emptyThreshold;
            bool isLow = level <= lowThreshold;

            if (isEmpty)
            {
                empty = true;
                low = true;
            }
            else if (isLow)
            {
                low = true;
            }

            string status = isEmpty ? "empty" : isLow ? "low" : "ok";
            string displayStatus = isEmpty ? "Empty" : isLow ? "Low" : "OK";
            string displayColor = isEmpty ? "#FF0000" : isLow ? "#FFFF00" : "#00FF00";

            message.AppendLine(colorName + ": <color=" + displayColor + ">" + displayStatus + "</color> (" + level + "%)");

            payload.inks.Add(new InkEntry
            {
                color = colorName.ToLower(),
                status = status,
                level = level
            });
        }

        FireIfChanged(low, empty, message.ToString().Trim());
        SendInkStatusToBackend(payload);
    }

    private void FireIfChanged(bool low, bool empty, string message)
    {
        if (empty) low = true;

        if (low == _lastLow && empty == _lastEmpty && message == _lastMessage)
        {
            return;
        }

        _lastLow = low;
        _lastEmpty = empty;
        _lastMessage = message;

        IsInkLow = low;
        IsInkEmpty = empty;
        InkStatusMessage = message;

        OnInkStatusChanged?.Invoke(low, empty, message);

        Debug.Log("[EpsonInkMonitor] Ink Status:\n" + message);
    }

    private void SendInkStatusToBackend(InkStatusPayload payload)
    {
        string boothId = PlayerPrefs.GetString("booth_id", "");

        if (string.IsNullOrEmpty(boothId))
        {
            return;
        }

        string url = API.BaseURL + "/api/photobooth/booths/" + boothId + "/ink-status";
        string json = JsonUtility.ToJson(payload);

        StartCoroutine(PostInkStatus(url, json));
    }

    private IEnumerator PostInkStatus(string url, string json)
    {
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[EpsonInkMonitor] Failed to send ink status: " + request.error);
            }
        }
    }

    private void ZeroMemory(IntPtr ptr, int size)
    {
        for (int i = 0; i < size; i++)
        {
            Marshal.WriteByte(ptr, i, 0);
        }
    }

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
        public int level;
    }
}