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
    public int framesPerPage = 6; // Number of frames visible per page
    public int minFramesForScroll = 6; // Configurable threshold
    private int currentPage = 0;
    private int totalPages = 0;
    private bool isManualScrolling = false;
    private float scrollPositionThreshold = 0.1f; // Threshold to detect page change

    [Header("API")]
    private string boothID = "";
    public FrameResponse cachedResponse;

    private string currentCategory = "default";
    private Button currentSelectedButton;
    public FrameItem currentSelectedFrame;
    public string currentSelectedFrameId = ""; 
    private bool isFetching = false;

    // Progress Tracking
    public float DownloadProgress { get; private set; } = 0f;
    private int totalDownloadCount = 0;
    private int completedDownloadCount = 0;

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
        SetupScrollRectListeners();
        ResetToDefaultCategory(); // Use the logic to decide correct starting category
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

        // Add sound to back button if it exists and clear selection
        if (backButton)
        {
            backButton.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayBackBtnSound();
                currentSelectedFrameId = "";
                if (currentSelectedFrame != null)
                {
                    currentSelectedFrame.Deselect();
                    currentSelectedFrame = null;
                }
            });
        }
    }

    private void SetupScrollRectListeners()
    {
        if (scrollRect != null)
        {
            scrollRect.onValueChanged.AddListener(OnScrollValueChanged);
        }
    }

    private void OnScrollValueChanged(Vector2 position)
    {
        if (isManualScrolling || totalPages <= 1) return;

        // Detect which page we're closest to based on scroll position
        int closestPage = Mathf.RoundToInt(scrollRect.horizontalNormalizedPosition * (totalPages - 1));
        closestPage = Mathf.Clamp(closestPage, 0, totalPages - 1);

        if (closestPage != currentPage)
        {
            currentPage = closestPage;
            UpdateNavigationButtons();
        }
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
        isManualScrolling = true;
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
        yield return new WaitForSeconds(0.1f); // Small delay before re-enabling touch detection
        isManualScrolling = false;
    }

    void OnNextClicked()
    {
        if (currentPage < totalPages - 1)
        {
            AudioManager.Instance?.PlayClick();
            currentPage++;
            float targetPosition = (float)currentPage / (totalPages - 1);
            StartCoroutine(ScrollTo(targetPosition));
            UpdateNavigationButtons();
        }
    }

    void OnPrevClicked()
    {
        if (currentPage > 0)
        {
            AudioManager.Instance?.PlayClick();
            currentPage--;
            float targetPosition = currentPage == 0 ? 0f : (float)currentPage / (totalPages - 1);
            StartCoroutine(ScrollTo(targetPosition));
            UpdateNavigationButtons();
        }
    }

    private void UpdateNavigationButtons()
    {
        if (prevButton != null)
            prevButton.interactable = currentPage > 0;

        if (nextButton != null)
            nextButton.interactable = currentPage < totalPages - 1;
    }

    private void CalculateTotalPages(int frameCount)
    {
        totalPages = Mathf.CeilToInt((float)frameCount / framesPerPage);
        currentPage = 0;
        UpdateNavigationButtons();
    }

    public void SetBoothID(string id)
    {
        if (boothID != id)
        {
            Debug.Log($"🆔 Booth ID changing: {boothID} -> {id}. Clearing all caches.");
            ClearAllCaches();
        }
        boothID = id;
    }

    public void ClearAllCaches()
    {
        imageCache.Clear();
        ClearAssetCache();
        downloadingAssets.Clear();
        // Clear frame pool to ensure fresh prefabs
        foreach (var go in framePool)
            if (go != null) Destroy(go);
        framePool.Clear();
        Debug.Log("🧹 All image and asset caches cleared.");
    }

   
    public void ResetToDefaultCategory()
    {
        Debug.Log("🔄 Resetting to default frame category...");

        // **FIX: Ensure boothID is synced from PlayerPrefs at session start**
        string savedBoothId = PlayerPrefs.GetString("booth_id", "");
        if (!string.IsNullOrEmpty(savedBoothId))
        {
            boothID = savedBoothId;
        }

        // Clear any selected frame
        if (currentSelectedFrame != null)
        {
            currentSelectedFrame.Deselect();
            currentSelectedFrame = null;
        }
        currentSelectedFrameId = ""; // Also clear the persistent ID

       
        imageCache.Clear();

        // Check if "Default" category is enabled
        bool isDefaultEnabled = PlayerPrefs.GetInt("default_enabled", 1) == 1;

        if (defaultButton != null)
        {
            defaultButton.gameObject.SetActive(isDefaultEnabled);
        }

        // Determine which category to start with
        if (isDefaultEnabled)
        {
            currentCategory = "default";
            if (defaultButton != null)
            {
                ApplySelectedSprite(defaultButton);
                currentSelectedButton = defaultButton;
            }
        }
        else
        {
            // Fallback to "recommended" if default is disabled
            currentCategory = "recommended";
            if (recommendationButton != null)
            {
                ApplySelectedSprite(recommendationButton);
                currentSelectedButton = recommendationButton;
            }
        }

        // Reset all button visuals to normal state (except selected)
        if (currentSelectedButton != null)
        {
            // Reset others
            if (defaultButton != currentSelectedButton) ResetButtonSprite(defaultButton);
            if (recommendationButton != currentSelectedButton) ResetButtonSprite(recommendationButton);
            if (gatchaButton != currentSelectedButton) ResetButtonSprite(gatchaButton);
            if (myFrameButton != currentSelectedButton) ResetButtonSprite(myFrameButton);
        }

        // Re-enable all category buttons (in case gacha disabled them)
        if (defaultButton) defaultButton.interactable = true;
        if (recommendationButton) recommendationButton.interactable = true;
        if (myFrameButton) myFrameButton.interactable = true;
        if (gatchaButton) gatchaButton.interactable = true;

        // Reset scroll position to start
        if (scrollRect != null)
        {
            scrollRect.horizontalNormalizedPosition = 0f;
        }

        // Clear gacha animations if any
        if (GatchaManager.Instance != null)
        {
            GatchaManager.Instance.ClearSpawnedFramesInstant();
        }

        // Fetch frames (will fetch currentCategory)
        StartCoroutine(FetchFramesFromServer());

        Debug.Log($"✅ Reset complete. Selected category: {currentCategory}");
    }

 
    
   
    public IEnumerator FetchFramesFromServer()
    {
        if (isFetching || string.IsNullOrEmpty(boothID))
        {
            DownloadProgress = 1f; // Ensure progress isn't stuck if we exit early
            yield break;
        }

        isFetching = true;
        string currentBoothAtStart = boothID;
        ClearFrames();

        string url = API.BaseURL + "/api/photobooth/frames";

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
                DownloadProgress = 1f; // ✅ Ensure progress completes even for empty states
                isFetching = false;
                yield break;
            }
        }

        string fullURL = url + "?" + string.Join("&", parameters);
        Debug.Log("Fetching frames → " + fullURL);

        bool isOnline = Application.internetReachability != NetworkReachability.NotReachable;

        if (isOnline)
        {
            // ✅ CHANGED: Use ServerAwareWebRequest instead of UnityWebRequest
            yield return ServerAwareWebRequest.Get(fullURL, (request) =>
            {
                // Ensure we are still on the same booth
                if (boothID != currentBoothAtStart)
                {
                    Debug.LogWarning("Booth ID changed during fetch. Aborting.");
                    DownloadProgress = 1f; // Ensure progress completes
                    return;
                }

                // ✅ Check for connectivity errors
                if (ServerAwareWebRequest.IsConnectivityError(request))
                {
                    Debug.LogWarning("⚠️ Server connectivity issue → loading from cache");
                    StartCoroutine(LoadFramesFromCache(currentCategory, boothID));
                    return;
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string json = request.downloadHandler.text;
                    cachedResponse = JsonUtility.FromJson<FrameResponse>(json);

                    // **FIX: Robustly handle My Frames - check both data.my_frames and data.frames**
                    List<Frame> framesToDisplay = null;

                    if (currentCategory == "myframe")
                    {
                        
                        if (cachedResponse?.data != null)
                        {
                            if (cachedResponse.data.my_frames != null && cachedResponse.data.my_frames.Count > 0)
                                framesToDisplay = cachedResponse.data.my_frames;
                            else
                                framesToDisplay = cachedResponse.data.frames;
                        }
                    }
                    else
                    {
                        framesToDisplay = cachedResponse?.data?.frames;
                    }

                    FrameCacheManager.SaveJSON(json, currentCategory, boothID);
                    DisplayFrames(framesToDisplay);
                }
                else
                {
                    Debug.LogWarning("API failed → loading from cache");
                    StartCoroutine(LoadFramesFromCache(currentCategory, boothID));
                }
            });
        }
        else
        {
            yield return LoadFramesFromCache(currentCategory, boothID);
        }

        isFetching = false;
    }


    public IEnumerator LoadFramesFromCache(string category, string targetBoothID)
    {
        if (!FrameCacheManager.HasCachedData(category, targetBoothID))
        {
            DownloadProgress = 1f; // Ensure progress isn't stuck if we exit early
            yield break;
        }

        string json = FrameCacheManager.LoadCachedJSON(category, targetBoothID);
        if (string.IsNullOrEmpty(json))
        {
            DownloadProgress = 1f; // Ensure progress isn't stuck if we exit early
            yield break;
        }

        cachedResponse = JsonUtility.FromJson<FrameResponse>(json);

        // **FIX: Robustly handle My Frames from cache as well**
        List<Frame> framesToDisplay = null;
        if (category == "myframe")
        {
            if (cachedResponse?.data != null)
            {
                if (cachedResponse.data.my_frames != null && cachedResponse.data.my_frames.Count > 0)
                    framesToDisplay = cachedResponse.data.my_frames;
                else
                    framesToDisplay = cachedResponse.data.frames;
            }
        }
        else
        {
            framesToDisplay = cachedResponse?.data?.frames;
        }

        if (framesToDisplay != null && boothID == targetBoothID)
            DisplayFrames(framesToDisplay);
    }

    private Queue<GameObject> framePool = new Queue<GameObject>();

    public void ClearFrames()
    {
        foreach (var item in currentFrameItems)
            item?.Deselect();

        currentFrameItems.Clear();
        currentSelectedFrame = null;

        // Populate pool with current children (only active ones to avoid double-enqueuing)
        for (int i = contentParent.childCount - 1; i >= 0; i--)
        {
            Transform child = contentParent.GetChild(i);
            if (child == null) continue;
            GameObject go = child.gameObject;
            
            // Check if it's a frame item
            if (go.GetComponent<FrameItem>())
            {
                if (go.activeSelf)
                {
                    go.SetActive(false);
                    framePool.Enqueue(go);
                }
            }
            else
            {
                // If it's the empty state message, destroy it
                if (go.GetComponentInChildren<TextMeshProUGUI>())
                    Destroy(go);
            }
        }
    }

    private void DisplayFrames(List<Frame> frames)
    {
        if (GatchaManager.Instance != null)
            GatchaManager.Instance.ClearSpawnedFramesInstant();

        ClearFrames();

        if (frames == null || frames.Count == 0)
        {
            ShowEmptyState(currentCategory == "myframe" ? "You have no frames yet" : "No frames available");
            UpdateScrollButtons(0); // Hide scroll buttons when empty
            DownloadProgress = 1f; // ✅ Ensure progress completes if there are no frames to download
            return;
        }

        foreach (Frame frame in frames)
        {
            GameObject obj = null;
            
            // Try to get a valid object from pool
            while (framePool.Count > 0)
            {
                GameObject temp = framePool.Dequeue();
                if (temp != null)
                {
                    obj = temp;
                    break;
                }
            }

            if (obj != null)
            {
                obj.transform.SetParent(contentParent, false); // Ensure it's at the end
                obj.SetActive(true);
            }
            else
            {
                obj = Instantiate(framePrefab, contentParent);
                obj.SetActive(true);
            }

            Button btn = obj.GetComponent<Button>();
            if (btn != null)
                btn.transition = Selectable.Transition.None;

            FrameItem item = obj.GetComponent<FrameItem>();
            if (item != null)
            {
                item.Setup(frame, currentCategory);
                item.DisableSelection(currentCategory == "gacha");
                
                // RESTORE SELECTION
                if (currentCategory != "gacha" && !string.IsNullOrEmpty(currentSelectedFrameId) && frame.frame_id == currentSelectedFrameId)
                {
                    currentSelectedFrame = item;
                    item.Select();
                }
                
                currentFrameItems.Add(item);
            }

            if (currentCategory == "gacha" && GatchaManager.Instance != null)
                GatchaManager.Instance.RegisterSpawnedFrame(obj);
        }

        decideButton.gameObject.SetActive(currentCategory != "gacha");
        playButton.gameObject.SetActive(currentCategory == "gacha");

        // Update scroll button visibility based on frame count
        UpdateScrollButtons(frames.Count);

        // **FIX: Pass a COPY of the list to the coroutine to safely handle rapid navigation**
        StartCoroutine(DownloadThumbnailsAndAssetsParallel(new List<FrameItem>(currentFrameItems)));
    }

    private void UpdateScrollButtons(int frameCount)
    {
        bool shouldShowScrollButtons = frameCount > minFramesForScroll;

        if (nextButton != null)
            nextButton.gameObject.SetActive(shouldShowScrollButtons);

        if (prevButton != null)
            prevButton.gameObject.SetActive(shouldShowScrollButtons);

        // Calculate pages and reset to first page
        if (shouldShowScrollButtons)
        {
            CalculateTotalPages(frameCount);
        }
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

        // Hide scroll buttons in empty state
        UpdateScrollButtons(0);
    }

    private IEnumerator DownloadThumbnailsAndAssetsParallel(List<FrameItem> items)
    {
        if (items == null || items.Count == 0)
        {
            DownloadProgress = 1f;
            yield break;
        }

        completedDownloadCount = 0;
        totalDownloadCount = 0;

        // Count downloads
        foreach (FrameItem item in items)
        {
            if (item == null || item.frameData == null) continue;
            string thumbUrl = ResolveUrl(item.frameData.thumb_path);
            if (!string.IsNullOrEmpty(thumbUrl) && !imageCache.ContainsKey(thumbUrl)) totalDownloadCount++;
            string assetUrl = ResolveUrl(item.frameData.asset_path);
            if (!string.IsNullOrEmpty(assetUrl) && !assetCache.ContainsKey(assetUrl) && !downloadingAssets.Contains(assetUrl)) totalDownloadCount++;
        }

      
        if (totalDownloadCount == 0)
        {
            DownloadProgress = 1f;
        }

        DownloadProgress = 0f;
        foreach (FrameItem item in items)
        {
            if (item == null || item.frameData == null) continue;
            
            string thumbUrl = ResolveUrl(item.frameData.thumb_path);
            if (!string.IsNullOrEmpty(thumbUrl))
            {
                if (!imageCache.ContainsKey(thumbUrl))
                    StartCoroutine(DownloadThumbnail(thumbUrl, item, OnDownloadItemComplete));
                else
                {
                    item.ApplySprite(imageCache[thumbUrl]);
                    item.SetThumbnailAlpha(1f);
                }
            }

            string assetUrl = ResolveUrl(item.frameData.asset_path);
            if (!string.IsNullOrEmpty(assetUrl) && !assetCache.ContainsKey(assetUrl) && !downloadingAssets.Contains(assetUrl))
                StartCoroutine(DownloadAndCacheTextureCoroutine(assetUrl, OnDownloadItemComplete));
        }

        while (completedDownloadCount < totalDownloadCount) yield return null;
        DownloadProgress = 1f;
    }

    private void OnDownloadItemComplete()
    {
        completedDownloadCount++;
        DownloadProgress = (float)completedDownloadCount / totalDownloadCount;
    }

    private IEnumerator DownloadThumbnail(string url, FrameItem item, System.Action onComplete = null)
    {
        if (imageCache.ContainsKey(url))
        {
            onComplete?.Invoke();
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
            else
            {
                Debug.LogWarning($"❌ Thumbnail download failed for URL: {url}");
                if (item != null)
                {
                    // Fallback to offline texture if available
                    if (item.offlineTexture != null)
                    {
                        item.ApplySprite(Sprite.Create(item.offlineTexture, new Rect(0,0, item.offlineTexture.width, item.offlineTexture.height), Vector2.one * 0.5f));
                        item.SetThumbnailAlpha(0.5f); // Dimmed to show it's offline/failed
                    }
                }
            }
            onComplete?.Invoke();
        });
    }

    private IEnumerator DownloadAndCacheTextureCoroutine(string url, System.Action onComplete = null)
    {
        if (assetCache.ContainsKey(url) || downloadingAssets.Contains(url))
        {
            onComplete?.Invoke();
            yield break;
        }
        
        downloadingAssets.Add(url);
        yield return FrameCacheManager.DownloadAndCacheTexture(url, tex =>
        {
            if (tex != null)
            {
                assetCache[url] = tex;
            }
            else
            {
                Debug.LogWarning($"❌ Asset download failed for URL: {url}");
            }
            
            downloadingAssets.Remove(url);
            onComplete?.Invoke();
        });
    }

    public string ResolveUrl(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith("http")) return path;
        string baseUrl = API.BaseURL.EndsWith("/") ? API.BaseURL : API.BaseURL + "/";
        if (path.StartsWith("/")) path = path.Substring(1);
        return baseUrl + path;
    }

    void OnCategoryButtonClicked(Button clickedButton)
    {
        AudioManager.Instance?.PlayClick();
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
        
        if (item != null && item.frameData != null)
        {
            currentSelectedFrameId = item.frameData.frame_id;
            currentSelectedFrame.Select();
        }
        else
        {
            currentSelectedFrameId = "";
        }

        // Play frame selection sound for specific categories
        if (currentCategory == "default" || currentCategory == "recommended" || currentCategory == "myframe")
        {
            AudioManager.Instance?.PlayFrameSelection();
        }

        if (item != null && item.frameData != null)
        {
            Debug.Log($"[Current Selected Frame] Name: {item.frameData.title}, ID: {item.frameData.frame_id}, Shots: {item.frameData.number_of_shots}, Category: {item.frameData.category}");
            Debug.Log($"[Frame Data Full]\n{JsonUtility.ToJson(item.frameData, true)}");
        }

        //LOG: Frame Selected
        LoggingManager.Instance?.LogCustomerClick(
       buttonName: "FrameSelection",
       screenName: "FrameManager",
       frameId: item.frameData.frame_id
   );
    }

    public FrameItem GetSelectedFrameItem() => currentSelectedFrame;

   
    public void ClearAssetCache()
    {
        foreach (var kvp in assetCache)
            if (kvp.Value != null) Destroy(kvp.Value);

        assetCache.Clear();
        imageCache.Clear();
        downloadingAssets.Clear();
    }




    public void OnDecideButtonClicked()
    {
        AudioManager.Instance?.PlayClick();
        FrameItem selectedItem = GetSelectedFrameItem();
        if (selectedItem == null)
        {
            Debug.LogWarning("❌ No frame selected!");
            return;
        }

        Debug.Log($"✅ Decide button clicked with frame: {selectedItem.frameData.frame_id}");

        // Use PaymentManager to get an order ID even if payment is OFF
        PaymentManager.Instance.InitiateFramePaymentForDecide(
            boothId: PaymentManager.Instance.frameManager.boothID,
            selectedFrame: selectedItem,
            price: PlayerPrefs.GetString("booth_price", "700"),
            frameType: currentCategory
        );
    }



    public void ContinueAfterPayment(FrameItem selectedItem, string orderID = null)
    {
        if (selectedItem == null)
        {
            Debug.LogError("❌ ContinueAfterPayment: selectedItem is NULL!");
            return;
        }

        Debug.Log($"📸 ContinueAfterPayment for frame: {selectedItem.frameData.frame_id}, orderID: {orderID}");

        // Clear previous shooting prefab
        foreach (Transform child in startShootingParent)
            Destroy(child.gameObject);

        if (startShootingPrefab == null)
        {
            Debug.LogError("❌ startShootingPrefab is NULL!");
            return;
        }

        GameObject instance = Instantiate(startShootingPrefab, startShootingParent);
        instance.SetActive(true);

        // Resize based on scene
        RectTransform rt = instance.GetComponent<RectTransform>();
        if (rt != null)
        {
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (sceneName.Equals("Portrait", System.StringComparison.OrdinalIgnoreCase))
            {
                rt.sizeDelta = new Vector2(1080, 1920);

                // Resize Background and darkpanel to 2000x2000 for Portrait
                Transform bg = instance.transform.Find("Background");
                if (bg != null)
                {
                    RectTransform bgRect = bg.GetComponent<RectTransform>();
                    if (bgRect != null) bgRect.sizeDelta = new Vector2(2000, 3000);
                }

                Transform dp = instance.transform.Find("darkpanel");
                if (dp != null)
                {
                    RectTransform dpRect = dp.GetComponent<RectTransform>();
                    if (dpRect != null) dpRect.sizeDelta = new Vector2(2000, 3000);
                }
            }
            else
            {
                rt.sizeDelta = new Vector2(1920, 1080);
            }
        }

        // Find the "Frame" child
        Transform frameTransform = instance.transform.Find("Frame");
        if (frameTransform != null)
        {
            // Create a new GameObject for the image
            GameObject frameImageObj = new GameObject("FrameImage");
            frameImageObj.transform.SetParent(frameTransform, false);

            // Add Image component
            Image img = frameImageObj.AddComponent<Image>();
            if (selectedItem.frameImg != null)
            {
                img.sprite = selectedItem.frameImg.sprite;
                img.preserveAspect = true; // ✅ Set Preserve Aspect to true
                Debug.Log("✅ Frame instantiated inside 'Frame' with PreserveAspect=true");
            }

            // Set RectTransform to stretch to fill the Frame container
            RectTransform imgRect = frameImageObj.GetComponent<RectTransform>();
            imgRect.anchorMin = Vector2.zero;
            imgRect.anchorMax = Vector2.one;
            imgRect.offsetMin = Vector2.zero;
            imgRect.offsetMax = Vector2.zero;
        }
        else
        {
            Debug.LogError("❌ 'Frame' child not found in startShootingPrefab!");
        }
        
        Button startButton = instance.GetComponentInChildren<Button>();
        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayClick();
                Debug.Log("🎬 START SHOOTING BUTTON CLICKED!");

                // Clear gacha flow flag when shooting actually starts
                PaymentManager.Instance?.ClearGachaFlowFlag();

                // Pass orderID to shooting manager if needed
                PhotoShootingManager.Instance?.StartShooting(selectedItem, orderID);
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
        AudioManager.Instance?.PlayClick();
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