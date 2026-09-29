using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Room scan frames for phones without ARCore Depth: the tracking points ARCore finds in the camera
    /// image. They are sparse and have no normals, so the scan is slower and less exact, but camera rays
    /// to them still remove plane parts that reach past a table. The point cloud manager runs only while
    /// this source is in use (scenes from older toolkit versions get one then).
    /// </summary>
    internal sealed class FeaturePointScanSource : IScanFrameSource
    {
        const float MinConfidence = 0.2f;

        readonly XROrigin origin;
        readonly Camera camera;
        ARPointCloudManager pointClouds;
        bool turnedOnForScan;

        public FeaturePointScanSource(XROrigin xrOrigin, Camera arCamera)
        {
            origin = xrOrigin;
            camera = arCamera;
        }

        public bool IsPrecise => false;

        public bool IsAvailable => origin != null && camera != null;

        public void Begin()
        {
            if (!IsAvailable)
            {
                return;
            }

            pointClouds = origin.GetComponent<ARPointCloudManager>();
            if (pointClouds == null)
            {
                pointClouds = origin.gameObject.AddComponent<ARPointCloudManager>();
                turnedOnForScan = true;
            }
            else if (!pointClouds.enabled)
            {
                pointClouds.enabled = true;
                turnedOnForScan = true;
            }
        }

        public void End()
        {
            if (turnedOnForScan && pointClouds != null)
            {
                pointClouds.enabled = false;
            }

            turnedOnForScan = false;
        }

        public bool TryReadFrame(ScanFrame frame)
        {
            if (pointClouds == null || camera == null)
            {
                return false;
            }

            var cameraTransform = camera.transform;
            frame.Reset(cameraTransform.position, false);
            frame.Rotation = cameraTransform.rotation;
            foreach (var cloud in pointClouds.trackables)
            {
                var positions = cloud.positions;
                if (!positions.HasValue)
                {
                    continue;
                }

                var confidences = cloud.confidenceValues;
                var points = positions.Value;
                var cloudTransform = cloud.transform;
                for (var i = 0; i < points.Length; i++)
                {
                    if (confidences.HasValue && i < confidences.Value.Length && confidences.Value[i] < MinConfidence)
                    {
                        continue;
                    }

                    frame.Add(cloudTransform.TransformPoint(points[i]), false);
                }
            }

            return frame.Count > 0;
        }
    }
}
