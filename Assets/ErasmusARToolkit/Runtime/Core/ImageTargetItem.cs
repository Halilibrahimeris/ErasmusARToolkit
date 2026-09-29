using System;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>A printed image that makes content appear when the camera sees it.</summary>
    [Serializable]
    public sealed class ImageTargetItem
    {
        [SerializeField, Tooltip("Unique name. Leave empty to use the texture name.")]
        string name = string.Empty;
        [SerializeField, Tooltip("The image to recognise. Use a detailed, high-contrast picture.")]
        Texture2D texture;
        [SerializeField, Min(0.01f), Tooltip("Real printed width of the image in meters (A4 portrait is about 0.21).")]
        float widthMeters = 0.2f;
        [SerializeField, Tooltip("Content shown on top of the image. Optional if an info label is set.")]
        GameObject prefab;
        [SerializeField] InfoLabelData info = new InfoLabelData();

        public ImageTargetItem()
        {
        }

        public ImageTargetItem(string name, Texture2D texture, float widthMeters, GameObject prefab,
            InfoLabelData info = null)
        {
            this.name = name ?? string.Empty;
            this.texture = texture;
            this.widthMeters = widthMeters;
            this.prefab = prefab;
            this.info = info ?? new InfoLabelData();
        }

        public string Name => name ?? string.Empty;
        public Texture2D Texture => texture;
        public float WidthMeters => widthMeters;
        public GameObject Prefab => prefab;
        public InfoLabelData Info => info ?? new InfoLabelData();

        /// <summary>The name used in the reference image library.</summary>
        public string ResolvedName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name.Trim();
                }

                return texture != null ? texture.name : string.Empty;
            }
        }

        /// <summary>Physical size in meters, keeping the texture aspect ratio.</summary>
        public Vector2 PhysicalSize
        {
            get
            {
                if (texture == null || texture.width == 0)
                {
                    return new Vector2(widthMeters, widthMeters);
                }

                return new Vector2(widthMeters, widthMeters * texture.height / texture.width);
            }
        }
    }
}
