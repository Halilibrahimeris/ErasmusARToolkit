using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>A flat grid that stands in for AR planes in Desktop mode.</summary>
    public sealed class VirtualGround : MonoBehaviour
    {
        const float CellSizeMeters = 0.5f;
        const float OverCameraAlpha = 0.45f;

        [SerializeField, Min(1f), Tooltip("Half the width of the ground square, in meters.")]
        float halfExtent = 5f;
        [SerializeField] ToolkitTheme theme;

        Texture2D gridTexture;
        Material materialInstance;
        MeshRenderer gridRenderer;
        bool visible = true;
        bool overCamera;

        public float Height => transform.position.y;
        public float HalfExtent => halfExtent;

        internal void Initialize(ToolkitTheme toolkitTheme)
        {
            theme = toolkitTheme;
        }

        /// <summary>Hides the grid (placement on the invisible floor keeps working).</summary>
        public void SetVisible(bool isVisible)
        {
            visible = isVisible;
            ApplyStyle();
        }

        /// <summary>Makes the grid fainter so the camera image behind it stays visible.</summary>
        public void SetOverCamera(bool isOverCamera)
        {
            overCamera = isOverCamera;
            ApplyStyle();
        }

        void Start()
        {
            BuildVisual();
            ApplyStyle();
        }

        void OnDestroy()
        {
            ObjectUtility.SafeDestroy(gridTexture);
            ObjectUtility.SafeDestroy(materialInstance);
        }

        void ApplyStyle()
        {
            if (gridRenderer != null)
            {
                gridRenderer.enabled = visible;
            }

            if (materialInstance != null)
            {
                materialInstance.color = new Color(1f, 1f, 1f, overCamera ? OverCameraAlpha : 1f);
            }
        }

        void BuildVisual()
        {
            if (theme == null || theme.GroundMaterial == null)
            {
                Debug.LogWarning("[Erasmus AR Toolkit] VirtualGround has no theme ground material; the grid is hidden.", this);
                return;
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Grid";
            ObjectUtility.SafeDestroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(halfExtent * 2f, halfExtent * 2f, 1f);

            gridTexture = GridTextureFactory.Create(128, 2, theme.GridLineColor, theme.GridFillColor);
            var tiles = halfExtent * 2f / CellSizeMeters;
            materialInstance = new Material(theme.GroundMaterial) { mainTexture = gridTexture };
            materialInstance.mainTextureScale = new Vector2(tiles, tiles);

            // Draw before the rings on it, which do not write depth. Set here, not on the asset: URP's material
            // editor keeps transparent materials at queue 3000 +/- 50.
            materialInstance.renderQueue = RenderOrder.VirtualGround;

            gridRenderer = quad.GetComponent<MeshRenderer>();
            gridRenderer.sharedMaterial = materialInstance;
            gridRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
