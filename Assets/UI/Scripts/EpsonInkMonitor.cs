using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            inkLevelbtn.onClick.AddListener(OpenPrinterStatusWindow);
        }
    }

    private void OnDestroy() {
        if (_pollCoroutine != null) StopCoroutine(_pollCoroutine);
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

    public void ForceCheck() {
        // SDK monitoring disabled
    }

    public void OpenPrinterStatusWindow()
    {
        try
        {
            string printerName = GetSelectedPrinter();
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

            // 1. Search for Epson Status Monitor executable in running processes
            // We look specifically for E_I* or E_S* processes, avoiding background tray apps like E_WTTI64.EXE
            string monitorExe = null;
            try
            {
                foreach (var proc in System.Diagnostics.Process.GetProcesses())
                {
                    string name = proc.ProcessName.ToUpper();
                    if ((name.StartsWith("E_I") || name.StartsWith("E_S")) && !name.StartsWith("E_W"))
                    {
                        try
                        {
                            string path = proc.MainModule.FileName;
                            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            {
                                monitorExe = path;
                                Debug.Log($"[EpsonInkMonitor] Found monitor executable from running process: {monitorExe}");
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EpsonInkMonitor] Process scan failed: {ex.Message}");
            }

            // 2. Search for Epson Status Monitor executable in typical installation & spool paths recursively
            if (string.IsNullOrEmpty(monitorExe))
            {
                string[] searchPaths = {
                    @"C:\Windows\System32\spool\DRIVERS",
                    @"C:\Windows\Sysnative\spool\DRIVERS",
                    @"C:\Program Files\Epson",
                    @"C:\Program Files (x86)\Epson",
                    @"C:\Program Files\Epson Software",
                    @"C:\Program Files (x86)\Epson Software",
                    @"C:\Program Files\Seiko Epson",
                    @"C:\Program Files (x86)\Seiko Epson"
                };

                foreach (string basePath in searchPaths)
                {
                    monitorExe = FindEpsonMonitorExeSafe(basePath);
                    if (!string.IsNullOrEmpty(monitorExe))
                    {
                        break;
                    }
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
                Process.Start(psi);
                return;
            }

            // 3. Fallback: Launch printing preferences dialog via printui.dll
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

    private string FindEpsonMonitorExeSafe(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return null;

        // Search patterns in order of preference. E_I*.exe is most likely the actual Status Monitor.
        // We avoid matching E_W*.exe files which are background tray apps (like E_WTTI64.EXE).
        string[] patterns = { "E_I*.exe", "E_S*.exe", "E_*.exe", "epson*.exe", "*status*.exe" };

        foreach (string pattern in patterns)
        {
            Queue<string> pending = new Queue<string>();
            pending.Enqueue(rootPath);

            while (pending.Count > 0)
            {
                string path = pending.Dequeue();
                try
                {
                    string[] files = Directory.GetFiles(path, pattern);
                    if (files.Length > 0)
                    {
                        foreach (string file in files)
                        {
                            string fileName = Path.GetFileName(file).ToUpper();
                            if (fileName.StartsWith("E_W"))
                            {
                                // Skip tray/writing helpers like E_WTTI64.EXE
                                continue;
                            }
                            return file;
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
        }
        return null;
    }

    /*
    private List<InkEntry> GetSimulatedInks() {
        return new List<InkEntry> { 
            new InkEntry { colorId = 3, level = 80, status = 0 }, // Black
            new InkEntry { colorId = 4, level = 75, status = 0 }, // Light Cyan
            new InkEntry { colorId = 1, level = 45, status = 0 }, // Magenta
            new InkEntry { colorId = 0, level = 60, status = 0 }, // Cyan
            new InkEntry { colorId = 2, level = 90, status = 0 }, // Yellow
            new InkEntry { colorId = 5, level = 30, status = 0 }, // Light Magenta
            new InkEntry { colorId = 10, level = 85, status = 0 } // Maintenance Box
        };
    }
    */

    [Serializable] public struct InkEntry { public uint colorId; public int level; public uint status; }
    [Serializable] public class InkWrapper { public List<InkEntry> inks; }
}