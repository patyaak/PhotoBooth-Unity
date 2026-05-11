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
        Debug.LogError("[ReprintReceiver] CRITICAL TEST: Script is loading!");
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // Optionally set deviceId from PlayerPrefs if not set in inspector
            if (string.IsNullOrEmpty(deviceId))
            {
                deviceId = SystemInfo.deviceUniqueIdentifier;
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
                Debug.Log($"[ReprintReceiver] Ignored event type: {evt}");
                return; // Ignore other events
            }

            Debug.Log("[ReprintReceiver] Event 'reprint-requested' detected. Parsing data...");
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
                Debug.LogWarning($"[ReprintReceiver] VALIDATION FAILED. Job for: {data.booth_id}. Local IDs: Booth={currentBoothId}, Hardware={deviceId}");
                return;
            }

            Debug.Log($"[ReprintReceiver] VALIDATION SUCCESS. Processing reprint for Order: {data.order_id}, Photo: {data.photo_id}");
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

            // Show Printing UI (using references from PhotoShootingManager)
            var psm = PhotoShootingManager.Instance;
            if (psm != null)
            {
                if (psm.printingPanel != null) psm.printingPanel.SetActive(true);
                if (psm.printingInProgress != null) psm.printingInProgress.SetActive(true);
                if (psm.printingDone != null) psm.printingDone.SetActive(false);
            }

            // Pass the texture and frame type (if any) to the printing manager
            PrintingManager.Instance.PrintFinalImage(tex, data.frame_type ?? "");

            // Wait for the print to complete (consistent with PhotoShootingManager logic)
            yield return new WaitForSeconds(2.0f);
            while (PrintingManager.Instance.IsPrinting)
            {
                yield return new WaitForSeconds(0.5f);
            }

            // Update UI to "Done"
            if (psm != null)
            {
                if (psm.printingInProgress != null) psm.printingInProgress.SetActive(false);
                if (psm.printingDone != null)
                {
                    psm.printingDone.SetActive(true);
                    AudioManager.Instance?.PlayPrintingDone();
                }
            }

            // Wait for user to see "Done" then hide the panel
            yield return new WaitForSeconds(4.0f);
            if (psm != null && psm.printingPanel != null)
            {
                psm.printingPanel.SetActive(false);
            }
            
            Debug.Log("[ReprintReceiver] Reprint printing workflow completed.");
        }
    }
}
