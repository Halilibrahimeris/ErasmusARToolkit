using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Where the user is, seen from a portal.</summary>
    public enum PortalState
    {
        /// <summary>In front of the door: the world is seen through it.</summary>
        Outside,

        /// <summary>Walked through the door: the world is all around; the real room is seen through the door.</summary>
        Inside,

        /// <summary>Walked around the portal without going through: only the frame is seen.</summary>
        Behind,
    }

    /// <summary>
    /// Follows the user through a portal door. The door stands at z = 0 of the portal, its front (+Z) faces
    /// the room and the world lies behind it (-Z). Pure logic, so it can be tested.
    /// </summary>
    public sealed class PortalCrossing
    {
        /// <summary>How far above the door and below the floor an eye may pass and still count as "through the door".</summary>
        const float HeightMargin = 0.4f;

        readonly float halfWidth;
        readonly float height;
        Vector3? last;

        public PortalCrossing(float doorWidth, float doorHeight)
        {
            halfWidth = Mathf.Max(0.1f, doorWidth) * 0.5f;
            height = Mathf.Max(0.5f, doorHeight);
        }

        public PortalState State { get; private set; } = PortalState.Outside;

        /// <summary>The eye moved to <paramref name="local"/> (portal space). True when the state changed.</summary>
        public bool Update(Vector3 local)
        {
            var before = State;
            if (!last.HasValue)
            {
                State = local.z >= 0f ? PortalState.Outside : PortalState.Behind;
                last = local;
                return State != before;
            }

            var previous = last.Value;
            last = local;
            var wasFront = previous.z >= 0f;
            var isFront = local.z >= 0f;
            if (wasFront == isFront)
            {
                return false;
            }

            if (isFront)
            {
                // Back to the room side, through the door or beside it.
                State = PortalState.Outside;
            }
            else if (State == PortalState.Outside)
            {
                State = ThroughDoor(previous, local) ? PortalState.Inside : PortalState.Behind;
            }

            return State != before;
        }

        public void Reset()
        {
            last = null;
            State = PortalState.Outside;
        }

        bool ThroughDoor(Vector3 from, Vector3 to)
        {
            var t = from.z / (from.z - to.z);
            var at = Vector3.Lerp(from, to, t);
            return Mathf.Abs(at.x) <= halfWidth && at.y >= -HeightMargin && at.y <= height + HeightMargin;
        }
    }

    /// <summary>Meshes of a portal. Pure geometry, so it can be tested.</summary>
    public static class PortalMeshes
    {
        /// <summary>
        /// The invisible shell around the world: a box behind the door plane whose front face has the doorway
        /// cut out. It hides the world from everywhere but the door.
        /// </summary>
        public static Mesh Shell(float doorWidth, float doorHeight, Vector3 worldSize, Mesh mesh = null)
        {
            var w = Mathf.Max(worldSize.x, doorWidth + 0.2f) * 0.5f;
            var h = Mathf.Max(worldSize.y, doorHeight + 0.2f);
            var d = Mathf.Max(0.5f, worldSize.z);
            var dw = doorWidth * 0.5f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            // Front face around the door: left, right and above.
            Quad(vertices, triangles, new Vector3(-w, 0f, 0f), new Vector3(-dw, 0f, 0f), new Vector3(-dw, h, 0f), new Vector3(-w, h, 0f));
            Quad(vertices, triangles, new Vector3(dw, 0f, 0f), new Vector3(w, 0f, 0f), new Vector3(w, h, 0f), new Vector3(dw, h, 0f));
            Quad(vertices, triangles, new Vector3(-dw, doorHeight, 0f), new Vector3(dw, doorHeight, 0f), new Vector3(dw, h, 0f), new Vector3(-dw, h, 0f));

            // Back, sides and top.
            Quad(vertices, triangles, new Vector3(-w, 0f, -d), new Vector3(w, 0f, -d), new Vector3(w, h, -d), new Vector3(-w, h, -d));
            Quad(vertices, triangles, new Vector3(-w, 0f, 0f), new Vector3(-w, 0f, -d), new Vector3(-w, h, -d), new Vector3(-w, h, 0f));
            Quad(vertices, triangles, new Vector3(w, 0f, -d), new Vector3(w, 0f, 0f), new Vector3(w, h, 0f), new Vector3(w, h, -d));
            Quad(vertices, triangles, new Vector3(-w, h, 0f), new Vector3(w, h, 0f), new Vector3(w, h, -d), new Vector3(-w, h, -d));
            return Fill(mesh, "Portal Shell", vertices, triangles);
        }

        /// <summary>The doorway itself (x across, y up, at z = 0).</summary>
        public static Mesh Doorway(float doorWidth, float doorHeight, Mesh mesh = null)
        {
            var dw = doorWidth * 0.5f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            Quad(vertices, triangles, new Vector3(-dw, 0f, 0f), new Vector3(dw, 0f, 0f), new Vector3(dw, doorHeight, 0f), new Vector3(-dw, doorHeight, 0f));
            return Fill(mesh, "Portal Doorway", vertices, triangles);
        }

        /// <summary>The sky of the world: a box behind the door without a front, seen from inside.</summary>
        public static Mesh Sky(Vector3 worldSize, Mesh mesh = null)
        {
            var w = worldSize.x * 0.5f - 0.02f;
            var h = worldSize.y - 0.02f;
            var d = worldSize.z - 0.02f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            Quad(vertices, triangles, new Vector3(-w, -0.05f, -d), new Vector3(w, -0.05f, -d), new Vector3(w, h, -d), new Vector3(-w, h, -d));
            Quad(vertices, triangles, new Vector3(-w, -0.05f, -0.02f), new Vector3(-w, -0.05f, -d), new Vector3(-w, h, -d), new Vector3(-w, h, -0.02f));
            Quad(vertices, triangles, new Vector3(w, -0.05f, -d), new Vector3(w, -0.05f, -0.02f), new Vector3(w, h, -0.02f), new Vector3(w, h, -d));
            Quad(vertices, triangles, new Vector3(-w, h, -0.02f), new Vector3(w, h, -0.02f), new Vector3(w, h, -d), new Vector3(-w, h, -d));
            return Fill(mesh, "Portal Sky", vertices, triangles);
        }

        static void Quad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
        }

        static Mesh Fill(Mesh mesh, string name, List<Vector3> vertices, List<int> triangles)
        {
            mesh = mesh != null ? mesh : new Mesh { name = name };
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// A portal in the room: a door frame through which another world is seen. Walking through the door puts
    /// the user inside that world, and turning back shows the real room through the door; walking around the
    /// portal instead shows only the frame. Put the world's content under the "Inside World" child: it lies
    /// behind the door (-Z), inside the World Size. On the PC the camera walks (W A S D) toward the door.
    /// </summary>
    [RequireComponent(typeof(ModulePlacement))]
    [DisallowMultipleComponent, AddComponentMenu("Erasmus AR Toolkit/Portal")]
    public sealed class PortalController : ToolkitModule
    {
        [SerializeField, Min(0.5f)] float doorWidth = 1.2f;
        [SerializeField, Min(1f)] float doorHeight = 2.2f;
        [SerializeField, Tooltip("Width (x), height (y) and depth (z) of the world behind the door, in meters.")]
        Vector3 worldSize = new Vector3(6f, 3f, 6f);
        [SerializeField] Color skyColor = new Color(0.45f, 0.7f, 0.95f);
        [SerializeField, Tooltip("The world behind the door. Its content must stay inside the World Size.")]
        GameObject insideWorld;

        readonly List<Object> owned = new List<Object>();
        PortalCrossing crossing;
        GameObject shell;
        GameObject doorClear;
        GameObject doorOccluder;
        GameObject sky;
        ModulePlacement placement;
        DesktopCameraController walkingCamera;

        public PortalState State => crossing != null ? crossing.State : PortalState.Outside;

        public float DoorWidth => doorWidth;

        public float DoorHeight => doorHeight;

        public Vector3 WorldSize => worldSize;

        public GameObject InsideWorld => insideWorld;

        public ModulePlacement Placement => placement != null ? placement : placement = GetComponent<ModulePlacement>();

        internal GameObject Shell => shell;

        internal GameObject DoorOccluder => doorOccluder;

        internal void Configure(float width, float height, Vector3 size, Color sky, GameObject world)
        {
            doorWidth = Mathf.Max(0.5f, width);
            doorHeight = Mathf.Max(1f, height);
            worldSize = size;
            skyColor = sky;
            insideWorld = world;
        }

        protected override void OnModeReady(ToolkitMode mode)
        {
            crossing = new PortalCrossing(doorWidth, doorHeight);
            BuildParts();
            ApplyState();
            if (mode == ToolkitMode.Desktop)
            {
                Placement.Placed += OnPlacedOnDesktop;
                if (Placement.IsPlaced)
                {
                    OnPlacedOnDesktop(Placement.PlacedPose);
                }
            }
        }

        protected override void OnDisable()
        {
            if (Manager != null && Manager.CameraImage != null)
            {
                Manager.CameraImage.ResumeOcclusion(this);
            }

            if (placement != null)
            {
                placement.Placed -= OnPlacedOnDesktop;
            }

            if (walkingCamera != null)
            {
                walkingCamera.ExitWalkMode();
                walkingCamera = null;
            }

            base.OnDisable();
        }

        void OnDestroy()
        {
            foreach (var item in owned)
            {
                ObjectUtility.SafeDestroy(item);
            }

            owned.Clear();
        }

        void LateUpdate()
        {
            var camera = ViewCamera;
            if (crossing == null || camera == null || !Placement.IsPlaced)
            {
                return;
            }

            if (crossing.Update(transform.InverseTransformPoint(camera.transform.position)))
            {
                ApplyState();
            }
        }

        /// <summary>Shows the portal as seen from <paramref name="state"/> (the crossing decides it while running).</summary>
        internal void ApplyState(PortalState state)
        {
            crossing ??= new PortalCrossing(doorWidth, doorHeight);
            ForceState(state);
            ApplyState();
        }

        void ForceState(PortalState state)
        {
            // Walk the crossing into the wanted state along a line through the door.
            crossing.Reset();
            var eye = new Vector3(0f, Mathf.Min(1.5f, doorHeight * 0.7f), 0f);
            switch (state)
            {
                case PortalState.Inside:
                    crossing.Update(eye + Vector3.forward);
                    crossing.Update(eye + Vector3.back);
                    break;
                case PortalState.Behind:
                    crossing.Update(eye + Vector3.back + Vector3.right * (doorWidth + 1f));
                    break;
                default:
                    crossing.Update(eye + Vector3.forward);
                    break;
            }
        }

        void ApplyState()
        {
            var state = State;
            if (shell != null)
            {
                shell.SetActive(state != PortalState.Inside);
            }

            if (doorClear != null)
            {
                doorClear.SetActive(state == PortalState.Outside);
            }

            if (doorOccluder != null)
            {
                doorOccluder.SetActive(state == PortalState.Inside);
            }

            var cameraImage = Manager != null ? Manager.CameraImage : null;
            if (cameraImage != null)
            {
                // Inside, real walls would cut the world: the real world only shows through the door.
                if (state == PortalState.Inside)
                {
                    cameraImage.PauseOcclusion(this);
                }
                else
                {
                    cameraImage.ResumeOcclusion(this);
                }
            }

            SetInfoLine(PortalText.Line(state));
        }

        void BuildParts()
        {
            if (shell != null)
            {
                return;
            }

            var theme = Manager != null ? Manager.Theme : null;
            var mask = MakeMaterial(theme != null ? theme.OccluderMaterial : null, "Erasmus AR Toolkit/Occluder", RenderOrder.PortalMask);
            var clear = MakeMaterial(theme != null ? theme.DepthClearMaterial : null, "Erasmus AR Toolkit/Depth Clear", RenderOrder.PortalMask);
            shell = Part("Portal Mask", PortalMeshes.Shell(doorWidth, doorHeight, worldSize), mask);
            doorClear = Part("Portal Doorway", PortalMeshes.Doorway(doorWidth, doorHeight), clear);
            doorOccluder = Part("Portal Doorway (inside)", PortalMeshes.Doorway(doorWidth, doorHeight), mask);

            var skySource = theme != null ? theme.ImageTargetMaterial : null;
            var skyMaterial = skySource != null ? new Material(skySource) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            skyMaterial.name = "Portal Sky";
            skyMaterial.mainTexture = null;
            skyMaterial.color = skyColor;
            skyMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 1;
            if (skyMaterial.HasProperty("_Cull"))
            {
                skyMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            }

            owned.Add(skyMaterial);
            sky = Part("Portal Sky", PortalMeshes.Sky(worldSize), skyMaterial);
        }

        Material MakeMaterial(Material source, string shaderName, int queue)
        {
            var shader = source != null ? null : Shader.Find(shaderName);
            var material = source != null ? new Material(source) : shader != null ? new Material(shader) : null;
            if (material == null)
            {
                Debug.LogWarning($"[Erasmus AR Toolkit] The theme has no material for '{shaderName}'. Run Setup.", this);
                return null;
            }

            material.renderQueue = queue;
            owned.Add(material);
            return material;
        }

        GameObject Part(string name, Mesh mesh, Material material)
        {
            owned.Add(mesh);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.enabled = material != null;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        void OnPlacedOnDesktop(Pose pose)
        {
            var camera = ViewCamera;
            walkingCamera = camera != null ? camera.GetComponent<DesktopCameraController>() : null;
            if (walkingCamera == null)
            {
                return;
            }

            // Stand a few steps in front of the door, looking at it.
            var eye = transform.TransformPoint(new Vector3(0f, 1.5f, 3f));
            walkingCamera.EnterWalkMode(eye, transform.eulerAngles.y + 180f);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, worldSize.y * 0.5f, -worldSize.z * 0.5f), worldSize);
            Gizmos.DrawWireCube(new Vector3(0f, doorHeight * 0.5f, 0f), new Vector3(doorWidth, doorHeight, 0.02f));
            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }

    /// <summary>Texts of the portal.</summary>
    public static class PortalText
    {
        public static string Line(PortalState state)
        {
            switch (state)
            {
                case PortalState.Inside: return "Portal: inside. Turn around to see the room through the door.";
                case PortalState.Behind: return "Portal: go around to the front and walk through the door.";
                default: return null;
            }
        }
    }
}
