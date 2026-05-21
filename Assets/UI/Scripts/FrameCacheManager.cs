using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public static class FrameCacheManager
{
    private static readonly string cacheDir = Path.Combine(Application.persistentDataPath, "FrameCache");

  
    private static void EnsureCacheDir()
    {
        if (!Directory.Exists(cacheDir))
            Directory.CreateDirectory(cacheDir);
    }

    private static string HashName(string input)
    {
        using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }

    private static string GetCleanExtension(string url)
    {
        if (string.IsNullOrEmpty(url)) return ".png";
        
        // Strip query parameters and fragments
        int queryIdx = url.IndexOf('?');
        if (queryIdx > 0) url = url.Substring(0, queryIdx);
        
        int fragmentIdx = url.IndexOf('#');
        if (fragmentIdx > 0) url = url.Substring(0, fragmentIdx);

        string ext = Path.GetExtension(url);
        return string.IsNullOrWhiteSpace(ext) ? ".png" : ext;
    }

    private static string GetJSONPath(string category, string boothID) =>
        Path.Combine(cacheDir, $"frames_{boothID}_{category}.json");

    public static bool HasCachedData(string category, string boothID) =>
        File.Exists(GetJSONPath(category, boothID));

    public static void SaveJSON(string json, string category, string boothID)
    {
        try
        {
            EnsureCacheDir();
            File.WriteAllText(GetJSONPath(category, boothID), json);
            Debug.Log($"✅ Saved JSON cache for '{category}' (Booth: {boothID})");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"❌ SaveJSON Error: {ex.Message}");
        }
    }

    public static string LoadCachedJSON(string category, string boothID)
    {
        try
        {
            string path = GetJSONPath(category, boothID);
            if (File.Exists(path))
                return File.ReadAllText(path);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"⚠️ LoadCachedJSON Error: {ex.Message}");
        }
        return null;
    }

 
    public static string GetCachedTexturePath(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        string ext = GetCleanExtension(url);
        string fileName = $"{HashName(url)}{ext}";
        return Path.Combine(cacheDir, fileName);
    }


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


    public static IEnumerator DownloadAndCacheTexture(string url, System.Action<Texture2D> onDone)
    {
        if (string.IsNullOrEmpty(url))
        {
            onDone?.Invoke(null);
            yield break;
        }

        EnsureCacheDir();

        string ext = GetCleanExtension(url);
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
            req.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            req.SetRequestHeader("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            
            // Use API.BaseURL as Referer
            string referer = API.BaseURL;
            if (!string.IsNullOrEmpty(referer))
            {
               req.SetRequestHeader("Referer", referer);
            }
            
            req.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
            req.SetRequestHeader("Cache-Control", "max-age=0");
            
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"⚠️ Download failed: {req.error} ({url})");
                if (req.responseCode > 0) Debug.LogWarning($"   Code: {req.responseCode}");
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
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"⚠️ Failed to save cached texture: {ex.Message}");
            }
        }
    }

    public static IEnumerator LoadCachedTexture(string url, System.Action<Texture2D> onDone)
    {
        if (string.IsNullOrEmpty(url))
        {
            onDone?.Invoke(null);
            yield break;
        }

        EnsureCacheDir();

        string ext = GetCleanExtension(url);
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
