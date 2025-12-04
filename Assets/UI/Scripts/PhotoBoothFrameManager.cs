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
    public GameObject emptyStateObject; // Optional: "No frames" message

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
    private Dictionary<string, Sprite> imageCache = new Dictionary<string, Sprite>();
    public Dictionary<string, Texture2D> assetCache = new Dictionary<string, Texture2D>();
    private HashSet<string> downloadingAssets = new HashSet<string>();
    private List<FrameItem> currentFrameItems = new List<FrameItem>();

    [Header("Heartbeat Settings")]
    public float fetchInterval = 300f;
    private Coroutine heartbeatCoroutine;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        StoreNormalSprites();
        SetupButtonListeners();
        OnCategoryButtonClicked(defaultButton);
    }

    private void OnEnable()
    {
        if (fetchInterval > 0 && heartbeatCoroutine == null)
            heartbeatCoroutine = StartCoroutine(FrameHeartbeat());
    }

    private void OnDisable()
    {
        if (heartbeatCoroutine != null)
            StopCoroutine(heartbeatCoroutine);
    }

    private void OnDestroy()
    {
        ClearAssetCache();
    }

    private void StoreNormalSprites()
    {
        if (defaultButton) normalSprites[defaultButton] = defaultButton.image.sprite;
        if (recommendationButton) normalSprites[recommendationButton] = recommendationButton.image.sprite;
        if (gatchaButton) normalSprites[gatchaButton] = gatchaButton.image.sprite;
        if (myFrameButton) normalSprites[myFrameButton] = myFrameButton.image.sprite;
    }

    private void SetupButtonListeners()
    {
        if (defaultButton) defaultButton.onClick.AddListener(() => OnCategoryButtonClicked(defaultButton));
        if (recommendationButton) recommendationButton.onClick.AddListener(() => OnCategoryButtonClicked(recommendationButton));
        if (gatchaButton) gatchaButton.onClick.AddListener(() => OnCategoryButtonClicked(gatchaButton));
        if (myFrameButton) myFrameButton.onClick.AddListener(() => OnCategoryButtonClicked(myFrameButton));

        if (playButton) playButton.onClick.AddListener(OnGatchaPlay);
        if (nextButton) nextButton.onClick.AddListener(OnNextClicked);
        if (prevButton) prevButton.onClick.AddListener(OnPrevClicked);
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

    // ==================================================================
    // MAIN FETCH METHOD – NOW WITH MYFRAME + USER_ID FILTER SUPPORT
    // ==================================================================
    public IEnumerator FetchFramesFromServer()
    {
        if (isFetching || string.IsNullOrEmpty(boothID)) yield break;

        isFetching = true;
        ClearFrames();

        string url = apiBaseURL + "api/photobooth/frames";

        var parameters = new List<string>
    {
        "booth_id=" + UnityWebRequest.EscapeURL(boothID),
        "assignment_type=" + currentCategory
    };

        // MYFRAME: Add user_id filter if logged in
        if (currentCategory == "myframe")
        {
            string userId = PlayerPrefs.GetString("user_id", "");

            if (!string.IsNullOrEmpty(userId))
            {
                parameters.Add("user_id=" + UnityWebRequest.EscapeURL(userId));
            }
            else
            {
                ShowEmptyState("Please log in to view your frames");
                isFetching = false;
                yield break;
            }
        }

        string fullURL = url + "?" + string.Join("&", parameters);
        Debug.Log("Fetching frames → " + fullURL);

        bool isOnline = Application.internetReachability != NetworkReachability.NotReachable;

        if (isOnline)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(fullURL))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string json = request.downloadHandler.text;
                    cachedResponse = JsonUtility.FromJson<FrameResponse>(json);

                    // **FIX: Use my_frames for myframe category**
                    List<Frame> framesToDisplay = null;

                    if (currentCategory == "myframe")
                    {
                        // Use my_frames field for purchased/owned frames
                        framesToDisplay = cachedResponse?.data?.my_frames;
                    }
                    else
                    {
                        // Use regular frames field for other categories
                        framesToDisplay = cachedResponse?.data?.frames;
                    }

                    FrameCacheManager.SaveJSON(json, currentCategory);
                    DisplayFrames(framesToDisplay);
                }
                else
                {
                    Debug.LogWarning("API failed → loading from cache");
                    yield return LoadFramesFromCache(currentCategory);
                }
            }
        }
        else
        {
            yield return LoadFramesFromCache(currentCategory);
        }

        isFetching = false;
    }


