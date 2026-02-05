using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public static class FrameCacheManager
{
    private static readonly string cacheDir = Path.Combine(Application.persistentDataPath, "FrameCache");

    // ---------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------
    private static void EnsureCacheDir()
    {
        if (!Directory.Exists(cacheDir))
            Directory.CreateDirectory(cacheDir);
    }

    private static string HashName(string input)
    {
        return input.GetHashCode().ToString();
    }

    // ---------------------------------------------------------
    // JSON Caching
    // ---------------------------------------------------------
    private static string GetJSONPath(string category) =>
        Path.Combine(cacheDir, $"frames_{category}.json");

    public static bool HasCachedData(string category) =>
        File.Exists(GetJSONPath(category));

    public static void SaveJSON(string json, string category)
    {
        try
        {
            EnsureCacheDir();
            File.WriteAllText(GetJSONPath(category), json);
            Debug.Log($"✅ Saved JSON cache for '{category}'");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"❌ SaveJSON Error: {ex.Message}");
        }
    }

    public static string LoadCachedJSON(string category)
    {
        try
        {
            string path = GetJSONPath(category);
            if (File.Exists(path))
                return File.ReadAllText(path);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"⚠️ LoadCachedJSON Error: {ex.Message}");
        }
        return null;
    }

    // ---------------------------------------------------------
    // General Texture Saving (Manual Key)
    // ---------------------------------------------------------
    public static void SaveTexture(Texture2D tex, string key)
    {
        if (tex == null || string.IsNullOrEmpty(key)) return;

        try
        {
            EnsureCacheDir();
            string file = Path.Combine(cacheDir, $"{HashName(key)}.png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
        }
        catch { }
    }

    // ---------------------------------------------------------
    // Texture Caching From URL
    // ---------------------------------------------------------
    public static IEnumerator DownloadAndCacheTexture(string url, System.Action<Texture2D> onDone)
    {
        if (string.IsNullOrEmpty(url))
        {
            onDone?.Invoke(null);
            yield break;
        }

        EnsureCacheDir();

        string ext = Path.GetExtension(url);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";

        string fileName = $"{HashName(url)}{ext}";
        string filePath = Path.Combine(cacheDir, fileName);

        // LOAD FROM CACHE (Using UnityWebRequest for non-blocking load)
        if (File.Exists(filePath))
        {
            string localUrl = "file://" + filePath;
            using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(localUrl))
            {
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    Texture2D tex = DownloadHandlerTexture.GetContent(req);
                    if (tex != null)
                    {
                         // Debug.Log($"[FrameCacheManager] Loaded cached texture: {filePath}");
                        onDone?.Invoke(tex);
                        yield break;
                    }
                }
                // If local load failed (corrupt?), fall through to download
                Debug.LogWarning($"⚠️ Failed reading cached texture (redownloading): {filePath}");
            }
        }

        // DOWNLOAD
        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
        {
            // Fix for 403: Mimic a real browser (Chrome on Windows)
            // Some servers require Referer/Origin or specific Accept headers
            req.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            req.SetRequestHeader("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            req.SetRequestHeader("Referer", "https://photo-stg-api.chvps3.aozora-okinawa.com/");
            // req.SetRequestHeader("Origin", "https://photo-stg-api.chvps3.aozora-okinawa.com"); // Sometimes needed, sometimes breaks it. Let's try Referer first.
            req.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
            req.SetRequestHeader("Cache-Control", "max-age=0");
            
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"⚠️ Download failed: {req.error} ({url})");
                onDone?.Invoke(null);
                yield break;
            }

            Texture2D downloaded = DownloadHandlerTexture.GetContent(req);
            onDone?.Invoke(downloaded);

            // SAVE TO CACHE (Async file write not easily available, but fast enough for binary)
            try
            {
                EnsureCacheDir();
                byte[] bytes = downloaded.EncodeToPNG();
                File.WriteAllBytes(filePath, bytes);
                // Debug.Log($"[FrameCacheManager] Cached texture → {filePath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"⚠️ Failed to save cached texture: {ex.Message}");
            }
        }
    }

    // ---------------------------------------------------------
    // Load Cached Texture (ONLY from local)
    // ---------------------------------------------------------
    public static IEnumerator LoadCachedTexture(string url, System.Action<Texture2D> onDone)
    {
        if (string.IsNullOrEmpty(url))
        {
            onDone?.Invoke(null);
            yield break;
        }

        EnsureCacheDir();

        string ext = Path.GetExtension(url);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";

        string fileName = $"{HashName(url)}{ext}";
        string filePath = Path.Combine(cacheDir, fileName);

        Debug.Log($"[FrameCacheManager] LoadCachedTexture → {filePath} Exists={File.Exists(filePath)}");

        if (!File.Exists(filePath))
        {
            onDone?.Invoke(null);
            yield break;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            onDone?.Invoke(tex);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"⚠️ LoadCachedTexture error: {ex.Message}");
            onDone?.Invoke(null);
        }
    }

    // ---------------------------------------------------------
    // Clear Cache
    // ---------------------------------------------------------
    public static void ClearCache()
    {
        try
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
                Debug.Log("🧹 Frame cache fully cleared.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"❌ ClearCache Error: {ex.Message}");
        }
    }
}
