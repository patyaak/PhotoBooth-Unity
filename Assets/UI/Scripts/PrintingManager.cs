using System;
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
    public TMP_Dropdown paperSizeDropdown; 

    [Header("Error Handling")]
    public GameObject printerErrorPanel;
    public TMP_Text printerErrorText;
    public UnityEngine.UI.Button closeErrorButton; 

    [Header("Printer")]
    public string selectedPrinter;
    public string selectedPaperSize = "4x6"; // Default
    
    [Header("Debug")]
    public bool simulateReady = false; // Force Ready Status

    private const string PRINTER_PREF = "SELECTED_PRINTER";
    private const string PAPER_SIZE_PREF = "SELECTED_PAPER_SIZE";

    // SNOOZE LOGIC
    private bool isErrorSnoozed = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        PopulatePrinters();
        PopulatePaperSizes();
        
        if (closeErrorButton != null)
        {
            closeErrorButton.onClick.AddListener(OnCloseErrorClicked);
        }

        StartCoroutine(CheckPrinterStatusRoutine());
    }

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

    void PopulatePaperSizes()
    {
        if (paperSizeDropdown == null) return;

        paperSizeDropdown.ClearOptions();
        // Common Epson SL-D1000/D500 Sizes
        // Format: "Name" (Internal Match String)
        List<string> sizes = new List<string>() { "4x6", "5x7", "6x8", "3.5x5", "4x4", "5x5", "6x6" };
        
        paperSizeDropdown.AddOptions(sizes);

        if (PlayerPrefs.HasKey(PAPER_SIZE_PREF))
        {
            selectedPaperSize = PlayerPrefs.GetString(PAPER_SIZE_PREF);
            int index = sizes.IndexOf(selectedPaperSize);
            if (index >= 0) paperSizeDropdown.value = index;
        }

        paperSizeDropdown.onValueChanged.AddListener(OnPaperSizeChanged);
    }

    void OnPrinterChanged(int index)
    {
        selectedPrinter = printerDropdown.options[index].text;
        PlayerPrefs.SetString(PRINTER_PREF, selectedPrinter);
        PlayerPrefs.Save();

        UnityEngine.Debug.Log("🖨️ Selected Printer: " + selectedPrinter);
    }

    void OnPaperSizeChanged(int index)
    {
        selectedPaperSize = paperSizeDropdown.options[index].text;
        PlayerPrefs.SetString(PAPER_SIZE_PREF, selectedPaperSize);
        PlayerPrefs.Save();
        UnityEngine.Debug.Log("📄 Selected Paper Size: " + selectedPaperSize);
    }


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

        // Detect if Portrait or Landscape based on Frame Type string
        bool isLandscape = frameType.ToLower().Contains("landscape");

        UnityEngine.Debug.Log($"🖨️ Printing on {selectedPrinter} | Mode: {(isLandscape ? "Landscape" : "Portrait")} | Size: {selectedPaperSize}");

        // LOGGING START
        LoggingManager.Instance?.LogPrinting(selectedPrinter, "started", selectedPaperSize, isLandscape);

        PrintDocument pd = new PrintDocument();
        pd.PrinterSettings.PrinterName = selectedPrinter;

        // --- PAPER SIZE DETECTION (General) ---
        PaperSize targetPaper = null;
        
        // Normalize user selection to parts (e.g. "4x6" -> 4, 6)
        string pSize = selectedPaperSize.Replace(" ", "").ToLower(); // "4x6"

        foreach (PaperSize size in pd.PrinterSettings.PaperSizes)
        {
            // Simple match: check if the driver's paper name contains "4x6" or "4 x 6" etc.
            // Also check for metric "102x152" if user selected 4x6
            string driverName = size.PaperName.Replace(" ", "").ToLower();

            if (driverName.Contains(pSize)) 
            {
                targetPaper = size;
                break;
            }
            // fallback Metric matches for common sizes
            if (pSize == "4x6" && (driverName.Contains("102x152") || driverName.Contains("10x15"))) targetPaper = size;
            else if (pSize == "5x7" && (driverName.Contains("127x178") || driverName.Contains("13x18"))) targetPaper = size;
            else if (pSize == "6x8" && (driverName.Contains("152x203"))) targetPaper = size;
        }

        if (targetPaper != null)
        {
            pd.DefaultPageSettings.PaperSize = targetPaper;
            UnityEngine.Debug.Log($"   [Paper] Found Driver Paper: {targetPaper.PaperName}");
        }
        else
        {
            UnityEngine.Debug.LogWarning($"   [Paper] '{selectedPaperSize}' not found in driver. Using default: {pd.DefaultPageSettings.PaperSize.PaperName}");
            // Optional: Show Warning UI to user?
        }

        // --- ORIENTATION & MARGINS ---
        pd.DefaultPageSettings.Landscape = isLandscape;
        pd.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0); // Hardware margins usually apply anyway, but we set 0 to be safe
        pd.OriginAtMargins = false; // We want to print on the physical page

        // --- PRINT EVENT ---
        pd.PrintPage += (sender, e) =>
        {
            // High Quality
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            e.Graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

            // Get Printable Area
            // Note: VisibleClipBounds matches the printable area inside margins.
            // Since we set margins to 0, this should match the paper size minus hardware limits.
            System.Drawing.RectangleF bounds = e.Graphics.VisibleClipBounds;

            // Convert Texture2D to System.Drawing.Image (Memory Stream)
            byte[] bytes = image.EncodeToPNG();
            using (MemoryStream ms = new MemoryStream(bytes))
            using (System.Drawing.Image img = System.Drawing.Image.FromStream(ms))
            {
                // Fit to Page Logic (Uniform Fill or Fit)
                // We typically want "Crop to Fill" if aspect ratios differ slightly, 
                // OR "Shrink to Fit" if we want to show everything.
                // Replicating previous logic: "Shrink to Fit" (Uniform)
                
                float scaleX = bounds.Width / img.Width;
                float scaleY = bounds.Height / img.Height;
                float scale = Math.Min(scaleX, scaleY);

                // If you want to FILL the page (and crop excess), use Math.Max instead.
                // For Photo Booths with borders, usually we want EXACT fit.
                // Let's force STRETCH if the aspect ratio is extremely close (borderless).
                
                float targetW = img.Width * scale;
                float targetH = img.Height * scale;

                float posX = bounds.Left + (bounds.Width - targetW) / 2;
                float posY = bounds.Top + (bounds.Height - targetH) / 2;

                e.Graphics.DrawImage(img, posX, posY, targetW, targetH);
            }
            e.HasMorePages = false;
        };

        try
        {
            pd.Print();
            LoggingManager.Instance?.LogPrinting(selectedPrinter, "success", "4x6", isLandscape);
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"Print Failed: {ex.Message}");
            LoggingManager.Instance?.LogPrinting(selectedPrinter, "failed", "4x6", isLandscape, ex.Message);
            ShowError("印刷エラー: " + ex.Message);
        }
    }

    // SNOOZE FUNCTION
    void OnCloseErrorClicked()
    {
        StartCoroutine(SnoozeErrorRoutine());
    }

    System.Collections.IEnumerator SnoozeErrorRoutine()
    {
        isErrorSnoozed = true;
        HideError(); // Hide immediately
        
        yield return new WaitForSeconds(2f);  //error snoozed for 2 seconds
        isErrorSnoozed = false;
        
        // Status check loop effectively picks this up on next tick
    }

    System.Collections.IEnumerator CheckPrinterStatusRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(3f);
        while (true)
        {
            if (!string.IsNullOrEmpty(selectedPrinter) && !isErrorSnoozed) // Check flag
            {
                CheckStatusNative();
            }
            yield return wait;
        }
    }

    void CheckStatusNative()
    {
        // DEBUG SIMULATION
        if (simulateReady)
        {
            HideError();
            return;
        }

        // Use our Helper to get status string
        string status = NativePrinterHelper.GetPrinterStatus(selectedPrinter);

        // Parse Standard Strings
        if (status == "Ready" || status == "Status_Printing" || status == "Status_Busy" || status == "Status_Processing")
        {
            HideError();
        }
        else if (status.Contains("PAPER_JAM")) ShowError("紙詰まりです\n" + status);
        else if (status.Contains("PAPER_OUT")) ShowError("用紙切れです\n" + status);
        else if (status.Contains("DOOR_OPEN")) ShowError("カバーが開いています\n" + status);
        else if (status.Contains("NO_TONER")) ShowError("インク切れです\n" + status);
        else if (status.Contains("TONER_LOW")) ShowError("インク残量低下\n" + status);
        else if (status.Contains("OFFLINE")) ShowError("プリンターが接続されていません\n" + status); // Offline
        else if (status.Contains("NOTFOUND")) ShowError("プリンターが見つかりません");
        else if (status.Contains("ERROR")) ShowError("プリンターエラー\n" + status); // Generic
        
        // Debug
        // UnityEngine.Debug.Log($"[Status Check] {selectedPrinter} -> {status}");
    }

    void ShowError(string msg)
    {
        // Don't show if snoozed (double heck)
        if (isErrorSnoozed) return;

        if (printerErrorPanel != null)
        {
            printerErrorPanel.SetActive(true);
            if (printerErrorText != null) printerErrorText.text = msg;
        }
    }

    void HideError()
    {
        if (printerErrorPanel != null && printerErrorPanel.activeSelf)
        {
            printerErrorPanel.SetActive(false);
        }
    }

}
