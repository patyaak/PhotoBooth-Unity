using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
//using System.Drawing.Printing;
using System.Drawing;

public class PrintingManager : MonoBehaviour
{
    public static PrintingManager Instance;

    [Header("Printing UI")]
    public GameObject printingPanel;
    public GameObject errorPanel;
    public TMP_Text statusText;
    public TMP_Text errorText;
    public Slider progressBar;
    public Button printButton;
    public Button retryButton;
    public Button cancelButton;
    public Image previewImage;

    [Header("Print Settings")]
    public string printerName = "EPSON SD-550"; // Exact printer name from Windows
    public int printWidth = 1844;  // 4x6 inch at 300 DPI
    public int printHeight = 1240;
    public int printCopies = 1;
    public bool autoCloseAfterPrint = true;
    public float autoCloseDelay = 2f;
    public float statusCheckInterval = 5f; // Check printer every 5 seconds

    [Header("Startup Check")]
    public bool checkPrinterOnStart = true;
    public GameObject startupErrorPanel;
    public TMP_Text startupErrorText;
    public Button startupRetryButton;

    private Texture2D currentImageToPrint;
    private bool isPrinting = false;
    private Queue<Texture2D> printQueue = new Queue<Texture2D>();
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
    private const int PRINTER_STATUS_PAPER_JAM = 0x00000008;
    private const int PRINTER_STATUS_PAPER_OUT = 0x00000010;
    private const int PRINTER_STATUS_OUTPUT_BIN_FULL = 0x00000800;
    private const int PRINTER_STATUS_NOT_AVAILABLE = 0x00001000;
    private const int PRINTER_STATUS_OFFLINE = 0x00000080;
#endif

    public enum PrinterStatus
    {
        Ready,
        Offline,
        PaperOut,
        PaperJam,
        Error,
        NotFound,
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
        if (printingPanel != null)
            printingPanel.SetActive(false);

        if (errorPanel != null)
            errorPanel.SetActive(false);

        if (printButton != null)
            printButton.onClick.AddListener(OnPrintButtonClicked);

        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelPrint);

        if (retryButton != null)
            retryButton.onClick.AddListener(OnRetryPrint);

        if (startupRetryButton != null)
            startupRetryButton.onClick.AddListener(CheckPrinterStatusOnStartup);

        // Check printer on startup
        if (checkPrinterOnStart)
        {
            CheckPrinterStatusOnStartup();
        }

