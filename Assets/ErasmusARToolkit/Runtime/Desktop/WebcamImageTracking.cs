using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Recognises image targets in the webcam image (PC / Editor) and shows their content on top
    /// of them. Frames are analysed on a background thread about ten times per second. Content is
    /// placed relative to the view camera, so it stays on the image shown behind the scene.
    /// Needs a detector from the optional OpenCV module (<see cref="ImageTargetDetectors"/>).
    /// </summary>
    public sealed class WebcamImageTracking : MonoBehaviour
    {
        const float IntervalSeconds = 0.1f;
        const float LostAfterSeconds = 0.6f;
        const int AnalysisMaxWidth = 640;
        const int TargetMaxSide = 640;
        const float PositionSmoothing = 0.5f;
        const float RotationSmoothing = 0.5f;

        readonly Dictionary<string, TrackedImage> tracked = new Dictionary<string, TrackedImage>();
        ARToolkitManager manager;
        WebcamBackground feed;
        Camera viewCamera;
        IImageTargetDetector detector;
        ImageTargetLookup lookup;
        Task<IReadOnlyList<ImageDetection>> pending;
        Color32[] pixels;
        float nextAnalysis;
        bool reportedFailure;

        public bool IsActive => detector != null && feed != null && feed.IsRunning;

        public static WebcamImageTracking TryCreate(GameObject host, ARToolkitManager toolkitManager,
            WebcamBackground cameraFeed, Camera camera)
        {
            var targets = toolkitManager.Config.ImageTargets.Where(t => t != null && t.Texture != null).ToList();
            if (targets.Count == 0 || !ImageTargetDetectors.IsAvailable)
            {
                return null;
            }

            var detector = ImageTargetDetectors.TryCreate();
            if (detector == null)
            {
                return null;
            }

            var tracking = host.AddComponent<WebcamImageTracking>();
            tracking.manager = toolkitManager;
            tracking.feed = cameraFeed;
            tracking.viewCamera = camera;
            tracking.detector = detector;
            tracking.lookup = new ImageTargetLookup(targets);
            tracking.PrepareTargets(targets);
            return tracking;
        }

        void PrepareTargets(IReadOnlyList<ImageTargetItem> targets)
        {
            var prepared = targets.Select(t =>
            {
                var gray = TexturePixelReader.ReadGray(t.Texture, TargetMaxSide, out var width, out var height);
                return new DetectorTarget(t.ResolvedName, gray, width, height, t.PhysicalSize);
            }).ToList();

            var usable = detector.SetTargets(prepared);
            Debug.Log("[Erasmus AR Toolkit] Webcam image recognition ready for: " +
                      (usable.Count > 0 ? string.Join(", ", usable) : "none (images need more detail)"));
        }

        void Update()
        {
            if (!IsActive)
            {
                HideAll();
                return;
            }

            if (pending != null)
            {
                if (!pending.IsCompleted)
                {
                    return;
                }

                CollectResult();
            }

            HideLostImages();
            if (Time.unscaledTime >= nextAnalysis)
            {
                nextAnalysis = Time.unscaledTime + IntervalSeconds;
                StartAnalysis();
            }
        }

        void OnDestroy()
        {
            // A running analysis finishes on its own; the detector is released afterwards.
            var toDispose = detector;
            detector = null;
            if (pending != null && !pending.IsCompleted)
            {
                pending.ContinueWith(_ => toDispose?.Dispose());
            }
            else
            {
                toDispose?.Dispose();
            }
        }

        void StartAnalysis()
        {
            var texture = feed.Texture;
            if (texture == null || !feed.TryGetIntrinsics(out var intrinsics))
            {
                return;
            }

            var width = texture.width;
            var height = texture.height;
            if (pixels == null || pixels.Length != width * height)
            {
                pixels = new Color32[width * height];
            }

            texture.GetPixels32(pixels);
            var step = Mathf.Max(1, width / AnalysisMaxWidth);
            var frame = pixels;
            var flip = !feed.IsFlippedVertically;
            var scaled = intrinsics.Scaled(1f / step);
            var activeDetector = detector;
            pending = Task.Run(() =>
            {
                var gray = GrayscaleConverter.FromColor32(frame, width, height, step, flip);
                return activeDetector.Detect(gray, width / step, height / step, scaled);
            });
        }

        void CollectResult()
        {
            var task = pending;
            pending = null;
            if (task.IsFaulted)
            {
                if (!reportedFailure)
                {
                    reportedFailure = true;
                    Debug.LogWarning("[Erasmus AR Toolkit] Webcam image analysis failed: " + task.Exception?.GetBaseException().Message);
                }

                return;
            }

            foreach (var detection in task.Result)
            {
                if (lookup.TryGet(detection.Name, out var item))
                {
                    Show(item, detection.CameraPose);
                }
            }
        }

        void Show(ImageTargetItem item, Pose pose)
        {
            if (!tracked.TryGetValue(item.ResolvedName, out var image))
            {
                image = CreateTrackedImage(item);
                tracked.Add(item.ResolvedName, image);
                Notify("Found image: " + item.ResolvedName);
            }

            var anchor = image.Anchor;
            if (!image.IsVisible)
            {
                anchor.localPosition = pose.position;
                anchor.localRotation = pose.rotation;
                anchor.gameObject.SetActive(true);
                ImageTargetEvents.RaiseFound(item, anchor.GetComponentInChildren<ToolkitObject>(true));
            }
            else
            {
                anchor.localPosition = Vector3.Lerp(anchor.localPosition, pose.position, PositionSmoothing);
                anchor.localRotation = Quaternion.Slerp(anchor.localRotation, pose.rotation, RotationSmoothing);
            }

            image.IsVisible = true;
            image.LastSeen = Time.unscaledTime;
        }

        TrackedImage CreateTrackedImage(ImageTargetItem item)
        {
            var anchor = new GameObject("Webcam Image - " + item.ResolvedName).transform;
            anchor.SetParent(viewCamera.transform, false);
            ToolkitObjectFactory.SpawnImageContent(item, anchor, item.PhysicalSize, manager.Theme, manager.Config.ScaleLimits);
            return new TrackedImage(anchor);
        }

        void HideLostImages()
        {
            foreach (var image in tracked.Values)
            {
                if (image.IsVisible && Time.unscaledTime - image.LastSeen > LostAfterSeconds)
                {
                    image.IsVisible = false;
                    image.Anchor.gameObject.SetActive(false);
                }
            }
        }

        void HideAll()
        {
            foreach (var image in tracked.Values)
            {
                if (image.IsVisible)
                {
                    image.IsVisible = false;
                    image.Anchor.gameObject.SetActive(false);
                }
            }
        }

        void Notify(string message)
        {
            // Shown by the HUD through the camera feed's notifications.
            feed.Announce(message);
        }

        sealed class TrackedImage
        {
            public TrackedImage(Transform anchor)
            {
                Anchor = anchor;
            }

            public Transform Anchor { get; }
            public float LastSeen { get; set; }
            public bool IsVisible { get; set; }
        }
    }
}
