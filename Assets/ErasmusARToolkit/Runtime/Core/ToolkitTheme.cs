using TMPro;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Colors, materials and fonts used by the 3D toolkit visuals. Materials are assets so
    /// their shaders are included in builds. The HUD is styled separately in ARToolkitHUD.uss.
    /// </summary>
    [CreateAssetMenu(menuName = "Erasmus AR Toolkit/Theme", fileName = "ARToolkitTheme")]
    public sealed class ToolkitTheme : ScriptableObject
    {
        [Header("Materials")]
        [SerializeField, Tooltip("Transparent unlit material for the Desktop virtual ground.")]
        Material groundMaterial;
        [SerializeField, Tooltip("Unlit material for the selection ring.")]
        Material highlightMaterial;
        [SerializeField, Tooltip("Unlit textured material used for image targets and the webcam background.")]
        Material imageTargetMaterial;
        [SerializeField, Tooltip("Transparent unlit material behind info label text.")]
        Material labelBackgroundMaterial;
        [SerializeField, Tooltip("Depth-only material for scanned tables (room scan): hides what is behind or under them.")]
        Material occluderMaterial;
        [SerializeField, Tooltip("Depth-only material that steadies noisy real-world depth on scanned surfaces (room scan).")]
        Material surfaceDepthMaterial;

        [Header("Shaders")]
        [SerializeField, Tooltip("Referenced here so the room scan shaders are part of every build.")]
        Shader occluderShader;
        [SerializeField, Tooltip("Referenced here so the room scan shaders are part of every build.")]
        Shader surfaceDepthShader;

        [Header("Info Labels")]
        [SerializeField, Tooltip("TextMeshPro font for info labels. Empty uses the TMP default font.")]
        TMP_FontAsset labelFont;
        [SerializeField] Color labelTitleColor = new Color(1f, 0.8f, 0f, 1f);   // EU yellow
        [SerializeField] Color labelTextColor = Color.white;

        [Header("Colors")]
        [SerializeField] Color accentColor = new Color(1f, 0.8f, 0f, 1f);
        [SerializeField] Color gridLineColor = new Color(1f, 1f, 1f, 0.55f);
        [SerializeField] Color gridFillColor = new Color(0.1f, 0.25f, 0.55f, 0.25f);

        public Material GroundMaterial => groundMaterial;
        public Material HighlightMaterial => highlightMaterial;
        public Material ImageTargetMaterial => imageTargetMaterial;
        public Material LabelBackgroundMaterial => labelBackgroundMaterial;
        public Material OccluderMaterial => occluderMaterial;
        public Material SurfaceDepthMaterial => surfaceDepthMaterial;
        public Shader OccluderShader => occluderShader;
        public Shader SurfaceDepthShader => surfaceDepthShader;
        public TMP_FontAsset LabelFont => labelFont;
        public Color LabelTitleColor => labelTitleColor;
        public Color LabelTextColor => labelTextColor;
        public Color AccentColor => accentColor;
        public Color GridLineColor => gridLineColor;
        public Color GridFillColor => gridFillColor;

        public bool HasAllMaterials =>
            groundMaterial != null && highlightMaterial != null && imageTargetMaterial != null
            && labelBackgroundMaterial != null;

        /// <summary>
        /// The room scan's depth-only materials and their shaders are assigned. Without them a build has no
        /// table occluders and no steady surface depth (Run Setup adds them).
        /// </summary>
        public bool HasRoomScanAssets =>
            occluderMaterial != null && surfaceDepthMaterial != null
            && occluderShader != null && surfaceDepthShader != null;

        internal void SetMaterials(Material ground, Material highlight, Material imageTarget, Material labelBackground)
        {
            groundMaterial = ground;
            highlightMaterial = highlight;
            imageTargetMaterial = imageTarget;
            labelBackgroundMaterial = labelBackground;
        }

        internal void SetOccluderMaterial(Material occluder)
        {
            occluderMaterial = occluder;
        }

        internal void SetSurfaceDepthMaterial(Material surfaceDepth)
        {
            surfaceDepthMaterial = surfaceDepth;
        }

        internal void SetRoomScanShaders(Shader occluder, Shader surfaceDepth)
        {
            occluderShader = occluder;
            surfaceDepthShader = surfaceDepth;
        }
    }
}
