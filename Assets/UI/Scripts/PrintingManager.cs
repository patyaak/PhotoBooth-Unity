using UnityEngine;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Drawing.Printing;
using TMPro;

public class PrintingManager : MonoBehaviour
{
    public static PrintingManager Instance;

    [Header("UI")]
    public TMP_Dropdown printerDropdown;

    [Header("Printer")]
    public string selectedPrinter;

    private const string PRINTER_PREF = "SELECTED_PRINTER";

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        PopulatePrinters();
    }

    // =========================
    // PRINTER LIST
    // =========================
    void PopulatePrinters()
    {
        if (printerDropdown == null) return;

        printerDropdown.ClearOptions();

        List<string> printers = new List<string>();

        foreach (string printer in PrinterSettings.InstalledPrinters)
        {
            printers.Add(printer);
        }

        printerDropdown.AddOptions(printers);

        // Restore last selected printer
        if (PlayerPrefs.HasKey(PRINTER_PREF))
        {
            selectedPrinter = PlayerPrefs.GetString(PRINTER_PREF);
            int index = printers.IndexOf(selectedPrinter);
            if (index >= 0)
                printerDropdown.value = index;
        }
        else if (printers.Count > 0)
        {
            selectedPrinter = printers[0];
        }

        printerDropdown.onValueChanged.AddListener(OnPrinterChanged);
    }

    void OnPrinterChanged(int index)
    {
        selectedPrinter = printerDropdown.options[index].text;
        PlayerPrefs.SetString(PRINTER_PREF, selectedPrinter);
        PlayerPrefs.Save();

        UnityEngine.Debug.Log("🖨️ Selected Printer: " + selectedPrinter);
    }

    // =========================
    // PRINT ENTRY POINT
    // =========================
    public void PrintFinalImage(Texture2D image, string frameType)
    {
        if (image == null)
        {
            UnityEngine.Debug.LogError("No image to print");
            return;
        }

        if (string.IsNullOrEmpty(selectedPrinter))
        {
            UnityEngine.Debug.LogError("No printer selected");
            return;
        }

        bool isLandscape = frameType.ToLower().Contains("landscape");

        string imagePath = Path.Combine(Application.persistentDataPath, "PHOTO_TO_PRINT.png");
        File.WriteAllBytes(imagePath, image.EncodeToPNG());

        RunPowerShellPrint(imagePath, isLandscape);
    }

    // =========================
    // POWERSHELL PRINT
    // =========================
    void RunPowerShellPrint(string imagePath, bool landscape)
    {
        UnityEngine.Debug.Log($"PRINT TEST: 🖨️ Printing on {selectedPrinter} (4x6 Mode)");

        string psScript = BuildPowerShellScript(
            imagePath.Replace("\\", "\\\\"),
            selectedPrinter,
            landscape
        );

        string tempPs = Path.Combine(Application.persistentDataPath, "print.ps1");
        File.WriteAllText(tempPs, psScript, Encoding.UTF8);

        ProcessStartInfo psi = new ProcessStartInfo()
        {
            FileName = "powershell.exe",
            Arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{tempPs}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        Process process = new Process();
        process.StartInfo = psi;
        
        process.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.Log($"PRINT TEST: [PS LOG] {e.Data}"); };
        process.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.LogError($"PRINT TEST: [PS ERR] {e.Data}"); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    string BuildPowerShellScript(string imagePath, string printer, bool landscape)
    {
        return $@"
Add-Type -AssemblyName System.Drawing
# System.Drawing.Printing is likely in System.Drawing, so we skip explicit Add-Type for it if it fails

$image = [System.Drawing.Image]::FromFile('{imagePath}')

$pd = New-Object System.Drawing.Printing.PrintDocument
$pd.PrinterSettings.PrinterName = '{printer}'

# --- FIND 4x6 or 102x152 PAPER ---
$targetPaper = $null
foreach ($paperSize in $pd.PrinterSettings.PaperSizes) {{
    # Match '4 x 6', '4x6', '102 x 152' or by dimensions (allow small tolerance)
    # 400x600 (1/100 inch) is standard 4x6. 
    if (($paperSize.PaperName -match '4\s*x\s*6') -or 
        ($paperSize.PaperName -match '102\s*x\s*152') -or 
        ($paperSize.Width -eq 400 -and $paperSize.Height -eq 600)) {{
        $targetPaper = $paperSize
        break
    }}
}}

if ($targetPaper) {{
    $pd.DefaultPageSettings.PaperSize = $targetPaper
    Write-Host ""PRINT TEST: DETECTED PAPER: $($targetPaper.PaperName) ($($targetPaper.Width) x $($targetPaper.Height))""
}}
else {{
    Write-Host ""PRINT TEST: WARNING: Specific paper size not found, using default: $($pd.DefaultPageSettings.PaperSize.PaperName)""
}}

$pd.DefaultPageSettings.Margins = New-Object System.Drawing.Printing.Margins(0,0,0,0)
$pd.OriginAtMargins = $false # Use physical page
$pd.DefaultPageSettings.Landscape = {(landscape ? "$true" : "$false")}
Write-Host ""PRINT TEST: ORIENTATION: Landscape=$($pd.DefaultPageSettings.Landscape)""

$pd.add_PrintPage({{
    param($sender, $e)

    $e.PageSettings.Margins = New-Object System.Drawing.Printing.Margins(0,0,0,0)
    # REMOVED: PageUnit = Pixel caused scaling issues (treated 1/100 inch units as pixels)
    # Standard is Display (approx 1/100 inch for PrintDocument), matching PaperSize units.
    
    $e.Graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # Full page bounds (Physical size, in 1/100 inch usually)
    $bounds = $e.PageBounds

    # Image Dimensions
    $imgW = $image.Width
    $imgH = $image.Height

    # === SHRINK TO FIT LOGIC (Uniform Scale) ===
    # Calculate scale factor to fit ENTIRE image into page without cropping
    $scaleX = $bounds.Width / $imgW
    $scaleY = $bounds.Height / $imgH
    
    # Use the smaller scale to ensure it fits (Shrink to Fit)
    $scale = [Math]::Min($scaleX, $scaleY)
    
    # New Dimensions
    $targetW = [Math]::Floor($imgW * $scale)
    $targetH = [Math]::Floor($imgH * $scale)

    # Center Position
    $posX = [Math]::Floor(($bounds.Width - $targetW) / 2)
    $posY = [Math]::Floor(($bounds.Height - $targetH) / 2)

    Write-Host ""PRINT TEST: PRINTING: Image($imgW x $imgH) -> Page($($bounds.Width) x $($bounds.Height))""
    Write-Host ""PRINT TEST: SCALING: Scale=$scale TargetSize=($targetW x $targetH) Position=($posX, $posY)""
    
    # Draw image centered
    $e.Graphics.DrawImage($image, $posX, $posY, $targetW, $targetH)

    $e.HasMorePages = $false
}})

try {{
    $pd.Print()
    Write-Host ""PRINT TEST: PRINT JOB SENT SUCCESSFULLY""
}}
catch {{
    Write-Error $_.Exception.Message
}}
finally {{
    $image.Dispose()
}}
";
    }
}
