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

    private void Start()
    {
 
        bool isPrinterOn = PlayerPrefs.GetInt("PrinterEnabled", 0) == 1;

        UpdateButtonStates(isPrinterOn);
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
        UpdateButtonStates(false);
    }

    public void PressOffButton()
    {
    
        SetPrinterState(true);
        UpdateButtonStates(true);
    }

    private void UpdateButtonStates(bool isOn)
    {
  
        if (onButton != null) onButton.interactable = isOn;
        if (offButton != null) offButton.interactable = !isOn;
    }

    private void SetPrinterState(bool isOn)
    {
        PlayerPrefs.SetInt("PrinterEnabled", isOn ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log($"🖨️ Printer state set to: {(isOn ? "ON" : "OFF")}");
    }
}
