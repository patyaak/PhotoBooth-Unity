using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Enhanced web request wrapper that monitors server connectivity
/// Use this instead of direct UnityWebRequest to automatically handle server failures
/// </summary>
public static class ServerAwareWebRequest
{
    /// <summary>
    /// GET request with automatic server monitoring
    /// </summary>
    public static IEnumerator Get(string url, Action<UnityWebRequest> onComplete, int timeout = 30)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = timeout;

            yield return request.SendWebRequest();

            // Check for connection errors
            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                HandleRequestFailure(url, request.error, request.responseCode);
            }

            onComplete?.Invoke(request);
        }
    }

    /// <summary>
    /// POST request with automatic server monitoring
    /// </summary>
    public static IEnumerator Post(string url, string jsonData, Action<UnityWebRequest> onComplete, int timeout = 30)
    {
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = timeout;

            yield return request.SendWebRequest();

            // Check for connection errors
            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                HandleRequestFailure(url, request.error, request.responseCode);
            }

            onComplete?.Invoke(request);
        }
    }

    /// <summary>
    /// POST request with form data
    /// </summary>
    public static IEnumerator PostForm(string url, WWWForm formData, Action<UnityWebRequest> onComplete, int timeout = 30)
    {
        using (UnityWebRequest request = UnityWebRequest.Post(url, formData))
        {
            request.timeout = timeout;

            yield return request.SendWebRequest();

            // Check for connection errors
            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                HandleRequestFailure(url, request.error, request.responseCode);
            }

            onComplete?.Invoke(request);
        }
    }

    /// <summary>
    /// Handle request failure and notify ServerConnectivityManager
    /// </summary>
    private static void HandleRequestFailure(string url, string error, long responseCode)
    {
        Debug.LogWarning($"⚠️ Request failed: {url} - {error} (Code: {responseCode})");

        // Check if it's a server connectivity issue (not just a 4xx error)
        bool isConnectivityIssue =
            responseCode == 0 || // No response
            responseCode >= 500 || // Server errors
            error.Contains("Cannot resolve") ||
            error.Contains("Failed to connect") ||
            error.Contains("Connection refused") ||
            error.Contains("timeout");

        if (isConnectivityIssue && ServerConnectivityManager.Instance != null)
        {
            ServerConnectivityManager.Instance.OnAPIRequestFailed(url, error);
        }
    }

    /// <summary>
    /// Check if request was successful
    /// </summary>
    public static bool IsSuccess(UnityWebRequest request)
    {
        return request.result == UnityWebRequest.Result.Success;
    }

    /// <summary>
    /// Check if error is a connectivity issue
    /// </summary>
    public static bool IsConnectivityError(UnityWebRequest request)
    {
        if (request.result != UnityWebRequest.Result.Success)
        {
            return request.responseCode == 0 ||
                   request.responseCode >= 500 ||
                   request.error.Contains("Cannot resolve") ||
                   request.error.Contains("Failed to connect") ||
                   request.error.Contains("Connection refused") ||
                   request.error.Contains("timeout");
        }
        return false;
    }
}