using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
public class EpsonInkMonitor : MonoBehaviour
{
    public static EpsonInkMonitor Instance { get; private set; }
    [Header("Polling")]
    public float pollIntervalSeconds = 10f;
    public bool autoStartPolling = true;
    [Header("Thresholds")]
    public int lowThreshold = 20;
    public int emptyThreshold = 5;
    [Header("Simulation")]
    public SimulatedInkState simulatedState = SimulatedInkState.None;
    public enum SimulatedInkState { None, SimulateLow, SimulateEmpty, Random }
    public bool IsInkLow { get; private set; }
    public bool IsInkEmpty { get; private set; }
    public string InkStatusMessage { get; private set; } = "";
    public static event Action<bool, bool, string> OnInkStatusChanged;
    [Header("UI References")]
    public Button inkLevelbtn;
    private Coroutine _pollCoroutine;
    private string _helperExePath;
    private void Awake() {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }
    private void Start() {
        // SDK polling and helper compilation commented out as requested
        // CompileBidiHelper();
        // if (autoStartPolling) _pollCoroutine = StartCoroutine(PollRoutine());
        if (inkLevelbtn != null) {
            inkLevelbtn.onClick.AddListener(OnInkLevelButtonClicked);
        }
    }
    private void OnDestroy() {
        if (_pollCoroutine != null) StopCoroutine(_pollCoroutine);
    }

    public void OnInkLevelButtonClicked()
    {
        StartCoroutine(OpenPrinterStatusWindowCoroutine());
    }