private IEnumerator LoadFramesFromCache(string category)
{
    if (!FrameCacheManager.HasCachedData(category)) yield break;

    string json = FrameCacheManager.LoadCachedJSON(category);
    if (string.IsNullOrEmpty(json)) yield break;

    cachedResponse = JsonUtility.FromJson<FrameResponse>(json);
    
    // Use correct field based on category
    List<Frame> framesToDisplay = (category == "myframe") 
        ? cachedResponse?.data?.my_frames 
        : cachedResponse?.data?.frames;
    
    if (framesToDisplay != null)
        DisplayFrames(framesToDisplay);
}

    private void DisplayFrames(List<Frame> frames)
    {
        if (GatchaManager.Instance != null)
            GatchaManager.Instance.ClearSpawnedFramesInstant();

        ClearFrames();

        if (frames == null || frames.Count == 0)
        {
            ShowEmptyState(currentCategory == "myframe" ? "You have no frames yet" : "No frames available");
            return;
        }

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

        StartCoroutine(DownloadThumbnailsAndAssetsParallel(currentFrameItems));
    }

    private void ShowEmptyState(string message = "No frames")
    {
        if (emptyStateObject != null)
        {
            GameObject go = Instantiate(emptyStateObject, contentParent);
            TextMeshProUGUI txt = go.GetComponentInChildren<TextMeshProUGUI>();
            if (txt) txt.text = message;
        }
        decideButton.gameObject.SetActive(false);
        playButton.gameObject.SetActive(false);
    }

    private IEnumerator DownloadThumbnailsAndAssetsParallel(List<FrameItem> items)
    {
        if (items == null) yield break;

        foreach (FrameItem item in items)
        {
            if (item == null || item.frameData == null) continue;

            string thumbUrl = ResolveUrl(item.frameData.thumb_path);
            if (!string.IsNullOrEmpty(thumbUrl) && !imageCache.ContainsKey(thumbUrl))
                StartCoroutine(DownloadThumbnail(thumbUrl, item));
            else if (imageCache.ContainsKey(thumbUrl))
                item.ApplySprite(imageCache[thumbUrl]);

            string assetUrl = ResolveUrl(item.frameData.asset_path);
            if (!string.IsNullOrEmpty(assetUrl) && !assetCache.ContainsKey(assetUrl) && !downloadingAssets.Contains(assetUrl))
                StartCoroutine(DownloadAndCacheTextureCoroutine(assetUrl));
        }
    }

    private IEnumerator DownloadThumbnail(string url, FrameItem item)
    {
        if (imageCache.ContainsKey(url))
        {
            item.ApplySprite(imageCache[url]);
            item.SetThumbnailAlpha(1f);
            yield break;
        }

        yield return FrameCacheManager.DownloadAndCacheTexture(url, tex =>
        {
            if (tex != null)
            {
                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
                imageCache[url] = sprite;
                if (item != null)
                {
                    item.ApplySprite(sprite);
                    item.SetThumbnailAlpha(1f);
                }
            }
        });
    }

    private IEnumerator DownloadAndCacheTextureCoroutine(string url)
    {
        if (downloadingAssets.Contains(url)) yield break;
        downloadingAssets.Add(url);

        yield return FrameCacheManager.DownloadAndCacheTexture(url, tex =>
        {
            if (tex != null) assetCache[url] = tex;
            downloadingAssets.Remove(url);
        });
    }

    private string ResolveUrl(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith("http")) return path;
        string baseUrl = apiBaseURL.EndsWith("/") ? apiBaseURL : apiBaseURL + "/";
        if (path.StartsWith("/")) path = path.Substring(1);
        return baseUrl + path;
    }

    void OnCategoryButtonClicked(Button clickedButton)
    {
        if (clickedButton == myFrameButton)
        {
            if (string.IsNullOrEmpty(PlayerPrefs.GetString("user_id")))
            {
                Debug.Log("Login required for My Frames");
                return;
            }
        }

        if (currentSelectedButton != null && currentSelectedButton != clickedButton)
            ResetButtonSprite(currentSelectedButton);

        ApplySelectedSprite(clickedButton);
        currentSelectedButton = clickedButton;

        currentCategory = clickedButton == defaultButton ? "default" :
                          clickedButton == recommendationButton ? "recommended" :
                          clickedButton == gatchaButton ? "gacha" :
                          clickedButton == myFrameButton ? "myframe" : "default";


        // LOG: Category change
        LoggingManager.Instance?.LogCustomerClick(
            buttonName: currentCategory,
            screenName: "CategorySelection"
        );

        StartCoroutine(FetchFramesFromServer());
    }

    void ResetButtonSprite(Button button)
    {
        if (button && normalSprites.TryGetValue(button, out Sprite s))
            button.image.sprite = s;
    }

    void ApplySelectedSprite(Button button)
    {
        if (button && button.spriteState.selectedSprite != null)
            button.image.sprite = button.spriteState.selectedSprite;
    }

    public void SelectFrame(FrameItem item)
    {
        if (currentSelectedFrame != null && currentSelectedFrame != item)
            currentSelectedFrame.Deselect();

        currentSelectedFrame = item;
        currentSelectedFrame?.Select();

        //LOG: Frame Selected
        LoggingManager.Instance?.LogCustomerClick(
       buttonName: "FrameSelection",
       screenName: "FrameManager",
       frameId: item.frameData.frame_id
   );
    }

    public FrameItem GetSelectedFrameItem() => currentSelectedFrame;

    // ==================================================================
    // MEMORY SAFETY – PREVENT VRAM CRASH
    // ==================================================================
    public void ClearAssetCache()
    {
        foreach (var kvp in assetCache)
            if (kvp.Value != null) Destroy(kvp.Value);

        assetCache.Clear();
        imageCache.Clear();
        downloadingAssets.Clear();
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

    // ==================================================================
    // YOUR EXISTING METHODS (Payment, Gacha, Shooting) – UNCHANGED
    // ==================================================================

    public void OnDecideButtonClicked()
    {
        FrameItem selectedItem = GetSelectedFrameItem();
        if (selectedItem == null)
        {
            Debug.LogWarning("❌ No frame selected!");
            return;
        }

        Debug.Log($"✅ Decide button clicked with frame: {selectedItem.frameData.frame_id}");

        // CRITICAL FIX: Check gacha flow flag
        if (PaymentManager.Instance != null && PaymentManager.Instance.IsInGachaFlow())
        {
            Debug.Log("✅ In gacha flow - proceeding directly to shooting");
            ContinueAfterPayment(selectedItem);
            return;
        }

        // Normal frame selection flow - check if payment is needed
        bool paymentsEnabled = PlayerPrefs.GetInt("payments_enabled", 0) == 1;
        if (paymentsEnabled && PaymentManager.Instance != null)
        {
            string price = PlayerPrefs.GetString("booth_price", "700");
            PaymentManager.Instance.InitiateFramePayment(boothID, selectedItem, price);
        }
        else
        {
            ContinueAfterPayment(selectedItem);
        }
    }

    public void ContinueAfterPayment(FrameItem selectedItem)
    {
        if (selectedItem == null)
        {
            Debug.LogError("❌ ContinueAfterPayment: selectedItem is NULL!");
            return;
        }

        Debug.Log($"📸 ContinueAfterPayment for frame: {selectedItem.frameData.frame_id}");

        foreach (Transform child in startShootingParent)
            Destroy(child.gameObject);

        if (startShootingPrefab == null)
        {
            Debug.LogError("❌ startShootingPrefab is NULL!");
            return;
        }

        GameObject instance = Instantiate(startShootingPrefab, startShootingParent);
        instance.SetActive(true);

        Debug.Log("✅ Start shooting prefab instantiated");

        Image img = instance.GetComponentInChildren<Image>();
        if (img != null && selectedItem.frameImg != null)
        {
            img.sprite = selectedItem.frameImg.sprite;
            Debug.Log("✅ Frame thumbnail set");
        }

        Button startButton = instance.GetComponentInChildren<Button>();
        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(() =>
            {
                Debug.Log("🎬 START SHOOTING BUTTON CLICKED!");

                // Clear gacha flow flag when shooting actually starts
                if (PaymentManager.Instance != null)
                {
                    PaymentManager.Instance.ClearGachaFlowFlag();
                }

                PhotoShootingManager.Instance?.StartShooting(selectedItem);
                instance.SetActive(false);
            });
            Debug.Log("✅ Start button configured");
        }
        else
        {
            Debug.LogError("❌ Start button not found in prefab!");
        }

        string assetUrl = ResolveUrl(selectedItem.frameData.asset_path);
        Debug.Log($"🔄 Frame asset URL: {assetUrl}");

        if (assetCache.TryGetValue(assetUrl, out Texture2D tex))
        {
            Debug.Log("✅ Frame asset found in cache");
            CapturedPhotosDisplayManager.Instance?.SetFrame(tex);
        }
        else
        {
            Debug.Log("📥 Downloading frame asset...");
            StartCoroutine(DownloadAndSetFrameForCapture(assetUrl));
        }
    }

    private IEnumerator DownloadAndSetFrameForCapture(string url)
    {
        Texture2D tex = null;
        yield return FrameCacheManager.DownloadAndCacheTexture(url, t => tex = t);
        if (tex != null)
        {
            assetCache[url] = tex;
            CapturedPhotosDisplayManager.Instance?.SetFrame(tex);
        }
    }

    public void OnGatchaPlay()
    {
        bool paymentsEnabled = PlayerPrefs.GetInt("payments_enabled", 0) == 1;
        if (paymentsEnabled && PaymentManager.Instance != null)
        {
            string gachaPrice = PlayerPrefs.GetString("gacha_price", "200");
            PaymentManager.Instance.InitiateGachaPayment(boothID, -1, gachaPrice);

        }
        else
        {
            defaultButton.interactable = false;
            recommendationButton.interactable = false;
            myFrameButton.interactable = false;
            GatchaManager.Instance?.SetBoothID(boothID);
            GatchaManager.Instance?.PlayGatchaAnimation();
        }
    }

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