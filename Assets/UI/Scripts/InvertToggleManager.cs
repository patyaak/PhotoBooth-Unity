using UnityEngine;
using UnityEngine.UI;

public class InvertToggleManager : MonoBehaviour
{
    [Header("Inverted Toggle UI")]
    public GameObject invertedPanel;
    public Button invertedOnButton;
    public Button invertedOffButton;

    private void Start()
    {
        bool isInvertedOn = PlayerPrefs.GetInt("InvertedEnabled", 0) == 1;
        UpdateInvertedButtonStates(isInvertedOn);
    }

    public void PressInvertedOnButton()
    {
        SetInvertedState(false);
    }

    public void PressInvertedOffButton()
    {
        SetInvertedState(true);
    }

    private void SetInvertedState(bool isOn)
    {
        PlayerPrefs.SetInt("InvertedEnabled", isOn ? 1 : 0);
        PlayerPrefs.Save();
        
        UpdateInvertedButtonStates(isOn);

        // Apply immediately if PhotoShootingManager exists
        if (PhotoShootingManager.Instance != null)
        {
            PhotoShootingManager.Instance.ApplyInvertedState(isOn);
        }
    }

    private void UpdateInvertedButtonStates(bool isOn)
    {
        if (invertedOnButton != null) invertedOnButton.gameObject.SetActive(isOn);
        if (invertedOffButton != null) invertedOffButton.gameObject.SetActive(!isOn);

        Debug.Log($"🔄 Inverted state set to: {(isOn ? "ON" : "OFF")}");
    }
}
