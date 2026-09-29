using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Debug = UnityEngine.Debug;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Room scan mode, like the space setup of a VR headset. While scanning, depth frames (or tracking
    /// points on phones without depth) are fused into a <see cref="RoomSurfaceMap"/>: the floor is found
    /// first, surfaces are confirmed by repeated observations, and parts of ARCore planes that the camera
    /// can see through (plane hulls reaching past a table edge, duplicate floors, small fragments)
    /// disappear. The scanned surfaces replace the raw ARCore planes for drawing and placement. After
    /// <see cref="FinishScan"/> the model stays as it is; scanning again adds to it,
    /// <see cref="ResetScan"/> starts over. Lives on the XR Origin.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomScanner : MonoBehaviour
    {
        const float IntegrateInterval = 0.125f;
        const float RebuildInterval = 0.5f;
        const float PriorInterval = 0.5f;
        const float MaxTurnDegreesPerSecond = 60f;

        /// <summary>Faster than a slow walk (meters per second) the depth image is smeared.</summary>
        const float MaxMetersPerSecond = 1.5f;

        /// <summary>A frame from this far (meters) away from the last one shows the room from a new side...</summary>
        const float KeyframeMeters = 0.05f;

        /// <summary>...and so does one that looks this many degrees another way.</summary>
        const float KeyframeDegrees = 4f;

        /// <summary>Frames that show nothing new count this often (seconds) and this much.</summary>
        const float SameViewInterval = 0.5f;
        const float SameViewWeight = 0.5f;

        /// <summary>The first frames always count: a scan has to start somewhere.</summary>
        const int FirstFrames = 6;

        const float RaycastDistance = 20f;
        const float DiagnosticsInterval = 2f;
        const float MinPlaneFloorArea = 1f;
        const float TimingSmoothing = 0.2f;

        /// <summary>With fewer usable depth pixels than this share, for this long, the floor is too shiny or plain.</summary>
        const float WeakDepthShare = 0.15f;
        const float WeakDepthSeconds = 5f;
        const float LookingDownDegrees = 30f;

        const string OccluderShaderName = "Erasmus AR Toolkit/Occluder";
        const string SurfaceDepthShaderName = "Erasmus AR Toolkit/Surface Depth";

        [SerializeField, Tooltip("Scanned tables hide the floor grid and virtual objects behind or under them.")]
        bool useOccluders = true;

        [SerializeField, Tooltip("On scanned surfaces, noisy real-world depth is replaced by the surface's own depth, " +
                                 "so grids and objects standing on the floor or a table are not cut into patches.")]
        bool steadySurfaceDepth = true;

        readonly RoomSurfaceMap map = new RoomSurfaceMap();
        readonly RoomModelBuilder model = new RoomModelBuilder();
        readonly ScanFrame frame = new ScanFrame();
        readonly List<Vector2> polygon = new List<Vector2>();
        readonly DepthResiduals residuals = new DepthResiduals();
        readonly StringBuilder report = new StringBuilder(256);

        ARPlaneManager planeManager;
        ARPlaneSurfaces planeSurfaces;
        ARCameraManager cameraManager;
        ToolkitTheme theme;
        Camera arCamera;
        Material fillMaterial;
        Material outlineMaterial;
        Material occluderMaterial;
        Material surfaceDepthMaterial;
        Material ownedOccluderMaterial;
        Material ownedSurfaceDepthMaterial;
        RoomSurfaceView view;
        IScanFrameSource preciseSource;
        IScanFrameSource sparseSource;
        IScanFrameSource source;
        IScanSourceDiagnostics watchedSource;
        Func<bool> isTracking = () => ARSession.state == ARSessionState.SessionTracking;
        bool visible = true;
        bool priorsDirty;
        bool mappingChanged;
        float nextIntegrate;
        float nextRebuild;
        float nextPriors;
        float lastRebuild;
        float lastIntegrated;
        Vector3 lastPosition;
        Quaternion lastRotation = Quaternion.identity;
        Vector3 keyPosition;
        Quaternion keyRotation = Quaternion.identity;
        float turnSpeed;
        float moveSpeed;
        float weakDepthSince = -1f;
        bool weakDepthSaid;
        ScanDiagnostics diagnostics;
        XRCameraBackgroundRenderingMode reportedBackground;
        float nextDiagnostics;
        float integrateMs;
        float rebuildMs;

        public event Action<bool> ScanningChanged;
        public event Action<RoomScanProgress> ProgressChanged;
        public event Action<string> Notification;

        public bool IsScanning { get; private set; }

        /// <summary>True once the scan has drawn at least one surface. Placement then uses the scan only.</summary>
        public bool HasModel { get; private set; }

        public RoomScanProgress Progress { get; private set; }

        /// <summary>What the scan measured last (updated with every redraw while scanning).</summary>
        public ScanDiagnostics Diagnostics => diagnostics;

        internal RoomSurfaceMap Map => map;

        /// <summary>Called by the toolkit: the AR camera and the theme with the depth-only materials.</summary>
        internal void Initialize(Camera camera, ToolkitTheme toolkitTheme)
        {
            arCamera = camera;
            theme = toolkitTheme;
            occluderMaterial = theme != null ? theme.OccluderMaterial : null;
            surfaceDepthMaterial = theme != null ? theme.SurfaceDepthMaterial : null;
        }

        /// <summary>Tests replace the AR inputs with their own.</summary>
        internal void ConfigureForTests(Camera camera, IScanFrameSource frameSource, Func<bool> tracking,
            Material fill, Material outline, Material occluder, Material surfaceDepth = null)
        {
            ConfigureForTests(camera, frameSource, frameSource, tracking, fill, outline, occluder, surfaceDepth);
        }

        /// <param name="precise">Stands in for the depth images.</param>
        /// <param name="sparse">Stands in for the tracking points the scan falls back to.</param>
        internal void ConfigureForTests(Camera camera, IScanFrameSource precise, IScanFrameSource sparse,
            Func<bool> tracking, Material fill, Material outline, Material occluder, Material surfaceDepth = null)
        {
            surfaceDepthMaterial = surfaceDepth;
            arCamera = camera;
            preciseSource = precise;
            sparseSource = sparse;
            isTracking = tracking;
            fillMaterial = fill;
            outlineMaterial = outline;
            occluderMaterial = occluder;
        }

        void Awake()
        {
            planeManager = GetComponent<ARPlaneManager>();
            planeSurfaces = GetComponent<ARPlaneSurfaces>();
            ReadPlaneMaterials();
        }

        void OnDisable()
        {
            // The AR rig was switched off: stop reading frames, keep what was scanned.
            StopScanning();
            UpdatePlaneSuppression();
        }

        void OnDestroy()
        {
            StopScanning();
            view?.Dispose();
            ObjectUtility.SafeDestroy(ownedOccluderMaterial);
            ObjectUtility.SafeDestroy(ownedSurfaceDepthMaterial);
        }

        void Update()
        {
            if (!IsScanning)
            {
                return;
            }

            TrackCameraSpeed();
            SwitchToSparseIfNeeded();
            if (mappingChanged)
            {
                StartOverAfterMappingChange();
            }

            var now = Time.unscaledTime;
            if (priorsDirty && now >= nextPriors)
            {
                UpdatePriors();
            }

            if (now >= nextIntegrate)
            {
                Integrate();
            }

            if (now >= nextRebuild)
            {
                Rebuild(false);
            }

            if (now >= nextDiagnostics)
            {
                ReportDiagnostics();
            }
        }

        public void StartScan()
        {
            if (IsScanning)
            {
                return;
            }

            CreateSources();
            source = PickSource();
            if (source == null)
            {
                Notify(RoomScanText.NoSurfaces);
                return;
            }

            UseSource(source);
            IsScanning = true;
            if (arCamera != null)
            {
                var cameraTransform = arCamera.transform;
                lastPosition = keyPosition = cameraTransform.position;
                lastRotation = keyRotation = cameraTransform.rotation;
            }

            if (planeManager != null)
            {
                planeManager.trackablesChanged.AddListener(OnPlanesChanged);
            }

            UpdatePriors();
            Rebuild(true);

            // Keep the plane reading and the redraw half a period apart so they never share a frame.
            var now = Time.unscaledTime;
            nextRebuild = now + RebuildInterval;
            nextPriors = now + PriorInterval * 0.5f;
            nextDiagnostics = now + DiagnosticsInterval;
            lastIntegrated = 0f;
            weakDepthSince = -1f;
            weakDepthSaid = false;
            residuals.Clear();
            ScanningChanged?.Invoke(true);
            if (!source.IsPrecise)
            {
                Notify(RoomScanText.LessPrecise);
            }
        }

        public void FinishScan()
        {
            if (!IsScanning)
            {
                return;
            }

            StopScanning();
            Rebuild(true);
            Notify(HasModel ? RoomScanText.Finished(Progress) : RoomScanText.NoSurfaces);
        }

        /// <summary>Forgets everything scanned. A running scan keeps going from scratch.</summary>
        public void ResetScan()
        {
            ClearModel();
            if (IsScanning)
            {
                UpdatePriors();
                Rebuild(true);
            }
            else
            {
                UpdatePlaneSuppression();
                Progress = default;
                ProgressChanged?.Invoke(Progress);
            }

            Notify(RoomScanText.ScanReset);
        }

        /// <summary>Shows or hides the scanned grid and outlines (occluders keep working).</summary>
        public void SetVisible(bool value)
        {
            visible = value;
            view?.SetVisible(value);
        }

        /// <summary>The scanned surface under a screen point (pixels). Only meaningful when <see cref="HasModel"/>.</summary>
        public bool TryGetPose(Vector2 screenPosition, out Pose pose)
        {
            pose = default;
            return HasModel && arCamera != null
                   && map.Raycast(arCamera.ScreenPointToRay(screenPosition), RaycastDistance, out pose);
        }

        /// <summary>
        /// The user says that the surface under a screen point (pixels) is the floor. For rooms where the
        /// scan cannot find it by itself; it stays the floor until the scan is reset.
        /// </summary>
        public bool SetFloorAt(Vector2 screenPosition)
        {
            if (arCamera == null || !map.Raycast(arCamera.ScreenPointToRay(screenPosition), RaycastDistance,
                    out _, out var levelId) || !map.SetFloor(levelId))
            {
                Notify(RoomScanText.NoScannedSurface);
                return false;
            }

            Rebuild(true);
            Notify(RoomScanText.FloorSet);
            return true;
        }

        void StopScanning()
        {
            if (!IsScanning)
            {
                return;
            }

            UseSource(null);
            IsScanning = false;
            if (planeManager != null)
            {
                planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
            }

            ScanningChanged?.Invoke(false);
        }

        /// <summary>Ends the source in use and begins another one (null: none).</summary>
        void UseSource(IScanFrameSource next)
        {
            if (watchedSource != null)
            {
                watchedSource.MappingChanged -= OnMappingChanged;
                watchedSource = null;
            }

            source?.End();
            source = next;
            if (source == null)
            {
                return;
            }

            map.PriorsNeedConfirmation = source.IsPrecise;
            watchedSource = source as IScanSourceDiagnostics;
            if (watchedSource != null)
            {
                watchedSource.MappingChanged += OnMappingChanged;
            }

            source.Begin();
        }

        void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> changes)
        {
            priorsDirty = true;
        }

        void OnMappingChanged()
        {
            mappingChanged = true;
        }

        /// <summary>
        /// The depth image was read with a mapping that turned out to be the wrong one: what was scanned
        /// with it lies in wrong places.
        /// </summary>
        void StartOverAfterMappingChange()
        {
            mappingChanged = false;
            ClearModel();
            UpdatePriors();
            Rebuild(true);
            Notify(RoomScanText.DepthRecalibrated);
        }

        void ClearModel()
        {
            map.Clear();
            model.Reset();
            view?.Clear();
            HasModel = false;
        }

        void Integrate()
        {
            nextIntegrate = Time.unscaledTime + IntegrateInterval;
            if (source == null || !isTracking() || turnSpeed > MaxTurnDegreesPerSecond || moveSpeed > MaxMetersPerSecond)
            {
                return;
            }

            if (!source.TryReadFrame(frame))
            {
                return;
            }

            CheckDepthStrength();
            if (!TakeFrame())
            {
                return;
            }

            var started = Stopwatch.GetTimestamp();
            map.Integrate(frame);
            integrateMs = Smooth(integrateMs, started);
            AddResiduals();
            UpdatePlaneSuppression();
        }

        /// <summary>
        /// Evidence has to come from different views: a phone that is held still sees the same noise
        /// again and again. A frame from a new position or direction counts fully; a frame that shows the
        /// same view counts half, twice a second, so things that change in the room are still noticed.
        /// </summary>
        bool TakeFrame()
        {
            var now = Time.unscaledTime;
            var isNewView = map.FramesIntegrated < FirstFrames
                            || Vector3.Distance(frame.Origin, keyPosition) >= KeyframeMeters
                            || Quaternion.Angle(frame.Rotation, keyRotation) >= KeyframeDegrees;
            if (!isNewView && now - lastIntegrated < SameViewInterval)
            {
                return false;
            }

            if (isNewView)
            {
                keyPosition = frame.Origin;
                keyRotation = frame.Rotation;
            }
            else
            {
                frame.Weight = SameViewWeight;
            }

            if (frame.Time <= 0.0)
            {
                frame.Time = now;
            }

            lastIntegrated = now;
            return true;
        }

        /// <summary>Reads the ARCore planes as a head start for the scan (only while scanning, only when they change).</summary>
        void UpdatePriors()
        {
            priorsDirty = false;
            nextPriors = Time.unscaledTime + PriorInterval;
            if (planeManager != null)
            {
                ArcorePlanePriors.Apply(planeManager, map, polygon);
            }
        }

        /// <summary>Decides what is shown and redraws the levels that changed (all of them when <paramref name="all"/>).</summary>
        void Rebuild(bool all)
        {
            var started = Stopwatch.GetTimestamp();
            var now = Time.unscaledTime;
            var seconds = lastRebuild > 0f ? Mathf.Min(now - lastRebuild, 2f * RebuildInterval) : RebuildInterval;
            lastRebuild = now;
            nextRebuild = now + RebuildInterval;

            model.Rebuild(map, seconds, all, GetView());
            HasModel = model.SurfaceArea > 0f;
            UpdatePlaneSuppression();
            rebuildMs = Smooth(rebuildMs, started);
            RefreshDiagnostics();
            Progress = new RoomScanProgress(IsScanning, model.SurfaceCount, model.SurfaceArea, map.CheckedArea,
                source == null || source.IsPrecise, map.FloorState != FloorState.Unknown);
            ProgressChanged?.Invoke(Progress);
        }

        void TrackCameraSpeed()
        {
            if (arCamera == null)
            {
                return;
            }

            var cameraTransform = arCamera.transform;
            var seconds = Mathf.Max(Time.unscaledDeltaTime, 1e-3f);
            turnSpeed = Quaternion.Angle(lastRotation, cameraTransform.rotation) / seconds;
            moveSpeed = Vector3.Distance(lastPosition, cameraTransform.position) / seconds;
            lastRotation = cameraTransform.rotation;
            lastPosition = cameraTransform.position;
        }

        /// <summary>
        /// On shiny or plain floors ARCore is sure of few depth pixels. The scan then shows the floor where
        /// ARCore detected its plane; the user is told once what helps.
        /// </summary>
        void CheckDepthStrength()
        {
            if (weakDepthSaid || watchedSource == null || arCamera == null || !source.IsPrecise)
            {
                return;
            }

            var lookingDown = arCamera.transform.forward.y < -Mathf.Sin(LookingDownDegrees * Mathf.Deg2Rad);
            if (!lookingDown || watchedSource.ConfidentRatio >= WeakDepthShare)
            {
                weakDepthSince = -1f;
                return;
            }

            var now = Time.unscaledTime;
            if (weakDepthSince < 0f)
            {
                weakDepthSince = now;
            }
            else if (now - weakDepthSince >= WeakDepthSeconds)
            {
                weakDepthSaid = true;
                Notify(RoomScanText.ShinyFloor);
            }
        }

        IScanFrameSource PickSource()
        {
            if (preciseSource != null && preciseSource.IsAvailable)
            {
                return preciseSource;
            }

            return sparseSource != null && sparseSource.IsAvailable ? sparseSource : null;
        }

        /// <summary>Phones whose depth turns out to be missing (or unusable) continue with tracking points.</summary>
        void SwitchToSparseIfNeeded()
        {
            if (source == null || source != preciseSource || source.IsAvailable || sparseSource == null
                || sparseSource == preciseSource || !sparseSource.IsAvailable)
            {
                return;
            }

            var unusable = watchedSource != null && watchedSource.Mapping == MappingState.Unverifiable;
            UseSource(sparseSource);
            Notify(unusable ? RoomScanText.DepthUnusable : RoomScanText.LessPrecise);
        }

        void CreateSources()
        {
            if (preciseSource != null || sparseSource != null)
            {
                return;
            }

            if (arCamera == null)
            {
                var xrOrigin = GetComponent<XROrigin>();
                arCamera = xrOrigin != null ? xrOrigin.Camera : null;
            }

            if (arCamera == null)
            {
                Debug.LogWarning("[Erasmus AR Toolkit] Room scan needs the AR camera.", this);
                return;
            }

            cameraManager = arCamera.GetComponent<ARCameraManager>();
            preciseSource = new DepthScanSource(arCamera.GetComponent<AROcclusionManager>(),
                cameraManager, GetComponent<ARRaycastManager>(), arCamera);
            sparseSource = new FeaturePointScanSource(GetComponent<XROrigin>(), arCamera);
        }

        /// <summary>Scanned surfaces use the same grid and outline materials as the ARCore planes.</summary>
        void ReadPlaneMaterials()
        {
            var prefab = planeManager != null ? planeManager.planePrefab : null;
            if (prefab == null)
            {
                return;
            }

            var meshRenderer = prefab.GetComponent<MeshRenderer>();
            var lineRenderer = prefab.GetComponent<LineRenderer>();
            fillMaterial = meshRenderer != null ? meshRenderer.sharedMaterial : null;
            outlineMaterial = lineRenderer != null ? lineRenderer.sharedMaterial : null;
        }

        RoomSurfaceView GetView()
        {
            if (view == null)
            {
                view = new RoomSurfaceView(fillMaterial, outlineMaterial,
                    useOccluders ? GetOccluderMaterial() : null,
                    steadySurfaceDepth ? GetSurfaceDepthMaterial() : null);
                view.SetVisible(visible);
            }

            return view;
        }

        /// <summary>
        /// The theme's depth-only materials (Run Setup adds them). Without a material the theme's shader
        /// reference is used, and last the shader is looked up by name, which only works in the Editor:
        /// a build contains a shader only when the theme refers to it.
        /// </summary>
        Material GetOccluderMaterial()
        {
            if (occluderMaterial == null)
            {
                occluderMaterial = ownedOccluderMaterial = CreateFallbackMaterial(
                    theme != null ? theme.OccluderShader : null, OccluderShaderName, RenderOrder.RoomOccluder);
            }

            return occluderMaterial;
        }

        Material GetSurfaceDepthMaterial()
        {
            if (surfaceDepthMaterial == null)
            {
                surfaceDepthMaterial = ownedSurfaceDepthMaterial = CreateFallbackMaterial(
                    theme != null ? theme.SurfaceDepthShader : null, SurfaceDepthShaderName, RenderOrder.SurfaceDepth);
            }

            return surfaceDepthMaterial;
        }

        Material CreateFallbackMaterial(Shader themeShader, string shaderName, int renderQueue)
        {
            var shader = themeShader != null ? themeShader : Shader.Find(shaderName);
            if (shader == null)
            {
                var message = $"[Erasmus AR Toolkit] Shader '{shaderName}' is missing, so scanned surfaces draw " +
                              "without it. Run Tools > Erasmus AR Toolkit > Run Setup and build again.";
                if (Application.isEditor)
                {
                    Debug.LogWarning(message, this);
                }
                else
                {
                    Debug.LogError(message, this);
                }

                return null;
            }

            return new Material(shader) { name = shaderName + " (runtime)", renderQueue = renderQueue };
        }

        /// <summary>
        /// The raw ARCore planes are hidden once the scan draws surfaces of its own. At the start of a scan
        /// with depth they stay until the first depth frame is in: until then the scan has nothing to show.
        /// </summary>
        void UpdatePlaneSuppression()
        {
            if (planeSurfaces == null)
            {
                return;
            }

            var scanDraws = IsScanning && (map.FramesIntegrated > 0 || source == null || !source.IsPrecise);
            planeSurfaces.SetSuppressed(HasModel || scanDraws);
        }

        void Notify(string message)
        {
            Notification?.Invoke(message);
        }

        /// <summary>Moving average of a duration in milliseconds that started at a Stopwatch timestamp.</summary>
        static float Smooth(float average, long startedAt)
        {
            var milliseconds = (Stopwatch.GetTimestamp() - startedAt) * 1000f / Stopwatch.Frequency;
            return average <= 0f ? milliseconds : Mathf.Lerp(average, milliseconds, TimingSmoothing);
        }

        /// <summary>
        /// Collects how far the floor points of the last frame lie from the floor: from the floor plane
        /// ARCore detected when there is one (it shows the bias of depth), else from the scanned floor.
        /// </summary>
        void AddResiduals()
        {
            var reference = FindPlaneFloorHeight();
            if (float.IsNaN(reference) && model.DrawnFloor.HasValue)
            {
                reference = model.DrawnFloor.Value;
            }

            if (!float.IsNaN(reference))
            {
                residuals.Add(frame, reference);
            }
        }

        /// <summary>Height of the lowest tracked floor-sized plane ARCore detected, or NaN.</summary>
        float FindPlaneFloorHeight()
        {
            var lowest = float.NaN;
            if (planeManager == null)
            {
                return lowest;
            }

            foreach (var plane in planeManager.trackables)
            {
                if (plane.subsumedBy != null || plane.alignment != PlaneAlignment.HorizontalUp
                    || plane.trackingState != TrackingState.Tracking
                    || plane.size.x * plane.size.y < MinPlaneFloorArea)
                {
                    continue;
                }

                var height = plane.transform.position.y;
                if (float.IsNaN(lowest) || height < lowest)
                {
                    lowest = height;
                }
            }

            return lowest;
        }

        void RefreshDiagnostics()
        {
            if (preciseSource is IScanSourceDiagnostics depth)
            {
                diagnostics.Mapping = depth.Mapping;
                diagnostics.DepthLagMs = depth.DepthLagMs;
                diagnostics.DepthWidth = depth.DepthWidth;
                diagnostics.DepthHeight = depth.DepthHeight;
                diagnostics.ConfidentRatio = depth.ConfidentRatio;
                diagnostics.CaptureMs = depth.CaptureMs;
            }
            else
            {
                diagnostics.Mapping = MappingState.Unverifiable;
                diagnostics.DepthLagMs = float.NaN;
            }

            var floor = map.Floor;
            diagnostics.IntegrateMs = integrateMs;
            diagnostics.RebuildMs = rebuildMs;
            diagnostics.CameraHeight = arCamera != null ? arCamera.transform.position.y : float.NaN;
            diagnostics.FloorHeight = model.DrawnFloor ?? float.NaN;
            diagnostics.PlaneFloorHeight = FindPlaneFloorHeight();
            diagnostics.Floor = floor.State;
            diagnostics.FloorFromPlane = floor.FromPlane;
            diagnostics.LevelCount = map.Levels.Count;
            diagnostics.DepthSlotsUsed = view != null ? view.DepthSlotsUsed : 0;
            diagnostics.DepthSlots = RenderOrder.SurfaceDepthSlots;
            diagnostics.Reflections = map.Reflections;
            diagnostics.Implausible = map.Implausible;
            diagnostics.FramesIntegrated = map.FramesIntegrated;
            diagnostics.ThermalStatus = -1;

            var background = cameraManager != null
                ? cameraManager.currentRenderingMode
                : XRCameraBackgroundRenderingMode.None;
            if (diagnostics.BackgroundMode == null || background != reportedBackground)
            {
                reportedBackground = background;
                diagnostics.BackgroundMode = background.ToString();
            }
        }

        /// <summary>
        /// Writes what the scan measures to the log (Android Logcat), to check the scan on a phone:
        /// the state, the height and area of every surface, and the depth error on the floor.
        /// </summary>
        void ReportDiagnostics()
        {
            nextDiagnostics = Time.unscaledTime + DiagnosticsInterval;
            RefreshDiagnostics();
            Log("Scan: " + diagnostics.ToLine());

            var culture = CultureInfo.InvariantCulture;
            report.Clear();
            foreach (var level in map.Levels)
            {
                report.Append(report.Length > 0 ? " | " : string.Empty);
                report.Append(level.Height.ToString("0.000", culture)).Append("m ");
                report.Append(level.ShownArea.ToString("0.00", culture)).Append("m2");
                var plane = level.PlaneHeight;
                if (plane.HasValue)
                {
                    report.Append(" plane ").Append(plane.Value.ToString("0.000", culture));
                }

                report.Append(level.IsFloor ? " floor" : string.Empty);
                report.Append(level.IsConfirmed ? string.Empty : " unconfirmed");
                report.Append(level.IsSuppressed ? " hidden(below floor)" : string.Empty);
                report.Append(level.IsGhost ? " hidden(reflection)" : string.Empty);
            }

            Log("Scan surfaces: " + (report.Length > 0 ? report.ToString() : "none"));
            Log("Scan floor depth error (mean±spread, points): " + residuals.ToLine());
            residuals.Clear();
        }

        void Log(string message)
        {
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "[Erasmus AR Toolkit] {0}", message);
        }
    }
}
