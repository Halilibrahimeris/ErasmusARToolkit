using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Debug = UnityEngine.Debug;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Room scan frames from ARCore Depth: a grid of points is looked up in the raw depth image and
    /// turned into points in space. Neighboring points give each point a normal, so table sides and walls
    /// are not mistaken for table tops. The grid covers the whole depth image, also the parts beside the
    /// screen (the camera sees more than the screen shows), so a scan covers the room faster.
    /// <para>
    /// The image is copied when the camera is about to draw: the camera then has the pose of the ARCore
    /// frame the image arrived with. The scan reads the copy when it takes its next frame.
    /// </para>
    /// <para>
    /// How the depth image lines up with the screen is checked against ARCore's own depth hit test (see
    /// <see cref="MappingSelfCheck"/>). Until a mapping is verified, frames are handed out with
    /// <see cref="ScanFrame.AllowCarving"/> off. If no mapping fits, or no depth arrives, the source reports
    /// itself unavailable and the scan continues with tracking points. If the occlusion manager was off
    /// (occlusion disabled in the config), it is switched on without occlusion for the scan and back off after.
    /// </para>
    /// </summary>
    internal sealed class DepthScanSource : IScanFrameSource, IScanSourceDiagnostics
    {
        /// <summary>
        /// When a depth image is older than its camera frame, which camera pose do its points belong to?
        /// ARCore moves the image to every new frame, so the default is the pose of that frame.
        /// </summary>
        public enum PosePolicy
        {
            /// <summary>The pose of the camera frame the image arrived with.</summary>
            CameraFrame,

            /// <summary>The pose the camera had at the time of the depth image.</summary>
            DepthTime,
        }

        /// <summary>Points of the grid across the screen. Beside the screen the grid goes on at the same distance.</summary>
        public const int Columns = 56;

        public const int MaxRows = 104;

        /// <summary>Most points a frame is sampled at.</summary>
        public const int MaxSamples = 6400;

        /// <summary>
        /// The grid reaches this far (in screen widths or heights) beyond the screen at most: far from
        /// the middle of the image depth is poor.
        /// </summary>
        public const float MaxBeyondScreen = 0.5f;

        /// <summary>Points closer than this (meters) to their neighbors in the grid tell the scan nothing new.</summary>
        public const float MinPointSpacing = 0.02f;

        public const byte MinConfidence = RoomSurfaceMap.MinConfidence;

        static readonly Unity.Profiling.ProfilerMarker CopyMarker =
            new Unity.Profiling.ProfilerMarker("Erasmus.RoomScan.CopyDepth");

        static readonly Unity.Profiling.ProfilerMarker SampleMarker =
            new Unity.Profiling.ProfilerMarker("Erasmus.RoomScan.SampleDepth");

        const int NormalStride = 2;
        const float MaxTangentPerMeter = 0.35f;
        const float HorizontalCosine = 0.85f;

        /// <summary>Neighbors whose depths differ more than this (meters, or this share of the depth) lie on different things.</summary>
        const float MaxDepthStep = 0.05f;
        const float MaxDepthStepPerMeter = 0.08f;

        const int MappingSearchRadius = 2;
        const int MappingReportEvery = 40;

        /// <summary>While the mapping is only assumed, one in so many images is checked again.</summary>
        const int RecheckEvery = 4;

        /// <summary>A depth image this much older (seconds) than its frame, or newer than it, has a wrong time.</summary>
        const double MaxPlausibleLag = 0.5;

        /// <summary>Reads (made only while tracking and holding steady) without any depth image before giving up.</summary>
        const int ReadsWithoutDepth = 40;

        static readonly Vector2[] MappingCheckPoints =
        {
            new Vector2(0.3f, 0.3f), new Vector2(0.7f, 0.3f), new Vector2(0.3f, 0.7f),
            new Vector2(0.7f, 0.7f), new Vector2(0.5f, 0.85f), new Vector2(0.5f, 0.15f),
            new Vector2(0.2f, 0.5f), new Vector2(0.8f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.35f, 0.9f), new Vector2(0.65f, 0.9f), new Vector2(0.5f, 0.65f),
        };

        readonly AROcclusionManager occlusion;
        readonly ARCameraManager cameraManager;
        readonly ARRaycastManager raycasts;
        readonly Camera camera;
        readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        readonly MappingSelfCheck check = new MappingSelfCheck(DepthImageMapping.Candidates.Length);
        readonly CameraPoseBuffer poses = new CameraPoseBuffer();
        readonly DepthSnapshot snapshot = new DepthSnapshot();

        Vector3[] samples = Array.Empty<Vector3>();
        float[] sampleDepths = Array.Empty<float>();
        byte[] sampleConfidences = Array.Empty<byte>();
        bool[] valid = Array.Empty<bool>();
        Vector3[] viewDirections = Array.Empty<Vector3>();
        Vector3[] lastInColumn = Array.Empty<Vector3>();
        bool[] hasLastInColumn = Array.Empty<bool>();
        Matrix4x4 directionsProjection;
        Rect directionsBounds;
        Rect sampleBounds = new Rect(0f, 0f, 1f, 1f);
        int columns = Columns;
        int directionsRows;
        int directionsColumns;
        Matrix4x4? displayMatrix;
        double lastTimestamp = double.NaN;
        long frameTimeNs;
        bool hasFrameTime;
        bool began;
        bool captureRequested;
        bool lastCaptureHadNoImage;
        bool turnedOnForScan;
        OcclusionPreferenceMode previousPreference;
        int readsWithoutDepth;
        int imagesSinceRecheck;
        bool receivedDepth;
        float copyMs;

        public DepthScanSource(AROcclusionManager occlusionManager, ARCameraManager arCameraManager,
            ARRaycastManager raycastManager, Camera arCamera)
        {
            occlusion = occlusionManager;
            cameraManager = arCameraManager;
            raycasts = raycastManager;
            camera = arCamera;
        }

        /// <summary>
        /// Frames were read with a mapping that was only assumed, and it was the wrong one: what was
        /// scanned with it lies in wrong places.
        /// </summary>
        public event Action MappingChanged;

        /// <summary>See <see cref="PosePolicy"/>. Decided by measuring on a phone (the log shows the lag).</summary>
        public PosePolicy Policy { get; set; } = PosePolicy.CameraFrame;

        public bool IsPrecise => true;

        public MappingState Mapping => check.State;

        public float DepthLagMs { get; private set; } = float.NaN;

        public int DepthWidth { get; private set; }

        public int DepthHeight { get; private set; }

        public float ConfidentRatio { get; private set; }

        /// <summary>Time to copy a depth image and to turn it into points, in milliseconds.</summary>
        public float CaptureMs { get; private set; }

        /// <summary>
        /// Uses the parts of the depth image that lie beside the screen, too. On by default; off, the
        /// scan sees what the screen shows.
        /// </summary>
        public bool UseWholeImage { get; set; } = true;

        /// <summary>Points of the last frame before and after those that lay too close together were left out.</summary>
        public int SampledPoints { get; private set; }

        public int KeptPoints { get; private set; }

        public bool IsAvailable
        {
            get
            {
                if (occlusion == null || cameraManager == null || camera == null)
                {
                    return false;
                }

                var descriptor = occlusion.descriptor;
                if (check.State == MappingState.Unverifiable
                    || (descriptor != null && descriptor.environmentDepthImageSupported == Supported.Unsupported))
                {
                    return false;
                }

                // Support can stay "unknown" for a while; many steady, tracked reads without depth mean none.
                return receivedDepth || readsWithoutDepth < ReadsWithoutDepth;
            }
        }

        public float MappingError(int candidate) => check.MeanError(candidate);

        public void Begin()
        {
            if (occlusion == null || cameraManager == null || began)
            {
                return;
            }

            began = true;
            captureRequested = true;
            snapshot.Ready = false;
            cameraManager.frameReceived += OnCameraFrame;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
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
            if (!began)
            {
                return;
            }

            began = false;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
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

            snapshot.Dispose();
            poses.Clear();
        }

        public bool TryReadFrame(ScanFrame frame)
        {
            if (!snapshot.Ready)
            {
                if (lastCaptureHadNoImage)
                {
                    lastCaptureHadNoImage = false;
                    readsWithoutDepth++;
                }

                captureRequested = true;
                return false;
            }

            var started = Stopwatch.GetTimestamp();
            using (SampleMarker.Auto())
            {
                Sample(frame);
            }

            frame.Time = snapshot.FrameTime;
            frame.AllowCarving = check.State == MappingState.Verified;
            snapshot.Ready = false;
            captureRequested = true;
            CaptureMs = copyMs + Milliseconds(started);
            return frame.Count > 0;
        }

        void OnCameraFrame(ARCameraFrameEventArgs args)
        {
            if (args.displayMatrix.HasValue)
            {
                displayMatrix = args.displayMatrix.Value;
            }

            if (args.timestampNs.HasValue)
            {
                frameTimeNs = args.timestampNs.Value;
                hasFrameTime = true;
            }
        }

        /// <summary>
        /// The camera is about to draw: its pose is the one of the ARCore frame that is current, and so are
        /// the depth image and the depth hit test.
        /// </summary>
        void OnBeginCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
        {
            if (renderingCamera != camera || !displayMatrix.HasValue || occlusion == null || !occlusion.enabled)
            {
                return;
            }

            var cameraTransform = camera.transform;
            var pose = new Pose(cameraTransform.position, cameraTransform.rotation);
            var frameTime = hasFrameTime ? frameTimeNs * 1e-9 : Time.unscaledTimeAsDouble;
            if (hasFrameTime)
            {
                poses.Push(frameTime, pose);
            }

            if (!captureRequested || snapshot.Ready)
            {
                return;
            }

            var started = Stopwatch.GetTimestamp();
            using (CopyMarker.Auto())
            {
                Capture(pose, frameTime);
            }

            copyMs = Milliseconds(started);
        }

        void Capture(Pose pose, double frameTime)
        {
            if (!occlusion.TryAcquireRawEnvironmentDepthCpuImage(out var depthImage))
            {
                lastCaptureHadNoImage = true;
                return;
            }

            receivedDepth = true;
            using (depthImage)
            {
                if (depthImage.timestamp.Equals(lastTimestamp))
                {
                    // ARCore only moved the last image to the new frame: nothing new was measured.
                    return;
                }

                if (!occlusion.TryAcquireEnvironmentDepthConfidenceCpuImage(out var confidenceImage))
                {
                    return;
                }

                using (confidenceImage)
                {
                    if (!snapshot.CopyFrom(depthImage, confidenceImage))
                    {
                        return;
                    }
                }

                lastTimestamp = depthImage.timestamp;
                snapshot.DepthTime = depthImage.timestamp;
            }

            DepthWidth = snapshot.Width;
            DepthHeight = snapshot.Height;
            var lag = frameTime - snapshot.DepthTime;
            var plausible = hasFrameTime && lag >= 0.0 && lag <= MaxPlausibleLag;
            DepthLagMs = hasFrameTime ? (float)(lag * 1000.0) : float.NaN;
            if (Policy == PosePolicy.DepthTime && plausible && poses.TrySample(snapshot.DepthTime, out var earlier))
            {
                pose = earlier;
            }

            snapshot.Position = pose.position;
            snapshot.Rotation = pose.rotation;
            snapshot.Projection = camera.projectionMatrix;
            snapshot.Display = displayMatrix.GetValueOrDefault(Matrix4x4.identity);
            snapshot.FrameTime = frameTime;
            ConfidentRatio = snapshot.ConfidentShare(MinConfidence);

            if (!check.IsFinal && (check.State == MappingState.Checking || ++imagesSinceRecheck >= RecheckEvery))
            {
                imagesSinceRecheck = 0;
                CheckMapping();
            }

            // Until a mapping can be used the next image is wanted right away.
            snapshot.Ready = check.IsUsable;
            captureRequested = !snapshot.Ready;
        }

        /// <summary>
        /// The grid of sample points: over the screen as before, and on beside it as far as the depth
        /// image reaches, with the same distance between the points (fewer when there would be too many).
        /// </summary>
        internal static void LayOutGrid(Rect imageBounds, float screenAspect, out Rect bounds, out int gridColumns,
            out int gridRows)
        {
            bounds = Rect.MinMaxRect(
                Mathf.Max(Mathf.Min(imageBounds.xMin, 0f), -MaxBeyondScreen),
                Mathf.Max(Mathf.Min(imageBounds.yMin, 0f), -MaxBeyondScreen),
                Mathf.Min(Mathf.Max(imageBounds.xMax, 1f), 1f + MaxBeyondScreen),
                Mathf.Min(Mathf.Max(imageBounds.yMax, 1f), 1f + MaxBeyondScreen));
            var screenRows = Mathf.Clamp(Mathf.RoundToInt(Columns * screenAspect), 1, MaxRows);
            var wide = Columns * bounds.width;
            var high = screenRows * bounds.height;
            var scale = Mathf.Min(1f, Mathf.Sqrt(MaxSamples / Mathf.Max(wide * high, 1f)));
            gridColumns = Mathf.Max(1, Mathf.FloorToInt(wide * scale));
            gridRows = Mathf.Max(1, Mathf.FloorToInt(high * scale));
        }

        void Sample(ScanFrame frame)
        {
            var mapping = DepthImageMapping.Candidates[check.Best];
            var imageBounds = new Rect(0f, 0f, 1f, 1f);
            if (UseWholeImage)
            {
                mapping.TryGetImageBounds(snapshot.Display, out imageBounds);
            }

            LayOutGrid(imageBounds, (float)camera.pixelHeight / Mathf.Max(1, camera.pixelWidth), out sampleBounds,
                out columns, out var rows);
            var count = columns * rows;
            if (samples.Length < count)
            {
                samples = new Vector3[count];
                sampleDepths = new float[count];
                sampleConfidences = new byte[count];
                valid = new bool[count];
            }

            UpdateViewDirections(rows);
            var origin = snapshot.Position;
            var rotation = snapshot.Rotation;
            var forward = rotation * Vector3.forward;
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var index = row * columns + column;
                    var viewport = ViewportOf(column, row, rows);
                    valid[index] = false;
                    if (!mapping.TryGetPixel(viewport, snapshot.Display, snapshot.Width, snapshot.Height,
                            out var x, out var y))
                    {
                        continue;
                    }

                    var meters = snapshot.MetersAt(x, y, MinConfidence);
                    if (meters <= 0f)
                    {
                        continue;
                    }

                    // Camera space looks down -z, the camera object looks down +z.
                    var local = viewDirections[index];
                    var direction = (rotation * new Vector3(local.x, local.y, -local.z)).normalized;
                    samples[index] = DepthGeometry.PointFromDepth(origin, forward, direction, meters);
                    sampleDepths[index] = meters;
                    sampleConfidences[index] = snapshot.ConfidenceAt(x, y);
                    valid[index] = true;
                }
            }

            frame.Reset(origin, true);
            frame.Rotation = rotation;
            if (lastInColumn.Length < columns)
            {
                lastInColumn = new Vector3[columns];
                hasLastInColumn = new bool[columns];
            }

            Array.Clear(hasLastInColumn, 0, columns);
            SampledPoints = 0;
            var spacing = MinPointSpacing * MinPointSpacing;
            for (var row = 0; row < rows; row++)
            {
                var hasLastInRow = false;
                var lastInRow = Vector3.zero;
                for (var column = 0; column < columns; column++)
                {
                    var index = row * columns + column;
                    if (!valid[index])
                    {
                        continue;
                    }

                    // Close to the camera the points of the grid lie a few millimeters apart: the scan
                    // works with cells of 5 cm, so most of them would only cost time.
                    SampledPoints++;
                    var point = samples[index];
                    if (hasLastInRow && hasLastInColumn[column] && (point - lastInRow).sqrMagnitude < spacing
                        && (point - lastInColumn[column]).sqrMagnitude < spacing)
                    {
                        continue;
                    }

                    frame.Add(point, FacesUp(column, row, rows, origin), sampleConfidences[index]);
                    lastInRow = point;
                    hasLastInRow = true;
                    lastInColumn[column] = point;
                    hasLastInColumn[column] = true;
                }
            }

            KeptPoints = frame.Count;
        }

        Vector2 ViewportOf(int column, int row, int rows)
        {
            return new Vector2(sampleBounds.xMin + (column + 0.5f) / columns * sampleBounds.width,
                sampleBounds.yMin + (row + 0.5f) / rows * sampleBounds.height);
        }

        /// <summary>
        /// Camera-space ray of every sample. They only change with the projection (not with the pose), so
        /// they are computed once instead of calling Camera.ViewportPointToRay thousands of times per frame.
        /// </summary>
        void UpdateViewDirections(int rows)
        {
            var projection = snapshot.Projection;
            if (rows == directionsRows && columns == directionsColumns && sampleBounds == directionsBounds
                && projection == directionsProjection && viewDirections.Length >= columns * rows)
            {
                return;
            }

            if (viewDirections.Length < columns * rows)
            {
                viewDirections = new Vector3[columns * rows];
            }

            var inverse = projection.inverse;
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    viewDirections[row * columns + column] = DepthGeometry.ViewDirection(inverse, ViewportOf(column, row, rows));
                }
            }

            directionsRows = rows;
            directionsColumns = columns;
            directionsBounds = sampleBounds;
            directionsProjection = projection;
        }

        /// <summary>Normal from the neighbors a few samples away in both directions.</summary>
        bool FacesUp(int column, int row, int rows, Vector3 origin)
        {
            var center = samples[row * columns + column];
            var maxLength = MaxTangentPerMeter * Vector3.Distance(origin, center);
            return TryTangent(column, row, 1, 0, rows, maxLength, out var alongRow)
                   && TryTangent(column, row, 0, 1, rows, maxLength, out var alongColumn)
                   && DepthGeometry.IsUpFacing(alongRow, alongColumn, origin - center, HorizontalCosine);
        }

        bool TryTangent(int column, int row, int stepColumn, int stepRow, int rows, float maxLength, out Vector3 tangent)
        {
            tangent = default;
            var index = row * columns + column;
            var center = samples[index];
            var depth = sampleDepths[index];
            var hasAfter = TryGetSample(column + stepColumn * NormalStride, row + stepRow * NormalStride, rows, depth,
                out var after);
            var hasBefore = TryGetSample(column - stepColumn * NormalStride, row - stepRow * NormalStride, rows, depth,
                out var before);
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

        /// <summary>
        /// A neighbor on the same thing. At the edge of a desk the next sample lies on the floor behind it:
        /// a normal across such a step would be tilted or, worse, look level.
        /// </summary>
        bool TryGetSample(int column, int row, int rows, float depth, out Vector3 point)
        {
            point = default;
            if (column < 0 || row < 0 || column >= columns || row >= rows)
            {
                return false;
            }

            var index = row * columns + column;
            if (!valid[index]
                || Mathf.Abs(sampleDepths[index] - depth) > Mathf.Max(MaxDepthStep, MaxDepthStepPerMeter * depth))
            {
                return false;
            }

            point = samples[index];
            return true;
        }

        /// <summary>
        /// Compares the mapping candidates with ARCore's depth hit test at some screen points, in the image
        /// that was just copied.
        /// </summary>
        void CheckMapping()
        {
            var cameraTransform = camera.transform;
            var origin = cameraTransform.position;
            var forward = cameraTransform.forward;
            var stateBefore = check.State;
            var bestBefore = check.Best;
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
                    if (!DepthImageMapping.Candidates[k].TryGetPixel(viewport, snapshot.Display, snapshot.Width,
                            snapshot.Height, out var x, out var y))
                    {
                        continue;
                    }

                    var meters = snapshot.MetersNear(x, y, MappingSearchRadius, MinConfidence);
                    if (meters > 0f)
                    {
                        var point = DepthGeometry.PointFromDepth(origin, forward, direction, meters);
                        check.AddSample(k, Mathf.Abs(Vector3.Distance(origin, point) - expected));
                    }
                }
            }

            if (check.EndImage(anyHit))
            {
                Report(stateBefore, bestBefore);
            }
            else if (check.State == MappingState.Checking && check.Images % MappingReportEvery == 0)
            {
                Debug.Log($"[Erasmus AR Toolkit] Depth mapping self-check: undecided after {check.Images} " +
                          $"image(s), {check.ImagesWithHits} with depth hits. Candidates: {DescribeCandidates()}.");
            }
        }

        void Report(MappingState stateBefore, int bestBefore)
        {
            var mapping = DepthImageMapping.Candidates[check.Best];
            switch (check.State)
            {
                case MappingState.Verified:
                    Debug.Log($"[Erasmus AR Toolkit] Depth mapping self-check: using {mapping} after " +
                              $"{check.Images} image(s). Candidates: {DescribeCandidates()}.");
                    break;
                case MappingState.Assumed:
                    Debug.Log("[Erasmus AR Toolkit] Depth mapping self-check: no clear winner after " +
                              $"{check.Images} image(s), assuming {mapping}. The scan adds surfaces but removes " +
                              $"nothing until a mapping is verified. Candidates: {DescribeCandidates()}.");
                    break;
                default:
                    Debug.LogWarning("[Erasmus AR Toolkit] Depth mapping self-check: the depth image cannot be " +
                                     "used, so the room scan uses tracking points. Candidates: " +
                                     DescribeCandidates() + ".");
                    break;
            }

            if (stateBefore == MappingState.Assumed && check.IsUsable && check.Best != bestBefore)
            {
                MappingChanged?.Invoke();
            }
        }

        /// <summary>Mean error of every candidate for the log ("-" without samples).</summary>
        string DescribeCandidates()
        {
            var text = new StringBuilder();
            for (var k = 0; k < DepthImageMapping.Candidates.Length; k++)
            {
                if (k > 0)
                {
                    text.Append("; ");
                }

                var error = check.MeanError(k);
                text.Append(DepthImageMapping.Candidates[k]).Append(' ');
                text.Append(float.IsNaN(error) ? "-" : error.ToString("0.000", CultureInfo.InvariantCulture));
                text.Append(" m (").Append(check.Points(k)).Append(')');
            }

            return text.ToString();
        }

        static float Milliseconds(long startedAt)
        {
            return (Stopwatch.GetTimestamp() - startedAt) * 1000f / Stopwatch.Frequency;
        }
    }
}
