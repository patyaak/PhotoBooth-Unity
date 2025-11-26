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

        // LOAD FROM CACHE
        if (File.Exists(filePath))
        {
            Debug.Log($"[FrameCacheManager] Loaded cached texture: {filePath}");

            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(bytes);
                onDone?.Invoke(tex);
                yield break;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"⚠️ Failed reading cached texture: {ex.Message}");
            }
        }

        // DOWNLOAD
        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"⚠️ Download failed: {req.error}");
                onDone?.Invoke(null);
                yield break;
            }

            Texture2D downloaded = DownloadHandlerTexture.GetContent(req);
            onDone?.Invoke(downloaded);

            // SAVE TO CACHE
            try
            {
                EnsureCacheDir();
                byte[] bytes = downloaded.EncodeToPNG();
                File.WriteAllBytes(filePath, bytes);

                Debug.Log($"[FrameCacheManager] Cached texture → {filePath} ({bytes.Length} bytes)");
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
