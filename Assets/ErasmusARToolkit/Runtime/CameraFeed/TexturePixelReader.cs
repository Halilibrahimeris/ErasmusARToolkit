using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Reads any texture (readable or not) as grayscale by drawing it into a render texture.
    /// Used to prepare image targets for webcam recognition without changing import settings.
    /// </summary>
    public static class TexturePixelReader
    {
        public static byte[] ReadGray(Texture texture, int maxSide, out int width, out int height)
        {
            var scale = Mathf.Min(1f, maxSide / (float)Mathf.Max(texture.width, texture.height));
            width = Mathf.Max(1, Mathf.RoundToInt(texture.width * scale));
            height = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));

            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                copy.Apply(false);
                return GrayscaleConverter.FromColor32(copy.GetPixels32(), width, height, 1, true);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                ObjectUtility.SafeDestroy(copy);
            }
        }
    }
}
