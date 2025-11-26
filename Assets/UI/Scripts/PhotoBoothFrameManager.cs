using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class PhotoBoothFrameManager : MonoBehaviour
{
    public static PhotoBoothFrameManager Instance;
    public static CapturedPhotosDisplayManager captureManager;

    [Header("UI References")]
    public Transform contentParent;
    public GameObject framePrefab;

    [Header("Category Buttons")]
    public Button defaultButton;
    public Button recommendationButton;
    public Button gatchaButton;
    public Button myFrameButton;

    [Header("Action Buttons")]
    public Button decideButton;
    public Button playButton;
    public Button backButton;

    [Header("Decide Prefab")]
    public GameObject startShootingPrefab;
    public Transform startShootingParent;

    [Header("Scroll & Navigation")]
    public ScrollRect scrollRect;
    public Button nextButton;
    public Button prevButton;
    public float scrollStep = 1f;

    [Header("API")]
    public string apiBaseURL = "http://photo-stg-api.chvps3.aozora-okinawa.com/";
    private string boothID = "";

    public FrameResponse cachedResponse;
    private string currentCategory = "default";
    private Button currentSelectedButton;
    public FrameItem currentSelectedFrame;
    private bool isFetching = false;

    private Dictionary<Button, Sprite> normalSprites = new Dictionary<Button, Sprite>();
    private Dictionary<string, Sprite> imageCache = new Dictionary<string, Sprite>(); // Thumbnail cache
    public Dictionary<string, Texture2D> assetCache = new Dictionary<string, Texture2D>(); // Full frame texture cache
    private HashSet<string> downloadingAssets = new HashSet<string>();
    private List<FrameItem> currentFrameItems = new List<FrameItem>();

    [Header("Heartbeat Settings")]
    public float fetchInterval = 300f; // 5 minutes
    private Coroutine heartbeatCoroutine;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // store original sprites
        if (defaultButton != null) normalSprites[defaultButton] = defaultButton.image.sprite;
        if (recommendationButton != null) normalSprites[recommendationButton] = recommendationButton.image.sprite;
        if (gatchaButton != null) normalSprites[gatchaButton] = gatchaButton.image.sprite;
        if (myFrameButton != null) normalSprites[myFrameButton] = myFrameButton.image.sprite;

        if (defaultButton != null) defaultButton.onClick.AddListener(() => OnCategoryButtonClicked(defaultButton));
        if (recommendationButton != null) recommendationButton.onClick.AddListener(() => OnCategoryButtonClicked(recommendationButton));
        if (gatchaButton != null) gatchaButton.onClick.AddListener(() => OnCategoryButtonClicked(gatchaButton));
        if (myFrameButton != null) myFrameButton.onClick.AddListener(() => OnCategoryButtonClicked(myFrameButton));

        if (playButton != null) playButton.onClick.AddListener(OnGatchaPlay);
        if (nextButton != null) nextButton.onClick.AddListener(OnNextClicked);
        if (prevButton != null) prevButton.onClick.AddListener(OnPrevClicked);

        // initial load
        OnCategoryButtonClicked(defaultButton);
    }

    void OnEnable()
    {
        if (fetchInterval > 0 && heartbeatCoroutine == null)
            heartbeatCoroutine = StartCoroutine(FrameHeartbeat());
    }

    void OnDisable()
    {
        if (heartbeatCoroutine != null)
            StopCoroutine(heartbeatCoroutine);
    }

    IEnumerator FrameHeartbeat()
    {
        while (true)
        {
            yield return new WaitForSeconds(fetchInterval);

            if (!isFetching && !string.IsNullOrEmpty(boothID) && Application.internetReachability != NetworkReachability.NotReachable)
                StartCoroutine(FetchFramesFromServer());
        }
    }

    IEnumerator ScrollTo(float target)
    {
        float start = scrollRect.horizontalNormalizedPosition;
        float time = 0f;
        float duration = 0.3f;

        while (time < duration)
        {
            time += Time.deltaTime;
            scrollRect.horizontalNormalizedPosition = Mathf.Lerp(start, target, time / duration);
            yield return null;
        }

        scrollRect.horizontalNormalizedPosition = target;
    }

    void OnNextClicked() => StartCoroutine(ScrollTo(Mathf.Clamp01(scrollRect.horizontalNormalizedPosition + scrollStep)));
    void OnPrevClicked() => StartCoroutine(ScrollTo(Mathf.Clamp01(scrollRect.horizontalNormalizedPosition - scrollStep)));

    public void SetBoothID(string id) => boothID = id;

    public IEnumerator FetchFramesFromServer()
    {
        if (isFetching || string.IsNullOrEmpty(boothID))
            yield break;

        isFetching = true;

        bool online = Application.internetReachability != NetworkReachability.NotReachable;

        if (online)
        {
            string fullURL = $"{apiBaseURL}api/photobooth/frames?booth_id={boothID}&assignment_type={currentCategory}";
            using (UnityWebRequest request = UnityWebRequest.Get(fullURL))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string json = request.downloadHandler.text;
                    cachedResponse = JsonUtility.FromJson<FrameResponse>(json);
                    FrameCacheManager.SaveJSON(json, currentCategory);
                    DisplayFrames(cachedResponse.data.frames);
                }
                else
                {
                    yield return StartCoroutine(LoadFramesFromCache(currentCategory));
                }
            }
        }
        else
        {
            yield return StartCoroutine(LoadFramesFromCache(currentCategory));
        }

        isFetching = false;
    }

    private IEnumerator LoadFramesFromCache(string category)
    {
        if (!FrameCacheManager.HasCachedData(category))
            yield break;

        string cachedJson = FrameCacheManager.LoadCachedJSON(category);
        if (string.IsNullOrEmpty(cachedJson))
            yield break;

        cachedResponse = JsonUtility.FromJson<FrameResponse>(cachedJson);
        if (cachedResponse != null && cachedResponse.data.frames.Count > 0)
            DisplayFrames(cachedResponse.data.frames);

        yield return null;
    }

    private void DisplayFrames(List<Frame> frames)
    {
        if (GatchaManager.Instance != null)
            GatchaManager.Instance.ClearSpawnedFramesInstant();

        ClearFrames();

        foreach (Frame frame in frames)
        {
            GameObject obj = Instantiate(framePrefab, contentParent);
            obj.SetActive(true);

            Button btn = obj.GetComponent<Button>();
            if (btn != null)
                btn.transition = (currentCategory == "gacha") ? Selectable.Transition.None : Selectable.Transition.SpriteSwap;

            FrameItem item = obj.GetComponent<FrameItem>();
            if (item != null)
            {
                item.Setup(frame);
                item.DisableSelection(currentCategory == "gacha");
                currentFrameItems.Add(item);
            }

            if (currentCategory == "gacha" && GatchaManager.Instance != null)
                GatchaManager.Instance.RegisterSpawnedFrame(obj);
        }

        decideButton.gameObject.SetActive(currentCategory != "gacha");
        playButton.gameObject.SetActive(currentCategory == "gacha");

        // start parallel download tasks (doesn't block)
        StartCoroutine(DownloadThumbnailsAndAssetsParallel(currentFrameItems));
    }

    private IEnumerator DownloadThumbnailsAndAssetsParallel(List<FrameItem> items)
    {
        if (items == null) yield break;

        foreach (FrameItem item in items)
        {
            if (item == null) continue;

            // thumbnail: start a coroutine for each thumbnail (applies when ready)
            if (!string.IsNullOrEmpty(item.frameData.thumb_path))
            {
                string thumbResolved = ResolveUrl(item.frameData.thumb_path);
                if (!imageCache.ContainsKey(thumbResolved))
                    StartCoroutine(DownloadThumbnail(thumbResolved, item));
                else
                {
                    item.ApplySprite(imageCache[thumbResolved]);
                    item.SetThumbnailAlpha(1f);
                }
            }

            // full asset: start download coroutine if not cached or already downloading
            if (!string.IsNullOrEmpty(item.frameData.asset_path))
            {
                string assetResolved = ResolveUrl(item.frameData.asset_path);
                if (!assetCache.ContainsKey(assetResolved) && !downloadingAssets.Contains(assetResolved))
                {
                    StartCoroutine(DownloadAndCacheTextureCoroutine(assetResolved));
                }
            }
        }

        yield return null;
    }

    // helper: download thumbnail and set sprite
    private IEnumerator DownloadThumbnail(string resolvedUrl, FrameItem item)
    {
        if (string.IsNullOrEmpty(resolvedUrl)) yield break;

        // if already cached (another coroutine finished meanwhile)
        if (imageCache.TryGetValue(resolvedUrl, out Sprite cachedSprite))
        {
            if (item != null)
            {
                item.ApplySprite(cachedSprite);
                item.SetThumbnailAlpha(1f);
            }
            yield break;
        }

        yield return FrameCacheManager.DownloadAndCacheTexture(resolvedUrl, tex =>
        {
            if (tex != null)
            {
                Sprite s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                imageCache[resolvedUrl] = s;

                if (item != null)
                {
                    item.ApplySprite(s);
                    item.SetThumbnailAlpha(1f);
                }
            }
            else
            {
                Debug.LogWarning($"⚠️ Failed to download thumbnail: {resolvedUrl}");
            }
        });
    }

    // wrapper so we can call FrameCacheManager.DownloadAndCacheTexture with yield return safely
    private IEnumerator DownloadAndCacheTextureCoroutine(string resolvedUrl)
    {
        if (string.IsNullOrEmpty(resolvedUrl)) yield break;
        if (downloadingAssets.Contains(resolvedUrl)) yield break;

        downloadingAssets.Add(resolvedUrl);

        yield return FrameCacheManager.DownloadAndCacheTexture(resolvedUrl, tex =>
        {
            if (tex != null)
            {
                assetCache[resolvedUrl] = tex;
            }
            else
            {
                Debug.LogWarning($"⚠️ Failed to download asset: {resolvedUrl}");
            }

            downloadingAssets.Remove(resolvedUrl);
        });
    }

    // Resolves path to full URL: if already absolute return as-is, otherwise prepend apiBaseURL
    private string ResolveUrl(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        path = path.Trim();

        if (path.StartsWith("http://") || path.StartsWith("https://"))
            return path;

        // ensure apiBaseURL ends with a single slash
        string baseUrl = apiBaseURL;
        if (!baseUrl.EndsWith("/")) baseUrl += "/";

        // drop leading slash from path if present
        if (path.StartsWith("/")) path = path.Substring(1);

        return baseUrl + path;
    }

    void OnCategoryButtonClicked(Button clickedButton)
    {
        if (currentSelectedButton != null && currentSelectedButton != clickedButton)
            ResetButtonSprite(currentSelectedButton);

        ApplySelectedSprite(clickedButton);
        currentSelectedButton = clickedButton;

        currentCategory = clickedButton == defaultButton ? "default" :
                          clickedButton == recommendationButton ? "recommended" :
                          clickedButton == gatchaButton ? "gacha" : "myframe";

        StartCoroutine(FetchFramesFromServer());
    }

    void ResetButtonSprite(Button button)
    {
        if (button == null) return;
        if (normalSprites.TryGetValue(button, out Sprite original))
            button.image.sprite = original;
    }

    void ApplySelectedSprite(Button button)
    {
        if (button == null) return;
        if (button.spriteState.selectedSprite != null)
            button.image.sprite = button.spriteState.selectedSprite;
    }

    public void SelectFrame(FrameItem item)
    {
        if (currentSelectedFrame != null && currentSelectedFrame != item)
            currentSelectedFrame.Deselect();

        currentSelectedFrame = item;
        currentSelectedFrame.Select();
    }

    public FrameItem GetSelectedFrameItem() => currentSelectedFrame;

    public void OnDecideButtonClicked()
    {
        FrameItem selectedItem = GetSelectedFrameItem();
        if (selectedItem == null) return;

        foreach (Transform child in startShootingParent)
            Destroy(child.gameObject);

        GameObject instance = Instantiate(startShootingPrefab, startShootingParent);
        Image img = instance.GetComponentInChildren<Image>();
        if (img != null)
            img.sprite = selectedItem.frameImg.sprite;

        Button startButton = instance.GetComponentInChildren<Button>();
        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(() =>
            {
                if (PhotoShootingManager.Instance != null)
                {
                    PhotoShootingManager.Instance.StartShooting(selectedItem);
                    instance.SetActive(false);
                }
            });
        }

        // Resolve and use cached asset if available; otherwise start download coroutine that will set the frame when done.
        string resolvedAssetUrl = ResolveUrl(selectedItem.frameData.asset_path);
        if (assetCache.TryGetValue(resolvedAssetUrl, out Texture2D tex))
        {
            if (CapturedPhotosDisplayManager.Instance != null)
                CapturedPhotosDisplayManager.Instance.SetFrame(tex);
        }
        else
        {
            StartCoroutine(DownloadAndSetFrameForCapture(resolvedAssetUrl));
        }
    }

    private IEnumerator DownloadAndSetFrameForCapture(string resolvedUrl)
    {
        if (string.IsNullOrEmpty(resolvedUrl)) yield break;

        Texture2D loaded = null;
        yield return FrameCacheManager.DownloadAndCacheTexture(resolvedUrl, tex => loaded = tex);

        if (loaded != null)
        {
            assetCache[resolvedUrl] = loaded;
            if (CapturedPhotosDisplayManager.Instance != null)
                CapturedPhotosDisplayManager.Instance.SetFrame(loaded);
        }
        else
        {
            Debug.LogWarning($"⚠️ Failed to download frame asset for capture: {resolvedUrl}");
        }
    }

    private IEnumerator DownloadFrameAssetForCapturedDisplay(string assetURL)
    {
        // kept for backward compatibility; resolves URL and forwards to DownloadAndSetFrameForCapture
        string resolved = ResolveUrl(assetURL);
        yield return DownloadAndSetFrameForCapture(resolved);
    }

    public void ClearFrames()
    {
        foreach (var item in currentFrameItems)
            item?.Deselect();

        foreach (Transform child in contentParent)
            Destroy(child.gameObject);

        currentFrameItems.Clear();
        currentSelectedFrame = null;
    }

    public void OnGatchaPlay()
    {
        if (GatchaManager.Instance != null)
        {
            if (defaultButton != null) defaultButton.interactable = false;
            if (recommendationButton != null) recommendationButton.interactable = false;
            if (myFrameButton != null) myFrameButton.interactable = false;

            GatchaManager.Instance.SetBoothID(boothID);
            GatchaManager.Instance.PlayGatchaAnimation();
        }
    }

    // ZoomIn helper used by GatchaManager
    public IEnumerator ZoomIn(Transform target, float duration, Vector3 targetScale)
    {
        float t = 0f;
        Vector3 startScale = Vector3.zero;
        while (t < duration)
        {
            t += Time.deltaTime;
            float s = Mathf.SmoothStep(0f, 1f, t / duration);
            target.localScale = Vector3.Lerp(startScale, targetScale, s);
            yield return null;
        }
        target.localScale = targetScale;
    }
}
