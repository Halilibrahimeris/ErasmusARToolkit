using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Room scan mode, like the space setup of a VR headset. While scanning, depth frames (or tracking
    /// points on phones without depth) are fused into a <see cref="RoomSurfaceMap"/>: surfaces are confirmed
    /// by repeated observations, and parts of ARCore planes that the camera can see through (plane hulls
    /// reaching past a table edge, duplicate floors, small fragments) disappear. The scanned surfaces replace
    /// the raw ARCore planes for drawing and placement. After <see cref="FinishScan"/> the model stays as it
    /// is; scanning again adds to it, <see cref="ResetScan"/> starts over. Lives on the XR Origin.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomScanner : MonoBehaviour
    {
        const float IntegrateInterval = 0.125f;
        const float RebuildInterval = 0.5f;
        const float PriorInterval = 0.5f;
        const float MaxTurnDegreesPerSecond = 60f;
        const float RaycastDistance = 20f;
        const float MinFloorArea = 0.5f;
        const float FloorChangeToRedraw = 0.02f;
        const string OccluderShaderName = "Erasmus AR Toolkit/Occluder";
        const string SurfaceDepthShaderName = "Erasmus AR Toolkit/Surface Depth";

        [SerializeField, Tooltip("Scanned tables hide the floor grid and virtual objects behind or under them.")]
        bool useOccluders = true;

        [SerializeField, Tooltip("On scanned surfaces, noisy real-world depth is replaced by the surface's own depth, " +
                                 "so grids and objects standing on the floor or a table are not cut into patches.")]
        bool steadySurfaceDepth = true;

        readonly RoomSurfaceMap map = new RoomSurfaceMap();
        readonly ScanFrame frame = new ScanFrame();
        readonly SurfaceGrid grid = new SurfaceGrid();
        readonly SurfaceRegionFilter filter = new SurfaceRegionFilter();
        readonly SurfaceMeshBuilder builder = new SurfaceMeshBuilder();
        readonly SurfaceMeshData meshData = new SurfaceMeshData();
        readonly List<Vector2> polygon = new List<Vector2>();
        readonly HashSet<int> levelIds = new HashSet<int>();

        ARPlaneManager planeManager;
        ARPlaneSurfaces planeSurfaces;
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
        Func<bool> isTracking = () => ARSession.state == ARSessionState.SessionTracking;
        bool visible = true;
        bool priorsDirty;
        float? drawnFloor;
        float nextIntegrate;
        float nextRebuild;
        float nextPriors;
        Quaternion lastRotation = Quaternion.identity;
        float turnSpeed;

        public event Action<bool> ScanningChanged;
        public event Action<RoomScanProgress> ProgressChanged;
        public event Action<string> Notification;

        public bool IsScanning { get; private set; }

        /// <summary>True once the scan has drawn at least one surface. Placement then uses the scan only.</summary>
        public bool HasModel { get; private set; }

        public RoomScanProgress Progress { get; private set; }

        internal RoomSurfaceMap Map => map;

        /// <summary>Called by the toolkit: the AR camera and the depth-only materials (from the theme).</summary>
        internal void Initialize(Camera camera, Material occluder, Material surfaceDepth)
        {
            arCamera = camera;
            occluderMaterial = occluder;
            surfaceDepthMaterial = surfaceDepth;
        }

        /// <summary>Tests replace the AR inputs with their own.</summary>
        internal void ConfigureForTests(Camera camera, IScanFrameSource frameSource, Func<bool> tracking,
            Material fill, Material outline, Material occluder, Material surfaceDepth = null)
        {
            surfaceDepthMaterial = surfaceDepth;
            arCamera = camera;
            preciseSource = frameSource;
            sparseSource = frameSource;
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
            if (!HasModel)
            {
                SuppressArcorePlanes(false);
            }
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

            TrackTurnSpeed();
            SwitchToSparseIfNeeded();
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

            source.Begin();
            IsScanning = true;
            if (arCamera != null)
            {
                lastRotation = arCamera.transform.rotation;
            }

            if (planeManager != null)
            {
                planeManager.trackablesChanged.AddListener(OnPlanesChanged);
            }

            SuppressArcorePlanes(true);
            UpdatePriors();
            Rebuild(true);

            // Keep the plane reading and the redraw half a period apart so they never share a frame.
            var now = Time.unscaledTime;
            nextRebuild = now + RebuildInterval;
            nextPriors = now + PriorInterval * 0.5f;
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
            if (HasModel)
            {
                Notify(RoomScanText.Finished(Progress));
            }
            else
            {
                SuppressArcorePlanes(false);
                Notify(RoomScanText.NoSurfaces);
            }
        }

        /// <summary>Forgets everything scanned. A running scan keeps going from scratch.</summary>
        public void ResetScan()
        {
            map.Clear();
            view?.Clear();
            HasModel = false;
            drawnFloor = null;
            if (IsScanning)
            {
                UpdatePriors();
                Rebuild(true);
            }
            else
            {
                SuppressArcorePlanes(false);
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

        void StopScanning()
        {
            if (!IsScanning)
            {
                return;
            }

            source?.End();
            source = null;
            IsScanning = false;
            if (planeManager != null)
            {
                planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
            }

            ScanningChanged?.Invoke(false);
        }

        void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> changes)
        {
            priorsDirty = true;
        }

        void Integrate()
        {
            nextIntegrate = Time.unscaledTime + IntegrateInterval;
            if (source == null || !isTracking() || turnSpeed > MaxTurnDegreesPerSecond)
            {
                return;
            }

            if (source.TryReadFrame(frame))
            {
                map.Integrate(frame);
            }
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

        /// <summary>
        /// Filters and redraws the levels that changed (all of them when <paramref name="all"/>). Tables get
        /// occluders down to the floor found by the previous redraw; when the floor moves, all are redrawn.
        /// </summary>
        void Rebuild(bool all)
        {
            nextRebuild = Time.unscaledTime + RebuildInterval;
            var floor = map.FloorHeight(MinFloorArea);
            if (floor.HasValue != drawnFloor.HasValue
                || (floor.HasValue && Mathf.Abs(floor.Value - drawnFloor.Value) > FloorChangeToRedraw))
            {
                all = true;
                drawnFloor = floor;
            }

            var surfaceView = GetView();
            var occluderBottom = useOccluders && surfaceView.HasOccluderMaterial ? drawnFloor : null;
            var surfaceCount = 0;
            var surfaceArea = 0f;
            levelIds.Clear();
            foreach (var level in map.Levels)
            {
                levelIds.Add(level.Id);
                if (all || level.IsDirty)
                {
                    level.MarkClean();
                    DrawLevel(level, occluderBottom, surfaceView);
                }

                if (level.ShownArea > 0f)
                {
                    surfaceCount++;
                    surfaceArea += level.ShownArea;
                }
            }

            surfaceView.Keep(levelIds);
            HasModel = surfaceArea > 0f;
            Progress = new RoomScanProgress(IsScanning, surfaceCount, surfaceArea, map.CheckedArea,
                source == null || source.IsPrecise);
            ProgressChanged?.Invoke(Progress);
        }

        void DrawLevel(SurfaceLevel level, float? occluderBottom, RoomSurfaceView surfaceView)
        {
            if (level.CopyTo(grid))
            {
                filter.Apply(grid);
                level.StoreShown(grid);
                builder.Build(grid, level.Height, occluderBottom, meshData);
            }
            else
            {
                level.HideAll();
                meshData.Clear();
            }

            surfaceView.Show(level.Id, meshData);
        }

        void TrackTurnSpeed()
        {
            if (arCamera == null)
            {
                return;
            }

            var rotation = arCamera.transform.rotation;
            turnSpeed = Quaternion.Angle(lastRotation, rotation) / Mathf.Max(Time.unscaledDeltaTime, 1e-3f);
            lastRotation = rotation;
        }

        IScanFrameSource PickSource()
        {
            if (preciseSource != null && preciseSource.IsAvailable)
            {
                return preciseSource;
            }

            return sparseSource != null && sparseSource.IsAvailable ? sparseSource : null;
        }

        /// <summary>Phones whose depth turns out to be missing (or unverifiable) continue with tracking points.</summary>
        void SwitchToSparseIfNeeded()
        {
            if (source == null || source != preciseSource || source.IsAvailable || sparseSource == null
                || sparseSource == preciseSource || !sparseSource.IsAvailable)
            {
                return;
            }

            source.End();
            source = sparseSource;
            source.Begin();
            Notify(RoomScanText.LessPrecise);
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

            preciseSource = new DepthScanSource(arCamera.GetComponent<AROcclusionManager>(),
                arCamera.GetComponent<ARCameraManager>(), GetComponent<ARRaycastManager>(), arCamera);
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
        /// The theme's depth-only materials (Run Setup adds them). Without them the shaders are looked up by
        /// name, which works in the Editor; a build needs the theme materials to include the shaders.
        /// </summary>
        Material GetOccluderMaterial()
        {
            if (occluderMaterial == null)
            {
                occluderMaterial = ownedOccluderMaterial = CreateFallbackMaterial(OccluderShaderName, RenderOrder.RoomOccluder);
            }

            return occluderMaterial;
        }

        Material GetSurfaceDepthMaterial()
        {
            if (surfaceDepthMaterial == null)
            {
                surfaceDepthMaterial = ownedSurfaceDepthMaterial = CreateFallbackMaterial(SurfaceDepthShaderName, RenderOrder.SurfaceDepth);
            }

            return surfaceDepthMaterial;
        }

        Material CreateFallbackMaterial(string shaderName, int renderQueue)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Erasmus AR Toolkit] Shader '{shaderName}' is missing, so scanned surfaces draw " +
                                 "without it. Run Tools > Erasmus AR Toolkit > Run Setup.", this);
                return null;
            }

            return new Material(shader) { name = shaderName + " (runtime)", renderQueue = renderQueue };
        }

        void SuppressArcorePlanes(bool suppressed)
        {
            if (planeSurfaces != null)
            {
                planeSurfaces.SetSuppressed(suppressed);
            }
        }

        void Notify(string message)
        {
            Notification?.Invoke(message);
        }
    }
}
