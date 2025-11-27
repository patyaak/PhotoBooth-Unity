using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PrintingManager : MonoBehaviour
{
    public static PrintingManager Instance;

    [Header("Printing UI")]
    public GameObject printingPanel;
    public GameObject errorPanel;
    public TMP_Text statusText;
    public TMP_Text errorText;
    public Slider progressBar;
    public Button retryButton;
    public Image previewImage;

    [Header("Print Settings")]
    public string printerName = "Brother DCP-L2540DW series";

    public enum PaperSize
    {
        Size3x4,    // 1200x1600 (3:4 ratio - typical photo booth)
        Size2x3,    // 1200x1800 (2:3 ratio)
        Size4x6,    // 1200x1800 (4:6 ratio - standard photo)
        Custom
    }

    public PaperSize paperSize = PaperSize.Size3x4;

    [Header("Custom Size (if Custom selected)")]
    public int printWidth = 1200;
    public int printHeight = 1600;

    [Header("Other Settings")]
    public int printCopies = 1;
    public bool autoCloseAfterPrint = true;
    public float autoCloseDelay = 2f;

    [Header("USB Disconnection Detection")]
    public bool showRealtimeUSBErrors = true;
    public float usbCheckIntervalWhenIdle = 3f;
    public float usbCheckIntervalDuringPrint = 0.5f;

    [Header("Startup Check")]
    public bool checkPrinterOnStart = true;
    public GameObject startupErrorPanel;
    public TMP_Text startupErrorText;
    public Button startupRetryButton;

    private Texture2D currentImageToPrint;
    private bool isPrinting = false;
    private Coroutine printerStatusCheckCoroutine;
    private PrinterStatus lastKnownStatus = PrinterStatus.Unknown;

    // Windows API for printer status
#if UNITY_STANDALONE_WIN
    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetPrinter(IntPtr hPrinter, int level, IntPtr pPrinter, int cbBuf, out int pcbNeeded);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct PRINTER_INFO_2
    {
        public string pServerName;
        public string pPrinterName;
        public string pShareName;
        public string pPortName;
        public string pDriverName;
        public string pComment;
        public string pLocation;
        public IntPtr pDevMode;
        public string pSepFile;
        public string pPrintProcessor;
        public string pDatatype;
        public string pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint cJobs;
        public uint AveragePPM;
    }

    // Printer status flags
    private const int PRINTER_STATUS_PAUSED = 0x00000001;
    private const int PRINTER_STATUS_ERROR = 0x00000002;
    private const int PRINTER_STATUS_PENDING_DELETION = 0x00000004;
    private const int PRINTER_STATUS_PAPER_JAM = 0x00000008;
    private const int PRINTER_STATUS_PAPER_OUT = 0x00000010;
    private const int PRINTER_STATUS_MANUAL_FEED = 0x00000020;
    private const int PRINTER_STATUS_PAPER_PROBLEM = 0x00000040;
    private const int PRINTER_STATUS_OFFLINE = 0x00000080;
    private const int PRINTER_STATUS_IO_ACTIVE = 0x00000100;
    private const int PRINTER_STATUS_BUSY = 0x00000200;
    private const int PRINTER_STATUS_PRINTING = 0x00000400;
    private const int PRINTER_STATUS_OUTPUT_BIN_FULL = 0x00000800;
    private const int PRINTER_STATUS_NOT_AVAILABLE = 0x00001000;
    private const int PRINTER_STATUS_WAITING = 0x00002000;
    private const int PRINTER_STATUS_PROCESSING = 0x00004000;
    private const int PRINTER_STATUS_INITIALIZING = 0x00008000;
    private const int PRINTER_STATUS_WARMING_UP = 0x00010000;
    private const int PRINTER_STATUS_TONER_LOW = 0x00020000;
    private const int PRINTER_STATUS_NO_TONER = 0x00040000;
    private const int PRINTER_STATUS_PAGE_PUNT = 0x00080000;
    private const int PRINTER_STATUS_USER_INTERVENTION = 0x00100000;
    private const int PRINTER_STATUS_OUT_OF_MEMORY = 0x00200000;
    private const int PRINTER_STATUS_DOOR_OPEN = 0x00400000;
    private const int PRINTER_STATUS_SERVER_UNKNOWN = 0x00800000;
    private const int PRINTER_STATUS_POWER_SAVE = 0x01000000;
#endif

    public enum PrinterStatus
    {
        Ready,
        Offline,
        PaperOut,
        PaperJam,
        CoverOpen,
        OutOfToner,
        TonerLow,
        Error,
        NotFound,
        Busy,
        Unknown
    }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        ConfigurePrintDimensions();

        if (printingPanel != null)
            printingPanel.SetActive(false);

        if (errorPanel != null)
            errorPanel.SetActive(false);

        if (retryButton != null)
            retryButton.onClick.AddListener(OnRetryPrint);

        if (startupRetryButton != null)
            startupRetryButton.onClick.AddListener(CheckPrinterStatusOnStartup);

        if (checkPrinterOnStart)
        {
            CheckPrinterStatusOnStartup();
        }

        if (printerStatusCheckCoroutine == null)
            printerStatusCheckCoroutine = StartCoroutine(PeriodicPrinterStatusCheck());
    }

    private void ConfigurePrintDimensions()
    {
        switch (paperSize)
        {
            case PaperSize.Size3x4:
                printWidth = 1200;
                printHeight = 1600;
                Debug.Log("📄 Paper size: 3:4 ratio (1200x1600)");
                break;
            case PaperSize.Size2x3:
                printWidth = 1200;
                printHeight = 1800;
                Debug.Log("📄 Paper size: 2:3 ratio (1200x1800)");
                break;
            case PaperSize.Size4x6:
                printWidth = 1200;
                printHeight = 1800;
                Debug.Log("📄 Paper size: 4:6 ratio (1200x1800)");
                break;
            case PaperSize.Custom:
                Debug.Log($"📄 Custom paper size: {printWidth}x{printHeight}");
                break;
        }

        Debug.Log($"🖨️ Print dimensions configured: {printWidth}x{printHeight}");
    }

    public void CheckPrinterStatusOnStartup()
    {
        Debug.Log("🖨️ Checking printer status on startup...");

        PrinterStatus status = GetPrinterStatus();
        lastKnownStatus = status;

        if (status == PrinterStatus.Ready)
        {
            Debug.Log("✅ Printer is ready");
            if (startupErrorPanel != null)
                startupErrorPanel.SetActive(false);
        }
        else
        {
            Debug.LogWarning($"⚠️ Printer issue detected: {status}");
            ShowStartupError(status);
        }
    }

    private void ShowStartupError(PrinterStatus status)
    {
        if (startupErrorPanel == null) return;

        startupErrorPanel.SetActive(true);

        string errorMessage = GetStatusMessage(status);

        if (startupErrorText != null)
            startupErrorText.text = errorMessage;

        Debug.LogError($"❌ Startup Error: {errorMessage}");
    }

    /// <summary>
    /// Improved periodic check with faster USB detection during printing
    /// </summary>
    private IEnumerator PeriodicPrinterStatusCheck()
    {
        while (true)
        {
            // Use faster checking during printing
            float checkInterval = isPrinting ? usbCheckIntervalDuringPrint : usbCheckIntervalWhenIdle;
            yield return new WaitForSeconds(checkInterval);

            PrinterStatus status = GetPrinterStatus();

            if (status != lastKnownStatus)
            {
                Debug.Log($"🖨️ Printer status changed: {lastKnownStatus} → {status}");
                lastKnownStatus = status;

                // Immediate USB disconnection detection
                if (status == PrinterStatus.NotFound || status == PrinterStatus.Offline)
                {
                    Debug.LogError($"❌ USB DISCONNECTED OR PRINTER OFFLINE!");

                    if (showRealtimeUSBErrors)
                    {
                        // Stop any ongoing print job
                        if (isPrinting)
                        {
                            StopCoroutine("PrintImageCoroutine");
                            isPrinting = false;
                        }

                        // Show error immediately
                        ShowPrinterError(status);
                    }
                }
                else if (status != PrinterStatus.Ready && status != PrinterStatus.Busy)
                {
                    OnPrinterError(status);
                }
            }
        }
    }

    /// <summary>
    /// Enhanced printer status detection with better USB error codes
    /// </summary>
    public PrinterStatus GetPrinterStatus()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        IntPtr hPrinter = IntPtr.Zero;

        try
        {
            // Try to open printer - this will fail if USB is disconnected
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
            {
                int errorCode = Marshal.GetLastWin32Error();

                // Error codes from Windows:
                // 1801 = ERROR_INVALID_PRINTER_NAME (printer doesn't exist)
                // 2 = ERROR_FILE_NOT_FOUND (USB disconnected)
                // 1722 = RPC_S_SERVER_UNAVAILABLE (network printer offline)

                Debug.LogWarning($"⚠️ Cannot open printer: {printerName}");
                Debug.LogWarning($"⚠️ Windows Error Code: {errorCode}");

                if (errorCode == 1801)
                {
                    Debug.LogError("❌ Printer name not found in Windows!");
                    return PrinterStatus.NotFound;
                }
                else if (errorCode == 2 || errorCode == 1722)
                {
                    Debug.LogError("❌ USB disconnected or printer not accessible!");
                    return PrinterStatus.Offline;
                }
                else
                {
                    return PrinterStatus.NotFound;
                }
            }

            // Get printer info
            int needed = 0;
            GetPrinter(hPrinter, 2, IntPtr.Zero, 0, out needed);

            if (needed <= 0)
            {
                Debug.LogWarning("⚠️ Could not get printer info size");
                return PrinterStatus.Error;
            }

            IntPtr pPrinterInfo = Marshal.AllocHGlobal(needed);

            try
            {
                if (GetPrinter(hPrinter, 2, pPrinterInfo, needed, out needed))
                {
                    PRINTER_INFO_2 printerInfo = (PRINTER_INFO_2)Marshal.PtrToStructure(pPrinterInfo, typeof(PRINTER_INFO_2));

                    uint status = printerInfo.Status;

                    Debug.Log($"🖨️ Raw printer status: 0x{status:X8}");

                    // Check status flags in priority order
                    if ((status & PRINTER_STATUS_DOOR_OPEN) != 0)
                        return PrinterStatus.CoverOpen;

                    if ((status & PRINTER_STATUS_OFFLINE) != 0)
                    {
                        Debug.LogWarning("⚠️ Printer is OFFLINE - USB may be disconnected!");
                        return PrinterStatus.Offline;
                    }

                    if ((status & PRINTER_STATUS_PAPER_OUT) != 0)
                        return PrinterStatus.PaperOut;

                    if ((status & PRINTER_STATUS_PAPER_JAM) != 0)
                        return PrinterStatus.PaperJam;

                    if ((status & PRINTER_STATUS_NO_TONER) != 0)
                        return PrinterStatus.OutOfToner;

                    if ((status & PRINTER_STATUS_TONER_LOW) != 0)
                        return PrinterStatus.TonerLow;

                    if ((status & PRINTER_STATUS_ERROR) != 0)
                        return PrinterStatus.Error;

                    if ((status & PRINTER_STATUS_USER_INTERVENTION) != 0)
                        return PrinterStatus.Error;

                    if ((status & PRINTER_STATUS_NOT_AVAILABLE) != 0)
                    {
                        Debug.LogWarning("⚠️ Printer NOT AVAILABLE - USB may be disconnected!");
                        return PrinterStatus.Offline;
                    }

                    if ((status & PRINTER_STATUS_BUSY) != 0 || (status & PRINTER_STATUS_PRINTING) != 0)
                        return PrinterStatus.Busy;

                    return PrinterStatus.Ready;
                }
                else
                {
                    Debug.LogWarning("⚠️ GetPrinter failed");
                    return PrinterStatus.Error;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pPrinterInfo);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ Error checking printer status: {ex.Message}");
            return PrinterStatus.Error;
        }
        finally
        {
            if (hPrinter != IntPtr.Zero)
                ClosePrinter(hPrinter);
        }
#else
        Debug.LogWarning("⚠️ Printer status check only works in Windows build");
        return PrinterStatus.Ready;
#endif
    }

    /// <summary>
    /// Improved error messages with USB disconnection details
    /// </summary>
    private string GetStatusMessage(PrinterStatus status)
    {
        switch (status)
        {
            case PrinterStatus.Ready:
                return "プリンター準備完了";

            case PrinterStatus.Offline:
                return "⚠️ プリンターがオフラインです\n\n" +
                       "• USBケーブルが接続されているか確認してください\n" +
                       "• プリンターの電源が入っているか確認してください\n" +
                       "• プリンターケーブルを再接続してください";

            case PrinterStatus.NotFound:
                return $"❌ プリンターが見つかりません\n\n" +
                       $"プリンター名: '{printerName}'\n\n" +
                       "• USBケーブルが正しく接続されているか確認してください\n" +
                       "• プリンターの電源を確認してください\n" +
                       "• Windowsでプリンターが認識されているか確認してください\n" +
                       "• プリンター名が正しいか確認してください";

            case PrinterStatus.PaperOut:
                return "📄 用紙切れ\n用紙を補充してください";

            case PrinterStatus.PaperJam:
                return "📄 紙詰まり\n詰まった用紙を取り除いてください";

            case PrinterStatus.CoverOpen:
                return "🚪 プリンターカバーが開いています\nカバーを閉じてください";

            case PrinterStatus.OutOfToner:
                return "🖨️ トナー切れ\nトナー/リボンを交換してください";

            case PrinterStatus.TonerLow:
                return "⚠️ トナー残量が少なくなっています\nまもなく交換が必要です";

            case PrinterStatus.Error:
                return "❌ プリンターエラー\n\n" +
                       "プリンターを確認してください\n" +
                       "エラーランプが点灯している場合は、プリンターの説明書を確認してください";

            case PrinterStatus.Busy:
                return "⏳ プリンター処理中\nしばらくお待ちください";

            default:
                return "❓ プリンターの状態を確認できません";
        }
    }

    public void PrintFinalImage(Texture2D imageToPrint)
    {
        if (imageToPrint == null)
        {
            Debug.LogError("❌ Cannot print: Image is null!");
            return;
        }

        Debug.Log($"🖨️ Print request received for image: {imageToPrint.width}x{imageToPrint.height}");

        PrinterStatus status = GetPrinterStatus();

        if (status != PrinterStatus.Ready)
        {
            Debug.LogWarning($"⚠️ Printer not ready: {status}");
            ShowPrinterError(status);
            return;
        }

        currentImageToPrint = imageToPrint;
        StartCoroutine(PrintImageCoroutine(imageToPrint));
    }

    private void ShowPrintingPanel()
    {
        if (printingPanel != null)
            printingPanel.SetActive(true);

        if (errorPanel != null)
            errorPanel.SetActive(false);

        if (statusText != null)
            statusText.text = "印刷中...";

        if (progressBar != null)
        {
            progressBar.gameObject.SetActive(true);
            progressBar.value = 0;
        }
    }

    private IEnumerator PrintImageCoroutine(Texture2D imageToPrint)
    {
        isPrinting = true;

        ShowPrintingPanel();

        if (statusText != null)
            statusText.text = "印刷中...";

        if (progressBar != null)
        {
            progressBar.gameObject.SetActive(true);
            progressBar.value = 0;
        }

        // Check status before printing
        PrinterStatus status = GetPrinterStatus();
        if (status != PrinterStatus.Ready)
        {
            ShowPrinterError(status);
            isPrinting = false;
            yield break;
        }

        UpdateProgress(0.2f, "画像を準備中...");
        yield return new WaitForSeconds(0.5f);

        // Prepare image for printing
        Texture2D printReadyImage = PrepareImageForPrint(imageToPrint);

        UpdateProgress(0.4f, "プリンターに送信中...");
        yield return new WaitForSeconds(0.5f);

        // Print using Windows
        bool printSuccess = PrintWithWindows(printReadyImage);

        if (printSuccess)
        {
            UpdateProgress(1f, "印刷完了！");
            yield return new WaitForSeconds(autoCloseDelay);
            ClosePrintingPanel();
            OnPrintComplete();
        }
        else
        {
            yield return new WaitForSeconds(1f);
            status = GetPrinterStatus();
            ShowPrinterError(status);
        }

        isPrinting = false;
    }

    private Texture2D PrepareImageForPrint(Texture2D sourceImage)
    {
        if (sourceImage.width == printWidth && sourceImage.height == printHeight)
        {
            Debug.Log("✅ Image already at correct print size");
            return sourceImage;
        }

        Debug.Log($"🖨️ Resizing image from {sourceImage.width}x{sourceImage.height} to {printWidth}x{printHeight}");
        return ResizeTexture(sourceImage, printWidth, printHeight);
    }

    private Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
        RenderTexture.active = rt;

        Graphics.Blit(source, rt);

        Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
        result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        result.Apply();

        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);

        return result;
    }
    private bool PrintWithWindows(Texture2D image)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    try
    {
        // STEP 1: Force exact size (change only these two numbers for any printer)
        int targetWidth  = printWidth;   // 1200 for 3:4, 1200 for 4:6, 1240 for DNP 4x6, etc.
        int targetHeight = printHeight;  // 1600 or 1800 or 1844

        // STEP 2: Resize + remove alpha (ALL printers hate alpha channel)
        Texture2D finalImage = ResizeTexture(image, targetWidth, targetHeight);
        Texture2D rgbImage = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
        rgbImage.SetPixels(finalImage.GetPixels());
        rgbImage.Apply();

        // STEP 3: Save to a FIXED, SIMPLE location with FIXED name
        // This is the #1 trick that makes Canon, Epson, DNP, HiTi, Brother ALL work
        string fixedPath = @"C:\PhotoBooth\printjob.png";  // ← CREATE THIS FOLDER FIRST!

        // Create folder if not exists
        System.IO.Directory.CreateDirectory(@"C:\PhotoBooth");
        if (System.IO.File.Exists(fixedPath))
            System.IO.File.Delete(fixedPath);

        System.IO.File.WriteAllBytes(fixedPath, rgbImage.EncodeToPNG());
        Debug.Log($"Print-ready file saved: {fixedPath} ({targetWidth}x{targetHeight})");

        // STEP 4: The magic command that works on EVERY printer in existence
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "mspaint",
            Arguments = $"/pt \"{fixedPath}\"",
            UseShellExecute = true,
            CreateNoWindow = true
        };
        System.Diagnostics.Process.Start(psi);

        // NEVER delete the file automatically in production
        // (or wait 10 minutes if you really want to)
        // StartCoroutine(DeleteTempFileAfterDelay(fixedPath, 600f));

        Destroy(rgbImage);
        return true;
    }
    catch (System.Exception ex)
    {
        Debug.LogError("Print failed: " + ex.Message);
        return false;
    }
