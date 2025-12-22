using System;
using System.Collections;
using System.IO;
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
    public int dpi = 600; // Standard photo printing DPI for Epson

    private int PaperWidthPixels => Mathf.RoundToInt(paperWidthInches * dpi);
    private int PaperHeightPixels => Mathf.RoundToInt(paperHeightInches * dpi);

    [Header("UI")]
    public GameObject printingPanel;
    public GameObject inProgressPanel;
    public GameObject printingDonePanel;
    public GameObject errorPanel;
    public TMP_Text statusText;
    public TMP_Text errorText;

    [Header("Completion Settings")]
    public float printingDoneDisplaySeconds = 3f;

    private Texture2D imageToPrint;
    private string currentFrameType = "portrait";
    private bool printingComplete = false;

    [Header("Printer Monitoring")]
    public float printerRecheckInterval = 2.5f;
    private Coroutine printerMonitorRoutine;


    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        if (printingPanel) printingPanel.SetActive(false);
        if (inProgressPanel) inProgressPanel.SetActive(false);
        if (printingDonePanel) printingDonePanel.SetActive(false);
        if (errorPanel) errorPanel.SetActive(false);

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    CheckPrinterStatusOnStartup();
#endif

    }
    private void CheckPrinterStatusOnStartup()
    {
        string error;
        bool ok = GetPrinterStatus(out error);

        if (!ok)
        {
            ShowError(error);
            Debug.LogError("🖨️ Printer startup check failed: " + error);
        }
        else
        {
            Debug.Log("✅ Printer is ready");
        }
    }

    private bool GetPrinterStatus(out string errorMessage)
    {
        errorMessage = "";

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    try
    {
        string script = @"
$printerName = '" + printerName.Replace(@"'", @"''") + @"'
$printer = Get-WmiObject Win32_Printer | Where-Object { $_.Name -eq $printerName }

if ($null -eq $printer) {
    Write-Output 'NOT_FOUND'
    exit
}

if ($printer.WorkOffline) {
    Write-Output 'OFFLINE'
    exit
}

if ($printer.PrinterStatus -eq 3) {
    Write-Output 'IDLE'
    exit
}

if ($printer.PrinterStatus -eq 4) {
    Write-Output 'PRINTING'
    exit
}

if ($printer.PrinterStatus -eq 5) {
    Write-Output 'WARMUP'
    exit
}

if ($printer.DetectedErrorState -ne $null -and $printer.DetectedErrorState -ne 0) {
    Write-Output 'ERROR'
    exit
}

if ($printer.PaperOut) {
    Write-Output 'PAPER_OUT'
    exit
}

Write-Output 'READY'
";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-ExecutionPolicy Bypass -Command \"" + script + "\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using (var process = System.Diagnostics.Process.Start(psi))
        {
            string result = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            switch (result)
            {
                case "READY":
                case "IDLE":
                    return true;

                case "NOT_FOUND":
                    errorMessage = "プリンターが見つかりません\n\n設定されたプリンター名:\n" + printerName;
                    return false;

                case "OFFLINE":
                    errorMessage = "プリンターがオフラインです\n\n電源・USB・LANを確認してください";
                    return false;

                case "PAPER_OUT":
                    errorMessage = "用紙切れです\n\n用紙を補充してください";
                    return false;

                case "ERROR":
                    errorMessage = "プリンターエラーが発生しています\n\nプリンター本体を確認してください";
                    return false;

                default:
                    errorMessage = "プリンターの状態を確認できません\n\n状態: " + result;
                    return false;
            }
        }
    }
    catch (Exception e)
    {
        errorMessage = "プリンター確認エラー\n\n" + e.Message;
        return false;
    }
#else
        return true;
#endif
    }


    public void PrintFinalImage(Texture2D composedImage, string frameType = "portrait")
    {

        string error;
        if (!GetPrinterStatus(out error))
        {
            ShowError(error);
            return;
        }
        if (composedImage == null)
        {
            Debug.LogError("Print error: image is null");
            return;
        }

        currentFrameType = frameType.ToLower();
        imageToPrint = composedImage;
        printingComplete = false;
        StartCoroutine(PrintCoroutine(composedImage));
    }

    public bool IsPrintingComplete()
    {
        return printingComplete;
    }

    private IEnumerator PrintCoroutine(Texture2D source)
    {
        ShowPrintingPanel(true, false);
        UpdateStatus("画像を準備中...", 0.2f);

        Debug.Log($"📄 Frame Type: {currentFrameType} | Source Size: {source.width}x{source.height}");

        Texture2D processedImage = source;

        // Step 1: Rotate landscape images 90° clockwise
        if (currentFrameType == "landscape")
        {
            UpdateStatus("画像を回転中...", 0.3f);
            processedImage = RotateTexture90Clockwise(source);
            Debug.Log($"🔄 Rotated landscape {source.width}x{source.height} → {processedImage.width}x{processedImage.height}");
        }

        // Step 2: Fit to paper WITH CROP (maintains aspect ratio, crops to fill)
        UpdateStatus("用紙サイズに調整中...", 0.5f);
        Texture2D fitted = FitToPaperWithCrop(processedImage, PaperWidthPixels, PaperHeightPixels);

        if (processedImage != source)
            Destroy(processedImage);

        // Step 3: Save debug image
        UpdateStatus("印刷準備中...", 0.7f);
        string debugFolder = Path.Combine(Application.persistentDataPath, "PrintDebug");
        Directory.CreateDirectory(debugFolder);
        string debugPath = Path.Combine(debugFolder, $"PRINT_{currentFrameType}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        File.WriteAllBytes(debugPath, fitted.EncodeToPNG());
        Debug.Log($"📸 DEBUG IMAGE SAVED → {debugPath}");
        Debug.Log($"📐 Final print size: {fitted.width}x{fitted.height}px ({fitted.width / (float)dpi:F2}x{fitted.height / (float)dpi:F2} inches)");

        UpdateStatus("印刷中...", 0.8f);

        // Step 4: Print (color printing)
        bool success = PrintWithPowerShell(fitted);
        if (fitted != source) Destroy(fitted);

        if (success)
        {
            UpdateStatus("印刷完了！", 1f);
            yield return new WaitForSeconds(0.5f);
            ShowPrintingPanel(false, true);
            Debug.Log($"✅ Printing successful! Showing completion for {printingDoneDisplaySeconds} seconds");
            yield return new WaitForSeconds(printingDoneDisplaySeconds);
            ShowPrintingPanel(false, false);
            printingComplete = true;
        }
        else
        {
            ShowError($"印刷失敗\n\nプリンター名: \"{printerName}\"\n\nWindowsの「プリンターとスキャナー」で名前を確認してください");
            printingComplete = true;
        }
    }

    private void ShowPrintingPanel(bool showInProgress, bool showDone)
    {
        if (printingPanel != null)
            printingPanel.SetActive(showInProgress || showDone);

        if (inProgressPanel != null)
            inProgressPanel.SetActive(showInProgress);

        if (printingDonePanel != null)
            printingDonePanel.SetActive(showDone);

        if (errorPanel != null)
            errorPanel.SetActive(false);
    }

    private Texture2D RotateTexture90Clockwise(Texture2D source)
    {
        int width = source.width;
        int height = source.height;

        Texture2D rotated = new Texture2D(height, width, TextureFormat.RGB24, false);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                rotated.SetPixel(height - 1 - y, x, source.GetPixel(x, y));
            }
        }

        rotated.Apply();
        return rotated;
    }

    /// <summary>
    /// Fit to paper WITH CROP - maintains aspect ratio, crops edges to fill entire paper
    /// </summary>
    private Texture2D FitToPaperWithCrop(Texture2D source, int paperWidth, int paperHeight)
    {
        float sourceAspect = (float)source.width / source.height;
        float paperAspect = (float)paperWidth / paperHeight;

        int scaledWidth, scaledHeight;

        // Scale to COVER the paper (opposite of FIT)
        if (sourceAspect > paperAspect)
        {
            // Source is wider - scale to match HEIGHT, crop WIDTH
            scaledHeight = paperHeight;
            scaledWidth = Mathf.RoundToInt(paperHeight * sourceAspect);
        }
        else
        {
            // Source is taller - scale to match WIDTH, crop HEIGHT
            scaledWidth = paperWidth;
            scaledHeight = Mathf.RoundToInt(paperWidth / sourceAspect);
        }

        // Resize to scaled dimensions
        Texture2D scaled = ResizeTexture(source, scaledWidth, scaledHeight);

        // Create paper texture
        Texture2D paper = new Texture2D(paperWidth, paperHeight, TextureFormat.RGB24, false);

        // Calculate crop offsets (center crop)
        int xOffset = (scaledWidth - paperWidth) / 2;
        int yOffset = (scaledHeight - paperHeight) / 2;

        // Copy cropped portion
        Color[] croppedPixels = scaled.GetPixels(xOffset, yOffset, paperWidth, paperHeight);
        paper.SetPixels(croppedPixels);
        paper.Apply();

        if (scaled != source)
            Destroy(scaled);

        Debug.Log($"📦 Fit with CROP: {source.width}x{source.height} → scaled to {scaledWidth}x{scaledHeight} → cropped to {paperWidth}x{paperHeight}");
        Debug.Log($"   ✂️ Cropped {xOffset}px from sides, {yOffset}px from top/bottom (maintains aspect ratio, full bleed)");

        return paper;
    }

    private Texture2D ResizeTexture(Texture2D source, int newWidth, int newHeight)
    {
        if (source.width == newWidth && source.height == newHeight)
            return source;

        RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight);
        RenderTexture.active = rt;
        Graphics.Blit(source, rt);

        Texture2D result = new Texture2D(newWidth, newHeight, TextureFormat.RGB24, false);
        result.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
        result.Apply();

        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }

    private bool PrintWithPowerShell(Texture2D img)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        try
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"print_{Guid.NewGuid()}.png");
            File.WriteAllBytes(tempFile, img.EncodeToPNG());

            string script = @"
