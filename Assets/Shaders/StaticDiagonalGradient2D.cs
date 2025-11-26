using UnityEngine;

public class StaticDiagonalGradient2D : MonoBehaviour
{
    [Header("Assign the material that uses 'Unlit/VerticalGradient2D'")]
    public Material gradientMaterial;

    [Header("Gradient Colors")]
    public Color topColor = Color.cyan;
    public Color bottomColor = Color.magenta;

    private void Start()
    {
        ApplyGradient();
    }

    private void OnValidate()
    {
        ApplyGradient();
    }

    // ✅ Public method you can safely call from VendorLogin
    public void ApplyGradient()
    {
        if (!gradientMaterial) return;

        gradientMaterial.SetColor("_TopColor", topColor);
        gradientMaterial.SetColor("_BottomColor", bottomColor);
    }
}