        // Start periodic status check
        if (printerStatusCheckCoroutine == null)
            printerStatusCheckCoroutine = StartCoroutine(PeriodicPrinterStatusCheck());
    }

    /// <summary>
    /// Check printer status on app startup
    /// </summary>
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
    /// Periodic background check for printer status
    /// </summary>
    private IEnumerator PeriodicPrinterStatusCheck()
    {
        while (true)
        {
            yield return new WaitForSeconds(statusCheckInterval);

            if (!isPrinting) // Don't check while printing
            {
                PrinterStatus status = GetPrinterStatus();

                if (status != lastKnownStatus)
                {
                    Debug.Log($"🖨️ Printer status changed: {lastKnownStatus} → {status}");
                    lastKnownStatus = status;

                    if (status != PrinterStatus.Ready)
                    {
                        OnPrinterError(status);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Get current printer status using Windows API
    /// </summary>
    public PrinterStatus GetPrinterStatus()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        IntPtr hPrinter = IntPtr.Zero;

        try
        {
            // Open printer
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
            {
                Debug.LogWarning($"⚠️ Could not open printer: {printerName}");
                return PrinterStatus.NotFound;
            }

            // Get printer info size
            int needed = 0;
            GetPrinter(hPrinter, 2, IntPtr.Zero, 0, out needed);

            // Allocate buffer
            IntPtr pPrinterInfo = Marshal.AllocHGlobal(needed);

            try
            {
                // Get printer info
                if (GetPrinter(hPrinter, 2, pPrinterInfo, needed, out needed))
                {
                    PRINTER_INFO_2 printerInfo = (PRINTER_INFO_2)Marshal.PtrToStructure(pPrinterInfo, typeof(PRINTER_INFO_2));

                    // Check status flags
                    if ((printerInfo.Status & PRINTER_STATUS_OFFLINE) != 0)
                        return PrinterStatus.Offline;

                    if ((printerInfo.Status & PRINTER_STATUS_PAPER_OUT) != 0)
                        return PrinterStatus.PaperOut;

                    if ((printerInfo.Status & PRINTER_STATUS_PAPER_JAM) != 0)
                        return PrinterStatus.PaperJam;

                    if ((printerInfo.Status & PRINTER_STATUS_ERROR) != 0)
                        return PrinterStatus.Error;

                    if ((printerInfo.Status & PRINTER_STATUS_NOT_AVAILABLE) != 0)
                        return PrinterStatus.Offline;

                    // Printer is ready
                    return PrinterStatus.Ready;
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

        return PrinterStatus.Unknown;
#else
        // In editor, simulate ready status
        Debug.LogWarning("⚠️ Printer status check only works in Windows build");
        return PrinterStatus.Ready;
#endif
    }

    /// <summary>
    /// Get human-readable status message
    /// </summary>
    private string GetStatusMessage(PrinterStatus status)
    {
        switch (status)
        {
            case PrinterStatus.Ready:
                return "プリンター準備完了";
            case PrinterStatus.Offline:
                return "プリンターが接続されていません\nプリンターの電源とケーブルを確認してください";
            case PrinterStatus.PaperOut:
                return "用紙切れ\n用紙を補充してください";
            case PrinterStatus.PaperJam:
                return "紙詰まり\n詰まった用紙を取り除いてください";
            case PrinterStatus.Error:
                return "プリンターエラー\nプリンターを確認してください";
            case PrinterStatus.NotFound:
                return $"プリンターが見つかりません\n'{printerName}'が正しく設定されているか確認してください";
            default:
                return "プリンターの状態を確認できません";
        }
    }

    /// <summary>
    /// Main method to print the final composed image
    /// </summary>
    public void PrintFinalImage(Texture2D imageToPrint)
    {
        if (imageToPrint == null)
        {
            Debug.LogError("❌ Cannot print: Image is null!");
            return;
        }

        Debug.Log($"🖨️ Print request received for image: {imageToPrint.width}x{imageToPrint.height}");

        // Check printer status before printing
        PrinterStatus status = GetPrinterStatus();

        if (status != PrinterStatus.Ready)
        {
            Debug.LogWarning($"⚠️ Printer not ready: {status}");
            ShowPrinterError(status);
            return;
        }

        currentImageToPrint = imageToPrint;
        ShowPrintingPanel(imageToPrint);
    }

    private void ShowPrintingPanel(Texture2D image)
    {
        if (printingPanel != null)
            printingPanel.SetActive(true);

        if (errorPanel != null)
            errorPanel.SetActive(false);

        if (previewImage != null && image != null)
        {
            Sprite sprite = Sprite.Create(
                image,
                new Rect(0, 0, image.width, image.height),
                new Vector2(0.5f, 0.5f)
            );
            previewImage.sprite = sprite;
        }

        if (statusText != null)
            statusText.text = "印刷準備完了";

        if (progressBar != null)
        {
            progressBar.gameObject.SetActive(false);
            progressBar.value = 0;
        }

        if (printButton != null)
            printButton.interactable = true;
    }

    private void OnPrintButtonClicked()
    {
        if (currentImageToPrint == null)
        {
            Debug.LogError("❌ No image to print!");
            return;
        }

        StartCoroutine(PrintImageCoroutine(currentImageToPrint));
    }

    private IEnumerator PrintImageCoroutine(Texture2D imageToPrint)
    {
        isPrinting = true;

        if (printButton != null)
            printButton.interactable = false;

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

        // Prepare image
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
            // Check what went wrong
            status = GetPrinterStatus();
            ShowPrinterError(status);
        }

        isPrinting = false;
    }

    private Texture2D PrepareImageForPrint(Texture2D sourceImage)
    {
        if (sourceImage.width == printWidth && sourceImage.height == printHeight)
            return sourceImage;

        Debug.Log($"🖨️ Resizing image from {sourceImage.width}x{sourceImage.height} to {printWidth}x{printHeight}");
        return ResizeTexture(sourceImage, printWidth, printHeight);
    }

    private Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
        RenderTexture.active = rt;

        UnityEngine.Graphics.Blit(source, rt);

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
            // Save image temporarily
            string tempPath = System.IO.Path.Combine(Application.temporaryCachePath, $"print_{System.DateTime.Now.Ticks}.png");
            byte[] imageData = image.EncodeToPNG();
            System.IO.File.WriteAllBytes(tempPath, imageData);

            Debug.Log($"🖨️ Temp file saved: {tempPath}");

            // Create print document
            PrintDocument printDoc = new PrintDocument();
            printDoc.PrinterSettings.PrinterName = printerName;
            printDoc.PrinterSettings.Copies = (short)printCopies;

            System.Drawing.Image printImage = System.Drawing.Image.FromFile(tempPath);

            printDoc.PrintPage += (sender, e) =>
            {
                e.Graphics.DrawImage(printImage, e.PageBounds);
            };

            // Print
            printDoc.Print();

            Debug.Log("✅ Print job sent successfully");

            // Cleanup
            printImage.Dispose();
            System.IO.File.Delete(tempPath);

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ Print failed: {ex.Message}");
            return false;
        }
#else
        Debug.LogWarning("⚠️ Printing only available in Windows build");
        // In editor, simulate success
        return true;
#endif
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
        if (errorPanel != null)
            errorPanel.SetActive(true);

        string errorMessage = GetStatusMessage(status);

        if (errorText != null)
            errorText.text = errorMessage;

        if (statusText != null)
            statusText.text = "印刷エラー";

        if (printButton != null)
            printButton.interactable = false;

        if (retryButton != null)
            retryButton.gameObject.SetActive(true);

        Debug.LogError($"❌ Printer Error: {errorMessage}");
    }

    private void OnPrinterError(PrinterStatus status)
    {
        Debug.LogWarning($"⚠️ Printer error detected: {status}");
        // You can show a notification here if needed
    }

    private void OnRetryPrint()
    {
        if (errorPanel != null)
            errorPanel.SetActive(false);

        // Check status again
        PrinterStatus status = GetPrinterStatus();

        if (status == PrinterStatus.Ready)
        {
            // Retry printing
            if (currentImageToPrint != null)
            {
                OnPrintButtonClicked();
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

    public void OnCancelPrint()
    {
        if (isPrinting)
        {
            StopAllCoroutines();
            isPrinting = false;
        }

        ClosePrintingPanel();
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

    // Public utility methods
    public bool IsPrinterReady()
    {
        return GetPrinterStatus() == PrinterStatus.Ready;
    }

    public string GetCurrentPrinterStatusMessage()
    {
        return GetStatusMessage(GetPrinterStatus());
    }
}