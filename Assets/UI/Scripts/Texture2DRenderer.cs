using System;
using UnityEngine;
using ZXing.Common;
using ZXing.Rendering;

namespace ZXing.Rendering
{
    public class Texture2DRenderer : IBarcodeRenderer<Texture2D>
    {
        public Color32 ForegroundColor { get; set; } = Color.black;
        public Color32 BackgroundColor { get; set; } = Color.white;

        public Texture2D Render(BitMatrix matrix, BarcodeFormat format, string content)
        {
            return Render(matrix, format, content, null);
        }

        public Texture2D Render(BitMatrix matrix, BarcodeFormat format, string content, EncodingOptions options)
        {
            int width = matrix.Width;
            int height = matrix.Height;
            var pixels = new Color32[width * height];

            // Convert bit matrix into pixel colors
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isBlack = matrix[x, y];
                    pixels[(height - y - 1) * width + x] = isBlack ? ForegroundColor : BackgroundColor;
                }
            }

            // Create Texture2D and apply pixels
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            return texture;
        }
    }
}
