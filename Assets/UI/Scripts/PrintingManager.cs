using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PrintingManager : MonoBehaviour
{
    public static PrintingManager Instance;

    [Header("Printer Settings")]
    public string printerName = "EPSON SL-D1050";

    [Header("Paper Size (inches)")]
    public float paperWidthInches = 4f;
    public float paperHeightInches = 6f;
    public int dpi = 600;

    private int PaperWidthPixels => Mathf.RoundToInt(paperWidthInches * dpi);
    private int PaperHeightPixels => Mathf.RoundToInt(paperHeightInches * dpi);

    [Header("UI")]
    public GameObject printingPanel;
    public GameObject inProgressPanel;
    public GameObject printingDonePanel;
    public GameObject errorPanel;
    public TMP_Text statusText;
    public TMP_Text errorText;

    [Header("Completion")]
    public float printingDoneDisplaySeconds = 3f;

    [Header("Printer Selection UI (Assign in Start Scene)")]
    public TMP_Dropdown printerDropdown;
    public Button refreshPrintersButton;
    public TMP_Text printerStatusText;
    public Button testPrintButton;

    [Header("Printer Monitoring")]
    public float printerRecheckInterval = 2.5f;
    private Coroutine printerMonitorRoutine;

    // Printer list
    public List<string> availablePrinters = new List<string>();
    
    private Texture2D imageToPrint;
    private string currentFrameType = "portrait";
    private bool printingComplete;
    
    private const string PRINTER_PREF_KEY = "SelectedPrinter";

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Persist across scenes
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        printingPanel?.SetActive(false);
        inProgressPanel?.SetActive(false);
        printingDonePanel?.SetActive(false);
        errorPanel?.SetActive(false);

        // Load saved printer preference
        LoadSavedPrinter();

        // Setup UI listeners if dropdown exists (Start Scene)
        SetupPrinterSelectionUI();

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        CheckPrinterStatusOnStartup();
#endif
    }

    #region PRINTER SELECTION & DETECTION

    /// <summary>
    /// Load previously saved printer from PlayerPrefs
    /// </summary>
    private void LoadSavedPrinter()
    {
        if (PlayerPrefs.HasKey(PRINTER_PREF_KEY))
        {
            printerName = PlayerPrefs.GetString(PRINTER_PREF_KEY);
            Debug.Log($"✓ Loaded saved printer: {printerName}");
        }
        else
        {
            Debug.Log($"No saved printer. Using default: {printerName}");
        }
    }

    /// <summary>
    /// Save selected printer to PlayerPrefs (persists forever until changed)
    /// </summary>
    private void SaveSelectedPrinter(string printer)
    {
        printerName = printer;
        PlayerPrefs.SetString(PRINTER_PREF_KEY, printer);
        PlayerPrefs.Save();
        Debug.Log($"✓ Saved printer: {printer}");
    }

    /// <summary>
    /// Get all installed printers on the system
    /// </summary>
    public List<string> GetInstalledPrinters()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        try
        {
            string script = @"Get-WmiObject Win32_Printer | ForEach-Object { $_.Name }";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-ExecutionPolicy Bypass -Command \"" + script + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var p = System.Diagnostics.Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            
            availablePrinters = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Where(s => !string.IsNullOrWhiteSpace(s))
                                      .Select(s => s.Trim())
                                      .ToList();
            
            Debug.Log($"Found {availablePrinters.Count} printers");
            return availablePrinters;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to get printers: {e.Message}");
            return new List<string>();
        }
#else
        // For Unity Editor testing
        availablePrinters = new List<string> { "Test Printer 1", "EPSON SL-D1050", "Canon Printer" };
        return availablePrinters;