#else
        return true;
#endif
    }

    private IEnumerator DeleteTempFileAfterDelay(string filePath, float delay)
    {
        yield return new WaitForSeconds(delay);

        try
        {
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
                Debug.Log($"🗑️ Temp file deleted: {filePath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"⚠️ Could not delete temp file: {ex.Message}");
        }
    }

    private void UpdateProgress(float progress, string message)
    {
        if (progressBar != null)
            progressBar.value = progress;

        if (statusText != null)
            statusText.text = message;
    }

    private void ShowPrinterError(PrinterStatus status)
    {
        if (printingPanel != null)
            printingPanel.SetActive(true);

        if (errorPanel != null)
            errorPanel.SetActive(true);

        string errorMessage = GetStatusMessage(status);

        if (errorText != null)
            errorText.text = errorMessage;

        if (statusText != null)
            statusText.text = "印刷エラー";

        if (retryButton != null)
            retryButton.gameObject.SetActive(true);

        Debug.LogError($"❌ Printer Error: {errorMessage}");
    }

    private void OnPrinterError(PrinterStatus status)
    {
        Debug.LogWarning($"⚠️ Printer error detected during monitoring: {status}");
    }

    private void OnRetryPrint()
    {
        if (errorPanel != null)
            errorPanel.SetActive(false);

        PrinterStatus status = GetPrinterStatus();

        if (status == PrinterStatus.Ready)
        {
            if (currentImageToPrint != null)
            {
                StartCoroutine(PrintImageCoroutine(currentImageToPrint));
            }
        }
        else
        {
            ShowPrinterError(status);
        }
    }

    private void OnPrintComplete()
    {
        Debug.Log("✅ Print completed successfully");
    }

    private void ClosePrintingPanel()
    {
        if (printingPanel != null)
            printingPanel.SetActive(false);

        if (errorPanel != null)
            errorPanel.SetActive(false);

        currentImageToPrint = null;
    }

    private void OnDestroy()
    {
        if (printerStatusCheckCoroutine != null)
            StopCoroutine(printerStatusCheckCoroutine);
    }

    // ============================================================
    // PUBLIC UTILITY METHODS
    // ============================================================

    public bool IsPrinterReady()
    {
        PrinterStatus status = GetPrinterStatus();
        return status == PrinterStatus.Ready;
    }

    public string GetCurrentPrinterStatusMessage()
    {
        return GetStatusMessage(GetPrinterStatus());
    }

    public void SetPaperSize(PaperSize size)
    {
        paperSize = size;
        ConfigurePrintDimensions();
    }

    public void SetCustomPrintSize(int width, int height)
    {
        paperSize = PaperSize.Custom;
        printWidth = width;
        printHeight = height;
        Debug.Log($"🖨️ Custom print size set: {printWidth}x{printHeight}");
    }

    /// <summary>
    /// Manual USB connection check - can be called from UI button
    /// </summary>
    public void CheckUSBConnection()
    {
        Debug.Log("🔌 Manually checking USB connection...");

        PrinterStatus status = GetPrinterStatus();

        if (status == PrinterStatus.NotFound || status == PrinterStatus.Offline)
        {
            Debug.LogError($"❌ USB Connection Error: {status}");
            ShowPrinterError(status);
        }
        else if (status == PrinterStatus.Ready)
        {
            Debug.Log("✅ USB connected - Printer is ready!");

            if (startupErrorPanel != null)
                startupErrorPanel.SetActive(false);

            if (errorPanel != null)
                errorPanel.SetActive(false);
        }
        else
        {
            Debug.LogWarning($"⚠️ Printer status: {status}");
            ShowPrinterError(status);
        }
    }
}