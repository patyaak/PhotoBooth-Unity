using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SceneManagement : MonoBehaviour
{
    [Header("Scene Names")]
    [Tooltip("Name of the scene to load for Landscape mode")]
    public string landscapeSceneName = "Landscape";

    [Tooltip("Name of the scene to load for Portrait mode")]
    public string portraitSceneName = "Portrait";

    [Header("Printer Toggle UI")]
    public GameObject printerPanel;
    public Button onButton;
    public Button offButton;

    [Header("Audio Toggle UI")]
    public GameObject audioPanel;
    public Button audioOnButton;
    public Button audioOffButton;


    private void Start()
    {
 
    
        bool isPrinterOn = PlayerPrefs.GetInt("PrinterEnabled", 1) == 1;
        UpdateButtonStates(isPrinterOn);

        bool isAudioOn = PlayerPrefs.GetInt("AudioEnabled", 1) == 1;
        UpdateAudioButtonStates(isAudioOn);
    }



    public void LoadLandscapeScene()
    {
        Debug.Log($"Loading Landscape Scene: {landscapeSceneName}");
        UnityEngine.SceneManagement.SceneManager.LoadScene(landscapeSceneName);
    }

    public void LoadPortraitScene()
    {
        Debug.Log($"Loading Portrait Scene: {portraitSceneName}");
        UnityEngine.SceneManagement.SceneManager.LoadScene(portraitSceneName);
    }

 

    public void PressOnButton()
    {
        SetPrinterState(false);
    }

    public void PressOffButton()
    {
        SetPrinterState(true);
    }


    private void UpdateButtonStates(bool isOn)
    {
        if (onButton != null) onButton.gameObject.SetActive(isOn);
        if (offButton != null) offButton.gameObject.SetActive(!isOn);
    }

    private void SetPrinterState(bool isOn)
    {
        PlayerPrefs.SetInt("PrinterEnabled", isOn ? 1 : 0);
        PlayerPrefs.Save();
        
        UpdateButtonStates(isOn);
        
        Debug.Log($"🖨️ Printer state set to: {(isOn ? "ON" : "OFF")}");
    }

    // --- Audio Toggle Logic (Independent from Printer) ---

    public void PressAudioOnButton()
    {
        SetAudioState(false);
    }

    public void PressAudioOffButton()
    {
        SetAudioState(true);
    }


    private void SetAudioState(bool isOn)
    {
        PlayerPrefs.SetInt("AudioEnabled", isOn ? 1 : 0);
        PlayerPrefs.Save();
        
        UpdateAudioButtonStates(isOn);

        // Apply immediately if AudioManager exists
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.ApplyAudioState();
        }
    }



    private void UpdateAudioButtonStates(bool isOn)
    {
        if (audioOnButton != null) audioOnButton.gameObject.SetActive(isOn);
        if (audioOffButton != null) audioOffButton.gameObject.SetActive(!isOn);

        Debug.Log($"🔊 Audio state set to: {(isOn ? "ON" : "OFF")}");
    }
}


