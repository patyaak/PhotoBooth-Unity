using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Wrapper for UnityWebRequest that automatically logs all API calls
/// </summary>
public static class LoggedWebRequest
{
    /// <summary>
    /// GET request with automatic logging
    /// </summary>
    /// <param name="url">API endpoint URL</param>
    /// <param name="onComplete">Callback with the completed request</param>
    public static IEnumerator Get(string url, Action<UnityWebRequest> onComplete)
    {
        long startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            long endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long latency = endTime - startTime;

            // Automatically log the API call
            LoggingManager.Instance?.LogConnection(
                connectionType: Application.internetReachability.ToString(),
                eventType: "api_call",
                endpoint: url,
                responseCode: (int)request.responseCode,
                latencyMs: latency,
                success: request.result == UnityWebRequest.Result.Success,
                errorMessage: request.error
            );

            // Return the request to caller
            onComplete?.Invoke(request);
        }
    }

    /// <summary>
    /// POST request with automatic logging
    /// </summary>
    /// <param name="url">API endpoint URL</param>
    /// <param name="jsonData">JSON payload as string</param>
    /// <param name="onComplete">Callback with the completed request</param>
    public static IEnumerator Post(string url, string jsonData, Action<UnityWebRequest> onComplete)
    {
        long startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            long endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long latency = endTime - startTime;

            // Automatically log the API call
            LoggingManager.Instance?.LogConnection(
                connectionType: Application.internetReachability.ToString(),
                eventType: "api_call",
                endpoint: url,
                responseCode: (int)request.responseCode,
                latencyMs: latency,
                success: request.result == UnityWebRequest.Result.Success,
                errorMessage: request.error
            );

            // Return the request to caller
            onComplete?.Invoke(request);
        }
    }

    /// <summary>
    /// POST request with custom headers (for file uploads, etc.)
    /// </summary>
    public static IEnumerator PostWithHeaders(string url, byte[] bodyData,
        System.Collections.Generic.Dictionary<string, string> headers,
        Action<UnityWebRequest> onComplete)
    {
        long startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyData);
            request.downloadHandler = new DownloadHandlerBuffer();

            // Add custom headers
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.SetRequestHeader(header.Key, header.Value);
                }
            }

            yield return request.SendWebRequest();

            long endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long latency = endTime - startTime;

            // Log the API call
            LoggingManager.Instance?.LogConnection(
                connectionType: Application.internetReachability.ToString(),
                eventType: "api_call",
                endpoint: url,
                responseCode: (int)request.responseCode,
                latencyMs: latency,
                success: request.result == UnityWebRequest.Result.Success,
                errorMessage: request.error
            );

            onComplete?.Invoke(request);
        }
    }
}