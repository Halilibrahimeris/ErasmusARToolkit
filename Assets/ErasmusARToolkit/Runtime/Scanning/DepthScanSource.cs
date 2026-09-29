using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Room scan frames from ARCore Depth: a grid of about 40 x 80 screen points is looked up in the raw
    /// depth image (confident pixels only) and turned into world points. Neighboring points give each
    /// point a normal, so table sides and walls are not mistaken for table tops.
    /// <para>
    /// No frame is used until the depth image mapping is verified against ARCore's own depth hit test at a
    /// few screen points (see <see cref="DepthImageMapping"/>); a wrong mapping would carve away real
    /// surfaces. If it cannot be verified, or no depth arrives, the source reports itself unavailable and
    /// the scan continues with tracking points. If the occlusion manager was off (occlusion disabled in
    /// the config), it is switched on without occlusion for the scan and back off after.
    /// </para>
    /// </summary>
    internal sealed class DepthScanSource : IScanFrameSource
    {
        const int Columns = 40;
        const int MaxRows = 96;
        const byte MinConfidence = 100;
        const int NormalStride = 2;
        const float MaxTangentPerMeter = 0.35f;
        const float HorizontalCosine = 0.85f;
        const int MappingChecksWithoutHits = 16;
        const float MappingMaxError = 0.12f;
        const float MappingMinLead = 0.04f;
        const int MappingSearchRadius = 2;
        const int MappingMinPoints = 6;

        /// <summary>Reads (made only while tracking and holding steady) without any depth image before giving up.</summary>
        const int ReadsWithoutDepth = 40;

        static readonly Vector2[] MappingCheckPoints =
        {
            new Vector2(0.3f, 0.3f), new Vector2(0.7f, 0.3f), new Vector2(0.3f, 0.7f),
            new Vector2(0.7f, 0.7f), new Vector2(0.5f, 0.85f), new Vector2(0.5f, 0.15f),
        };

        readonly AROcclusionManager occlusion;
        readonly ARCameraManager cameraManager;
        readonly ARRaycastManager raycasts;
        readonly Camera camera;
        readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        readonly float[] mappingErrors = new float[4];
        readonly int[] mappingCounts = new int[4];

        Vector3[] samples = Array.Empty<Vector3>();
        bool[] valid = Array.Empty<bool>();
        Vector3[] viewDirections = Array.Empty<Vector3>();
        Matrix4x4 directionsProjection;
        int directionsRows;
        Matrix4x4? displayMatrix;
        double lastTimestamp = double.NaN;
        DepthImageMapping mapping = DepthImageMapping.Candidates[0];
        bool mappingChecked;
        bool mappingUnverifiable;
        int mappingAttempts;
        int mappingAttemptsWithHits;
        bool turnedOnForScan;
        OcclusionPreferenceMode previousPreference;
        int readsWithoutDepth;
        bool receivedDepth;

        public DepthScanSource(AROcclusionManager occlusionManager, ARCameraManager arCameraManager,
            ARRaycastManager raycastManager, Camera arCamera)
        {
            occlusion = occlusionManager;
            cameraManager = arCameraManager;
            raycasts = raycastManager;
            camera = arCamera;
        }

        public bool IsPrecise => true;

        public bool IsAvailable
        {
            get
            {
                if (occlusion == null || cameraManager == null || camera == null)
                {
                    return false;
                }

                var descriptor = occlusion.descriptor;
                if (mappingUnverifiable
                    || (descriptor != null && descriptor.environmentDepthImageSupported == Supported.Unsupported))
                {
                    return false;
                }

                // Support can stay "unknown" for a while; many steady, tracked reads without depth mean none.
                return receivedDepth || readsWithoutDepth < ReadsWithoutDepth;
            }
        }

        public void Begin()
        {
            if (occlusion == null || cameraManager == null)
            {
                return;
            }

            cameraManager.frameReceived += OnCameraFrame;
            if (!occlusion.enabled)
            {
                // Occlusion is off in the config: switch depth on for the scan without hiding anything.
                previousPreference = occlusion.requestedOcclusionPreferenceMode;
                occlusion.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.NoOcclusion;
                occlusion.enabled = true;
                turnedOnForScan = true;
            }
        }

        public void End()
        {
            if (cameraManager != null)
            {
                cameraManager.frameReceived -= OnCameraFrame;
            }

            if (turnedOnForScan && occlusion != null)
            {
                occlusion.enabled = false;
                occlusion.requestedOcclusionPreferenceMode = previousPreference;
                turnedOnForScan = false;
            }
        }

        public bool TryReadFrame(ScanFrame frame)
        {
            if (!displayMatrix.HasValue || occlusion == null || !occlusion.enabled)
            {
                return false;
            }

            if (!occlusion.TryAcquireRawEnvironmentDepthCpuImage(out var depthImage)
                && !occlusion.TryAcquireEnvironmentDepthCpuImage(out depthImage))
            {
                readsWithoutDepth++;
                return false;
            }

            receivedDepth = true;
            using (depthImage)
            {
                if (depthImage.timestamp.Equals(lastTimestamp))
                {
                    return false;
                }

                lastTimestamp = depthImage.timestamp;
                var hasConfidence = occlusion.TryAcquireEnvironmentDepthConfidenceCpuImage(out var confidenceImage);
                try
                {
                    if (!DepthPixels.TryCreate(depthImage, confidenceImage, hasConfidence, MinConfidence, out var pixels))
                    {
                        return false;
                    }

                    if (!mappingChecked)
                    {
                        CheckMapping(pixels);
                        return false;
                    }

                    Sample(pixels, frame);
                    return frame.Count > 0;
                }
                finally
                {
                    if (hasConfidence)
                    {
                        confidenceImage.Dispose();
                    }
                }
            }
        }

        void OnCameraFrame(ARCameraFrameEventArgs args)
        {
            if (args.displayMatrix.HasValue)
            {
                displayMatrix = args.displayMatrix.Value;
            }
        }

        void Sample(DepthPixels pixels, ScanFrame frame)
        {
            var rows = Mathf.Clamp(Mathf.RoundToInt(Columns * (float)camera.pixelHeight / Mathf.Max(1, camera.pixelWidth)),
                1, MaxRows);
            var count = Columns * rows;
            if (samples.Length < count)
            {
                samples = new Vector3[count];
                valid = new bool[count];
            }

            UpdateViewDirections(rows);
            var cameraTransform = camera.transform;
            var origin = cameraTransform.position;
            var forward = cameraTransform.forward;
            var cameraToWorld = camera.cameraToWorldMatrix;
            var display = displayMatrix.GetValueOrDefault(Matrix4x4.identity);
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < Columns; column++)
                {
                    var index = row * Columns + column;
                    var viewport = new Vector2((column + 0.5f) / Columns, (row + 0.5f) / rows);
                    valid[index] = false;
                    if (!mapping.TryGetPixel(viewport, display, pixels.Width, pixels.Height, out var x, out var y))
                    {
                        continue;
                    }

                    var meters = pixels.MetersAt(x, y);
                    if (meters <= 0f)
                    {
                        continue;
                    }

                    var direction = cameraToWorld.MultiplyVector(viewDirections[index]).normalized;
                    samples[index] = DepthGeometry.PointFromDepth(origin, forward, direction, meters);
                    valid[index] = true;
                }
            }

            frame.Reset(origin, true);
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < Columns; column++)
                {
                    var index = row * Columns + column;
                    if (valid[index])
                    {
                        frame.Add(samples[index], FacesUpOrDown(column, row, rows, origin));
                    }
                }
            }
        }

        /// <summary>
        /// Camera-space ray of every sample. They only change with the projection (not with the pose), so
        /// they are computed once instead of calling Camera.ViewportPointToRay thousands of times per frame.
        /// </summary>
        void UpdateViewDirections(int rows)
        {
            var projection = camera.projectionMatrix;
            if (rows == directionsRows && projection == directionsProjection && viewDirections.Length >= Columns * rows)
            {
                return;
            }

            if (viewDirections.Length < Columns * rows)
            {
                viewDirections = new Vector3[Columns * rows];
            }

            var inverse = projection.inverse;
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < Columns; column++)
                {
                    var viewport = new Vector2((column + 0.5f) / Columns, (row + 0.5f) / rows);
                    viewDirections[row * Columns + column] = DepthGeometry.ViewDirection(inverse, viewport);
                }
            }

            directionsRows = rows;
            directionsProjection = projection;
        }

        /// <summary>Normal from the neighbors a few samples away in both directions.</summary>
        bool FacesUpOrDown(int column, int row, int rows, Vector3 origin)
        {
            var index = row * Columns + column;
            var maxLength = MaxTangentPerMeter * Vector3.Distance(origin, samples[index]);
            return TryTangent(column, row, 1, 0, rows, maxLength, out var alongRow)
                   && TryTangent(column, row, 0, 1, rows, maxLength, out var alongColumn)
                   && DepthGeometry.IsHorizontal(alongRow, alongColumn, HorizontalCosine);
        }

        bool TryTangent(int column, int row, int stepColumn, int stepRow, int rows, float maxLength, out Vector3 tangent)
        {
            tangent = default;
            var hasAfter = TryGetSample(column + stepColumn * NormalStride, row + stepRow * NormalStride, rows, out var after);
            var hasBefore = TryGetSample(column - stepColumn * NormalStride, row - stepRow * NormalStride, rows, out var before);
            var center = samples[row * Columns + column];
            if (hasAfter && hasBefore)
            {
                tangent = after - before;
                return tangent.magnitude <= maxLength * 2f;
            }

            if (hasAfter || hasBefore)
            {
                tangent = hasAfter ? after - center : center - before;
                return tangent.magnitude <= maxLength;
            }

            return false;
        }

        bool TryGetSample(int column, int row, int rows, out Vector3 point)
        {
            point = default;
            if (column < 0 || row < 0 || column >= Columns || row >= rows || !valid[row * Columns + column])
            {
                return false;
            }

            point = samples[row * Columns + column];
            return true;
        }

        /// <summary>
        /// Compares the four mapping candidates with ARCore's depth hit test, adding up the errors over many
        /// frames. A candidate is accepted when it is close and clearly better than the others; a flat floor
        /// straight ahead cannot tell them apart, so the check waits for views with some depth variety.
        /// </summary>
        void CheckMapping(DepthPixels pixels)
        {
            mappingAttempts++;
            var origin = camera.transform.position;
            var forward = camera.transform.forward;
            var display = displayMatrix.GetValueOrDefault(Matrix4x4.identity);
            var anyHit = false;
            foreach (var viewport in MappingCheckPoints)
            {
                var screen = new Vector2(viewport.x * camera.pixelWidth, viewport.y * camera.pixelHeight);
                if (raycasts == null || !raycasts.Raycast(screen, hits, TrackableType.Depth))
                {
                    continue;
                }

                anyHit = true;
                var expected = Vector3.Distance(origin, hits[0].pose.position);
                var direction = camera.ViewportPointToRay(viewport).direction;
                for (var k = 0; k < DepthImageMapping.Candidates.Length; k++)
                {
                    if (!DepthImageMapping.Candidates[k].TryGetPixel(viewport, display, pixels.Width, pixels.Height,
                            out var x, out var y))
                    {
                        continue;
                    }

                    var meters = pixels.MetersNear(x, y, MappingSearchRadius);
                    if (meters > 0f)
                    {
                        var point = DepthGeometry.PointFromDepth(origin, forward, direction, meters);
                        mappingErrors[k] += Mathf.Abs(Vector3.Distance(origin, point) - expected);
                        mappingCounts[k]++;
                    }
                }
            }

            if (anyHit)
            {
                mappingAttemptsWithHits++;
            }

            if (TryPickMapping(out var best))
            {
                mapping = DepthImageMapping.Candidates[best];
                mappingChecked = true;
                Debug.Log($"[Erasmus AR Toolkit] Depth mapping self-check: using {mapping} " +
                          $"(mean error {mappingErrors[best] / mappingCounts[best]:0.000} m).");
                return;
            }

            if (mappingAttempts >= MappingChecksWithoutHits && mappingAttemptsWithHits == 0)
            {
                // Without ARCore's depth hit test the mapping cannot be trusted: use tracking points instead.
                mappingUnverifiable = true;
                Debug.LogWarning("[Erasmus AR Toolkit] Depth mapping self-check: no depth hit test available, " +
                                 "so the room scan uses tracking points.");
            }
        }

        bool TryPickMapping(out int best)
        {
            best = -1;
            var bestError = float.MaxValue;
            var secondError = float.MaxValue;
            for (var k = 0; k < mappingErrors.Length; k++)
            {
                if (mappingCounts[k] < MappingMinPoints)
                {
                    continue;
                }

                var error = mappingErrors[k] / mappingCounts[k];
                if (error < bestError)
                {
                    secondError = bestError;
                    bestError = error;
                    best = k;
                }
                else if (error < secondError)
                {
                    secondError = error;
                }
            }

            return best >= 0 && bestError <= MappingMaxError && secondError - bestError >= MappingMinLead;
        }
    }
}