$imgPath = '" + tempFile.Replace(@"\", @"\\") + @"'
$printer = '" + printerName.Replace(@"'", @"''") + @"'

Add-Type -AssemblyName System.Drawing
$image = [System.Drawing.Image]::FromFile($imgPath)

$pd = New-Object System.Drawing.Printing.PrintDocument
$pd.PrinterSettings.PrinterName = $printer
$pd.DefaultPageSettings.Margins = New-Object System.Drawing.Printing.Margins(0,0,0,0)

$pd.add_PrintPage({
    param($sender, $e)
    $e.Graphics.DrawImage($image, 0, 0, $image.Width, $image.Height)
    $e.HasMorePages = $false
})

$pd.Print()
$image.Dispose()
Remove-Item $imgPath -Force
";

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            using (var process = System.Diagnostics.Process.Start(startInfo))
            {
                process.WaitForExit(20000);
                return process.ExitCode == 0;
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Print failed: " + e.Message);
            return false;
        }
#else
        Debug.Log($"✅ Print simulated (Editor) - Would print {img.width}x{img.height}px image");
        return true;
#endif
    }

    private void UpdateStatus(string text, float progress)
    {
        if (statusText) statusText.text = text;
    }

    private void ShowError(string msg)
    {
        if (errorPanel) errorPanel.SetActive(true);
        if (errorText) errorText.text = msg;
        if (statusText) statusText.text = "印刷エラー";

        if (printingPanel) printingPanel.SetActive(false);
        if (inProgressPanel) inProgressPanel.SetActive(false);
        if (printingDonePanel) printingDonePanel.SetActive(false);

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    if (printerMonitorRoutine == null)
        printerMonitorRoutine = StartCoroutine(MonitorPrinterStatus());
#endif
    }

    private IEnumerator MonitorPrinterStatus()
    {
        Debug.Log("🔄 Started monitoring printer status...");

        while (true)
        {
            string error;
            bool ready = GetPrinterStatus(out error);

            if (ready)
            {
                Debug.Log("✅ Printer recovered");

                if (errorPanel) errorPanel.SetActive(false);
                if (statusText) statusText.text = "";

                printerMonitorRoutine = null;
                yield break;
            }

            yield return new WaitForSeconds(printerRecheckInterval);
        }
    }


    public void RetryLastPrint()
    {
        if (imageToPrint != null)
            StartCoroutine(PrintCoroutine(imageToPrint));
    }
}