using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Image targets in Desktop mode. When the webcam is on and webcam image recognition is available
    /// (Windows / Editor), the real images are recognised in the webcam picture. Otherwise the images
    /// lie on the virtual ground with their content, so content can still be checked without a phone.
    /// </summary>
    public sealed class DesktopImageTargetSimulator : MonoBehaviour
    {
        const float Spacing = 0.15f;
        const float RowDistance = 0.6f;
        const float HeightAboveGround = 0.001f;
        const float SeenCheckSeconds = 0.5f;

        /// <summary>A simulated image is "found" when it is in view and not further away than this.</summary>
        const float SeenDistance = 4f;

        [SerializeField] ARToolkitManager manager;

        readonly List<Material> createdMaterials = new List<Material>();
        readonly List<SimulatedImage> simulated = new List<SimulatedImage>();
        readonly Plane[] frustum = new Plane[6];
        Transform floorTargets;
        float nextSeenCheck;
        WebcamImageTracking liveTracking;

        /// <summary>True while images are recognised in the webcam picture instead of simulated.</summary>
        public bool IsRecognisingWebcamImages => liveTracking != null && liveTracking.IsActive;

        internal void Initialize(ARToolkitManager toolkitManager)
        {
            manager = toolkitManager;
        }

        void Start()
        {
            if (manager == null || manager.Config == null)
            {
                return;
            }

            if (manager.Config.DesktopSimulateImageTargets)
            {
                CreateFloorTargets();
            }

            var feed = manager.CameraFeed;
            if (feed != null && feed.IsEnabledInConfig && manager.ActiveCamera != null)
            {
                liveTracking = WebcamImageTracking.TryCreate(gameObject, manager, feed, manager.ActiveCamera);
                if (liveTracking != null)
                {
                    feed.StateChanged += UpdateFloorTargets;
                }
            }

            UpdateFloorTargets();
        }

        void OnDestroy()
        {
            if (liveTracking != null && manager != null && manager.CameraFeed != null)
            {
                manager.CameraFeed.StateChanged -= UpdateFloorTargets;
            }

            foreach (var material in createdMaterials)
            {
                ObjectUtility.SafeDestroy(material);
            }

            createdMaterials.Clear();
        }

        void Update()
        {
            if (simulated.Count == 0 || Time.unscaledTime < nextSeenCheck || manager == null || manager.ActiveCamera == null)
            {
                return;
            }

            nextSeenCheck = Time.unscaledTime + SeenCheckSeconds;
            var viewCamera = manager.ActiveCamera;
            GeometryUtility.CalculateFrustumPlanes(viewCamera, frustum);
            var active = floorTargets != null && floorTargets.gameObject.activeInHierarchy;
            foreach (var image in simulated)
            {
                var seen = active && image.Content != null
                           && Vector3.Distance(viewCamera.transform.position, image.Bounds.center) <= SeenDistance
                           && GeometryUtility.TestPlanesAABB(frustum, image.Bounds);
                if (seen && !image.IsSeen)
                {
                    ImageTargetEvents.RaiseFound(image.Item, image.Content);
                }

                image.IsSeen = seen;
            }
        }

        /// <summary>The images lie on the virtual ground now (not while the webcam picture is searched for them).</summary>
        internal bool ShowsFloorImages => floorTargets != null && floorTargets.gameObject.activeInHierarchy;

        /// <summary>The simulated image is in view now (it was "found").</summary>
        internal bool IsSeen(string imageName)
        {
            foreach (var image in simulated)
            {
                if (image.Item.ResolvedName == imageName)
                {
                    return image.IsSeen;
                }
            }

            return false;
        }

        sealed class SimulatedImage
        {
            public ImageTargetItem Item;
            public ToolkitObject Content;
            public Bounds Bounds;
            public bool IsSeen;
        }

        /// <summary>Floor copies are hidden while the real images are recognised in the webcam picture.</summary>
        void UpdateFloorTargets()
        {
            if (floorTargets != null)
            {
                var liveRecognition = liveTracking != null && manager.CameraFeed != null && manager.CameraFeed.IsRunning;
                floorTargets.gameObject.SetActive(!liveRecognition);
            }
        }

        void CreateFloorTargets()
        {
            floorTargets = new GameObject("Simulated Images").transform;
            floorTargets.SetParent(transform, false);

            var targets = manager.Config.ImageTargets;
            var totalWidth = 0f;
            foreach (var target in targets)
            {
                if (target != null && target.Texture != null)
                {
                    totalWidth += target.PhysicalSize.x + Spacing;
                }
            }

            var x = -totalWidth * 0.5f;
            foreach (var target in targets)
            {
                if (target == null || target.Texture == null)
                {
                    continue;
                }

                var size = target.PhysicalSize;
                var position = new Vector3(x + size.x * 0.5f, HeightAboveGround, RowDistance);
                CreateSimulatedTarget(target, position, size);
                x += size.x + Spacing;
            }
        }

        void CreateSimulatedTarget(ImageTargetItem target, Vector3 position, Vector2 size)
        {
            // The anchor mimics ARTrackedImage: Y is the image normal, content stands on the image.
            var anchor = new GameObject("Simulated Image - " + target.ResolvedName).transform;
            anchor.SetParent(floorTargets, false);
            anchor.localPosition = position;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Image";
            ObjectUtility.SafeDestroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(anchor, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);

            var baseMaterial = manager.Theme != null ? manager.Theme.ImageTargetMaterial : null;
            if (baseMaterial != null)
            {
                var material = new Material(baseMaterial) { mainTexture = target.Texture };
                createdMaterials.Add(material);
                quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            var content = ToolkitObjectFactory.SpawnImageContent(target, anchor, size, manager.Theme, manager.Config.ScaleLimits);
            simulated.Add(new SimulatedImage
            {
                Item = target,
                Content = content,
                Bounds = new Bounds(anchor.position, new Vector3(size.x, 0.02f, size.y)),
            });
        }
    }
}
