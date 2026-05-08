using System;
using System.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Handles reprint requests received via WebSocket.
/// Downloads the target image from the API and sends it to the PrintingManager.
/// </summary>
public class ReprintReceiver : MonoBehaviour
{
    public static ReprintReceiver Instance;

    [Header("Configuration")]
    [Tooltip("This should match the booth_id/deviceId from the backend")]
    public string deviceId;

    [Serializable]
    public class ReprintData
    {
        public string booth_id;
        public string order_id;
        public string photo_id;
        public string frame_type; // Optional from backend
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // Optionally set deviceId from PlayerPrefs if not set in inspector
            if (string.IsNullOrEmpty(deviceId))
            {
                deviceId = PlayerPrefs.GetString("booth_id", "");
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Processes the incoming JSON from WebSocket.
    /// Handles both raw Pusher objects and stringified data.
    /// </summary>
    public void ReceiveReprintJson(string json)
    {
        Debug.Log("[ReprintReceiver] Received Message: " + json);

        try
        {
            JObject j = JObject.Parse(json);
            string evt = (string)(j["event"] ?? j["@event"]);

            if (evt != "reprint-requested")
            {
                return; // Ignore other events
            }

            JToken dataToken = j["data"];
            if (dataToken == null) return;

            ReprintData data;

            // If data is a string (escaped JSON), parse it again
            if (dataToken.Type == JTokenType.String)
            {
                data = JsonConvert.DeserializeObject<ReprintData>(dataToken.ToString());
            }
            else
            {
                data = dataToken.ToObject<ReprintData>();
            }

            if (data == null)
            {
                Debug.LogError("[ReprintReceiver] Failed to parse reprint data");
                return;
            }

            // Verify if this job is for this specific booth
            // Backend sends booth_id or booth_key
            string currentBoothId = PlayerPrefs.GetString("booth_id", "");
            if (data.booth_id != currentBoothId && data.booth_id != deviceId)
            {
                Debug.Log($"[ReprintReceiver] Ignored job for another device. Target: {data.booth_id}, Local: {currentBoothId}");
                return;
            }

            Debug.Log($"[ReprintReceiver] Processing reprint for Order: {data.order_id}, Photo: {data.photo_id}");
            StartCoroutine(DownloadAndPrint(data));
        }
        catch (Exception ex)
        {
            Debug.LogError("[ReprintReceiver] Error parsing JSON: " + ex.Message);
        }
    }

    private IEnumerator DownloadAndPrint(ReprintData data)
    {
        // Construct the download URL based on API settings
        // Format: /api/sales/photos/{photoId}/download/{orderId}
        string baseUrl = API.BaseURL.TrimEnd('/');
        string url = $"{baseUrl}/api/sales/photos/{data.photo_id}/download/{data.order_id}";

        Debug.Log("[ReprintReceiver] Downloading image from: " + url);

        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
        {
            // Add authorization if required (check if session/token is needed)
            // For now, simple GET as requested
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[ReprintReceiver] Download failed: {req.error} (URL: {url})");
                yield break;
            }

            Texture2D tex = DownloadHandlerTexture.GetContent(req);

            if (tex == null)
            {
                Debug.LogError("[ReprintReceiver] Downloaded texture is null");
                yield break;
            }

            if (PrintingManager.Instance == null)
            {
                Debug.LogError("[ReprintReceiver] PrintingManager.Instance not found!");
                yield break;
            }

            Debug.Log("[ReprintReceiver] Image downloaded. Sending to PrintingManager...");

            // Pass the texture and frame type (if any) to the printing manager
            // If frame_type is missing, PrintingManager will fallback to orientation detection
            PrintingManager.Instance.PrintFinalImage(tex, data.frame_type ?? "");
        }
    }
}
