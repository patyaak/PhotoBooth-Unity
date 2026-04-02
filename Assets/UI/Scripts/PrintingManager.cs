using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Drawing;
using System.Drawing.Printing;
using Image = System.Drawing.Image;
using Graphics = System.Drawing.Graphics;
using TMPro;
using UnityEngine;
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
    public Button errorMessageButton;

    [Header("Printer")]
    public string selectedPrinter;
    public string selectedPaperSize = "4x6";

    [Header("Printer Config")]
    public bool scaleToFill = true;

    [Tooltip("AutoExpand時の拡大量。1.00 = 拡大なし")]
    [Range(1.00f, 1.15f)]
    public float bleedFactor = 1.04f;

    [Tooltip("印刷位置の微調整（1/100 inch換算で使用される描画座標へ加算）")]
    public Vector2 manualOffset = Vector2.zero;

    public enum PrinterSimulationMode
    {
        Disable,
        SimulateSuccess,
        SimulatePaperJam,
        SimulatePaperOut,
        SimulateOffline
    }

    [Header("Simulation")]
    public PrinterSimulationMode simulationMode = PrinterSimulationMode.Disable;
    public bool simulateReady = false; // legacy

    [Header("SL-D1000 Profile")]
    public bool useEpsonSLD1000Profile = true;

    public enum BorderlessMode
    {
        AutoExpand, // ドライバの borderless + アプリ側少し拡大
        RetainSize, // 元サイズ維持 + 外側へ拡張余白
        Off         // 余白ありフィット
    }

    public BorderlessMode borderlessMode = BorderlessMode.AutoExpand;

    [Tooltip("RetainSize時に四辺へ追加する余白(mm)。Epson運用向け初期値 2.3mm")]
    [Range(0f, 5f)]
    public float retainSizeExpansionMm = 2.3f;

    [Tooltip("Landscape frameType判定キーワード")]
    public string[] landscapeKeywords = new[] { "landscape", "horizontal", "wide" };

    private const string PRINTER_PREF = "SELECTED_PRINTER";
    private const string PAPER_SIZE_PREF = "SELECTED_PAPER_SIZE";

    // Snooze / error
    private bool isErrorSnoozed = false;
    private int closeButtonTapCount = 0;
    private string lastReportedErrorOrderId = "";

    // Public status
    public bool IsPrinting { get; private set; }
    public string LastStatus { get; private set; } = "Unknown";

    private Coroutine printerStatusRoutine;

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        PopulatePrinters();
        PopulatePaperSizes();

        if (closeErrorButton != null)
            closeErrorButton.onClick.AddListener(OnCloseErrorClicked);

        if (errorMessageButton != null)
            errorMessageButton.onClick.AddListener(OnErrorMessageClicked);

        printerStatusRoutine = StartCoroutine(CheckPrinterStatusRoutine());
    }

    private void OnDestroy()
    {
        if (printerStatusRoutine != null)
        {
            StopCoroutine(printerStatusRoutine);
            printerStatusRoutine = null;
        }

        if (closeErrorButton != null)
            closeErrorButton.onClick.RemoveListener(OnCloseErrorClicked);

        if (errorMessageButton != null)
            errorMessageButton.onClick.RemoveListener(OnErrorMessageClicked);
    }

    #endregion

    #region UI Populate

    private void PopulatePrinters()
    {
        if (printerDropdown == null) return;

        printerDropdown.onValueChanged.RemoveListener(OnPrinterChanged);
        printerDropdown.ClearOptions();

        List<string> printers = new List<string>();
        foreach (string printer in PrinterSettings.InstalledPrinters)
            printers.Add(printer);

        printerDropdown.AddOptions(printers);

        if (printers.Count == 0)
        {
            selectedPrinter = string.Empty;
            return;
        }

        int indexToSet = -1;

        // 1. Try PlayerPrefs
        if (PlayerPrefs.HasKey(PRINTER_PREF))
        {
            string saved = PlayerPrefs.GetString(PRINTER_PREF);
            indexToSet = printers.IndexOf(saved);
        }

        // 2. Fallback to Inspector
        if (indexToSet < 0)
        {
            indexToSet = printers.IndexOf(selectedPrinter);
        }

        // 3. Fallback to first available
        if (indexToSet < 0)
        {
            indexToSet = 0;
        }

        printerDropdown.value = indexToSet;
        selectedPrinter = printers[indexToSet];

        printerDropdown.onValueChanged.AddListener(OnPrinterChanged);
    }

    private void PopulatePaperSizes()
    {
        if (paperSizeDropdown == null) return;

        paperSizeDropdown.onValueChanged.RemoveListener(OnPaperSizeChanged);
        paperSizeDropdown.ClearOptions();

        // SL-D1000運用想定でよく使う候補
        List<string> sizes = new List<string>
        {
            "100x148",
            "4x6",
            "5x7",
            "6x8",
            "A4",
            "3.5x5",
            "4x4",
            "5x5",
            "6x6"
        };

        paperSizeDropdown.AddOptions(sizes);

        int indexToSet = -1;

        // 1. Try PlayerPrefs
        if (PlayerPrefs.HasKey(PAPER_SIZE_PREF))
        {
            string saved = PlayerPrefs.GetString(PAPER_SIZE_PREF);
            indexToSet = sizes.IndexOf(saved);
        }

        // 2. Fallback to Inspector
        if (indexToSet < 0)
        {
            indexToSet = sizes.IndexOf(selectedPaperSize);
        }

        // 3. Fallback to first available
        if (indexToSet < 0)
        {
            indexToSet = 0;
        }

        paperSizeDropdown.value = indexToSet;
        selectedPaperSize = sizes[indexToSet];

        paperSizeDropdown.onValueChanged.AddListener(OnPaperSizeChanged);
    }

    private void OnPrinterChanged(int index)
    {
        if (printerDropdown == null || index < 0 || index >= printerDropdown.options.Count) return;

        selectedPrinter = printerDropdown.options[index].text;
        PlayerPrefs.SetString(PRINTER_PREF, selectedPrinter);
        PlayerPrefs.Save();

        UnityEngine.Debug.Log("🖨️ Selected Printer: " + selectedPrinter);
    }

    private void OnPaperSizeChanged(int index)
    {
        if (paperSizeDropdown == null || index < 0 || index >= paperSizeDropdown.options.Count) return;

        selectedPaperSize = paperSizeDropdown.options[index].text;
        PlayerPrefs.SetString(PAPER_SIZE_PREF, selectedPaperSize);
        PlayerPrefs.Save();

        UnityEngine.Debug.Log("📄 Selected Paper Size: " + selectedPaperSize);
    }

    #endregion

    #region Main Print Entry

    public void PrintFinalImage(Texture2D image, string frameType)
    {
        if (image == null)
        {
            UnityEngine.Debug.LogError("No image to print.");
            return;
        }

        if (string.IsNullOrWhiteSpace(selectedPrinter))
        {
            UnityEngine.Debug.LogError("No printer selected.");
            ShowError("プリンターが選択されていません");
            return;
        }

        bool requestedLandscape = IsLandscapeFrame(frameType);

        UnityEngine.Debug.Log(
            $"🖨️ Printing on {selectedPrinter} | Frame: {frameType} | " +
            $"Orientation: {(requestedLandscape ? "Landscape" : "Portrait")} | " +
            $"Size: {selectedPaperSize} | BorderlessMode: {borderlessMode}");

        LoggingManager.Instance?.LogPrinting(selectedPrinter, "started", selectedPaperSize, requestedLandscape);

        PrintDocument pd = new PrintDocument();
        pd.PrinterSettings.PrinterName = selectedPrinter;
        pd.DocumentName = $"Print_{DateTime.Now:yyyyMMdd_HHmmss}";

        ConfigurePrinterSettings(pd, requestedLandscape);

        IsPrinting = true;

        pd.BeginPrint += (sender, args) =>
        {
            IsPrinting = true;
            UnityEngine.Debug.Log("🟢 BeginPrint");
        };

        pd.EndPrint += (sender, args) =>
        {
            IsPrinting = false;
            UnityEngine.Debug.Log($"🏁 EndPrint | Cancel:{args.Cancel}");
        };

        pd.PrintPage += (sender, e) =>
        {
            RenderPrintPage(e, image, requestedLandscape);
            e.HasMorePages = false;
        };

        try
        {
            pd.Print();
            LoggingManager.Instance?.LogPrinting(selectedPrinter, "success", selectedPaperSize, requestedLandscape);
            
            // 🔄 Instant ink check after job completes
            if (EpsonInkMonitor.Instance != null)
                EpsonInkMonitor.Instance.ForceCheck();
        }
        catch (Exception ex)
        {
            IsPrinting = false;
            UnityEngine.Debug.LogError($"❌ Print Failed: {ex.Message}");
            LoggingManager.Instance?.LogPrinting(selectedPrinter, "failed", selectedPaperSize, requestedLandscape, ex.Message);
            ShowError("印刷エラー: " + ex.Message);
        }
        finally
        {
            pd.Dispose();
        }
    }

    #endregion

    #region Printer Setup

    private void ConfigurePrinterSettings(PrintDocument pd, bool requestedLandscape)
    {
        PaperSize targetPaper = ResolvePaperSize(pd, selectedPaperSize, useEpsonSLD1000Profile);

        if (targetPaper != null)
        {
            pd.DefaultPageSettings.PaperSize = targetPaper;
            UnityEngine.Debug.Log($"📄 Resolved driver paper: {targetPaper.PaperName} ({targetPaper.Width}x{targetPaper.Height})");
        }
        else
        {
            UnityEngine.Debug.LogWarning($"⚠️ Paper '{selectedPaperSize}' not found. Using driver default: {pd.DefaultPageSettings.PaperSize?.PaperName}");
        }

        pd.DefaultPageSettings.Landscape = requestedLandscape;
        pd.PrinterSettings.DefaultPageSettings.Landscape = requestedLandscape;
        pd.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        pd.OriginAtMargins = false;
    }

    private PaperSize ResolvePaperSize(PrintDocument pd, string requestedSize, bool applySLD1000Profile)
    {
        if (pd == null || pd.PrinterSettings == null || pd.PrinterSettings.PaperSizes == null)
            return null;

        List<PaperSize> available = pd.PrinterSettings.PaperSizes.Cast<PaperSize>().ToList();
        if (available.Count == 0) return null;

        string normalizedRequested = NormalizePaperKey(requestedSize);

        if (!applySLD1000Profile)
        {
            return FallbackResolvePaperSize(available, normalizedRequested);
        }

        List<string> aliases = EpsonSLD1000PaperProfile.GetAliases(normalizedRequested);

        // 1. Borderless完全一致優先
        PaperSize borderlessExact = available.FirstOrDefault(ps =>
        {
            string driver = NormalizeDriverPaperName(ps.PaperName);
            return IsBorderlessPaperName(driver) && aliases.Any(a => driver.Contains(a));
        });
        if (borderlessExact != null) return borderlessExact;

        // 2. 通常完全一致
        PaperSize normalExact = available.FirstOrDefault(ps =>
        {
            string driver = NormalizeDriverPaperName(ps.PaperName);
            return aliases.Any(a => driver.Contains(a));
        });
        if (normalExact != null) return normalExact;

        // 3. 正方形 / User Defined fallback
        if (EpsonSLD1000PaperProfile.IsSquareSize(normalizedRequested))
        {
            PaperSize userDefinedSquare = available.FirstOrDefault(ps =>
            {
                string driver = NormalizeDriverPaperName(ps.PaperName);
                return driver.Contains("userdefined") || driver.Contains("custom");
            });

            if (userDefinedSquare != null)
            {
                UnityEngine.Debug.Log("📐 Using User Defined / Custom paper fallback for square print.");
                return userDefinedSquare;
            }
        }

        // 4. 一般 fallback
        return FallbackResolvePaperSize(available, normalizedRequested);
    }

    private PaperSize FallbackResolvePaperSize(List<PaperSize> available, string normalizedRequested)
    {
        // Borderless優先
        PaperSize borderless = available.FirstOrDefault(ps =>
        {
            string driver = NormalizeDriverPaperName(ps.PaperName);
            return driver.Contains(normalizedRequested) && IsBorderlessPaperName(driver);
        });
        if (borderless != null) return borderless;

        // 単純一致
        PaperSize simple = available.FirstOrDefault(ps =>
        {
            string driver = NormalizeDriverPaperName(ps.PaperName);
            return driver.Contains(normalizedRequested);
        });
        if (simple != null) return simple;

        // 別名 fallback
        if (normalizedRequested == "4x6")
        {
            return available.FirstOrDefault(ps =>
            {
                string driver = NormalizeDriverPaperName(ps.PaperName);
                return driver.Contains("102x152") || driver.Contains("10x15") || driver.Contains("kg");
            });
        }
        if (normalizedRequested == "5x7")
        {
            return available.FirstOrDefault(ps =>
            {
                string driver = NormalizeDriverPaperName(ps.PaperName);
                return driver.Contains("127x178") || driver.Contains("13x18");
            });
        }
        if (normalizedRequested == "6x8")
        {
            return available.FirstOrDefault(ps =>
            {
                string driver = NormalizeDriverPaperName(ps.PaperName);
                return driver.Contains("152x203");
            });
        }
        if (normalizedRequested == "100x148")
        {
            return available.FirstOrDefault(ps =>
            {
                string driver = NormalizeDriverPaperName(ps.PaperName);
                return driver.Contains("100x148");
            });
        }
        if (normalizedRequested == "a4")
        {
            return available.FirstOrDefault(ps =>
            {
                string driver = NormalizeDriverPaperName(ps.PaperName);
                return driver.Contains("a4") || driver.Contains("210x297");
            });
        }

        return null;
    }

    private static string NormalizePaperKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        string v = value.Trim().ToLower();
        v = v.Replace(" ", "");
        v = v.Replace("in", "");
        v = v.Replace("inch", "");
        return v;
    }

    private static string NormalizeDriverPaperName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        string v = value.Trim().ToLower();
        v = v.Replace(" ", "");
        v = v.Replace("-", "");
        v = v.Replace("_", "");
        return v;
    }

    private static bool IsBorderlessPaperName(string normalizedDriverName)
    {
        return normalizedDriverName.Contains("borderless")
            || normalizedDriverName.Contains("borderfree")
            || normalizedDriverName.Contains("border");
    }

    #endregion

    #region Print Rendering

    private void RenderPrintPage(PrintPageEventArgs e, Texture2D texture, bool requestedLandscape)
    {
        if (e == null || texture == null) return;

        ApplyHighQualityGraphics(e.Graphics);
        LogPageDebug(e);

        RectangleF clip = e.Graphics.VisibleClipBounds;
        RectangleF logicalBounds = new RectangleF(0, 0, clip.Width, clip.Height);

        bool physicalPaperLandscape = e.PageBounds.Width > e.PageBounds.Height;

        if (requestedLandscape != physicalPaperLandscape)
        {
            UnityEngine.Debug.Log(
                $"🔄 Orientation mismatch. Requested:{requestedLandscape} Physical:{physicalPaperLandscape}. Applying transform.");

            e.Graphics.TranslateTransform(clip.Width / 2f, clip.Height / 2f);
            e.Graphics.RotateTransform(requestedLandscape ? 90f : -90f);
            e.Graphics.TranslateTransform(-clip.Height / 2f, -clip.Width / 2f);

            logicalBounds = new RectangleF(0, 0, clip.Height, clip.Width);
        }

        using (Image img = ConvertTextureToImage(texture))
        {
            if (img == null)
                throw new Exception("Failed to convert Texture2D to printable image.");

            bool imageLandscape = img.Width > img.Height;
            bool targetLandscape = logicalBounds.Width > logicalBounds.Height;

            UnityEngine.Debug.Log(
                $"🖼️ Image: {img.Width}x{img.Height} (L:{imageLandscape}) | " +
                $"Target: {logicalBounds.Width:F1}x{logicalBounds.Height:F1} (L:{targetLandscape})");

            if (imageLandscape != targetLandscape)
            {
                UnityEngine.Debug.Log("↪ Image rotation required to match target.");
                img.RotateFlip(RotateFlipType.Rotate90FlipNone);
            }

            RectangleF drawBounds = logicalBounds;

            if (borderlessMode == BorderlessMode.RetainSize)
            {
                float expansionHundredthInch = MmToHundredthInch(retainSizeExpansionMm);
                drawBounds = new RectangleF(
                    logicalBounds.X - expansionHundredthInch,
                    logicalBounds.Y - expansionHundredthInch,
                    logicalBounds.Width + (expansionHundredthInch * 2f),
                    logicalBounds.Height + (expansionHundredthInch * 2f));
            }

            float scaleX = drawBounds.Width / img.Width;
            float scaleY = drawBounds.Height / img.Height;

            float scale;
            switch (borderlessMode)
            {
                case BorderlessMode.Off:
                    scale = Mathf.Min(scaleX, scaleY);
                    break;

                case BorderlessMode.RetainSize:
                    scale = Mathf.Max(scaleX, scaleY);
                    break;

                case BorderlessMode.AutoExpand:
                default:
                    scale = Mathf.Max(scaleX, scaleY);
                    scale *= bleedFactor;
                    break;
            }

            // borderlessMode優先だが、scaleToFill=falseなら常にFit寄りへ倒す
            if (!scaleToFill)
                scale = Mathf.Min(scaleX, scaleY);

            float targetW = img.Width * scale;
            float targetH = img.Height * scale;

            float posX = drawBounds.X + ((drawBounds.Width - targetW) / 2f) + manualOffset.x;
            float posY = drawBounds.Y + ((drawBounds.Height - targetH) / 2f) + manualOffset.y;

            UnityEngine.Debug.Log(
                $"🧾 DrawImage => Mode:{borderlessMode} Pos({posX:F2},{posY:F2}) " +
                $"Size({targetW:F2}x{targetH:F2}) Bounds({drawBounds.Width:F2}x{drawBounds.Height:F2})");

            e.Graphics.DrawImage(img, posX, posY, targetW, targetH);
        }
    }

    private static void ApplyHighQualityGraphics(Graphics g)
    {
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
    }

    private static void LogPageDebug(PrintPageEventArgs e)
    {
        UnityEngine.Debug.Log($"[PrintPage] PageBounds: {e.PageBounds.Width}x{e.PageBounds.Height} (Landscape:{e.PageSettings.Landscape})");
        UnityEngine.Debug.Log($"[PrintPage] MarginBounds: {e.MarginBounds.Width}x{e.MarginBounds.Height}");
        UnityEngine.Debug.Log($"[PrintPage] PrintableArea: {e.PageSettings.PrintableArea.Width:F1}x{e.PageSettings.PrintableArea.Height:F1}");
        UnityEngine.Debug.Log($"[PrintPage] VisibleClipBounds: {e.Graphics.VisibleClipBounds.Width:F1}x{e.Graphics.VisibleClipBounds.Height:F1}");
    }

    private static Image ConvertTextureToImage(Texture2D texture)
    {
        // PNGの方が透過・品質面で安全
        byte[] bytes = texture.EncodeToPNG();
        MemoryStream ms = new MemoryStream(bytes);
        return Image.FromStream(ms);
    }

    private static float MmToHundredthInch(float mm)
    {
        return (mm / 25.4f) * 100f;
    }

    private bool IsLandscapeFrame(string frameType)
    {
        if (string.IsNullOrWhiteSpace(frameType)) return false;

        string lower = frameType.ToLower();
        for (int i = 0; i < landscapeKeywords.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(landscapeKeywords[i]) && lower.Contains(landscapeKeywords[i].ToLower()))
                return true;
        }

        return false;
    }

    #endregion

    #region Error Panel / Snooze

    private void OnCloseErrorClicked()
    {
        if (closeButtonTapCount >= 5)
        {
            StartCoroutine(SnoozeErrorRoutine());
            closeButtonTapCount = 0;
            UnityEngine.Debug.Log("🔒 Error panel closed via secret unlock.");
        }
        else
        {
            UnityEngine.Debug.Log($"🚫 Close button locked. Need {5 - closeButtonTapCount} more taps on error message.");
        }
    }

    private void OnErrorMessageClicked()
    {
        closeButtonTapCount++;
        UnityEngine.Debug.Log($"👆 Error message tapped {closeButtonTapCount}/5 times.");
    }

    private IEnumerator SnoozeErrorRoutine()
    {
        isErrorSnoozed = true;
        HideError();

        yield return new WaitForSeconds(2f);

        isErrorSnoozed = false;
    }

    private void ShowError(string msg)
    {
        if (isErrorSnoozed) return;
        if (printerErrorPanel == null) return;

        printerErrorPanel.SetActive(true);

        if (printerErrorText != null)
            printerErrorText.text = msg;

        bool isSessionActive = IsAnyCustomerSessionActive();
        string currentOrderId = PaymentManager.Instance?.currentOrderId;

        if (isSessionActive && lastReportedErrorOrderId != (currentOrderId ?? "no_order"))
        {
            string condition = MapJapaneseErrorMessageToCondition(msg);

            if (!string.IsNullOrEmpty(currentOrderId))
            {
                lastReportedErrorOrderId = currentOrderId;
                StartCoroutine(SendPrintStatusToBackend(currentOrderId, false, condition));
                UnityEngine.Debug.Log($"🚨 Immediate printer error reported for order {currentOrderId}: {condition}");
            }
            else
            {
                lastReportedErrorOrderId = "no_order";
                UnityEngine.Debug.Log($"🚨 Session printer error detected before order creation: {condition}");
            }

            if (PhotoShootingManager.Instance != null)
            {
                PhotoShootingManager.Instance.ResetToLoginScreen();
                UnityEngine.Debug.Log("🏠 Session aborted due to printer error -> Reset to Login");
            }
        }

        UpdateCloseButtonVisibility(isSessionActive);
    }

    private void HideError()
    {
        if (printerErrorPanel != null && printerErrorPanel.activeSelf)
        {
            printerErrorPanel.SetActive(false);
            closeButtonTapCount = 0;
        }
    }

    private bool IsAnyCustomerSessionActive()
    {
        bool active = false;

        if (LoginManager.Instance != null)
        {
            if (LoginManager.Instance.frameSelectionPanel != null && LoginManager.Instance.frameSelectionPanel.activeSelf)
                active = true;

            if (LoginManager.Instance.paymentPanel != null && LoginManager.Instance.paymentPanel.activeSelf)
                active = true;
        }

        if (PhotoShootingManager.Instance != null &&
            PhotoShootingManager.Instance.photoShootPanel != null &&
            PhotoShootingManager.Instance.photoShootPanel.activeSelf)
        {
            active = true;
        }

        return active;
    }

    private void UpdateCloseButtonVisibility(bool isSessionActive)
    {
        if (closeErrorButton == null) return;

        UnityEngine.UI.Image img = closeErrorButton.GetComponent<UnityEngine.UI.Image>();
        if (img == null) return;

        float alpha = isSessionActive ? 0f : 1f;
        img.color = new UnityEngine.Color(img.color.r, img.color.g, img.color.b, alpha);
    }

    private string MapJapaneseErrorMessageToCondition(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return "printer error";

        if (msg.Contains("紙詰まり")) return "paper jam";
        if (msg.Contains("用紙切れ")) return "paper out";
        if (msg.Contains("接続されていません")) return "printer offline";
        if (msg.Contains("カバー")) return "cover open";
        if (msg.Contains("インク切れ")) return "ink empty";
        if (msg.Contains("残量低下")) return "ink low";

        return "printer error";
    }

    #endregion

    #region Printer Status Monitoring

    private IEnumerator CheckPrinterStatusRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(1.0f);

        while (this != null && gameObject != null && gameObject.activeInHierarchy)
        {
            if (!string.IsNullOrWhiteSpace(selectedPrinter) && !isErrorSnoozed)
                CheckStatusNative();

            yield return wait;
        }
    }

    private void CheckStatusNative()
    {
        string status = GetSimulatedOrNativePrinterStatus();
        if (string.IsNullOrWhiteSpace(status))
            status = "Unknown";

        LastStatus = status;

        if (simulationMode == PrinterSimulationMode.Disable && !simulateReady)
        {
            IsPrinting = IsPrinting ||
                         status == "Status_Printing" ||
                         status == "Status_Busy" ||
                         status == "Status_Processing";
        }

        if (status == "Ready" || status == "Status_Printing" || status == "Status_Busy" || status == "Status_Processing")
        {
            if (!IsPrinting || status == "Ready")
            {
                // Readyに戻った時点で終息扱い
                IsPrinting = false;
            }
            HideError();
            return;
        }

        string uiMessage = InterpretStatusToJapaneseMessage(status);

        if (!string.IsNullOrEmpty(uiMessage))
            ShowError(uiMessage);
    }

    private string GetSimulatedOrNativePrinterStatus()
    {
        if (simulationMode != PrinterSimulationMode.Disable)
        {
            switch (simulationMode)
            {
                case PrinterSimulationMode.SimulateSuccess:
                    IsPrinting = false;
                    return "Ready";

                case PrinterSimulationMode.SimulatePaperJam:
                    IsPrinting = false;
                    return "ERROR_PAPER_JAM (Simulated)";

                case PrinterSimulationMode.SimulatePaperOut:
                    IsPrinting = false;
                    return "ERROR_PAPER_OUT (Simulated)";

                case PrinterSimulationMode.SimulateOffline:
                    IsPrinting = false;
                    return "ERROR_OFFLINE (Simulated)";
            }
        }

        if (simulateReady)
        {
            IsPrinting = false;
            return "Ready";
        }

        try
        {
            return NativePrinterHelper.GetPrinterStatus(selectedPrinter);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"NativePrinterHelper failed: {ex.Message}");
            return "ERROR_UNKNOWN";
        }
    }

    private string InterpretStatusToJapaneseMessage(string status)
    {
        if (string.IsNullOrEmpty(status)) return "プリンター状態が取得できません";

        string s = status.ToUpperInvariant();

        if (s.Contains("PAPER_JAM")) return "紙詰まりです";
        if (s.Contains("PAPER_OUT")) return "用紙切れです";
        if (s.Contains("DOOR_OPEN")) return "カバーが開いています";
        if (s.Contains("NO_TONER") || s.Contains("NO_INK")) return "インク切れです";
        if (s.Contains("TONER_LOW") || s.Contains("INK_LOW")) return "インク残量低下";
        if (s.Contains("OFFLINE")) return "プリンターが接続されていません";
        if (s.Contains("NOTFOUND")) return "プリンターが見つかりません";
        if (s.Contains("ERROR")) return "プリンターエラー";
        if (s.Contains("UNKNOWN")) return "プリンター状態が不明です";

        return "プリンターエラー";
    }

    #endregion

    #region Backend Reporting

    public IEnumerator SendPrintStatusToBackend(string orderId, bool printingStatus, string condition)
    {
        if (string.IsNullOrEmpty(orderId))
            yield break;

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
                catch
                {
                    // ignored
                }

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
                    catch
                    {
                        // ignored
                    }

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

    #endregion

    #region Epson SL-D1000 Paper Profile

    private static class EpsonSLD1000PaperProfile
    {
        private static readonly Dictionary<string, List<string>> AliasMap = new Dictionary<string, List<string>>
        {
            { "100x148", new List<string> { "100x148", "100×148", "4x6", "4×6", "102x152", "10x15", "kg" } },
            { "4x6",     new List<string> { "4x6", "4×6", "102x152", "10x15", "kg" } },
            { "5x7",     new List<string> { "5x7", "5×7", "127x178", "13x18" } },
            { "6x8",     new List<string> { "6x8", "6×8", "152x203" } },
            { "a4",      new List<string> { "a4", "210x297" } },
            { "3.5x5",   new List<string> { "3.5x5", "35x5", "89x127" } },
            { "4x4",     new List<string> { "4x4", "102x102" } },
            { "5x5",     new List<string> { "5x5", "127x127" } },
            { "6x6",     new List<string> { "6x6", "152x152" } }
        };

        public static List<string> GetAliases(string normalizedRequested)
        {
            if (AliasMap.TryGetValue(normalizedRequested, out List<string> aliases))
            {
                return aliases.Select(NormalizeAlias).Distinct().ToList();
            }

            return new List<string> { NormalizeAlias(normalizedRequested) };
        }

        public static bool IsSquareSize(string normalizedRequested)
        {
            return normalizedRequested == "4x4" ||
                   normalizedRequested == "5x5" ||
                   normalizedRequested == "6x6";
        }

        private static string NormalizeAlias(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            string v = value.ToLower().Trim();
            v = v.Replace(" ", "");
            v = v.Replace("-", "");
            v = v.Replace("_", "");
            return v;
        }
    }

    #endregion
}
