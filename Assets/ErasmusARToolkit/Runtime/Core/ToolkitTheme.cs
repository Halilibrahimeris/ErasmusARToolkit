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
        [SerializeField, Tooltip("See-through material of the boxes around scanned obstacles and walls (room scan).")]
        Material obstacleMaterial;
        [SerializeField, Tooltip("Material of the edges of those boxes.")]
        Material obstacleEdgeMaterial;

        [Header("Shaders")]
        [SerializeField, Tooltip("Referenced here so the room scan shaders are part of every build.")]
        Shader occluderShader;
        [SerializeField, Tooltip("Referenced here so the room scan shaders are part of every build.")]
        Shader surfaceDepthShader;
        [SerializeField, Tooltip("Referenced here so the room scan shaders are part of every build.")]
        Shader obstacleShader;

        [Header("Project Modules")]
        [SerializeField, Tooltip("Full-screen filter of the vision simulator (colour blindness, low vision ...).")]
        Material visionMaterial;
        [SerializeField, Tooltip("Water and smoke of the room fill.")]
        Material fillMaterial;
        [SerializeField, Tooltip("Referenced here so the module shaders are part of every build.")]
        Shader visionShader;
        [SerializeField, Tooltip("Referenced here so the module shaders are part of every build.")]
        Shader fillShader;
        [SerializeField, Tooltip("Doorway of a portal: forgets the real depth so the world behind the door shows.")]
        Material depthClearMaterial;
        [SerializeField, Tooltip("Referenced here so the module shaders are part of every build.")]
        Shader depthClearShader;

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
        public Material ObstacleMaterial => obstacleMaterial;
        public Material ObstacleEdgeMaterial => obstacleEdgeMaterial;
        public Shader ObstacleShader => obstacleShader;
        public Material VisionMaterial => visionMaterial;
        public Material FillMaterial => fillMaterial;
        public Shader VisionShader => visionShader;
        public Shader FillShader => fillShader;
        public Material DepthClearMaterial => depthClearMaterial;
        public Shader DepthClearShader => depthClearShader;
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
        /// The room scan's materials and their shaders are assigned. Without them a build has no table
        /// occluders, no steady surface depth and no boxes around obstacles (Run Setup adds them).
        /// </summary>
        public bool HasRoomScanAssets =>
            occluderMaterial != null && surfaceDepthMaterial != null
            && occluderShader != null && surfaceDepthShader != null
            && obstacleMaterial != null && obstacleEdgeMaterial != null && obstacleShader != null;

        /// <summary>The materials and shaders of the project modules are assigned (Run Setup adds them).</summary>
        public bool HasModuleAssets =>
            visionMaterial != null && fillMaterial != null && visionShader != null && fillShader != null
            && depthClearMaterial != null && depthClearShader != null;

        internal void SetPortalAssets(Material depthClear, Shader shader)
        {
            depthClearMaterial = depthClear;
            depthClearShader = shader;
        }

        internal void SetModuleAssets(Material vision, Shader visionFilter, Material fill, Shader fillSurface)
        {
            visionMaterial = vision;
            visionShader = visionFilter;
            fillMaterial = fill;
            fillShader = fillSurface;
        }

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

        internal void SetObstacleAssets(Material fill, Material edge, Shader shader)
        {
            obstacleMaterial = fill;
            obstacleEdgeMaterial = edge;
            obstacleShader = shader;
        }
    }
}
