using System;
using UnityEngine;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Drawing.Printing;
using TMPro;
using UnityEngine.UI;

public class PrintingManager : MonoBehaviour
{
    public static PrintingManager Instance;

    [Header("UI")]
    public TMP_Dropdown printerDropdown;
    public TMP_Dropdown paperSizeDropdown; 

    [Header("Error Handling")]
    public GameObject printerErrorPanel;
    public TMP_Text printerErrorText;
    public Button closeErrorButton; 
    public Button errorMessageButton; // Hidden button on error message 

    [Header("Printer")]
    public string selectedPrinter;
    public string selectedPaperSize = "4x6"; // Default
    

    public enum PrinterSimulationMode { Disable, SimulateSuccess, SimulatePaperJam, SimulatePaperOut, SimulateOffline }
    public PrinterSimulationMode simulationMode = PrinterSimulationMode.Disable;
    public bool simulateReady = false; // Legacy - will be replaced by simulationMode

    private const string PRINTER_PREF = "SELECTED_PRINTER";
    private const string PAPER_SIZE_PREF = "SELECTED_PAPER_SIZE";

    // SNOOZE LOGIC
    private bool isErrorSnoozed = false;
    private int closeButtonTapCount = 0; // Secret close counter
    private string lastReportedErrorOrderId = ""; // NEW: Avoid duplicate error reports

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
        if (errorMessageButton != null)
        {
            errorMessageButton.onClick.AddListener(OnErrorMessageClicked);
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
               // float scale = Math.Max(scaleX, scaleY); 
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
        if (closeButtonTapCount >= 5)
        {
            // Unlocked: Snooze immediately
            StartCoroutine(SnoozeErrorRoutine());
            closeButtonTapCount = 0;
            UnityEngine.Debug.Log("🔒 Error panel closed via secret unlock.");
        }
        else
        {
            UnityEngine.Debug.Log($"🚫 Close button locked. Need {5 - closeButtonTapCount} more taps on error message.");
        }
    }

    void OnErrorMessageClicked()
    {
        closeButtonTapCount++;
        UnityEngine.Debug.Log($"👆 Error message tapped {closeButtonTapCount}/5 times.");
    }

    System.Collections.IEnumerator SnoozeErrorRoutine()
    {
        isErrorSnoozed = true;
        HideError(); // Hide immediately
        
        yield return new WaitForSeconds(2f);  //error snoozed for 2 seconds
        isErrorSnoozed = false;
        
        
    }

    // NEW: Expose printing status
    public bool IsPrinting { get; private set; }
    public string LastStatus { get; private set; } = "Unknown";

    IEnumerator CheckPrinterStatusRoutine()
    {
        // Increase polling rate to catch short print jobs (1s instead of 3s)
        WaitForSeconds wait = new WaitForSeconds(1.0f);
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
        string status;

        // --- SIMULATION LOGIC ---
        if (simulationMode != PrinterSimulationMode.Disable)
        {
            switch (simulationMode)
            {
                case PrinterSimulationMode.SimulateSuccess: status = "Ready"; break;
                case PrinterSimulationMode.SimulatePaperJam: status = "ERROR_PAPER_JAM (Simulated)"; break;
                case PrinterSimulationMode.SimulatePaperOut: status = "ERROR_PAPER_OUT (Simulated)"; break;
                case PrinterSimulationMode.SimulateOffline: status = "ERROR_OFFLINE (Simulated)"; break;
                default: status = "Ready"; break;
            }
            IsPrinting = false; // In simulation, we assume job finishes instantly
        }
        else if (simulateReady)
        {
            status = "Ready";
            IsPrinting = false;
        }
        else
        {
            status = NativePrinterHelper.GetPrinterStatus(selectedPrinter);
        }
        // ------------------------

        LastStatus = status;

        // Update IsPrinting flag
        if (simulationMode == PrinterSimulationMode.Disable && !simulateReady)
        {
            // "Status_Printing" and "Status_Busy" are returned by NativePrinterHelper when jobs > 0 or status bits are set
            IsPrinting = (status == "Status_Printing" || status == "Status_Busy" || status == "Status_Processing");
        }

        // Parse Standard Strings
        if (status == "Ready" || IsPrinting)
        {
            HideError();
        }
        else if (status.Contains("PAPER_JAM")) ShowError("紙詰まりです");
        else if (status.Contains("PAPER_OUT")) ShowError("用紙切れです");
        else if (status.Contains("DOOR_OPEN")) ShowError("カバーが開いています");
        else if (status.Contains("NO_TONER")) ShowError("インク切れです");
        else if (status.Contains("TONER_LOW")) ShowError("インク残量低下");
        else if (status.Contains("OFFLINE")) ShowError("プリンターが接続されていません"); // Offline
        else if (status.Contains("NOTFOUND")) ShowError("プリンターが見つかりません");
        else if (status.Contains("ERROR")) ShowError("プリンターエラー"); // Generic
        
      
    }

