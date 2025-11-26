using System;
using System.IO;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class DeviceRegistration : MonoBehaviour
{
    [Header("API URL")]
    public string apiURL = "http://photo-stg-api.chvps3.aozora-okinawa.com/api/photobooth/device";

    private string jsonPath;

    public TextMeshProUGUI deviceIdText;
    public TextMeshProUGUI boothIdText;

    public GameObject wifi;

    void Awake()
    {
        jsonPath = Path.Combine(Application.persistentDataPath, "device.json");
    }

    void Start()
    {
        LoadLocalBoothID();     // Load booth data if already saved locally
        StartCoroutine(RegisterFlow());

        if (deviceIdText != null)
            deviceIdText.text = SystemInfo.deviceUniqueIdentifier;

        if (wifi != null)
            wifi.SetActive(false);
    }

    // -------------------------------
    // MAIN FLOW
    // -------------------------------

    IEnumerator RegisterFlow()
    {
        while (true)
        {
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                Debug.LogWarning("No Internet — Offline Mode");
                if (wifi != null) wifi.SetActive(true);
                yield return new WaitForSeconds(3f);
                continue;
            }

            yield return RegisterDevice();
            yield break;
        }
    }

    // -------------------------------
    // REGISTER DEVICE WITH SERVER
    // -------------------------------

    IEnumerator RegisterDevice()
    {
        string deviceID = SystemInfo.deviceUniqueIdentifier;

        DeviceIdPayload payload = new DeviceIdPayload()
        {
            device_id = deviceID
        };

        string json = JsonUtility.ToJson(payload);
        Debug.Log("Sending Payload: " + json);

        UnityWebRequest request = new UnityWebRequest(apiURL, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);

        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("API Error: " + request.error);
            Debug.LogError("Response Code: " + request.responseCode);
            Debug.LogError("Raw Response: " + request.downloadHandler.text);

            if (wifi != null) wifi.SetActive(true);

            Debug.LogWarning("Retrying in 3 seconds...");
            yield return new WaitForSeconds(3f);
            yield return RegisterDevice();
            yield break;
        }

        Debug.Log("API Response: " + request.downloadHandler.text);

        DeviceResponse response = JsonUtility.FromJson<DeviceResponse>(request.downloadHandler.text);

        if (response != null && response.success && response.data != null)
        {
            Debug.Log("Booth ID: " + response.data.booth_id);
            SaveLocalBoothID(response.data.booth_id);

            if (wifi != null) wifi.SetActive(false);
        }
        else
        {
            if (wifi != null) wifi.SetActive(true);
            string message = response != null ? response.message : "Unknown server response";
            Debug.LogWarning("Server responded but registration failed: " + message);
            yield return new WaitForSeconds(3f);
            yield return RegisterDevice();
        }
    }

    // -------------------------------
    // LOCAL JSON SAVE
    // -------------------------------

    private void SaveLocalBoothID(string boothID)
    {
        if (string.IsNullOrEmpty(boothID))
        {
            Debug.LogWarning("Cannot save empty booth ID.");
            boothID = "No Booth ID";
        }

        DeviceLocalData data = new DeviceLocalData()
        {
            booth_id = boothID
        };

        try
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(jsonPath, json);

            PlayerPrefs.SetString("booth_id", boothID);
            PlayerPrefs.Save();

            if (boothIdText != null)
                boothIdText.text = boothID;

            var login = FindAnyObjectByType<VendorLogin>();
            if (login != null && login.boothIDInput != null)
                login.boothIDInput.text = boothID;

            Debug.Log("Saved Booth ID locally: " + boothID);
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to save booth ID locally: " + ex.Message);
        }
    }

    // -------------------------------
    // LOAD FROM LOCAL JSON
    // -------------------------------

    private void LoadLocalBoothID()
    {
        DeviceLocalData data = null;

        if (File.Exists(jsonPath))
        {
            string json = File.ReadAllText(jsonPath);
            try
            {
                data = JsonUtility.FromJson<DeviceLocalData>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Failed to parse device.json: " + ex.Message);
            }
        }
        else
        {
            Debug.Log("No local device.json found.");
        }

        if (data == null || string.IsNullOrEmpty(data.booth_id))
        {
            Debug.LogWarning("device.json is empty or malformed. Creating default booth ID.");
            data = new DeviceLocalData() { booth_id = "No Booth ID" };

            try
            {
                string defaultJson = JsonUtility.ToJson(data, true);
                File.WriteAllText(jsonPath, defaultJson);
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to write default device.json: " + ex.Message);
            }
        }

        if (boothIdText != null)
            boothIdText.text = data.booth_id;

        var login = FindAnyObjectByType<VendorLogin>();
        if (login != null && login.boothIDInput != null)
            login.boothIDInput.text = data.booth_id;

        Debug.Log("Loaded Booth ID: " + data.booth_id);
    }

    // -------------------------------
    // OTHER METHODS
    // -------------------------------

    public void QuitApp()
    {
        Application.Quit();
        Debug.Log("quit");
    }

    public string GetSavedBoothID()
    {
        if (File.Exists(jsonPath))
        {
            string json = File.ReadAllText(jsonPath);
            DeviceLocalData data = JsonUtility.FromJson<DeviceLocalData>(json);
            return data != null ? data.booth_id : "";
        }

        return "";
    }

    // -----------------------------------
    // DATA MODELS
    // -----------------------------------

    [Serializable]
    public class DeviceIdPayload
    {
        public string device_id;
    }

    [Serializable]
    public class DeviceResponse
    {
        public bool success;
        public string message;
        public DeviceData data;
    }

    [Serializable]
    public class DeviceData
    {
        public string booth_id;
    }

    [Serializable]
    public class DeviceLocalData
    {
        public string booth_id;
    }
}