#endif
    }

    /// <summary>
    /// Setup dropdown and buttons if they exist (for Start Scene)
    /// </summary>
    private void SetupPrinterSelectionUI()
    {
        if (printerDropdown != null)
        {
            printerDropdown.onValueChanged.AddListener(OnPrinterSelectedFromDropdown);
            RefreshPrinterList();
        }

        if (refreshPrintersButton != null)
        {
            refreshPrintersButton.onClick.AddListener(RefreshPrinterList);
        }

        if (testPrintButton != null)
        {
            testPrintButton.onClick.AddListener(TestPrint);
        }
    }

    /// <summary>
    /// Refresh printer list and update dropdown
    /// </summary>
    public void RefreshPrinterList()
    {
        if (printerDropdown == null) return;

        List<string> printers = GetInstalledPrinters();
        
        printerDropdown.ClearOptions();

        if (printers.Count == 0)
        {
            UpdatePrinterStatus("プリンターが見つかりません", false);
            return;
        }

        printerDropdown.AddOptions(printers);

        // Try to select the currently saved printer
        int currentIndex = printers.IndexOf(printerName);
        if (currentIndex >= 0)
        {
            printerDropdown.value = currentIndex;
        }
        else if (printers.Count > 0)
        {
            printerDropdown.value = 0;
            OnPrinterSelectedFromDropdown(0);
        }

        // Check status of selected printer
        CheckAndUpdatePrinterStatus();
    }

    /// <summary>
    /// Called when user selects a printer from dropdown
    /// </summary>
    private void OnPrinterSelectedFromDropdown(int index)
    {
        if (index < 0 || index >= availablePrinters.Count) return;

        string selected = availablePrinters[index];
        SaveSelectedPrinter(selected);
        CheckAndUpdatePrinterStatus();
    }

    /// <summary>
    /// Check printer status and update UI
    /// </summary>
    private void CheckAndUpdatePrinterStatus()
    {
        if (printerStatusText == null) return;

        bool isReady = GetPrinterStatus(printerName, out string error);
        
        if (isReady)
        {
            UpdatePrinterStatus($"✓ {printerName} - 準備完了", true);
        }
        else
        {
            UpdatePrinterStatus($"✗ {printerName} - {error}", false);
        }
    }

    /// <summary>
    /// Update printer status text and color
    /// </summary>
    private void UpdatePrinterStatus(string message, bool isReady)
    {
        if (printerStatusText != null)
        {
            printerStatusText.text = message;
            printerStatusText.color = isReady ? Color.green : Color.red;
        }
    }

    /// <summary>
    /// Test print a sample pattern
    /// </summary>
    private void TestPrint()
    {
        // Create a simple test pattern
        Texture2D testImage = new Texture2D(800, 600, TextureFormat.RGB24, false);
        Color[] pixels = new Color[800 * 600];
        
        // Create a simple gradient test pattern
        for (int y = 0; y < 600; y++)
        {
            for (int x = 0; x < 800; x++)
            {
                float r = (float)x / 800;
                float g = (float)y / 600;
                pixels[y * 800 + x] = new Color(r, g, 0.5f);
            }
        }
        
        testImage.SetPixels(pixels);
        testImage.Apply();

        PrintFinalImage(testImage, "portrait");
        
        Debug.Log("Test print initiated");
    }

    #endregion

    #region PRINTER STATUS
    
    /// <summary>
    /// Check if specific printer is ready to print
    /// </summary>
    public bool GetPrinterStatus(string printer, out string errorMessage)
    {
        errorMessage = "";

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        try
        {
            string script = @"
$printer = Get-WmiObject Win32_Printer | Where-Object { $_.Name -eq '" + printer + @"' }

if ($null -eq $printer) { 'NOT_FOUND'; exit }
if ($printer.WorkOffline) { 'OFFLINE'; exit }
if ($printer.PaperOut) { 'PAPER_OUT'; exit }
if ($printer.DetectedErrorState -ne 0) { 'ERROR'; exit }

'READY'
";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-ExecutionPolicy Bypass -Command \"" + script + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var p = System.Diagnostics.Process.Start(psi);
            string result = p.StandardOutput.ReadToEnd().Trim();

            switch (result)
            {
                case "READY": return true;
                case "NOT_FOUND": errorMessage = "プリンターが見つかりません"; break;
                case "OFFLINE": errorMessage = "プリンターがオフラインです"; break;
                case "PAPER_OUT": errorMessage = "用紙切れです"; break;
                case "ERROR": errorMessage = "プリンターエラー"; break;
                default: errorMessage = "プリンター状態不明"; break;
            }
            return false;
        }
        catch (Exception e)
        {
            errorMessage = e.Message;
            return false;
        }
#else
        return true; // Always return true in editor
#endif
    }

    /// <summary>
    /// Check status of currently selected printer
    /// </summary>
    private bool GetPrinterStatus(out string errorMessage)
    {
        return GetPrinterStatus(printerName, out errorMessage);
    }

    private void CheckPrinterStatusOnStartup()
    {
        if (!GetPrinterStatus(out string error))
            ShowError(error);
    }
    
    #endregion

    #region PUBLIC API
    
    public void PrintFinalImage(Texture2D image, string frameType = "portrait")
    {
        if (!GetPrinterStatus(out string error))
        {
            ShowError(error);
            return;
        }

        imageToPrint = image;
        currentFrameType = frameType.ToLower();
        printingComplete = false;
        Debug.Log(frameType);
        StartCoroutine(PrintCoroutine(image));
    }

    public bool IsPrintingComplete() => printingComplete;
    
    /// <summary>
    /// Get currently selected printer name
    /// </summary>
    public string GetCurrentPrinterName() => printerName;
    
    #endregion

    #region PRINT FLOW
    
    private IEnumerator PrintCoroutine(Texture2D source)
    {
        ShowPrintingPanel(true, false);
        UpdateStatus("画像を準備中...");

        Texture2D processed = source;

        if (currentFrameType == "landscape")
            processed = RotateTexture90Clockwise(source);

        Texture2D fitted = FitToPaperWithCrop(processed, PaperWidthPixels, PaperHeightPixels);

        UpdateStatus("印刷中...");
        bool success = PrintWithPowerShell(fitted);

        Destroy(fitted);

        if (success)
        {
            ShowPrintingPanel(false, true);
            yield return new WaitForSeconds(printingDoneDisplaySeconds);
            ShowPrintingPanel(false, false);
        }
        else
        {
            ShowError("印刷に失敗しました");
        }

        printingComplete = true;
    }
    
    #endregion

    #region IMAGE PROCESSING
    
    private Texture2D RotateTexture90Clockwise(Texture2D src)
    {
        Texture2D tex = new Texture2D(src.height, src.width, TextureFormat.RGB24, false);
        for (int x = 0; x < src.width; x++)
            for (int y = 0; y < src.height; y++)
                tex.SetPixel(src.height - 1 - y, x, src.GetPixel(x, y));
        tex.Apply();
        return tex;
    }

    private Texture2D FitToPaperWithCrop(Texture2D src, int pw, int ph)
    {
        float srcAspect = (float)src.width / src.height;
        float paperAspect = (float)pw / ph;

        int sw, sh;
        if (srcAspect > paperAspect)
        {
            sh = ph;
            sw = Mathf.RoundToInt(ph * srcAspect);
        }
        else
        {
            sw = pw;
            sh = Mathf.RoundToInt(pw / srcAspect);
        }

        Texture2D scaled = ResizeTexture(src, sw, sh);
        Texture2D paper = new Texture2D(pw, ph, TextureFormat.RGB24, false);

        int x = (sw - pw) / 2;
        int y = (sh - ph) / 2;

        paper.SetPixels(scaled.GetPixels(x, y, pw, ph));
        paper.Apply();

        Destroy(scaled);
        return paper;
    }

    private Texture2D ResizeTexture(Texture2D src, int w, int h)
    {
        RenderTexture rt = RenderTexture.GetTemporary(w, h);
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();

        RenderTexture.ReleaseTemporary(rt);
        RenderTexture.active = null;
        return tex;
    }
    
    #endregion

    #region WINDOWS PRINT (FIXED 4x6)
    
    private bool PrintWithPowerShell(Texture2D img)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        try
        {
            string temp = Path.Combine(Path.GetTempPath(), $"print_{Guid.NewGuid()}.png");
            File.WriteAllBytes(temp, img.EncodeToPNG());

            Debug.Log($"🖨️ Printing {img.width}x{img.height}px to {printerName}");

            string script = @"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Drawing.Printing

$image = [System.Drawing.Image]::FromFile('" + temp.Replace("\\", "\\\\") + @"')

$pd = New-Object System.Drawing.Printing.PrintDocument
$pd.PrinterSettings.PrinterName = '" + printerName + @"'

$paper = New-Object System.Drawing.Printing.PaperSize('4x6', 400, 600)
$pd.DefaultPageSettings.PaperSize = $paper
$pd.DefaultPageSettings.Margins = New-Object System.Drawing.Printing.Margins(0,0,0,0)

$pd.add_PrintPage({
    param($s, $e)
    
    $paperW = $e.PageBounds.Width
    $paperH = $e.PageBounds.Height
    
    $imgAspect = [double]$image.Width / [double]$image.Height
    $paperAspect = [double]$paperW / [double]$paperH
    
    if ($imgAspect -gt $paperAspect) {
        $w = $paperW
        $h = $paperW / $imgAspect
        $x = 0
        $y = ($paperH - $h) / 2
    } else {
        $h = $paperH
        $w = $paperH * $imgAspect
        $x = ($paperW - $w) / 2
        $y = 0
    }
    
    $rect = New-Object System.Drawing.RectangleF($x, $y, $w, $h)
    $e.Graphics.DrawImage($image, $rect)
    
    $e.HasMorePages = $false
})

$pd.Print()
$image.Dispose()
Start-Sleep -Milliseconds 500
Remove-Item '" + temp.Replace("\\", "\\\\") + @"' -Force
";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-ExecutionPolicy Bypass -Command \"" + script + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var p = System.Diagnostics.Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();
            p.WaitForExit(20000);
            
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"⚠️ PowerShell stderr: {error}");
            }
                
            return p.ExitCode == 0;
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Print error: {e.Message}");
            return false;
        }
#else
        Debug.Log($"[EDITOR] Would print to: {printerName}");
        return true;
#endif
    }
    
    #endregion

    #region UI
    
    private void UpdateStatus(string t)
    {
        if (statusText) statusText.text = t;
    }

    private void ShowPrintingPanel(bool progress, bool done)
    {
        printingPanel?.SetActive(progress || done);
        inProgressPanel?.SetActive(progress);
        printingDonePanel?.SetActive(done);
        errorPanel?.SetActive(false);
    }

    private void ShowError(string msg)
    {
        errorPanel?.SetActive(true);
        if (errorText) errorText.text = msg;
        printingPanel?.SetActive(false);
    }
    
    #endregion
}