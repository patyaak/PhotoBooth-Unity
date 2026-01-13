using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class CapturedPhotosDisplayManager : MonoBehaviour
{
    public static CapturedPhotosDisplayManager Instance;

    [Header("UI References")]
    public GameObject photoPrefab;

    public List<Texture2D> confirmedPhotos = new List<Texture2D>();

    [Header("Frame Display")]
    public Transform frameDisplayParent;
    public GameObject frameDisplayPrefab;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public void AddConfirmedPhoto(Texture2D tex)
    {
        if (tex == null) return;

        confirmedPhotos.Add(tex);
        DisplayPhoto(tex);
    }

    private void DisplayPhoto(Texture2D tex)
    {
        if (photoPrefab == null)
        {
            Debug.LogWarning("PhotosParent or PhotoPrefab not assigned!");
            return;
        }

        GameObject go = Instantiate(photoPrefab);
        Image img = go.GetComponent<Image>();
        img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        img.preserveAspect = false;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(tex.width, tex.height);
    }


    public void DisplayAllPhotos(List<Texture2D> photos)
    {
        if (photoPrefab == null)
        {
            Debug.LogWarning("PhotosParent or PhotoPrefab not assigned!");
            return;
        }


        confirmedPhotos.Clear();

        foreach (Texture2D tex in photos)
            AddConfirmedPhoto(tex);

        var selectedFrame = PhotoBoothFrameManager.Instance.currentSelectedFrame;
        if (selectedFrame == null || string.IsNullOrEmpty(selectedFrame.frameData.asset_path))
        {
            Debug.LogWarning("No frame selected or missing asset path!");
            return;
        }

        StartCoroutine(InstantiateSelectedFrame(selectedFrame.frameData.asset_path));
    }



    private IEnumerator InstantiateSelectedFrame(string assetURL)
    {
        if (frameDisplayParent == null)
        {
            Debug.LogWarning("⚠️ FrameDisplayParent not assigned!");
            yield break;
        }

        foreach (Transform child in frameDisplayParent)
            Destroy(child.gameObject);

        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(assetURL))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("❌ Failed to download frame texture: " + assetURL);
                yield break;
            }

            Texture2D tex = DownloadHandlerTexture.GetContent(req);
            Sprite frameSprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect
            );

            GameObject frameObj = Instantiate(frameDisplayPrefab, frameDisplayParent);
            frameObj.SetActive(true);

            // --- NEW LOGIC START ---
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            string targetLandscapeName = "Landscape";
            string targetPortraitName = "Portrait";
            
            SceneManagement sceneMgr = FindObjectOfType<SceneManagement>();
            if (sceneMgr != null)
            {
                targetLandscapeName = sceneMgr.landscapeSceneName;
                targetPortraitName = sceneMgr.portraitSceneName;
            }

            // Determine orientation based on scene name (Approximate match if exact fails)
            bool isPortraitScene = currentSceneName.Equals(targetPortraitName, System.StringComparison.OrdinalIgnoreCase) 
                                   || currentSceneName.Contains("Portrait");
            bool isLandscapeScene = currentSceneName.Equals(targetLandscapeName, System.StringComparison.OrdinalIgnoreCase) 
                                    || currentSceneName.Contains("Landscape");

            Transform bgTransform = frameObj.transform.Find("Bg");
            if (bgTransform != null)
            {
                RectTransform bgRect = bgTransform.GetComponent<RectTransform>();
                if (bgRect != null)
                {
                    if (isLandscapeScene)
                    {
                        bgRect.sizeDelta = new Vector2(1920, 1080);
                    }
                    else if (isPortraitScene)
                    {
                        bgRect.sizeDelta = new Vector2(1080, 1920);
                    }
                }
            }

            // Check for portrait scene and landscape frame type scaling
            if (isPortraitScene)
            {
                var selectedFrame = PhotoBoothFrameManager.Instance.currentSelectedFrame;
                if (selectedFrame != null && selectedFrame.frameData != null)
                {
                    if (string.Equals(selectedFrame.frameData.type, "landscape", System.StringComparison.OrdinalIgnoreCase))
                    {
                        // Scale the "frame" container instead of the whole root, so Bg isn't affected
                        Transform contentFrame = frameObj.transform.Find("frame");
                        if (contentFrame != null)
                        {
                            contentFrame.localScale = new Vector3(0.5f, 0.5f, 1f);
                        }
                        else
                        {
                            // Fallback if "frame" container is missing, though we expect it from hierarchy
                           frameObj.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
                        }
                    }
                }
            }
            // --- NEW LOGIC END ---

            Transform frameImgChild = frameObj.transform.Find("frame/frameImg");
            if (frameImgChild == null)
            {
                // Fallback: try finding just "frameImg" in case hierarchy varies
                 Transform frameContainer = frameObj.transform.Find("frame");
                 if (frameContainer != null)
                 {
                     frameImgChild = frameContainer.Find("frameImg");
                 }
            }

            if (frameImgChild == null)
            {
                 // Last resort
                 frameImgChild = frameObj.transform.Find("frameImg");
            }

            if (frameImgChild == null)
            {
                Debug.LogError("❌ 'FrameImg' child not found in frameDisplayPrefab!");
                yield break;
            }

            Image frameImg = frameImgChild.GetComponent<Image>();
            if (frameImg == null)
            {
                Debug.LogError("❌ 'FrameImg' child does not have an Image component!");
                yield break;
            }

            frameImg.sprite = frameSprite;
            frameImg.preserveAspect = true;

            RectTransform rt = frameImg.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(tex.width, tex.height);
            rt.anchoredPosition = Vector2.zero;

            Debug.Log($"✅ Frame instantiated on 'FrameImg' at native size: {tex.width}x{tex.height}px");
        }
    }

    public void ClearPhotos()
    {

        confirmedPhotos.Clear();
    }

    public void SetFrame(Texture2D frameTexture)
    {
        Debug.Log("✅ Frame texture set for captured photos display.");
    }
}
