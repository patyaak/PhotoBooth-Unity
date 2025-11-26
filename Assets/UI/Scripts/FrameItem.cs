using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

public
class FrameItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,IPointerClickHandler
{
    [Header("UI References")] public Image frameImg;
    public Transform layoutParent;
    public TMP_Text shotCountText;
    public TMP_Text layoutCountText;

    [Header("Colors")]
    public Color defaultColor = Color.black;
    public Color highlightColor = Color.white;

    [Header("Full Frame Display Prefab")]
    public GameObject framePrefab;

    [HideInInspector]
    public Frame frameData;
    private List<Image> layoutSlots = new List<Image>();
    private bool isSelected = false;

    // Flag for whether this frame can be selected
    private bool isSelectable = true;

    [Header("Offline Frame Asset")]
    public Texture2D offlineTexture;

    public string cachedFrameAssetPath;

    public void Setup(Frame frame)
    {
        frameData = frame;

        if (shotCountText != null)
        {
            shotCountText.text = Mathf.Max(1, frame.number_of_shots).ToString();
            shotCountText.color = defaultColor;
        }

        if (layoutCountText != null)
        {
            layoutCountText.text = Mathf.Max(1, frame.number_of_layouts).ToString();
            layoutCountText.color = defaultColor;
        }

        int slotCount = frame.number_of_layouts > 0
                            ? frame.number_of_layouts
                            : Mathf.Max(1, frame.number_of_shots);
        CreateLayoutSlots(slotCount);
        cachedFrameAssetPath = Path.Combine(Application.persistentDataPath, "FrameCache", Path.GetFileName(frame.asset_path));

}

    public void CreateLayoutSlots(int count)
    {
        foreach (Transform t in layoutParent) Destroy(t.gameObject);
        layoutSlots.Clear();

        GridLayoutGroup grid = layoutParent.GetComponent<GridLayoutGroup>();
        if (!grid) grid = layoutParent.gameObject.AddComponent<GridLayoutGroup>();

        grid.cellSize = new Vector2(250, 250);
        grid.spacing = new Vector2(15, 15);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount =
            count <= 3 ? count : Mathf.CeilToInt(Mathf.Sqrt(count));

        for (int i = 0; i < count; i++)
        {
            GameObject slotGO = new GameObject($"PhotoSlot_{i + 1}",
                                               typeof(RectTransform), typeof(Image));
            slotGO.transform.SetParent(layoutParent, false);

            Image img = slotGO.GetComponent<Image>();
            img.color = new Color(1, 1, 1, 0.1f);
            img.preserveAspect = true;
            img.enabled = false;
            layoutSlots.Add(img);
        }
    }

    public void ApplyCapturedPhotos(List<Texture2D> photos)
    {
        for (int i = 0; i < photos.Count && i < layoutSlots.Count; i++)
        {
            var tex = photos[i];
            var slot = layoutSlots[i];

            Sprite s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                     new Vector2(0.5f, 0.5f));
            slot.sprite = s;
            slot.enabled = true;
            slot.canvasRenderer.SetAlpha(0);
            slot.CrossFadeAlpha(1f, 0.4f, false);
        }
    }

    public void ApplySprite(Sprite s)
    {
        if (frameImg != null) frameImg.sprite = s;
    }

    public void Select()
    {
        if (!isSelectable) return;
        isSelected = true;
        UpdateTextColor();
    }

    public void Deselect()
    {
        isSelected = false;
        UpdateTextColor();
    }

    private void UpdateTextColor()
    {
        Color c = isSelected ? highlightColor : defaultColor;
        if (shotCountText != null) shotCountText.color = c;
        if (layoutCountText != null) layoutCountText.color = c;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isSelectable) return;

        if (!isSelected)
        {
            if (shotCountText != null) shotCountText.color = highlightColor;
            if (layoutCountText != null) layoutCountText.color = highlightColor;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isSelectable) return;

        if (!isSelected) UpdateTextColor();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isSelectable) return;

        if (PhotoBoothFrameManager.Instance != null)
            PhotoBoothFrameManager.Instance.SelectFrame(this);
    }

    public void DisableSelection(bool disable)
    {
        isSelectable = !disable;
        if (disable)
        {
            if (frameImg != null)
                frameImg.color = new Color(1, 1, 1, 0.5f);  // dimmed
        }
        else
        {
            if (frameImg != null) frameImg.color = Color.white;  // normal
        }
    }
    public void SetThumbnailAlpha(float alpha)
    {
        if (frameImg != null)
        {
            Color c = frameImg.color;
            c.a = alpha;
            frameImg.color = c;
        }
    }


    public void DisplayFullAsset()
    {
        if (string.IsNullOrEmpty(frameData.asset_path))
        {
            Debug.LogWarning("Asset path not set for this frame!");
            return;
        }

        foreach (Transform child in layoutParent) Destroy(child.gameObject);

        PhotoBoothFrameManager.Instance.StartCoroutine(
            DownloadAndInstantiateAsset(frameData.asset_path));
    }

    private IEnumerator DownloadAndInstantiateAsset(string url)
    {
        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
        {
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                Texture2D tex = DownloadHandlerTexture.GetContent(req);

                GameObject go = Instantiate(framePrefab, layoutParent);
                RawImage rawImg = go.GetComponent<RawImage>();
                if (rawImg != null)
                {
                    rawImg.texture = tex;
                    RectTransform rt = go.GetComponent<RectTransform>();
                    rt.sizeDelta = new Vector2(tex.width, tex.height);
                }
            }
            else
            {
                Debug.LogWarning("Failed to download frame asset: " + url);
            }
        }
    }

    public void SetupFromGacha(Frame frame)
    {
        this.frameData = frame;

        // Set basic info
        if (shotCountText != null)
            shotCountText.text = Mathf.Max(1, frame.number_of_shots).ToString();
        if (layoutCountText != null)
            layoutCountText.text = Mathf.Max(1, frame.number_of_layouts).ToString();

        // Create layout slots
        int slotCount = frame.number_of_layouts > 0
                            ? frame.number_of_layouts
                            : Mathf.Max(1, frame.number_of_shots);
        CreateLayoutSlots(slotCount);

        // Clear initial image until downloaded
        if (frameImg != null) frameImg.sprite = null;
    }
}
