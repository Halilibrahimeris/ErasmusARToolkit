using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Builds a tileable grid texture at runtime so no texture asset is needed.</summary>
    public static class GridTextureFactory
    {
        public static Texture2D Create(int size, int lineWidth, Color lineColor, Color fillColor)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "ARToolkit Grid",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };

            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var onLine = x < lineWidth || y < lineWidth;
                    pixels[y * size + x] = onLine ? lineColor : fillColor;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true);
            return texture;
        }
    }
}
