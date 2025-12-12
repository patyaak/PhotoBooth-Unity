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
    public string printerName = "Canon MF3010";

    [Header("Paper Size (inches)")]
    public float paperWidthInches = 4f;
    public float paperHeightInches = 6f;
    public int dpi = 360; // Epson standard DPI

    private int PaperWidthPixels => Mathf.RoundToInt(paperWidthInches * dpi);
    private int PaperHeightPixels => Mathf.RoundToInt(paperHeightInches * dpi);

    [Header("UI")]
    public GameObject printingPanel;
    public GameObject errorPanel;
    public TMP_Text statusText;
    public TMP_Text errorText;

    private Texture2D imageToPrint;
    private string currentFrameType = "portrait";

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
        if (errorPanel) errorPanel.SetActive(false);
    }

    /// <summary>
    /// Main entry point - called from PhotoShootingManager
    /// </summary>
    public void PrintFinalImage(Texture2D composedImage, string frameType = "portrait")
    {
        if (composedImage == null)
        {
            Debug.LogError("Print error: image is null");
            return;
        }

        currentFrameType = frameType.ToLower();
        imageToPrint = composedImage;
        StartCoroutine(PrintCoroutine(composedImage));
    }

    private IEnumerator PrintCoroutine(Texture2D source)
    {
        ShowPanel(true);
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

        // Step 2: Fit to 4x6 paper while maintaining aspect ratio (shows entire image)
        UpdateStatus("用紙サイズに調整中...", 0.5f);
        Texture2D fitted = FitToPaperWithBorders(processedImage, PaperWidthPixels, PaperHeightPixels);

        if (processedImage != source)
            Destroy(processedImage);

        // Step 3: Convert to monochrome for thermal printer
        UpdateStatus("白黒変換中...", 0.6f);
        Texture2D bw = ConvertToMonochrome(fitted);
        if (fitted != source) Destroy(fitted);

        // Step 4: Save debug image
        string debugFolder = Path.Combine(Application.persistentDataPath, "PrintDebug");
        Directory.CreateDirectory(debugFolder);
        string debugPath = Path.Combine(debugFolder, $"PRINT_{currentFrameType}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        File.WriteAllBytes(debugPath, bw.EncodeToPNG());
        Debug.Log($"📸 DEBUG IMAGE SAVED → {debugPath}");
        Debug.Log($"📐 Final print size: {bw.width}x{bw.height}px ({bw.width / (float)dpi:F2}x{bw.height / (float)dpi:F2} inches)");

        UpdateStatus("印刷中...", 0.8f);

        // Step 5: Print
        bool success = PrintWithPowerShell(bw);
        Destroy(bw);

        if (success)
        {
            UpdateStatus("印刷完了！", 1f);
            yield return new WaitForSeconds(2f);
            ShowPanel(false);
        }
        else
        {
            ShowError($"印刷失敗\n\nプリンター名: \"{printerName}\"\n\nWindowsの「プリンターとスキャナー」で名前を確認してください");
        }
    }

    /// <summary>
    /// Rotate texture 90 degrees clockwise (for landscape frames)
    /// Landscape 1920x1080 becomes 1080x1920 after rotation
    /// </summary>
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
    /// Fit entire image to paper size while maintaining aspect ratio
    /// Adds white borders (letterbox/pillarbox) as needed to fill paper
    /// Shows the COMPLETE image without cropping
    /// </summary>
    private Texture2D FitToPaperWithBorders(Texture2D source, int paperWidth, int paperHeight)
    {
        float sourceAspect = (float)source.width / source.height;
        float paperAspect = (float)paperWidth / paperHeight;

        int targetWidth, targetHeight;

        // Calculate scaled dimensions that fit WITHIN paper (opposite of crop-to-fill)
        if (sourceAspect > paperAspect)
        {
            // Image is wider relative to paper - fit to width, add bars on top/bottom
            targetWidth = paperWidth;
            targetHeight = Mathf.RoundToInt(paperWidth / sourceAspect);
        }
        else
        {
            // Image is taller relative to paper - fit to height, add bars on left/right
            targetHeight = paperHeight;
            targetWidth = Mathf.RoundToInt(paperHeight * sourceAspect);
        }

        // Resize image to target dimensions
        Texture2D resized = ResizeTexture(source, targetWidth, targetHeight);

        // Create final paper-sized texture with white background
        Texture2D paper = new Texture2D(paperWidth, paperHeight, TextureFormat.RGB24, false);

        // Fill entire paper with white
        Color[] whitePixels = new Color[paperWidth * paperHeight];
        for (int i = 0; i < whitePixels.Length; i++)
            whitePixels[i] = Color.white;
        paper.SetPixels(whitePixels);

        // Center the resized image on the paper
        int xOffset = (paperWidth - targetWidth) / 2;
        int yOffset = (paperHeight - targetHeight) / 2;

        Color[] imagePixels = resized.GetPixels();
        paper.SetPixels(xOffset, yOffset, targetWidth, targetHeight, imagePixels);
        paper.Apply();

        if (resized != source)
            Destroy(resized);

        Debug.Log($"📦 Fitted {source.width}x{source.height} into paper {paperWidth}x{paperHeight}");
        Debug.Log($"   → Content size: {targetWidth}x{targetHeight} (centered with white borders)");

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

    private Texture2D ConvertToMonochrome(Texture2D source)
    {
        Texture2D mono = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
        for (int y = 0; y < source.height; y++)
        {
            for (int x = 0; x < source.width; x++)
            {
                float gray = source.GetPixel(x, y).grayscale;
                mono.SetPixel(x, y, gray > 0.5f ? Color.white : Color.black);
            }
        }
        mono.Apply();
        return mono;
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

    private void ShowPanel(bool show)
    {
        if (printingPanel) printingPanel.SetActive(show);
        if (errorPanel) errorPanel.SetActive(!show);
    }

    private void ShowError(string msg)
    {
        if (errorPanel) errorPanel.SetActive(true);
        if (errorText) errorText.text = msg;
        if (statusText) statusText.text = "印刷エラー";
    }

    public void RetryLastPrint()
    {
        if (imageToPrint != null)
            StartCoroutine(PrintCoroutine(imageToPrint));
    }
}