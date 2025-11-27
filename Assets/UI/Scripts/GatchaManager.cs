using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class GatchaManager : MonoBehaviour
{
    public static GatchaManager Instance;

    [Header("Gatcha Settings")]
    public GameObject gatchaObject;
    public GameObject gatchaButtons;
    public GameObject gatchaResult;
    public GameObject framePrefab;

    public string gatchaAnimationName = "GatchaAnim";
    public float dissolveDuration = 1.0f;

    public GameObject darkpanel;
    public GameObject darkpanel1;
    public GameObject celebration;

    [Header("Gatcha Win Panel")]
    public GameObject gatchaWin;
    public Transform winFrameParent;
    public float gatchaWinFadeDuration = 0.5f;

    [Header("API Settings")]
    public string apiBaseURL = "http://photo-stg-api.chvps3.aozora-okinawa.com/";
    private string boothID = "";

    private readonly List<GameObject> spawnedFrames = new List<GameObject>();
    private List<Frame> gachaFrames = new List<Frame>();
    private Frame clickedResultFrame;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }


    public void SetBoothID(string id) => boothID = id;

    // ============================================================
    // MODIFIED: ShowGatchaButtons() - NEW METHOD for payment flow
    // ============================================================
    public void ShowGatchaButtons()
    {
        darkpanel.SetActive(true);
        gatchaButtons.SetActive(false);
        PhotoBoothFrameManager.Instance.playButton.gameObject.SetActive(false);
        PhotoBoothFrameManager.Instance.backButton.gameObject.SetActive(false);
        StartCoroutine(DissolveFramesThenShowButtons());
    }

    // ============================================================
    // KEPT: PlayGatchaAnimation() - For non-payment flow (if needed)
    // ============================================================
    public void PlayGatchaAnimation()
    {
        darkpanel.SetActive(true);
        gatchaButtons.SetActive(false);
        PhotoBoothFrameManager.Instance.playButton.gameObject.SetActive(false);
        PhotoBoothFrameManager.Instance.backButton.gameObject.SetActive(false);
        StartCoroutine(DissolveFramesThenPlayAnimation());
    }

    // ============================================================
    // NEW: PlayGatchaAnimationAfterPayment() - Called after payment success
    // ============================================================
    public void PlayGatchaAnimationAfterPayment()
    {
        Debug.Log("🎰 Starting gacha animation after payment");

        darkpanel.SetActive(true);
        gatchaButtons.SetActive(false);
        PhotoBoothFrameManager.Instance.playButton.gameObject.SetActive(false);
        PhotoBoothFrameManager.Instance.backButton.gameObject.SetActive(false);

        StartCoroutine(DissolveFramesThenPlayAnimationAndShowButtons());
    }

    private IEnumerator DissolveFramesThenPlayAnimationAndShowButtons()
    {
        // Dissolve existing frames
        if (spawnedFrames.Count > 0)
        {
            foreach (GameObject frame in spawnedFrames)
                if (frame != null)
                    StartCoroutine(DissolveAndDestroy(frame));

            yield return new WaitForSeconds(dissolveDuration + 0.1f);
            spawnedFrames.Clear();
        }

        // Play gacha animation
        if (gatchaObject != null)
        {
            gatchaObject.SetActive(true);
            Animator anim = gatchaObject.GetComponent<Animator>();
            if (anim != null)
            {
                anim.Play(gatchaAnimationName, 0, 0f);
                yield return new WaitForSeconds(anim.GetCurrentAnimatorStateInfo(0).length);
            }
            gatchaObject.SetActive(false);
        }

        // Show gacha selection buttons
        gatchaButtons.SetActive(true);

        for (int i = 0; i < gatchaButtons.transform.childCount; i++)
        {
            int index = i;
            Button btn = gatchaButtons.transform.GetChild(i).GetComponent<Button>();
            if (btn != null)
            {
                btn.interactable = true;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnGachaChildButtonClickedAfterPayment(index));
            }
        }
    }

    // ============================================================
    // NEW: OnGachaChildButtonClickedAfterPayment() - No payment check
    // ============================================================
    private void OnGachaChildButtonClickedAfterPayment(int buttonIndex)
    {
        Debug.Log($"🎰 Gacha button {buttonIndex} clicked (payment already done)");

        // Proceed directly to reveal
        foreach (Transform child in gatchaButtons.transform)
            child.GetComponent<Button>().interactable = false;

        StartCoroutine(ShakeThenReveal(buttonIndex));
    }

    // ============================================================
    // NEW: DissolveFramesThenShowButtons() - Shows buttons without animation
    // ============================================================
    private IEnumerator DissolveFramesThenShowButtons()
    {
        if (spawnedFrames.Count > 0)
        {
            foreach (GameObject frame in spawnedFrames)
                if (frame != null)
                    StartCoroutine(DissolveAndDestroy(frame));

            yield return new WaitForSeconds(dissolveDuration + 0.1f);
            spawnedFrames.Clear();
        }

        gatchaButtons.SetActive(true);

        for (int i = 0; i < gatchaButtons.transform.childCount; i++)
        {
            int index = i;
            Button btn = gatchaButtons.transform.GetChild(i).GetComponent<Button>();
            if (btn != null)
            {
                btn.interactable = true;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnGachaChildButtonClicked(index));
            }
        }
    }

    private IEnumerator DissolveFramesThenPlayAnimation()
    {
        if (spawnedFrames.Count > 0)
        {
            foreach (GameObject frame in spawnedFrames)
                if (frame != null)
                    StartCoroutine(DissolveAndDestroy(frame));

            yield return new WaitForSeconds(dissolveDuration + 0.1f);
            spawnedFrames.Clear();
        }

        if (gatchaObject != null)
        {
            gatchaObject.SetActive(true);
            Animator anim = gatchaObject.GetComponent<Animator>();
            if (anim != null)
            {
                anim.Play(gatchaAnimationName, 0, 0f);
                yield return new WaitForSeconds(anim.GetCurrentAnimatorStateInfo(0).length);
            }
            gatchaObject.SetActive(false);
        }

        gatchaButtons.SetActive(true);

        for (int i = 0; i < gatchaButtons.transform.childCount; i++)
        {
            int index = i;
            Button btn = gatchaButtons.transform.GetChild(i).GetComponent<Button>();
            if (btn != null)
            {
                btn.interactable = true;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnGachaChildButtonClicked(index));
            }
        }
    }

    // ============================================================
    // MODIFIED: OnGachaChildButtonClicked() - Payment integration
    // ============================================================
    private void OnGachaChildButtonClicked(int buttonIndex)
    {
        bool paymentsEnabled = PlayerPrefs.GetInt("payments_enabled", 0) == 1;

        if (paymentsEnabled && PaymentManager.Instance != null)
        {
            // Get gacha price
            string gachaPrice = PlayerPrefs.GetString("gacha_price", "500");

            // Initiate payment with button index
            PaymentManager.Instance.InitiateGachaPayment(boothID, gachaPrice, buttonIndex);
        }
        else
        {
            // No payment - proceed directly
            ContinueGachaAfterPayment(buttonIndex);
        }
    }

    // ============================================================
    // NEW: ContinueGachaAfterPayment() - Called after payment success
    // ============================================================
    public void ContinueGachaAfterPayment(int buttonIndex)
    {
        foreach (Transform child in gatchaButtons.transform)
            child.GetComponent<Button>().interactable = false;

        StartCoroutine(ShakeThenReveal(buttonIndex));
    }

    private IEnumerator ShakeThenReveal(int index)
    {
        Transform btn = gatchaButtons.transform.GetChild(index);

        // 1. Shake clicked button
        yield return StartCoroutine(ShakeButton(btn, 1f, 25f));

        // 2. Fade out remaining buttons smoothly (they will be revealed later)
        for (int i = 0; i < gatchaButtons.transform.childCount; i++)
        {
            if (i == index) continue;
            Transform otherBtn = gatchaButtons.transform.GetChild(i);
            CanvasGroup cg = otherBtn.GetComponent<CanvasGroup>();
            if (cg == null) cg = otherBtn.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            otherBtn.gameObject.SetActive(true);
        }

        // 3. Move clicked button to center
        Vector3 originalPos = btn.localPosition;
        Vector3 centerPos = Vector3.zero;
        float moveDuration = 0.5f;
        float t = 0f;
        while (t < moveDuration)
        {
            t += Time.deltaTime;
            btn.localPosition = Vector3.Lerp(originalPos, centerPos, Mathf.SmoothStep(0f, 1f, t / moveDuration));
            yield return null;
        }
        btn.localPosition = centerPos;

        // 4. Fetch gacha result and instantiate in gatchaResult
        yield return StartCoroutine(FetchGachaResultAndInstantiate(index));
    }


    private IEnumerator ShakeButton(Transform target, float duration, float magnitude)
    {
        Vector3 originalPos = target.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float damping = 1f - (elapsed / duration);
            float x = Random.Range(-1f, 1f) * magnitude * damping;
            float y = Random.Range(-1f, 1f) * magnitude * damping;
            target.localPosition = originalPos + new Vector3(x, y, 0);
            elapsed += Time.deltaTime;
            yield return null;
        }

        target.localPosition = originalPos;
    }

    private IEnumerator FetchGachaResultAndInstantiate(int index)
    {
        darkpanel1.SetActive(true);
        if (string.IsNullOrEmpty(boothID))
        {
            Debug.LogWarning("BoothID not set!");
            yield break;
        }

        bool useCache = Application.internetReachability == NetworkReachability.NotReachable;
        string url = $"{apiBaseURL}api/photobooth/booths/{boothID}/gacha-frame";

        if (!useCache)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string jsonResponse = request.downloadHandler.text;
                    GachaFrameResponse response = JsonUtility.FromJson<GachaFrameResponse>(jsonResponse);

                    if (response != null && response.frame != null)
                    {
                        clickedResultFrame = response.frame;
                        if (!gachaFrames.Contains(response.frame))
                            gachaFrames.Insert(0, response.frame);

                        yield return InstantiateFrameOnButton(index, response.frame, Vector3.one, true);
                        StartCoroutine(RevealRemainingRandomFrames(index));
                        yield break;
                    }
                }
                else useCache = true;
            }
        }

        // --- Offline fallback ---
        if (useCache && FrameCacheManager.HasCachedData("gacha"))
        {
            string cachedJson = FrameCacheManager.LoadCachedJSON("gacha");
            if (!string.IsNullOrEmpty(cachedJson))
            {
                FrameResponse cachedResponse = JsonUtility.FromJson<FrameResponse>(cachedJson);
                if (cachedResponse != null && cachedResponse.data.frames.Count > 0)
                {
                    clickedResultFrame = cachedResponse.data.frames[Random.Range(0, cachedResponse.data.frames.Count)];
                    if (!gachaFrames.Contains(clickedResultFrame))
                        gachaFrames.Insert(0, clickedResultFrame);

                    yield return InstantiateFrameOnButton(index, clickedResultFrame, Vector3.one, true);
                    StartCoroutine(RevealRemainingRandomFrames(index));
                    yield break;
                }
            }
        }

        Debug.LogWarning("No cached data available for Gacha fallback.");
    }

    private IEnumerator RevealRemainingRandomFrames(int revealedIndex)
    {
        yield return new WaitForSeconds(0.2f);

        List<int> remainingIndices = new List<int>();
        for (int i = 0; i < gatchaButtons.transform.childCount; i++)
            if (i != revealedIndex) remainingIndices.Add(i);

        // Build available frame list
        List<Frame> availableFrames = new List<Frame>();
        if (gachaFrames.Count > 1)
        {
            availableFrames.AddRange(gachaFrames);
            availableFrames.Remove(clickedResultFrame);
        }
        else if (FrameCacheManager.HasCachedData("gacha"))
        {
            string cachedJson = FrameCacheManager.LoadCachedJSON("gacha");
            if (!string.IsNullOrEmpty(cachedJson))
            {
                FrameResponse cachedResponse = JsonUtility.FromJson<FrameResponse>(cachedJson);
                if (cachedResponse != null && cachedResponse.data.frames.Count > 0)
                {
                    availableFrames.AddRange(cachedResponse.data.frames);
                    availableFrames.Remove(clickedResultFrame);
                }
            }
        }

        List<GameObject> revealedFrames = new List<GameObject>();

        foreach (int btnIndex in remainingIndices)
        {
            Frame randomFrame = availableFrames[Random.Range(0, availableFrames.Count)];
            yield return InstantiateFrameOnButton(btnIndex, randomFrame, new Vector3(0.6f, 0.6f, 0.6f));

            if (gatchaButtons.transform.GetChild(btnIndex).childCount > 0)
            {
                revealedFrames.Add(
                    gatchaButtons.transform.GetChild(btnIndex).GetChild(0).gameObject
                );
            }
        }

        yield return new WaitForSeconds(5f);

        float fadeDuration = 1.2f;
        float t = 0f;

        CanvasGroup btnCg = gatchaButtons.GetComponent<CanvasGroup>();
        if (btnCg == null) btnCg = gatchaButtons.AddComponent<CanvasGroup>();

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, t / fadeDuration);

            foreach (GameObject f in revealedFrames)
            {
                if (f != null)
                {
                    CanvasGroup cg = f.GetComponent<CanvasGroup>();
                    if (cg == null) cg = f.AddComponent<CanvasGroup>();
                    cg.alpha = alpha;
                }
            }

            btnCg.alpha = alpha;
            yield return null;
        }

        foreach (GameObject f in revealedFrames)
            if (f != null) Destroy(f);

        gatchaButtons.SetActive(false);
        StartCoroutine(ShowGatchaWinPanel(clickedResultFrame));
    }


    private IEnumerator InstantiateFrameOnButton(int buttonIndex, Frame frame, Vector3 targetScale, bool isResultFrame = false)
    {
        celebration.SetActive(true);
        StartCoroutine(WaitForCelebrationThenFadeButtons());

        Transform parentTransform;

        if (isResultFrame)
        {
            if (gatchaResult == null || buttonIndex >= gatchaResult.transform.childCount)
            {
                Debug.LogWarning("Gacha Result parent not properly set or index out of range!");
                yield break;
            }
            parentTransform = gatchaResult.transform.GetChild(buttonIndex);
        }
        else
        {
            if (buttonIndex < 0 || buttonIndex >= gatchaButtons.transform.childCount)
            {
                Debug.LogWarning("Invalid button index");
                yield break;
            }
            parentTransform = gatchaButtons.transform.GetChild(buttonIndex);
            parentTransform.gameObject.SetActive(true);
            parentTransform.localScale = Vector3.one;
            CanvasGroup cgParent = parentTransform.GetComponent<CanvasGroup>();
            if (cgParent == null) cgParent = parentTransform.gameObject.AddComponent<CanvasGroup>();
            cgParent.alpha = 1f;
        }

        foreach (Transform child in parentTransform)
            Destroy(child.gameObject);

        GameObject obj = Instantiate(framePrefab, parentTransform);
        obj.SetActive(true);
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localScale = Vector3.zero;

        if (isResultFrame) obj.transform.SetAsLastSibling();

        CanvasGroup cg = obj.GetComponent<CanvasGroup>();
        if (cg == null) cg = obj.AddComponent<CanvasGroup>();
        cg.alpha = 1f;

        Image rootImg = obj.GetComponent<Image>();
        if (rootImg != null)
        {
            Color resultColor = new Color(233 / 255f, 199 / 255f, 32 / 255f, 1f);
            Color remainingColor = new Color(207 / 255f, 207 / 255f, 207 / 255f, 1f);
            rootImg.color = isResultFrame ? resultColor : remainingColor;
        }

        FrameItem item = obj.GetComponent<FrameItem>();
        if (item != null)
        {
            item.SetupFromGacha(frame);
            item.DisableSelection(true);
        }

        if (!string.IsNullOrEmpty(frame.thumb_path) && item != null && item.frameImg != null)
        {
            bool isOffline = Application.internetReachability == NetworkReachability.NotReachable;
            Texture2D loadedTex = null;

            if (isOffline)
            {
                yield return FrameCacheManager.LoadCachedTexture(frame.thumb_path, tex => loadedTex = tex);
                if (loadedTex == null)
                    Debug.LogWarning("[Gatcha] No cached thumbnail for offline frame: " + frame.thumb_path);
            }
            else
            {
                Debug.Log("[Gatcha] Downloading thumbnail: " + frame.thumb_path);
                yield return FrameCacheManager.DownloadAndCacheTexture(frame.thumb_path, tex => loadedTex = tex);

                if (loadedTex == null)
                    yield return FrameCacheManager.LoadCachedTexture(frame.thumb_path, tex => loadedTex = tex);
            }

            if (loadedTex != null)
            {
                Sprite s = Sprite.Create(loadedTex, new Rect(0, 0, loadedTex.width, loadedTex.height), new Vector2(0.5f, 0.5f));
                item.frameImg.sprite = s;
                Color c = item.frameImg.color; c.a = 1f; item.frameImg.color = c;
            }
        }

        spawnedFrames.Add(obj);

        if (isResultFrame)
            StartCoroutine(ZoomIn(obj.transform, 0.7f, new Vector3(1.3f, 1.3f, 0)));
        else
            StartCoroutine(ZoomIn(obj.transform, 0.6f, targetScale));

        yield return null;
    }

    private IEnumerator WaitForCelebrationThenFadeButtons()
    {
        Animator anim = celebration.GetComponent<Animator>();
        if (anim != null)
        {
            yield return new WaitForSeconds(anim.GetCurrentAnimatorStateInfo(0).length);
        }

        StartCoroutine(FadeOutGatchaButtonsAndDissolve(1.5f));
    }

    private IEnumerator FadeOutGatchaButtonsAndDissolve(float duration)
    {
        CanvasGroup cg = gatchaButtons.GetComponent<CanvasGroup>();
        if (cg == null) cg = gatchaButtons.AddComponent<CanvasGroup>();

        float t = 0f;
        Vector3 originalScale = gatchaButtons.transform.localScale;

        while (t < duration)
        {
            t += Time.deltaTime;
            cg.alpha = Mathf.Lerp(1f, 0f, t / duration);
            gatchaButtons.transform.localScale = Vector3.Lerp(originalScale, Vector3.zero, t / duration);
            yield return null;
        }

        cg.alpha = 0f;
        gatchaButtons.SetActive(false);
        darkpanel.SetActive(false);
        darkpanel1.SetActive(false);
    }

    private IEnumerator ZoomIn(Transform target, float duration, Vector3 targetScale)
    {
        float t = 0f;
        Vector3 startScale = Vector3.zero;
        while (t < duration)
        {
            if (target == null) yield break;
            t += Time.deltaTime;
            float scale = Mathf.SmoothStep(0f, 1f, t / duration);
            target.localScale = Vector3.Lerp(startScale, targetScale, scale);
            yield return null;
        }
        if (target != null) target.localScale = targetScale;
    }

    private IEnumerator DissolveAndDestroy(GameObject obj)
    {
        float t = 0f;
        CanvasGroup cg = obj.GetComponent<CanvasGroup>();
        if (cg == null) cg = obj.AddComponent<CanvasGroup>();

        while (t < dissolveDuration)
        {
            t += Time.deltaTime;
            cg.alpha = Mathf.Lerp(1f, 0f, t / dissolveDuration);
            yield return null;
        }

        Destroy(obj);
    }

    public void RegisterSpawnedFrame(GameObject frameObj)
    {
        if (frameObj != null && !spawnedFrames.Contains(frameObj))
            spawnedFrames.Add(frameObj);
    }

    public void ClearSpawnedFramesInstant()
    {
        foreach (var obj in spawnedFrames)
            if (obj != null) Destroy(obj);
        spawnedFrames.Clear();
    }

    public void SetGachaFrameList(List<Frame> frames)
    {
        gachaFrames = new List<Frame>(frames);
    }

    private IEnumerator ShowGatchaWinPanel(Frame resultFrame)
    {
        if (gatchaWin == null || winFrameParent == null)
            yield break;

        gatchaWin.SetActive(true);

        CanvasGroup cg = gatchaWin.GetComponent<CanvasGroup>();
        if (cg == null) cg = gatchaWin.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        float t = 0f;
        float panelFadeTime = 0.25f;

        while (t < panelFadeTime)
        {
            t += Time.deltaTime;
            cg.alpha = Mathf.Lerp(0f, 1f, t / panelFadeTime);
            yield return null;
        }
        cg.alpha = 1f;

        foreach (Transform child in winFrameParent)
            Destroy(child.gameObject);

        GameObject obj = Instantiate(framePrefab, winFrameParent);

        RectTransform rect = obj.GetComponent<RectTransform>();
        if (rect == null) rect = obj.AddComponent<RectTransform>();

        rect.localPosition = Vector3.zero;
        obj.transform.localScale = new Vector3(2f, 2f, 2f);
        obj.transform.localRotation = Quaternion.identity;

        FrameItem item = obj.GetComponent<FrameItem>();
        if (item != null)
        {
            item.SetupFromGacha(resultFrame);
            item.DisableSelection(true);
            PhotoBoothFrameManager.Instance.SelectFrame(item);
        }

        if (!string.IsNullOrEmpty(resultFrame.thumb_path) && item != null && item.frameImg != null)
        {
            bool isOffline = Application.internetReachability == NetworkReachability.NotReachable;
            Texture2D tex = null;

            if (isOffline)
                yield return FrameCacheManager.LoadCachedTexture(resultFrame.thumb_path, t2 => tex = t2);
            else
                yield return FrameCacheManager.DownloadAndCacheTexture(resultFrame.thumb_path, t2 => tex = t2);

            if (tex != null)
            {
                Sprite s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                item.frameImg.sprite = s;
                Color c = item.frameImg.color; c.a = 1f; item.frameImg.color = c;
            }
        }

        CanvasGroup frameCg = obj.GetComponent<CanvasGroup>();
        if (frameCg == null) frameCg = obj.AddComponent<CanvasGroup>();
        frameCg.alpha = 0f;

        float frameFadeTime = 0.3f;
        t = 0f;

        while (t < frameFadeTime)
        {
            t += Time.deltaTime;
            frameCg.alpha = Mathf.Lerp(0f, 1f, t / frameFadeTime);
            yield return null;
        }
        frameCg.alpha = 1f;

        yield return new WaitForSeconds(1.0f);

        float fadeOutTime = 0.7f;
        t = 0f;
        Vector3 startScale = obj.transform.localScale;
        Vector3 endScale = Vector3.zero;

        while (t < fadeOutTime)
        {
            t += Time.deltaTime;
            float p = t / fadeOutTime;

            frameCg.alpha = Mathf.Lerp(1f, 0f, p);
            obj.transform.localScale = Vector3.Lerp(startScale, endScale, p);

            yield return null;
        }

        PhotoBoothFrameManager.Instance.OnDecideButtonClicked();
        Debug.Log("shoot start");
    }
}