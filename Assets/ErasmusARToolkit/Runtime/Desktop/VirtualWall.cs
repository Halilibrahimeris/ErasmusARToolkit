using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// A wall behind the virtual ground in Desktop mode, for things that hang on walls (pictures of a wall
    /// gallery). It is only made when the config has such things.
    /// </summary>
    public sealed class VirtualWall : MonoBehaviour
    {
        const float CellSizeMeters = 0.5f;

        float halfWidth = 2.5f;
        float height = 2.5f;
        Texture2D gridTexture;
        Material materialInstance;
        MeshRenderer gridRenderer;

        /// <summary>The side of the wall that faces the room.</summary>
        public Vector3 Normal => -transform.forward;

        public Vector3 Center => transform.position;

        public float HalfWidth => halfWidth;

        public float Bottom => transform.position.y - height * 0.5f;

        public float Top => transform.position.y + height * 0.5f;

        /// <param name="groundHeight">The wall stands on the ground at this height.</param>
        /// <param name="distance">How far behind the middle of the ground (+Z) it stands.</param>
        public static VirtualWall Create(Transform parent, ToolkitTheme theme, float groundHeight, float distance = 2f,
            float width = 5f, float wallHeight = 2.5f)
        {
            var go = new GameObject("Virtual Wall");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, groundHeight + wallHeight * 0.5f, distance);
            var wall = go.AddComponent<VirtualWall>();
            wall.halfWidth = width * 0.5f;
            wall.height = wallHeight;
            wall.Build(theme, width, wallHeight);
            return wall;
        }

        public bool TryGetPose(Ray ray, out Pose pose)
        {
            pose = default;
            if (!WallRaycast.TryIntersectRectangle(ray, Center, Normal, halfWidth, Bottom, Top, out var point))
            {
                return false;
            }

            pose = WallRaycast.WallPose(point, Normal, ray.origin);
            return true;
        }

        public void SetVisible(bool visible)
        {
            if (gridRenderer != null)
            {
                gridRenderer.enabled = visible;
            }
        }

        void OnDestroy()
        {
            ObjectUtility.SafeDestroy(gridTexture);
            ObjectUtility.SafeDestroy(materialInstance);
        }

        void Build(ToolkitTheme theme, float width, float wallHeight)
        {
            if (theme == null || theme.GroundMaterial == null)
            {
                return;
            }

            // A quad is seen from its -Z side: the wall's forward points away from the room.
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Grid";
            ObjectUtility.SafeDestroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            quad.transform.localScale = new Vector3(width, wallHeight, 1f);

            gridTexture = GridTextureFactory.Create(128, 2, theme.GridLineColor, theme.GridFillColor);
            materialInstance = new Material(theme.GroundMaterial) { mainTexture = gridTexture };
            materialInstance.mainTextureScale = new Vector2(width / CellSizeMeters, wallHeight / CellSizeMeters);
            materialInstance.renderQueue = RenderOrder.VirtualGround;
            gridRenderer = quad.GetComponent<MeshRenderer>();
            gridRenderer.sharedMaterial = materialInstance;
            gridRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
