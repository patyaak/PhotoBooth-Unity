using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Monitors Epson SL-D500 ink levels via PSM_SDK.dll.
/// Fires OnInkStatusChanged whenever the ink state changes.
/// </summary>
public class EpsonInkMonitor : MonoBehaviour
{
    public static EpsonInkMonitor Instance { get; private set; }

    [Header("Polling")]
    public float pollIntervalSeconds = 300f;

    [Header("Thresholds (0-100)")]
    [Range(0, 100)] public int lowThreshold  = 20;
    [Range(0, 100)] public int emptyThreshold = 5;

    [Header("Simulation (Editor / test)")]
    public SimulatedInkState simulatedState = SimulatedInkState.None;
    public enum SimulatedInkState { None, SimulateLow, SimulateEmpty, Random }

    public bool IsInkLow   { get; private set; }
    public bool IsInkEmpty { get; private set; }
    public string InkStatusMessage { get; private set; } = string.Empty;

    public static event Action<bool, bool, string> OnInkStatusChanged;

    private const string PSM_DLL = "PSM_SDK";

    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int PSM_InitInstance();
    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int PSM_ExitInstance();
    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int PSM_InitInstanceEx(int nType);
    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int PSM_GetSDKVersion(IntPtr pVersion);

    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto)]
    private static extern int PSM_OpenPrinter(string printerName, out IntPtr phPrinter);
    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto)]
    private static extern int PSM_OpenPrinterEx(string printerName, int nOption, out IntPtr phPrinter);
    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int PSM_ClosePrinter(IntPtr hPrinter);
    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto)]
    private static extern int PSM_RegisterPrinter(string printerName, int nOption);
    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto)]
    private static extern int PSM_UnregisterPrinter(string printerName);
    [DllImport(PSM_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int PSM_GetPrinterInformation(IntPtr hPrinter, int nInfoId, IntPtr pInfo, int infoSize);

    private const int INK_INFO_ELEMENT_SIZE = 12;
    private const int MAX_INK_SLOTS         = 8;
    private const int INFO_HEADER_SIZE      = 8;
    private const int INFO_BUF_SIZE         = INFO_HEADER_SIZE + (MAX_INK_SLOTS * INK_INFO_ELEMENT_SIZE) + 512;

    private const uint INK_STATUS_EMPTY    = 0x01;
    private const uint INK_STATUS_LOW      = 0x02;

    private static string ColorName(uint colorId) => colorId switch
    {
        0 => "Cyan", 1 => "Magenta", 2 => "Yellow", 3 => "Black", 
        4 => "Light Cyan", 5 => "Light Magenta", _ => $"Ink#{colorId}"
    };

    private bool _sdkInitialized = false;
    private bool _lastLow = false, _lastEmpty = false;
    private string _lastMsg = string.Empty;
    private string _sdkVersion = "Unknown";

    private void Awake() { if (Instance == null) Instance = this; else Destroy(gameObject); }
    private void Start() { InitSDK(); StartCoroutine(PollRoutine()); }
    private void OnDestroy() { ShutdownSDK(); }

    private void InitSDK()
    {
        try
        {
            // Try standard init first as it's the safest.
            int ret = PSM_InitInstance();
            
            // If standard fails, try Standard mode via Ex
            if (ret != 0) ret = PSM_InitInstanceEx(0);
            
            // If still fails, try Industrial mode (nType=1)
            if (ret != 0) ret = PSM_InitInstanceEx(1);

            _sdkInitialized = (ret == 0);
            if (_sdkInitialized)
            {
                IntPtr pVer = Marshal.AllocHGlobal(512);
                try { 
                    int vRet = PSM_GetSDKVersion(pVer); 
                    // Try Auto (Unicode on modern Windows) then fallback to Ansi if empty
                    _sdkVersion = (vRet == 0) ? Marshal.PtrToStringAuto(pVer) : "VerErr";
                    if (string.IsNullOrEmpty(_sdkVersion)) _sdkVersion = Marshal.PtrToStringAnsi(pVer);
                }
                catch { _sdkVersion = "VerEx"; }
                finally { Marshal.FreeHGlobal(pVer); }
                Debug.Log($"[EpsonInkMonitor] SDK Init Success ({ret}). Arch: {(IntPtr.Size == 8 ? "64" : "32")}-bit | Ver: {_sdkVersion}");
            }
            else {
                Debug.LogWarning($"[EpsonInkMonitor] SDK Init Failed with code: {ret}");
                InkStatusMessage = $"SDK Init Failed: {ret}";
            }
        }
        catch (EntryPointNotFoundException ex) { 
            Debug.LogError($"[EpsonInkMonitor] DLL Entry point missing: {ex.Message}");
            _sdkInitialized = false;
        }
        catch (Exception ex) { 
            Debug.LogError($"[EpsonInkMonitor] Init Error: {ex.Message}"); 
            _sdkInitialized = false;
        }
    }

    private void ShutdownSDK() { if (_sdkInitialized) PSM_ExitInstance(); _sdkInitialized = false; }

    private IEnumerator PollRoutine()
    {
        yield return new WaitForSeconds(5f);
        while (true) { CheckInkLevel(); yield return new WaitForSeconds(pollIntervalSeconds); }
    }

    private void Update()
    {
        if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.S)) {
            simulatedState = (simulatedState == SimulatedInkState.None) ? SimulatedInkState.Random : SimulatedInkState.None;
            CheckInkLevel();
        }
        if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.I)) CheckInkLevel();
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

    private void CheckInkLevel()
    {
        if (simulatedState != SimulatedInkState.None) { RunSimulation(); return; }
        if (!_sdkInitialized) return;

        string pName = PrintingManager.Instance?.selectedPrinter?.Trim() ?? "";
        IntPtr hPrinter = IntPtr.Zero;
        IntPtr pBuf = IntPtr.Zero;
        try
        {
            var variations = new System.Collections.Generic.List<string>();
            
            // 0. Try empty string (Default printer)
            variations.Add("");

            // 1. Add all installed printers that match keywords FIRST (Highest priority)
            foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters) {
                if (p.Contains("SL-D") || p.Contains("D500") || p.Contains("SLD") || p.Contains("SureLab") || p.Contains("Epson")) {
                    if (!variations.Contains(p)) {
                        variations.Add(p);
                    }
                }
            }

            // 2. Add current selected printer and its variations
            if (!string.IsNullOrEmpty(pName)) {
                if (!variations.Contains(pName)) variations.Insert(0, pName);
                string v1 = pName.Replace(" Series", "");
                string v2 = pName.Replace("EPSON ", "");
                if (!variations.Contains(v1)) variations.Add(v1);
                if (!variations.Contains(v2)) variations.Add(v2);
            }

            // 3. Add USB ports and Epson special ports
            for (int i=1; i<=8; i++) {
                variations.Add($"USB00{i}");
                variations.Add($"EPUSB{i}:");
                variations.Add($"EPUSB{i}");
            }
            
            int lastErr = 0;
            string bestName = "";
            string errSummary = "";

            // Increase USB port range
            for (int i=5; i<=8; i++) variations.Add($"USB00{i}");

            foreach (var name in variations)
            {
                // Try multiple open modes (Simplified to avoid crashes)
                IntPtr hDirect = IntPtr.Zero;
                int[] directModes = { -1, 1 }; 
                foreach (int mode in directModes) {
                    int ret = (mode == -1) ? PSM_OpenPrinter(name, out hDirect) : PSM_OpenPrinterEx(name, mode, out hDirect);
                    if (ret == 0 && hDirect != IntPtr.Zero) { hPrinter = hDirect; bestName = name; break; }
                    lastErr = ret;
                }
                if (hPrinter != IntPtr.Zero) break;

                // Try with registration
                for (int m=0; m<=1; m++) {
                    PSM_RegisterPrinter(name, m);
                    
                    // Small sync wait
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while(watch.ElapsedMilliseconds < 50) { }
                    
                    IntPtr h = IntPtr.Zero;
                    int[] openModes = { -1, 1 }; 
                    foreach (int mode in openModes) {
                        int ret = (mode == -1) ? PSM_OpenPrinter(name, out h) : PSM_OpenPrinterEx(name, mode, out h);
                        if (ret == 0 && h != IntPtr.Zero) { hPrinter = h; bestName = name; break; }
                        lastErr = ret;
                    }

                    if (hPrinter != IntPtr.Zero) break;
                    else PSM_UnregisterPrinter(name);
                }
                
                if (hPrinter != IntPtr.Zero) break;
                else if (errSummary.Length < 60) errSummary += $"{name}:{lastErr} ";
            }

            if (hPrinter == IntPtr.Zero) {
                FireIfChanged(false, false, $"<color=red>Error: {lastErr}</color>\nSDK: {_sdkVersion} | Checked: {variations.Count}\nLast: {errSummary}\nTry Shift+S");
                return;
            }

            pBuf = Marshal.AllocHGlobal(INFO_BUF_SIZE);
            for (int i = 0; i < INFO_BUF_SIZE; i++) Marshal.WriteByte(pBuf, i, 0);

            if (PSM_GetPrinterInformation(hPrinter, 1, pBuf, INFO_BUF_SIZE) == 0) ParseInkInfo(pBuf);
        }
        catch (Exception ex) { Debug.LogError($"[EpsonInkMonitor] Check Error: {ex.Message}"); }
        finally {
            if (pBuf != IntPtr.Zero) Marshal.FreeHGlobal(pBuf);
            if (hPrinter != IntPtr.Zero) PSM_ClosePrinter(hPrinter);
        }
    }

    private void ParseInkInfo(IntPtr pInfo)
    {
        uint count = (uint)Marshal.ReadInt32(pInfo, 4);
        if (count == 0 || count > MAX_INK_SLOTS) {
            FireIfChanged(false, false, "Printer Found\n<color=yellow>Reading Ink Info...</color>");
            return;
        }

        bool low = false, empty = false;
        var sb = new System.Text.StringBuilder();
        var payload = new InkStatusPayload { inks = new System.Collections.Generic.List<InkEntry>() };

        for (int i = 0; i < (int)count; i++)
        {
            int off = INFO_HEADER_SIZE + i * INK_INFO_ELEMENT_SIZE;
            uint id = (uint)Marshal.ReadInt32(pInfo, off);
            int lvl = Marshal.ReadInt32(pInfo, off + 4);
            uint flg = (uint)Marshal.ReadInt32(pInfo, off + 8);

            string name = ColorName(id);
            bool e = (flg & INK_STATUS_EMPTY) != 0 || lvl <= emptyThreshold;
            bool l = (flg & INK_STATUS_LOW) != 0 || lvl <= lowThreshold;

            if (e) empty = true; else if (l) low = true;
            string st = e ? "Empty" : (l ? "Low" : "OK");
            string col = e ? "#FF0000" : (l ? "#FFFF00" : "#00FF00");

            sb.AppendLine($"{name}: <color={col}>{st}</color>");
            payload.inks.Add(new InkEntry { color = name.ToLower().Replace(" ",""), status = st.ToLower(), level = lvl });
        }

        FireIfChanged(low, empty, sb.ToString().Trim());
        SendInkStatusToBackend(payload);
    }

    private void RunSimulation()
    {
        // Simple random sim
        var sb = new System.Text.StringBuilder();
        var payload = new InkStatusPayload { inks = new System.Collections.Generic.List<InkEntry>() };
        string[] colors = { "Cyan", "Magenta", "Yellow", "Black" };
        foreach(var c in colors) {
            int lvl = UnityEngine.Random.Range(10, 90);
            sb.AppendLine($"{c}: <color=#00FF00>OK</color>");
            payload.inks.Add(new InkEntry { color = c.ToLower(), status = "ok", level = lvl });
        }
        FireIfChanged(false, false, sb.ToString().Trim());
        SendInkStatusToBackend(payload);
    }

    private void FireIfChanged(bool low, bool empty, string msg)
    {
        if (empty) low = true;
        if (low == _lastLow && empty == _lastEmpty && msg == _lastMsg) return;
        _lastLow = low; _lastEmpty = empty; _lastMsg = msg;
        IsInkLow = low; IsInkEmpty = empty; InkStatusMessage = msg;
        OnInkStatusChanged?.Invoke(low, empty, msg);
    }

    private void SendInkStatusToBackend(InkStatusPayload payload)
    {
        string bid = PlayerPrefs.GetString("booth_id", "");
        if (string.IsNullOrEmpty(bid)) return;
        string url = $"{API.BaseURL}/api/photobooth/booths/{bid}/ink-status";
        StartCoroutine(Post(url, JsonUtility.ToJson(payload)));
    }

    private IEnumerator Post(string url, string json)
    {
        using (UnityWebRequest req = new UnityWebRequest(url, "POST")) {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();
        }
    }

    [Serializable] public class InkStatusPayload { public System.Collections.Generic.List<InkEntry> inks; }
    [Serializable] public class InkEntry { public string color; public string status; public int level; }
}