    private IEnumerator OpenPrinterStatusWindowCoroutine()
    {
        string printerName = GetSelectedPrinter();
        Debug.Log($"[EpsonInkMonitor] Ink-level button clicked, locating Epson status monitor for printer '{printerName}'...");

        Task<string> locateTask = Task.Run(() => FindEpsonStatusMonitorExecutable());
        float timeout = 8f; // give a bit more time for disk/registry scans on slower machines
        while (!locateTask.IsCompleted && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (!locateTask.IsCompleted)
        {
            Debug.LogWarning("[EpsonInkMonitor] Epson status monitor lookup timed out. Falling back to direct printer settings.");
        }

        string monitorExe = locateTask.IsCompletedSuccessfully ? locateTask.Result : null;
        OpenPrinterStatusWindow(monitorExe, printerName);
    }
    /*
    private void CompileBidiHelper() {
        try {
            string cacheDir = Application.temporaryCachePath.Replace('/', '\\');
            string csPath = Path.Combine(cacheDir, "BidiScanner.cs");
            _helperExePath = Path.Combine(cacheDir, "BidiScanner.exe");
            // Normalize path separators for Windows
            csPath = Path.GetFullPath(csPath);
            _helperExePath = Path.GetFullPath(_helperExePath);
            // Delete existing exe to force recompilation and prevent stale cached versions
            try {
                if (File.Exists(_helperExePath)) {
                    File.Delete(_helperExePath);
                }
            } catch {}
            string csCode = @"
using System;
using System.Runtime.InteropServices;
class BidiScanner {
    [ComImport, Guid(""2A614240-A4C5-4C33-BD87-1BC709331639"")] class BidiSpl {}
    [ComImport, Guid(""B9162A23-45F9-47CC-80F5-FE0FE9B9E1A2"")] class BidiRequest {}
    [ComImport, Guid(""D580DC0E-DE39-4649-BAA8-BF0B85A03A97""), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IBidiSpl {
        void BindDevice([In, MarshalAs(UnmanagedType.LPWStr)] string prnName, [In] int dwAccess);
        void UnbindDevice();
        void SendRecv([In, MarshalAs(UnmanagedType.LPWStr)] string action, [In, MarshalAs(UnmanagedType.Interface)] IBidiRequest pRequest, [Out, MarshalAs(UnmanagedType.Interface)] out IBidiRequest ppResponse);
        void MultiSendRecv([In, MarshalAs(UnmanagedType.LPWStr)] string action, [In, MarshalAs(UnmanagedType.Interface)] object pRequestContainer, [Out, MarshalAs(UnmanagedType.Interface)] out object ppResponseContainer);
    }
    [ComImport, Guid(""8F348BD7-4B47-4755-8A9D-0F422DF3DC89""), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IBidiRequest {
        void SetSchema([In, MarshalAs(UnmanagedType.LPWStr)] string pszSchema);
        void SetInputData([In] uint dwType, [In] IntPtr pData, [In] uint cbSize);
        void GetResult([Out] out int phr);
        void GetOutputData([In] int dwIndex, [Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszSchema, [Out] out uint pdwType, [Out] out IntPtr ppData, [Out] out uint pcbSize);
        void GetEnumCount([Out] out int pdwTotal);
    }
    [STAThread]
    static void Main(string[] args) {
        if (args.Length == 0) return;
        string printerName = args[0];
        try {
            IBidiSpl bidi = (IBidiSpl)new BidiSpl();
            bidi.BindDevice(printerName, 1);
            IBidiRequest req = (IBidiRequest)new BidiRequest();
            req.SetSchema(@""\Printer.Consumables"");
            
            IBidiRequest resp = null;
            bidi.SendRecv(""Get"", req, out resp);
            
            int hr = 0;
            resp.GetResult(out hr);
            if (hr == 0) {
                int count = 0;
                resp.GetEnumCount(out count);
                for (int i=0; i<count; i++) {
                    string schema = """";
                    uint type = 0;
                    IntPtr pData = IntPtr.Zero;
                    uint size = 0;
                    resp.GetOutputData(i, out schema, out type, out pData, out size);
                    if (type == 1) Console.WriteLine(schema + ""|"" + Marshal.ReadInt32(pData));
                    else if (type == 4 || type == 5) Console.WriteLine(schema + ""|"" + Marshal.PtrToStringUni(pData));
                }
            }
            bidi.UnbindDevice();
        } catch (Exception e) { Console.WriteLine(""ERROR|"" + e.Message); }
    }
}";
            File.WriteAllText(csPath, csCode);
            // Compile using the standard Windows .NET framework compiler
            string cscPath = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe";
            if (!File.Exists(cscPath)) cscPath = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe";
            if (File.Exists(cscPath)) {
                Debug.Log($"[EpsonInkMonitor] Found compiler at '{cscPath}'. Compiling '{csPath}' to '{_helperExePath}'...");
                ProcessStartInfo psi = new ProcessStartInfo(cscPath, $"/nologo /out:\"{_helperExePath}\" \"{csPath}\"") {
                    CreateNoWindow = true, 
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                Process proc = Process.Start(psi);
                if (proc != null) {
                    string cscOut = proc.StandardOutput.ReadToEnd();
                    string cscErr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    if (!File.Exists(_helperExePath)) {
                        Debug.LogError($"[EpsonInkMonitor] csc.exe failed to compile BidiHelper!\nOut: {cscOut}\nErr: {cscErr}");
                    } else {
                        Debug.Log("[EpsonInkMonitor] Compiled External BidiHelper successfully at: " + _helperExePath);
                    }
                }
            } else {
                Debug.LogWarning("[EpsonInkMonitor] csc.exe not found on system. Cannot compile BidiHelper.");
            }
        } catch (Exception ex) {
            Debug.LogError("[EpsonInkMonitor] Failed to compile BidiHelper: " + ex.Message);
        }
    }
    */
    private string GetSelectedPrinter() {
        string selected = "";
        var pManagerType = Type.GetType("PrintingManager");
        if (pManagerType != null) {
            var instanceProp = pManagerType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (instanceProp != null) {
                var instance = instanceProp.GetValue(null);
                if (instance != null) {
                    var selectedProp = pManagerType.GetField("selectedPrinter", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (selectedProp != null) selected = selectedProp.GetValue(instance) as string;
                }
            }
        }
        if (string.IsNullOrEmpty(selected)) {
            foreach (string pName in System.Drawing.Printing.PrinterSettings.InstalledPrinters) {
                if (pName.ToUpper().Contains("EPSON") || pName.ToUpper().Contains("SL-D")) {
                    selected = pName; break;
                }
            }
        }
        return string.IsNullOrEmpty(selected) ? "EPSON SL-D500 Series" : selected;
    }
    /*
    private IEnumerator PollRoutine() {
        while (true) {
            bool found = false;
            List<InkEntry> currentInks = new List<InkEntry>();
            string printerName = GetSelectedPrinter();
            string portName = NativePrinterHelper.GetPrinterPort(printerName);
            string ip = ExtractIP(portName);
            Debug.Log($"[EpsonInkMonitor] Polling printer. Detected Name: '{printerName}', Detected Port: '{portName}', Extracted IP: '{ip}'");
            // 1. Try Web Scraper (if printer is on the Network and has an IP)
            if (!string.IsNullOrEmpty(ip)) {
                Debug.Log("[EpsonInkMonitor] Attempting Strategy 1: Web Scraper...");
                yield return CheckInkWebScraper(ip, (inks) => {
                    if (inks != null && inks.Count > 0) {
                        currentInks = inks;
                        found = true;
                        Debug.Log($"[EpsonInkMonitor] Strategy 1 (Web Scraper) SUCCESS! Found {inks.Count} inks.");
                    } else {
                        Debug.Log("[EpsonInkMonitor] Strategy 1 (Web Scraper) failed to return inks.");
                    }
                });
            }
            // 2. Try External Bidi Helper (Handles USB + Spooler Network fallback)
            if (!found && File.Exists(_helperExePath)) {
                Debug.Log($"[EpsonInkMonitor] Attempting Strategy 2: External Bidi Helper ({_helperExePath})...");
                currentInks = CheckInkExternal(printerName);
                if (currentInks.Count > 0) {
                    found = true;
                    Debug.Log($"[EpsonInkMonitor] Strategy 2 (Bidi Helper) SUCCESS! Found {currentInks.Count} inks.");
                } else {
                    Debug.Log("[EpsonInkMonitor] Strategy 2 (Bidi Helper) failed to return inks.");
                }
            }
            // 3. Try Windows Registry Scanner (Deep fallback for USB Epson Drivers)
            if (!found) {
                Debug.Log("[EpsonInkMonitor] Attempting Strategy 3: Registry Scanner...");
                currentInks = CheckInkRegistry(printerName);
                if (currentInks.Count > 0) {
                    found = true;
                    Debug.Log($"[EpsonInkMonitor] Strategy 3 (Registry Scanner) SUCCESS! Found {currentInks.Count} inks.");
                } else {
                    Debug.Log("[EpsonInkMonitor] Strategy 3 (Registry Scanner) failed to return inks.");
                }
            }
            // Fallback: Simulation
            if (!found && simulatedState != SimulatedInkState.None) {
                Debug.Log("[EpsonInkMonitor] Falling back to Strategy 4: Simulation.");
                currentInks = GetSimulatedInks();
                found = true;
            }
            UpdateStatus(currentInks);
            if (found) SendToBackend(currentInks);
            yield return new WaitForSeconds(pollIntervalSeconds);
        }
    }
    private string ExtractIP(string portName) {
        if (string.IsNullOrEmpty(portName)) return null;
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(portName, @"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b");
        return m.Success ? m.Value : null;
    }
    private IEnumerator CheckInkWebScraper(string ip, Action<List<InkEntry>> onComplete) {
        // Try the standard Epson Web GUI paths
        string[] urlsToTry = {
            $"http://{ip}/PRESENTATION/HTML/TOP/PRTINFO.HTML",
            $"http://{ip}/"
        };
        List<InkEntry> foundInks = new List<InkEntry>();
        foreach (string url in urlsToTry) {
            using (UnityWebRequest req = UnityWebRequest.Get(url)) {
                req.timeout = 5;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) {
                    string html = req.downloadHandler.text;
                    
                    // Epson usually draws ink bars using images like 'Ink_K.PNG' and sets their 'height' to max 50px.
                    // SL-D500 colors: Black (K), Cyan (C), Magenta (M), Yellow (Y), Light Cyan (LC), Light Magenta (LM)
                    string[] colors = { "K", "M", "Y", "C", "LC", "LM" };
                    uint[] colorIds = { 3, 1, 2, 0, 4, 5 };
                    
                    for (int i = 0; i < colors.Length; i++) {
                        // Regex looks for Ink_X.PNG followed closely by a height attribute
                        string pattern = $@"Ink_{colors[i]}\.PNG.*?height['""]?\s*[:=]\s*['""]?(\d+)";
                        var match = System.Text.RegularExpressions.Regex.Match(html, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                        
                        if (match.Success) {
                            int height = int.Parse(match.Groups[1].Value);
                            // Convert 50px max height to 100%
                            int percentage = Mathf.Clamp(Mathf.RoundToInt((height / 50f) * 100f), 0, 100);
                            foundInks.Add(new InkEntry { colorId = colorIds[i], level = percentage, status = 0 });
                        }
                    }
                    if (foundInks.Count > 0) {
                        Debug.Log($"[EpsonInkMonitor] Web Scraper successfully found {foundInks.Count} inks at {url}");
                        break; // Stop trying other URLs if we found data
                    }
                }
            }
        }
        
        onComplete(foundInks);
    }
    private List<InkEntry> CheckInkRegistry(string printerName) {
        List<InkEntry> results = new List<InkEntry>();
        try {
            string path = $@"SYSTEM\CurrentControlSet\Control\Print\Printers\{printerName}\PrinterDriverData";
            Debug.Log($"[EpsonInkMonitor] Checking Registry at: HKLM\\{path}");
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path)) {
                if (key != null) {
                    Debug.Log("[EpsonInkMonitor] HKLM Registry key successfully opened!");
                    int k = -1, c = -1, m = -1, y = -1, lc = -1, lm = -1, mb = -1;
                    string[] valNames = key.GetValueNames();
                    Debug.Log($"[EpsonInkMonitor] Registry value count: {valNames.Length}. Names: {string.Join(", ", valNames)}");
                    
                    foreach (string v in valNames) {
                        string vu = v.ToUpper();
                        if (vu.Contains("INK") || vu.Contains("LEVEL") || vu.Contains("REMAIN") || vu.Contains("STATUS")) {
                            int level = -1;
                            object val = key.GetValue(v);
                            if (val is int i) level = i;
                            else if (val is string s) int.TryParse(s, out level);
                            Debug.Log($"[EpsonInkMonitor] Registry ink key found: '{v}' = {val} (parsed: {level})");
                            if (level >= 0 && level <= 100) {
                                if (vu.Contains("K") || vu.Contains("BLACK")) k = level;
                                else if (vu.Contains("LC") || vu.Contains("LIGHTCYAN")) lc = level;
                                else if (vu.Contains("LM") || vu.Contains("LIGHTMAGENTA")) lm = level;
                                else if (vu.Contains("C") || vu.Contains("CYAN")) c = level;
                                else if (vu.Contains("M") || vu.Contains("MAGENTA")) m = level;
                                else if (vu.Contains("Y") || vu.Contains("YELLOW")) y = level;
                                else if (vu.Contains("BOX") || vu.Contains("MAIN")) mb = level;
                            }
                        }
                    }
                    if (k != -1) results.Add(new InkEntry { colorId = 3, level = k, status = 0 });
                    if (c != -1) results.Add(new InkEntry { colorId = 0, level = c, status = 0 });
                    if (m != -1) results.Add(new InkEntry { colorId = 1, level = m, status = 0 });
                    if (y != -1) results.Add(new InkEntry { colorId = 2, level = y, status = 0 });
                    if (lc != -1) results.Add(new InkEntry { colorId = 4, level = lc, status = 0 });
                    if (lm != -1) results.Add(new InkEntry { colorId = 5, level = lm, status = 0 });
                    if (mb != -1) results.Add(new InkEntry { colorId = 10, level = mb, status = 0 });
                } else {
                    Debug.LogWarning($"[EpsonInkMonitor] Registry key path HKLM\\{path} does not exist.");
                }
            }
        } catch (Exception ex) {
            Debug.LogWarning("[EpsonInkMonitor] Registry Scanner failed: " + ex.Message);
        }
        return results;
    }
    private List<InkEntry> CheckInkExternal(string printerName) {
        List<InkEntry> results = new List<InkEntry>();
        try {
            Debug.Log($"[EpsonInkMonitor] Spawning BidiScanner.exe \"{printerName}\"");
            ProcessStartInfo psi = new ProcessStartInfo(_helperExePath, $"\"{printerName}\"") {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            
            Process proc = Process.Start(psi);
            if (proc != null) {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);
                
                Debug.Log($"[EpsonInkMonitor] BidiScanner raw stdout:\n{output}");
                
                List<int> foundLevels = new List<int>();
                
                foreach(string line in output.Split('\n')) {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    if (trimmed.StartsWith("ERROR|")) {
                        Debug.LogWarning("[EpsonInkMonitor] External Helper Error: " + trimmed.Substring(6));
                        continue;
                    }
                    
                    string[] parts = trimmed.Split('|');
                    if (parts.Length == 2) {
                        int parsed;
                        if (int.TryParse(parts[1], out parsed) && parsed >= 0 && parsed <= 100) {
                            foundLevels.Add(parsed);
                        }
                    }
                }
                
                // If we found levels (6 colors + maintenance box usually)
                if (foundLevels.Count >= 6) {
                    // Map SL-D500 typical output
                    results.Add(new InkEntry { colorId = 3, level = foundLevels[0], status = 0 }); // Black
                    results.Add(new InkEntry { colorId = 4, level = foundLevels[1], status = 0 }); // Light Cyan
                    results.Add(new InkEntry { colorId = 1, level = foundLevels[2], status = 0 }); // Magenta
                    results.Add(new InkEntry { colorId = 0, level = foundLevels[3], status = 0 }); // Cyan
                    results.Add(new InkEntry { colorId = 2, level = foundLevels[4], status = 0 }); // Yellow
                    results.Add(new InkEntry { colorId = 5, level = foundLevels[5], status = 0 }); // Light Magenta
                    
                    if (foundLevels.Count >= 7) {
                        results.Add(new InkEntry { colorId = 10, level = foundLevels[6], status = 0 }); // Maintenance Box
                    }
                }
            }
        } catch (Exception ex) {
            Debug.LogWarning("[EpsonInkMonitor] Failed to run External Helper: " + ex.Message);
        }
        return results;
    }
    private void UpdateStatus(List<InkEntry> inks) {
        bool low = false, empty = false;
        string msg = "";
        if (inks.Count == 0) {
            msg = "<color=red>Printer not found or SDK error.</color>";
        } else {
            foreach (var ink in inks) {
                string color = GetColorName(ink.colorId);
                msg += $"{color}: {ink.level}%\n";
                if (ink.level <= emptyThreshold) empty = true;
                else if (ink.level <= lowThreshold) low = true;
            }
        }
        IsInkLow = low; IsInkEmpty = empty; InkStatusMessage = msg;
        OnInkStatusChanged?.Invoke(low, empty, msg);
    }
    private string GetColorName(uint id) {
        switch (id) {
            case 0: return "Cyan"; case 1: return "Magenta"; case 2: return "Yellow"; case 3: return "Black";
            case 4: return "Light Cyan"; case 5: return "Light Magenta";
            case 10: return "Maintenance Box"; default: return "Ink " + id;
        }
    }
    private void SendToBackend(List<InkEntry> inks) {
        string boothId = PlayerPrefs.GetString("booth_id", "test_booth");
        string url = $"{API.BaseURL}/api/photobooth/booths/{boothId}/ink-status";
        string json = "{\"inks\":" + JsonUtility.ToJson(new InkWrapper { inks = inks }) + "}";
        StartCoroutine(PostInk(url, json));
    }
    private IEnumerator PostInk(string url, string json) {
        using (UnityWebRequest req = new UnityWebRequest(url, "POST")) {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();
        }
    }
    */
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;

    public void ForceCheck()
    {
        // SDK monitoring disabled
    }
    private void OpenPrinterStatusWindow(string monitorExe, string printerName)
    {
        try
        {
            Debug.Log($"[EpsonInkMonitor] Attempting to open printer status window for: {printerName}");

            // Check if the printer is actually installed on this system
            bool printerExists = false;
            foreach (string pName in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            {
                if (pName.Equals(printerName, StringComparison.OrdinalIgnoreCase))
                {
                    printerExists = true;
                    break;
                }
            }

            if (!printerExists)
            {
                Debug.LogWarning($"[EpsonInkMonitor] Printer '{printerName}' is not installed on this computer. Opening Windows Printers & Scanners settings page as fallback.");
                OpenPrintersSettingsFallback();
                return;
            }

            // Prefer the executable path found by the background task (passed in), otherwise try spool folder first
            if (string.IsNullOrEmpty(monitorExe))
            {
                string spoolPath = @"C:\Windows\System32\spool\drivers\x64\3";
                try
                {
                    if (Directory.Exists(spoolPath))
                    {
                        string found = FindEpsonMonitorExeSafe(spoolPath);
                        if (!string.IsNullOrEmpty(found))
                        {
                            monitorExe = found;
                            Debug.Log($"[EpsonInkMonitor] Found monitor executable in spool folder: '{monitorExe}'");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[EpsonInkMonitor] Error scanning spool folder: {ex.Message}");
                }
            }

            if (string.IsNullOrEmpty(monitorExe))
            {
                monitorExe = FindEpsonStatusMonitorExecutable();
                if (!string.IsNullOrEmpty(monitorExe))
                {
                    Debug.Log($"[EpsonInkMonitor] Status monitor executable found: '{monitorExe}'");
                }
            }

            if (!string.IsNullOrEmpty(monitorExe) && File.Exists(monitorExe))
            {
                Debug.Log($"[EpsonInkMonitor] Launching Status Monitor executable: '{monitorExe}' with printer '{printerName}'");
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = monitorExe,
                    Arguments = $"/3 /PQ /N \"{printerName}\"",
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                Process proc = Process.Start(psi);
                
                // Try to bring the opened status monitor window to the front
                StartCoroutine(BringWindowToFront(proc, monitorExe));
                return;
            }

            // If we didn't find an absolute path, try launching common Epson monitor executable names
            if (string.IsNullOrEmpty(monitorExe))
            {
                string[] tryNames = new string[] {
                    "StatusMonitor3.exe",
                    "StatusMonitor.exe",
                    "EpsonStatusMonitor.exe",
                    "EPJPRSts.exe",
                    "EPSONPR.exe",
                    "E_S10.exe",
                    "E_S50.exe",
                    "E_IJPLBK.exe"
                };

                foreach (var name in tryNames)
                {
                    try
                    {
                        Debug.Log($"[EpsonInkMonitor] Attempting direct launch of '{name}'...");
                        ProcessStartInfo psi2 = new ProcessStartInfo
                        {
                            FileName = name,
                            Arguments = $"/3 /PQ /N \"{printerName}\"",
                            UseShellExecute = true,
                            CreateNoWindow = false
                        };
                        var proc2 = Process.Start(psi2);
                        if (proc2 != null)
                        {
                            StartCoroutine(BringWindowToFront(proc2, name));
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"[EpsonInkMonitor] Direct launch attempt '{name}' failed: {ex.Message}");
                    }
                }
            }

            // Fallback: Launch printing preferences dialog via printui.dll
            Debug.Log("[EpsonInkMonitor] Status monitor executable not found. Falling back to printing preferences.");
            ProcessStartInfo printuiPsi = new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = $"printui.dll,PrintUIEntry /e /n \"{printerName}\"",
                UseShellExecute = true,
                CreateNoWindow = false
            };
            Process.Start(printuiPsi);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[EpsonInkMonitor] Failed to open printer status window: {ex.Message}");
            OpenPrintersSettingsFallback();
        }
    }

    private IEnumerator BringWindowToFront(Process proc, string exePath)
    {
        // Wait a bit for the window to open
        yield return new WaitForSeconds(1.5f);

        try
        {
            IntPtr hWnd = IntPtr.Zero;

            // If we have a process object and it has a main window handle
            if (proc != null && !proc.HasExited)
            {
                proc.Refresh();
                hWnd = proc.MainWindowHandle;
            }

            // If we couldn't get the handle from the started process (e.g. it delegates to an existing process), 
            // search running processes for the executable
            if (hWnd == IntPtr.Zero)
            {
                string exeName = Path.GetFileNameWithoutExtension(exePath);
                Process[] procs = Process.GetProcessesByName(exeName);
                foreach (var p in procs)
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        hWnd = p.MainWindowHandle;
                        break;
                    }
                }
            }

            if (hWnd != IntPtr.Zero)
            {
                Debug.Log($"[EpsonInkMonitor] Bringing window {hWnd} to front.");
                SetForegroundWindow(hWnd);
                // Also make it topmost briefly to ensure it punches through Unity's fullscreen
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }
            else
            {
                Debug.Log("[EpsonInkMonitor] Could not find window handle to bring to front.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[EpsonInkMonitor] Error bringing window to front: {ex.Message}");
        }
    }

    private void OpenPrintersSettingsFallback()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "ms-settings:printers",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[EpsonInkMonitor] Ultimate fallback to settings failed: {ex.Message}");
        }
    }

    private string FindEpsonStatusMonitorExecutable()
    {
        string exe = FindRunningEpsonMonitorExecutable();
        if (!string.IsNullOrEmpty(exe))
            return exe;

        exe = FindEpsonMonitorFromRegistry();
        if (!string.IsNullOrEmpty(exe))
            return exe;

        exe = FindEpsonMonitorFromKnownPaths();
        if (!string.IsNullOrEmpty(exe))
            return exe;

        return null;
    }

    private string FindRunningEpsonMonitorExecutable()
    {
        try
        {
            foreach (var proc in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    string path = proc.MainModule.FileName;
                    if (IsEpsonStatusMonitorExecutable(path))
                    {
                        return path;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[EpsonInkMonitor] Running process scan failed: {ex.Message}");
        }
        return null;
    }

    private string FindEpsonMonitorFromKnownPaths()
    {
        string[] searchRoots = {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"C:\Windows\System32\spool\drivers\x64\3",
            @"C:\Windows\Sysnative\spool\drivers\x64\3",
            @"C:\Windows\System32\spool\DRIVERS",
            @"C:\Windows\Sysnative\spool\DRIVERS"
        };

        string[] knownNames = {
            "StatusMonitor3.exe",
            "StatusMonitor.exe",
            "EpsonStatusMonitor.exe",
            "E_IJPLBK.exe",
            "E_IJPLBA.exe",
            "E_S10.exe",
            "E_S50.exe",
            "E_S21.exe",
            "EPJPRSts.exe",
            "EPSONPR.exe"
        };

        foreach (string root in searchRoots)
        {
            if (string.IsNullOrEmpty(root))
                continue;

            foreach (string name in knownNames)
            {
                try
                {
                    string path = Path.Combine(root, name);
                    if (File.Exists(path) && IsEpsonStatusMonitorExecutable(path))
                        return path;
                }
                catch { }
            }

            string found = FindEpsonMonitorExeSafe(root);
            if (!string.IsNullOrEmpty(found))
                return found;
        }

        return null;
    }

    private bool IsEpsonStatusMonitorExecutable(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;

        string fileName = Path.GetFileName(filePath).ToUpperInvariant();
        if (fileName.Contains("UNINST") || fileName.Contains("SETUP") || fileName.Contains("E_W"))
            return false;

        if (fileName.Contains("STATUSMONITOR") || fileName.Contains("EPSON") || fileName.StartsWith("E_I") || fileName.StartsWith("E_S") || fileName.StartsWith("E_"))
        {
            try
            {
                var ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(filePath);
                string desc = ver.FileDescription ?? "";
                string prod = ver.ProductName ?? "";

                bool isStatusMonitor = desc.IndexOf("Status Monitor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       desc.IndexOf("StatusMonitor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       desc.IndexOf("ステータスモニタ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       desc.IndexOf("Printer Window", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       prod.IndexOf("Status Monitor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       prod.IndexOf("ステータスモニタ", StringComparison.OrdinalIgnoreCase) >= 0;

                return isStatusMonitor || fileName.Contains("STATUS") || fileName.Contains("MONITOR");
            }
            catch
            {
                return fileName.Contains("STATUS") || fileName.Contains("MONITOR");
            }
        }

        return false;
    }

    private string FindEpsonMonitorExeSafe(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return null;

        string bestExe = null;
        int bestScore = -1;

        Queue<string> pending = new Queue<string>();
        pending.Enqueue(rootPath);
        HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            string path = pending.Dequeue();
            if (visited.Contains(path)) continue;
            visited.Add(path);

            try
            {
                string[] files = Directory.GetFiles(path, "*.exe");
                foreach (string file in files)
                {
                    int score = ScoreEpsonExecutable(file);
                    if (score > bestScore && score > 0)
                    {
                        bestScore = score;
                        bestExe = file;
                    }
                }

                string[] dirs = Directory.GetDirectories(path);
                foreach (string dir in dirs)
                {
                    pending.Enqueue(dir);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Safe to ignore unauthorized access
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EpsonInkMonitor] Safe search warning at '{path}': {ex.Message}");
            }
        }

        if (bestExe != null)
        {
            Debug.Log($"[EpsonInkMonitor] Best executable found in '{rootPath}': {bestExe} with score {bestScore}");
        }
        return bestExe;
    }

    private string FindEpsonMonitorFromRegistry()
    {
        string[] registryPaths = {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
            @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce"
        };

        Microsoft.Win32.RegistryKey[] rootKeys = {
            Microsoft.Win32.Registry.CurrentUser,
            Microsoft.Win32.Registry.LocalMachine
        };

        foreach (var rootKey in rootKeys)
        {
            foreach (var relPath in registryPaths)
            {
                try
                {
                    using (var key = rootKey.OpenSubKey(relPath))
                    {
                        if (key == null) continue;
                        foreach (string valueName in key.GetValueNames())
                        {
                            object valObj = key.GetValue(valueName);
                            if (valObj == null) continue;
                            string valStr = valObj.ToString().Trim();
                            if (string.IsNullOrEmpty(valStr)) continue;

                            string exePath = ExtractPathFromCommandLine(valStr);
                            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                            {
                                string fileName = Path.GetFileName(exePath).ToUpper();
                                if ((fileName.StartsWith("E_I") || fileName.StartsWith("E_S") || fileName.StartsWith("E_")) && !fileName.StartsWith("E_W"))
                                {
                                    var ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                                    string desc = ver.FileDescription ?? "";
                                    string prod = ver.ProductName ?? "";
                                    
                                    bool isMatch = desc.IndexOf("Status Monitor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  desc.IndexOf("StatusMonitor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  desc.IndexOf("ステータスモニタ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  desc.IndexOf("Printer Window", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  prod.IndexOf("Status Monitor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  prod.IndexOf("ステータスモニタ", StringComparison.OrdinalIgnoreCase) >= 0;

                                    if (isMatch)
                                    {
                                        Debug.Log($"[EpsonInkMonitor] Found monitor executable from registry run key '{rootKey.Name}\\{relPath}\\{valueName}': {exePath}");
                                        return exePath;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[EpsonInkMonitor] Registry search error at '{rootKey.Name}\\{relPath}': {ex.Message}");
                }
            }
        }
        return null;
    }

    private string ExtractPathFromCommandLine(string cmdLine)
    {
        if (string.IsNullOrEmpty(cmdLine)) return null;
        cmdLine = cmdLine.Trim();
        string path = "";
        if (cmdLine.StartsWith("\""))
        {
            int nextQuote = cmdLine.IndexOf("\"", 1);
            if (nextQuote > 1)
            {
                path = cmdLine.Substring(1, nextQuote - 1);
            }
            else
            {
                path = cmdLine.Substring(1);
            }
        }
        else
        {
            int spaceIndex = cmdLine.IndexOf(" ");
            if (spaceIndex > 0)
            {
                path = cmdLine.Substring(0, spaceIndex);
            }
            else
            {
                path = cmdLine;
            }
        }
        return path;
    }

    private int ScoreEpsonExecutable(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return -1;
        string fileName = Path.GetFileName(filePath).ToUpper();
        if (fileName.StartsWith("E_W") || fileName.Contains("UNINST") || fileName.Contains("SETUP"))
        {
            return -1;
        }
        bool hasEpsonPattern = fileName.StartsWith("E_") || fileName.Contains("EPSON") || fileName.Contains("STATUS");
        if (!hasEpsonPattern)
        {
            return -1;
        }
        int score = 0;
        try
        {
            var ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(filePath);
            string desc = ver.FileDescription ?? "";
            string prod = ver.ProductName ?? "";
            bool isStatusMonitor = desc.IndexOf("Status Monitor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  desc.IndexOf("StatusMonitor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  desc.IndexOf("ステータスモニタ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  desc.IndexOf("Printer Window", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  prod.IndexOf("Status Monitor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  prod.IndexOf("ステータスモニタ", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isStatusMonitor)
            {
                score += 1000;
            }
            if (desc.IndexOf("EPSON", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prod.IndexOf("EPSON", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 100;
            }
        }
        catch
        {
            // Ignore version info read errors, just score based on filename
        }
        if (fileName.StartsWith("E_YATIS") || fileName.StartsWith("E_S10") || fileName.StartsWith("E_YATI"))
        {
            score += 200;
        }
        else if (fileName.StartsWith("E_I") || fileName.StartsWith("E_S"))
        {
            score += 50;
        }
        else if (fileName.StartsWith("E_"))
        {
            score += 10;
        }
        return score;
    }
    [Serializable] public struct InkEntry { public uint colorId; public int level; public uint status; }
    [Serializable] public class InkWrapper { public List<InkEntry> inks; }
}