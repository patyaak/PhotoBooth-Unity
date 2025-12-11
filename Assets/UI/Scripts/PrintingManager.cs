using System;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class PrintingManager : MonoBehaviour
{
    public static PrintingManager Instance;

    [Header("Printer Settings")]
    public string printerName = "Canon MF3010";           // ←←← CHANGE THIS TO YOUR EXACT PRINTER NAME IN WINDOWS
    public bool useExact1Inch = false;              // true = real 25.4×25.4mm (203×203px), false = bigger square (384×384px)

    private int printWidth => useExact1Inch ? 203 : 384;
    private int printHeight => useExact1Inch ? 203 : 384;

    [Header("UI")]
    public GameObject printingPanel;
    public GameObject errorPanel;
    public TMP_Text statusText;
    public TMP_Text errorText;
   

    private Texture2D imageToPrint;

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

    // Called from PhotoShootingManager
    public void PrintFinalImage(Texture2D composedImage)
    {
        if (composedImage == null)
        {
            Debug.LogError("Print error: image is null");
            return;
        }

        imageToPrint = composedImage;
        StartCoroutine(PrintCoroutine(composedImage));
    }

    private IEnumerator PrintCoroutine(Texture2D source)
    {
        ShowPanel(true);
        UpdateStatus("画像を準備中...", 0.3f);

        // 1. Resize
        Texture2D resized = ResizeTexture(source, printWidth, printHeight);

        // 2. Convert to pure black & white (thermal printers need this!)
        Texture2D bw = ConvertToMonochrome(resized);
        if (resized != source) Destroy(resized);

        // 3. SAVE DEBUG IMAGE — open this folder to see exactly what will be printed
        string debugFolder = Path.Combine(Application.persistentDataPath, "PrintDebug");
        Directory.CreateDirectory(debugFolder);
        string debugPath = Path.Combine(debugFolder, $"PRINT_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        File.WriteAllBytes(debugPath, bw.EncodeToPNG());
        Debug.Log($"DEBUG IMAGE SAVED → {debugPath}");

        UpdateStatus("印刷中...", 0.7f);

        // 4. Print via Windows GDI + PowerShell (works on ALL thermal printers)
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
        Debug.Log("Print simulated (running in Editor)");
        return true;
#endif
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

    private Texture2D ResizeTexture(Texture2D source, int newWidth, int newHeight)
    {
        if (source.width == newWidth && source.height == newHeight) return source;

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

    private void RetryLastPrint()
    {
        if (imageToPrint != null)
            StartCoroutine(PrintCoroutine(imageToPrint));
    }
}