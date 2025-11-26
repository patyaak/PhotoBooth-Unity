using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class PhotoFrameSlotDetector : MonoBehaviour
{
    public Texture2D frameTexture;
    public GameObject imagePrefab; // RawImage or Image with material mask
    public RectTransform canvasRoot;

    public float alphaThreshold = 0.01f;

    struct SlotRegion { public int minX, minY, maxX, maxY; }

    void Start()
    {
        FindSlotsAndPlaceImages();
    }

    void FindSlotsAndPlaceImages()
    {
        int w = frameTexture.width;
        int h = frameTexture.height;

        bool[,] visited = new bool[w, h];
        List<SlotRegion> slots = new List<SlotRegion>();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!visited[x, y])
                {
                    Color c = frameTexture.GetPixel(x, y);
                    if (c.a < alphaThreshold)
                    {
                        SlotRegion region = FloodFill(x, y, visited);
                        slots.Add(region);
                    }
                }
            }
        }

        foreach (var slot in slots)
        {
            float width = slot.maxX - slot.minX;
            float height = slot.maxY - slot.minY;

            string aspect = GetClosestAspectRatio(width, height);
            Debug.Log($"Detected slot approx ratio: {aspect} (w:{width}, h:{height})");

            GameObject imgObj = Instantiate(imagePrefab, canvasRoot);
            RectTransform rt = imgObj.GetComponent<RectTransform>();

            // Position in UI canvas coordinates
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(slot.minX + width / 2f, -(slot.minY + height / 2f));
        }

    }

    SlotRegion FloodFill(int startX, int startY, bool[,] visited)
    {
        int w = frameTexture.width;
        int h = frameTexture.height;

        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        stack.Push(new Vector2Int(startX, startY));

        SlotRegion region = new SlotRegion
        {
            minX = startX,
            maxX = startX,
            minY = startY,
            maxY = startY
        };

        while (stack.Count > 0)
        {
            var p = stack.Pop();
            int x = p.x; int y = p.y;

            if (x < 0 || y < 0 || x >= w || y >= h) continue;
            if (visited[x, y]) continue;

            Color c = frameTexture.GetPixel(x, y);
            if (c.a >= alphaThreshold) continue;

            visited[x, y] = true;

            region.minX = Mathf.Min(region.minX, x);
            region.maxX = Mathf.Max(region.maxX, x);
            region.minY = Mathf.Min(region.minY, y);
            region.maxY = Mathf.Max(region.maxY, y);

            stack.Push(new Vector2Int(x+1, y));
            stack.Push(new Vector2Int(x-1, y));
            stack.Push(new Vector2Int(x, y+1));
            stack.Push(new Vector2Int(x, y-1));
        }

        return region;
    }
    
    
    
    string GetClosestAspectRatio(float w, float h)
    {
        float ratio = w / h;

        // List of standard ratios (name, numeric ratio)
        (string, float)[] standards = 
        {
            ("1:1", 1f),
            ("3:4", 3f/4f),
            ("4:3", 4f/3f),
            ("4:5", 4f/5f),
            ("9:16", 9f/16f),
            ("16:9", 16f/9f),
            ("2:1", 2f),
            ("1:2", 0.5f)
        };

        string closest = "";
        float minDiff = Mathf.Infinity;

        foreach (var s in standards)
        {
            float diff = Mathf.Abs(ratio - s.Item2);
            if (diff < minDiff)
            {
                minDiff = diff;
                closest = s.Item1;
            }
        }

        return closest;
    }

}
