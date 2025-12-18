using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Helper class for making API requests that automatically notify ServerConnectivityManager
/// of connectivity issues
/// </summary>
public static class ServerAwareWebRequest
{
    /// <summary>
    /// Make a POST request with automatic server connectivity handling
    /// </summary>
    public static IEnumerator Post(string url, string jsonBody, Action<UnityWebRequest> callback)
    {
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 10;

            yield return request.SendWebRequest();

            // Check for connectivity errors and notify ServerConnectivityManager
            if (IsConnectivityError(request))
            {
                Debug.LogError($"❌ Connection error detected: {url}");
                if (ServerConnectivityManager.Instance != null)
                {
                    ServerConnectivityManager.Instance.OnAPIRequestFailed(url, request.error);
                }
            }

            // Always invoke callback so caller can handle the response
            callback?.Invoke(request);
        }
    }

    /// <summary>
    /// Make a GET request with automatic server connectivity handling
    /// </summary>
    public static IEnumerator Get(string url, Action<UnityWebRequest> callback)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = 10;
            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            // Check for connectivity errors and notify ServerConnectivityManager
            if (IsConnectivityError(request))
            {
                Debug.LogError($"❌ Connection error detected: {url}");
                if (ServerConnectivityManager.Instance != null)
                {
                    ServerConnectivityManager.Instance.OnAPIRequestFailed(url, request.error);
                }
            }

            // Always invoke callback so caller can handle the response
            callback?.Invoke(request);
        }
    }

    /// <summary>
    /// Check if the request failed due to connectivity issues
    /// </summary>
    public static bool IsConnectivityError(UnityWebRequest request)
    {
        return request.result == UnityWebRequest.Result.ConnectionError ||
               request.result == UnityWebRequest.Result.DataProcessingError ||
               request.result == UnityWebRequest.Result.ProtocolError; // 404, 500
    }



    /// <summary>
    /// Check if the request was successful
    /// </summary>
    public static bool IsSuccess(UnityWebRequest request)
    {
        return request.result == UnityWebRequest.Result.Success;
    }
}