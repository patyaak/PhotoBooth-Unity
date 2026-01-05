using UnityEngine;

public class SceneManagement : MonoBehaviour
{
    [Header("Scene Names")]
    [Tooltip("Name of the scene to load for Landscape mode")]
    public string landscapeSceneName = "Landscape";

    [Tooltip("Name of the scene to load for Portrait mode")]
    public string portraitSceneName = "Portrait";

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
}
