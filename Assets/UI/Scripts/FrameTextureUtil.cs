using UnityEngine;

public static class FrameTextureUtil
{
    // Creates a readable clone of any Texture2D (for editing / pixel access)
    public static Texture2D CreateReadableTextureClone(Texture2D source)
    {
        Texture2D tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        tex.SetPixels32(source.GetPixels32());
        tex.Apply();
        return tex;
    }

    // Convert a Texture2D to Sprite for UI/Image usage
    public static Sprite CreateSpriteFromTexture(Texture2D tex)
    {
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }
}
