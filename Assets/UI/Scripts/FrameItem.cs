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
    
    [Header("Usage Info")]
    public GameObject totalUsesObject;
    public TMP_Text usesRemainingText;

    [Header("Colors")]
    public Color defaultColor = Color.black;
    public Color highlightColor = Color.white;

    [Header("Full Frame Display Prefab")]
    public GameObject framePrefab;

    [HideInInspector]
    public Frame frameData;
    private List<Image> layoutSlots = new List<Image>();
    private bool isSelected = false;
    private Sprite normalSprite;
    private Button btn;

    private void Awake()
    {
        btn = GetComponent<Button>();
        if (btn != null && btn.image != null)
        {
            normalSprite = btn.image.sprite;
        }
    }

    // Flag for whether this frame can be selected
    private bool isSelectable = true;

    [Header("Offline Frame Asset")]
    public Texture2D offlineTexture;

    public string cachedFrameAssetPath;

    public void Setup(Frame frame, string category = "")
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

        // Handle Total Uses display (Only for 'myframe' category)
        if (totalUsesObject != null)
        {
            bool showUses = (category == "myframe");
            totalUsesObject.SetActive(showUses);

            if (showUses && usesRemainingText != null)
            {
                usesRemainingText.text = string.IsNullOrEmpty(frame.uses_remaining) ? "∞" : frame.uses_remaining;
            }
        }

        // **FIX: Reset sprite and alpha to prevent stale thumbnails when reusing pooled items**
        if (frameImg != null)
        {
            frameImg.sprite = null;
            Color c = frameImg.color;
            c.a = 0f;
            frameImg.color = c;
        }

        int slotCount = frame.number_of_layouts > 0
                            ? frame.number_of_layouts
                            : Mathf.Max(1, frame.number_of_shots);
        CreateLayoutSlots(slotCount);
        // Compute the correct cache path using the same URL + MD5 hash scheme as FrameCacheManager.
        // frame.asset_path may be relative, so resolve it to a full URL first.
        string resolvedAssetUrl = PhotoBoothFrameManager.Instance != null
            ? PhotoBoothFrameManager.Instance.ResolveUrl(frame.asset_path)
            : frame.asset_path;
        cachedFrameAssetPath = FrameCacheManager.GetCachedTexturePath(resolvedAssetUrl);

        // Ensure we have the button reference and store original sprite
        if (btn == null) btn = GetComponent<Button>();
        if (btn != null && normalSprite == null) normalSprite = btn.image.sprite;

        // Reset to default state
        isSelected = false;
        if (btn != null && normalSprite != null) btn.image.sprite = normalSprite;
        UpdateTextColor();
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
        frameImg.color = new Color(1f, 1f, 1f, 1f);
    }

    public void Select()
    {
        if (!isSelectable) return;
        isSelected = true;
        UpdateTextColor();
        
        if (btn == null) btn = GetComponent<Button>();
        if (btn != null && btn.spriteState.selectedSprite != null)
        {
            if (normalSprite == null) normalSprite = btn.image.sprite;
            btn.image.sprite = btn.spriteState.selectedSprite;
        }
    }

    public void Deselect()
    {
        isSelected = false;
        UpdateTextColor();
        
        if (btn != null && normalSprite != null)
        {
            btn.image.sprite = normalSprite;
        }
    }

    private void UpdateTextColor()
    {
        Color c = isSelected ? highlightColor : defaultColor;
        if (shotCountText != null) shotCountText.color = c;
        if (layoutCountText != null) layoutCountText.color = c;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Hover effects disabled to prevent stuck visual states
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // Hover effects disabled to prevent stuck visual states
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
                frameImg.color = new Color(1, 1, 1, 1f);  // dimmed
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


    public string GetOrientation()
    {
        if (frameData == null)
            return "portrait";

        // Check if type field exists in JSON
        if (!string.IsNullOrEmpty(frameData.type))
        {
            return frameData.type.ToLower();
        }

        // Fallback: return portrait as default
        return "portrait";
    }

    public string GetOrientationLabel()
    {
        string orientation = GetOrientation();
        return orientation == "landscape" ? "横向き" : "縦向き";
    }
}
