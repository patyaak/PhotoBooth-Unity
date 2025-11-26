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

            Transform frameImgChild = frameObj.transform.Find("frameImg");
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
