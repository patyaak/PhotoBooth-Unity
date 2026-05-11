using System;
using System.Text;
using NativeWebSocket;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Maintains a persistent WebSocket connection to the backend for background events.
/// Implements Pusher-compatible subscription protocol.
/// </summary>
public class WebSocketManager : MonoBehaviour
{
    public static WebSocketManager Instance;

    [Header("Connection")]
    public bool connectOnStart = true;
    public bool secureConnection = true;

    private WebSocket ws;
    private bool isSubscribed = false;

    // Pusher protocol classes
    [Serializable]
    private class PusherEvent
    {
        [JsonProperty("event")] public string Event;
        public object data;
    }

    [Serializable]
    private class SubscribeData
    {
        public string channel;
    }

    private void Awake()
    {
        Debug.LogError("[WS] CRITICAL TEST: WebSocketManager script is loading!");
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        if (connectOnStart)
        {
            Connect();
        }
    }

    private void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
    }

    public async void Connect()
    {
        if (ws != null && (ws.State == WebSocketState.Open || ws.State == WebSocketState.Connecting))
        {
            return;
        }

        string appKey = LoginManager.Instance != null ? LoginManager.Instance.boothKey : "boothkey123";
        string deviceID = SystemInfo.deviceUniqueIdentifier;

        string url = API.GetWebSocketURL(secureConnection, appKey);
        Debug.Log($"[WS] Connecting with App Key: {appKey} | Device ID: {deviceID}");
        Debug.Log("[WS] Connecting to: " + url);

        ws = new WebSocket(url);

        ws.OnOpen += () =>
        {
            Debug.Log("[WS] Connected");
            SubscribeToReprintChannel(deviceID);
        };

        ws.OnMessage += (bytes) =>
        {
            string json = Encoding.UTF8.GetString(bytes);
            
            // Dispatch to ReprintReceiver
            if (ReprintReceiver.Instance != null)
            {
                ReprintReceiver.Instance.ReceiveReprintJson(json);
            }
        };

        ws.OnError += (e) =>
        {
            Debug.LogError("[WS] Error: " + e);
            isSubscribed = false;
        };

        ws.OnClose += (e) =>
        {
            Debug.Log("[WS] Closed: " + e);
            isSubscribed = false;
            // Optionally implement auto-reconnect here
        };

        try
        {
            await ws.Connect();
        }
        catch (Exception ex)
        {
            Debug.LogError("[WS] Connection Exception: " + ex.Message);
        }
    }

    private async void SubscribeToReprintChannel(string deviceID)
    {
        if (ws == null || ws.State != WebSocketState.Open) return;

        string channelName = $"reprint.{deviceID}";
        
        var subEvent = new PusherEvent
        {
            Event = "pusher:subscribe",
            data = new SubscribeData { channel = channelName }
        };

        string json = JsonConvert.SerializeObject(subEvent);
        Debug.Log($"[WS] Attempting subscription to channel: {channelName}");

        try
        {
            await ws.SendText(json);
            isSubscribed = true;
            Debug.Log($"[WS] Subscription request sent for: {channelName}");
        }
        catch (Exception ex)
        {
            Debug.LogError("[WS] Subscription failed: " + ex.Message);
        }
    }

    private async void OnApplicationQuit()
    {
        if (ws != null)
        {
            await ws.Close();
        }
    }
}
