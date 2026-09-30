using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// A route through the school: image targets with a stop number (Content tab, Route Stop) are its
    /// stops. When the camera finds a stop, its content appears with an arrow to the next stop and the
    /// way there ("Next: Library, upstairs"); the HUD shows "Route 2 / 6". Stops can be found in any
    /// order; when all are found the route is complete. Progress lasts while the app runs.
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("Erasmus AR Toolkit/Route Tracker")]
    public sealed class RouteTracker : ToolkitModule
    {
        [SerializeField] string routeName = "School Route";
        [SerializeField] string completeMessage = "Route complete! You found every stop.";
        [SerializeField, Tooltip("Show an arrow to the next stop on each stop.")]
        bool showArrows = true;

        readonly Dictionary<int, RouteStop> stops = new Dictionary<int, RouteStop>();
        RouteProgress progress = new RouteProgress(new int[0]);
        string lastHint = string.Empty;
        bool announcedComplete;

        public string RouteName => string.IsNullOrWhiteSpace(routeName) ? "Route" : routeName.Trim();

        public RouteProgress Progress => progress;

        protected override void OnEnable()
        {
            base.OnEnable();
            ImageTargetEvents.Found += OnFound;
        }

        protected override void OnDisable()
        {
            ImageTargetEvents.Found -= OnFound;
            base.OnDisable();
        }

        protected override void OnModeReady(ToolkitMode mode)
        {
            if (stops.Count == 0 && Manager != null && Manager.Config != null)
            {
                UseStops(Manager.Config.ImageTargets);
            }

            RefreshLine();
        }

        /// <summary>The stops of the route (image targets with a stop number).</summary>
        internal void UseStops(IEnumerable<ImageTargetItem> targets)
        {
            stops.Clear();
            foreach (var target in targets)
            {
                if (target != null && target.RouteStop.IsStop && !stops.ContainsKey(target.RouteStop.Order))
                {
                    stops.Add(target.RouteStop.Order, target.RouteStop);
                }
            }

            progress = new RouteProgress(stops.Keys);
            announcedComplete = false;
            lastHint = string.Empty;
        }

        void OnFound(ImageTargetItem item, ToolkitObject content)
        {
            var stop = item.RouteStop;
            if (!stop.IsStop || !stops.ContainsKey(stop.Order))
            {
                return;
            }

            var isNew = progress.Mark(stop.Order);
            lastHint = RouteText.Next(stop);
            if (showArrows && content != null)
            {
                RouteStopView.Attach(content, stop, item.PhysicalSize, Manager != null ? Manager.Theme : null);
            }

            RefreshLine();
            if (progress.IsComplete && !announcedComplete)
            {
                announcedComplete = true;
                Toast(completeMessage);
            }
            else if (isNew)
            {
                Toast(RouteText.Found(stop.Order, progress));
            }
        }

        void RefreshLine()
        {
            if (progress.Total == 0)
            {
                SetInfoLine(null);
                return;
            }

            SetInfoLine(RouteText.Line(RouteName, progress, lastHint));
        }
    }

    /// <summary>Which stops of a route were found. Pure logic, so it can be tested.</summary>
    public sealed class RouteProgress
    {
        readonly SortedSet<int> stops;
        readonly HashSet<int> found = new HashSet<int>();

        public RouteProgress(IEnumerable<int> stopNumbers)
        {
            stops = new SortedSet<int>(stopNumbers.Where(n => n > 0));
        }

        public int Total => stops.Count;

        public int FoundCount => found.Count;

        public bool IsComplete => Total > 0 && FoundCount == Total;

        /// <summary>Marks a stop as found. True the first time.</summary>
        public bool Mark(int stopNumber)
        {
            return stops.Contains(stopNumber) && found.Add(stopNumber);
        }

        public bool IsFound(int stopNumber) => found.Contains(stopNumber);

        /// <summary>The lowest stop not found yet, or 0.</summary>
        public int FirstMissing => stops.FirstOrDefault(n => !found.Contains(n));
    }

    /// <summary>Texts of the route.</summary>
    public static class RouteText
    {
        public static string Line(string routeName, RouteProgress progress, string next)
        {
            var line = $"{routeName} · {progress.FoundCount} / {progress.Total}";
            if (progress.IsComplete)
            {
                return line + " · complete";
            }

            return string.IsNullOrWhiteSpace(next) ? line : line + " · " + next;
        }

        public static string Next(RouteStop stop)
        {
            var arrow = RouteArrowMath.Words(stop.Arrow);
            var hint = stop.NextHint.Trim();
            if (hint.Length == 0 && arrow.Length == 0)
            {
                return string.Empty;
            }

            if (hint.Length == 0)
            {
                return "Next: " + arrow;
            }

            return arrow.Length == 0 ? "Next: " + hint : $"Next: {hint} ({arrow})";
        }

        public static string Found(int stopNumber, RouteProgress progress)
        {
            return $"Stop {stopNumber} found ({progress.FoundCount} / {progress.Total}).";
        }
    }

    /// <summary>Directions of route arrows in the space of an image. Pure math, so it can be tested.</summary>
    public static class RouteArrowMath
    {
        /// <summary>
        /// The arrow's direction in the space of a tracked image (Y out of the image toward the viewer,
        /// Z up along the image, X to the right).
        /// </summary>
        public static Vector3 Direction(RouteArrow arrow)
        {
            switch (arrow)
            {
                case RouteArrow.Left: return Vector3.left;
                case RouteArrow.Right: return Vector3.right;
                case RouteArrow.Up: return Vector3.forward;
                case RouteArrow.Down: return Vector3.back;
                case RouteArrow.Straight: return Vector3.down;
                case RouteArrow.Back: return Vector3.up;
                default: return Vector3.zero;
            }
        }

        /// <summary>The rotation that turns an arrow pointing along +Z to <see cref="Direction"/>.</summary>
        public static Quaternion Rotation(RouteArrow arrow)
        {
            var direction = Direction(arrow);
            if (direction == Vector3.zero)
            {
                return Quaternion.identity;
            }

            // Arrows in the plane of the image lie flat on it; arrows out of the image stand upright.
            var up = Mathf.Abs(direction.y) > 0.5f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(direction, up);
        }

        /// <summary>The direction in words (the card font may have no arrow signs).</summary>
        public static string Words(RouteArrow arrow)
        {
            switch (arrow)
            {
                case RouteArrow.Left: return "left";
                case RouteArrow.Right: return "right";
                case RouteArrow.Up: return "up";
                case RouteArrow.Down: return "down";
                case RouteArrow.Straight: return "straight on";
                case RouteArrow.Back: return "turn around";
                default: return string.Empty;
            }
        }
    }

    /// <summary>The arrow and the "Next" text on a found route stop.</summary>
    public sealed class RouteStopView : MonoBehaviour
    {
        public const string ObjectName = "Route Arrow";
        const float ArrowLengthOfImage = 0.5f;
        const float HeightAboveImage = 0.05f;

        InfoLabel label;

        /// <summary>Adds the arrow once to the content of a stop (it follows the content when shown and hidden).</summary>
        public static RouteStopView Attach(ToolkitObject content, RouteStop stop, Vector2 imageSize, ToolkitTheme theme)
        {
            var existing = content.GetComponentInChildren<RouteStopView>(true);
            if (existing != null)
            {
                return existing;
            }

            var root = new GameObject(ObjectName);
            var parent = content.transform;
            var scale = parent.localScale;
            root.transform.SetParent(parent, false);
            root.transform.localScale = new Vector3(Inverse(scale.x), Inverse(scale.y), Inverse(scale.z));
            root.transform.localPosition = new Vector3(0f, HeightAboveImage * Inverse(scale.y), 0f);
            var view = root.AddComponent<RouteStopView>();

            var length = Mathf.Max(0.08f, imageSize.x * ArrowLengthOfImage);
            if (stop.Arrow != RouteArrow.None)
            {
                var arrow = new GameObject("Arrow", typeof(MeshFilter), typeof(MeshRenderer));
                arrow.transform.SetParent(root.transform, false);
                arrow.transform.localRotation = RouteArrowMath.Rotation(stop.Arrow);
                arrow.transform.localScale = Vector3.one * length;
                arrow.GetComponent<MeshFilter>().sharedMesh = RouteArrowMesh.Shared;
                arrow.GetComponent<MeshRenderer>().sharedMaterial = ArrowMaterial(theme);
            }

            var next = RouteText.Next(stop);
            if (next.Length > 0)
            {
                var anchor = stop.Arrow != RouteArrow.None ? root.transform.GetChild(0) : root.transform;
                view.label = InfoLabel.Create(anchor, new InfoLabelData($"Stop {stop.Order}", next, true), theme);
            }

            return view;
        }

        void OnEnable()
        {
            if (label != null)
            {
                label.SetVisible(true);
            }
        }

        void OnDisable()
        {
            if (label != null)
            {
                label.SetVisible(false);
            }
        }

        void OnDestroy()
        {
            if (label != null)
            {
                ObjectUtility.SafeDestroy(label.gameObject);
            }
        }

        static Material arrowMaterial;

        static Material ArrowMaterial(ToolkitTheme theme)
        {
            if (arrowMaterial != null)
            {
                return arrowMaterial;
            }

            var source = theme != null ? theme.ImageTargetMaterial : null;
            arrowMaterial = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            arrowMaterial.name = "Route Arrow";
            arrowMaterial.mainTexture = null;
            arrowMaterial.color = theme != null ? theme.AccentColor : new Color(1f, 0.8f, 0f);
            if (arrowMaterial.HasProperty("_Cull"))
            {
                arrowMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            }
            arrowMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            return arrowMaterial;
        }

        static float Inverse(float value) => Mathf.Abs(value) < 0.0001f ? 1f : 1f / value;
    }

    /// <summary>A flat, thick arrow one unit long, pointing along +Z, lying in the XZ plane.</summary>
    public static class RouteArrowMesh
    {
        const float ShaftHalfWidth = 0.14f;
        const float HeadHalfWidth = 0.34f;
        const float HeadLength = 0.42f;
        const float HalfThickness = 0.04f;

        static Mesh shared;

        public static Mesh Shared => shared != null ? shared : shared = Create();

        public static Mesh Create()
        {
            // Outline (x, z), counter-clockwise seen from above, from the tail around to the tip.
            var outline = new[]
            {
                new Vector2(-ShaftHalfWidth, -0.5f), new Vector2(ShaftHalfWidth, -0.5f),
                new Vector2(ShaftHalfWidth, 0.5f - HeadLength), new Vector2(HeadHalfWidth, 0.5f - HeadLength),
                new Vector2(0f, 0.5f), new Vector2(-HeadHalfWidth, 0.5f - HeadLength),
                new Vector2(-ShaftHalfWidth, 0.5f - HeadLength),
            };

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            // Top and bottom: the shaft (a rectangle) and the head (a triangle).
            foreach (var y in new[] { HalfThickness, -HalfThickness })
            {
                var start = vertices.Count;
                foreach (var point in outline)
                {
                    vertices.Add(new Vector3(point.x, y, point.y));
                }

                var up = y > 0f;
                AddTriangle(triangles, start, 0, 6, 2, up);
                AddTriangle(triangles, start, 0, 2, 1, up);
                AddTriangle(triangles, start, 3, 5, 4, up);
            }

            // Sides.
            for (var i = 0; i < outline.Length; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Length];
                var start = vertices.Count;
                vertices.Add(new Vector3(a.x, HalfThickness, a.y));
                vertices.Add(new Vector3(b.x, HalfThickness, b.y));
                vertices.Add(new Vector3(b.x, -HalfThickness, b.y));
                vertices.Add(new Vector3(a.x, -HalfThickness, a.y));
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 1);
                triangles.Add(start);
                triangles.Add(start + 3);
                triangles.Add(start + 2);
            }

            var mesh = new Mesh { name = "Route Arrow" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddTriangle(List<int> triangles, int start, int a, int b, int c, bool facingUp)
        {
            triangles.Add(start + a);
            triangles.Add(start + (facingUp ? b : c));
            triangles.Add(start + (facingUp ? c : b));
        }
    }
}
