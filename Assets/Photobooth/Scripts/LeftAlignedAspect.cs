using UnityEngine;
using UnityEngine.UI;

public class LeftAlignedAspect : MonoBehaviour
{
    private void Start()
    {
        RectTransform rt = GetComponent<RectTransform>();

        // Force left anchoring (NO disabling of AspectRatioFitter needed)
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 0.5f);

        // Ensure it sticks to the left
        rt.anchoredPosition = new Vector2(0f, 0f);
    }
}