    void ShowError(string msg)
    {

        if (isErrorSnoozed) return;

        if (printerErrorPanel != null)
        {
            printerErrorPanel.SetActive(true);
            if (printerErrorText != null) printerErrorText.text = msg;

            // NEW: Immediate reporting if session is active
            string currentOrderId = PaymentManager.Instance?.currentOrderId;
            
            bool isSessionActive = false;
            if (LoginManager.Instance != null)
            {
                if ((LoginManager.Instance.frameSelectionPanel != null && LoginManager.Instance.frameSelectionPanel.activeSelf) ||
                    (LoginManager.Instance.paymentPanel != null && LoginManager.Instance.paymentPanel.activeSelf))
                {
                    isSessionActive = true;
                }
            }

            if (isSessionActive && lastReportedErrorOrderId != (currentOrderId ?? "no_order"))
            {
                string condition = msg; 
                
                // Map the Japanese message back to English conditions if possible, or use the raw message
                if (msg.Contains("紙詰まり")) condition = "paper jam";
                else if (msg.Contains("用紙切れ")) condition = "no print out";
                else if (msg.Contains("プリンターが接続")) condition = "printer offline";

                if (!string.IsNullOrEmpty(currentOrderId))
                {
                    lastReportedErrorOrderId = currentOrderId;
                    StartCoroutine(SendPrintStatusToBackend(currentOrderId, false, condition));
                    UnityEngine.Debug.Log($"🚨 [PrintingManager] Immediate error reported for order {currentOrderId}: {condition}");
                }
                else
                {
                    lastReportedErrorOrderId = "no_order";
                    UnityEngine.Debug.Log($"🚨 [PrintingManager] Session error detected (before order creation): {condition}");
                }

                // Direct move to Login
                if (PhotoShootingManager.Instance != null)
                {
                    PhotoShootingManager.Instance.ResetToLoginScreen();
                    UnityEngine.Debug.Log("🏠 [PrintingManager] Session aborted due to printer error -> Returning to Login");
                }
            }

            if (LoginManager.Instance != null)
            {
                if ((LoginManager.Instance.frameSelectionPanel != null && LoginManager.Instance.frameSelectionPanel.activeSelf) ||
                    (LoginManager.Instance.paymentPanel != null && LoginManager.Instance.paymentPanel.activeSelf) ||
                    (PhotoShootingManager.Instance != null && PhotoShootingManager.Instance.photoShootPanel != null && PhotoShootingManager.Instance.photoShootPanel.activeSelf))
                {
                    isSessionActive = true;
                }
            }

            if (closeErrorButton != null)
            {
                var img = closeErrorButton.GetComponent<Image>();
                if (img != null)
                {
                    float alpha = isSessionActive ? 0f : 1f;
                    img.color = new Color(img.color.r, img.color.g, img.color.b, alpha);
                }
            }
        }
    }

    void HideError()
    {
        if (printerErrorPanel != null && printerErrorPanel.activeSelf)
        {
            printerErrorPanel.SetActive(false);
            closeButtonTapCount = 0; // Reset count when panel is hidden
        }
    }

   // public IEnumerator SendPrintStatusToBackend(string orderId, string paymentId, bool printingStatus, string condition)


    public IEnumerator SendPrintStatusToBackend(string orderId,  bool printingStatus, string condition)
    {
        if (string.IsNullOrEmpty(orderId)) yield break;

        string url = $"{API.BaseURL}/api/payment/print-status";
        
        var payload = new
        {
            order_id = orderId,
           
            printingStatus = printingStatus,
            condition = condition
        };

        string jsonPayload = JsonConvert.SerializeObject(payload);
        UnityEngine.Debug.Log($"[PrintingManager] Sending Print Status: {jsonPayload}");

        yield return LoggedWebRequest.Post(url, jsonPayload, (request) =>
        {
            string responseText = request.downloadHandler?.text;
            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                try
                {
                    var res = JsonConvert.DeserializeObject<dynamic>(responseText);
                    if (res != null && res.message != null)
                        UnityEngine.Debug.Log($"✅ Backend: {res.message}");
                }
                catch { }

                UnityEngine.Debug.Log($"✅ Print status reported successfully! Response: {responseText}");
            }
            else
            {
                UnityEngine.Debug.LogError($"❌ Failed to report print status: {request.error}");
                if (!string.IsNullOrEmpty(responseText))
                {
                    try
                    {
                        var res = JsonConvert.DeserializeObject<dynamic>(responseText);
                        if (res != null && res.error != null)
                            UnityEngine.Debug.LogError($"❌ Backend Error: {res.error}");
                    }
                    catch { }
                    UnityEngine.Debug.LogError($"❌ Full Response: {responseText}");
                }
            }
        });
    }

    public void ResetErrorState()
    {
        lastReportedErrorOrderId = "";
        UnityEngine.Debug.Log("🔄 [PrintingManager] Error reporting state reset for next customer.");
    }

}
