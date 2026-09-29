using System;
using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Entry point of the toolkit scene. Chooses AR or Desktop mode, switches the matching
    /// rig on, and exposes the shared state (camera, placement surface, placed objects).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class ARToolkitManager : MonoBehaviour
    {
        const float StatusRefreshSeconds = 0.25f;

        [Header("Setup")]
        [SerializeField] ARToolkitConfig config;
        [SerializeField] ToolkitTheme theme;
        [SerializeField, Tooltip("Parent for placed objects. Optional.")]
        Transform placedObjectsRoot;

        [Header("AR Rig")]
        [SerializeField] GameObject arSessionObject;
        [SerializeField] GameObject arOriginObject;
        [SerializeField] Camera arCamera;
        [SerializeField] ARPlacementSurface arSurface;
        [SerializeField] ARPlaneManager planeManager;

        [Header("Desktop Rig")]
        [SerializeField] GameObject desktopRigObject;
        [SerializeField] Camera desktopCamera;
        [SerializeField] DesktopPlacementSurface desktopSurface;
        [SerializeField, Tooltip("Found automatically under the Desktop rig when empty.")]
        VirtualGround desktopGround;
        [SerializeField, Tooltip("Found automatically under the Desktop rig when empty.")]
        WebcamBackground cameraFeed;

        ARPlaneSurfaces planeSurfaces;
        RoomScanner roomScanner;
        AROcclusionManager occlusionManager;
        float nextStatusRefresh;

        public event Action<ToolkitMode> ModeChanged;
        public event Action<string> StatusChanged;

        public ToolkitMode Mode { get; private set; } = ToolkitMode.Undetermined;
        public bool IsFallback { get; private set; }
        public bool PlanesVisible { get; private set; }
        public string Status { get; private set; } = string.Empty;
        public PlacedObjectRegistry Registry { get; private set; }
        public ARToolkitConfig Config => config;
        public ToolkitTheme Theme => theme;
        public Transform PlacedObjectsRoot => placedObjectsRoot;

        /// <summary>The PC webcam / phone camera shown in Desktop mode (null if the scene has none).</summary>
        public WebcamBackground CameraFeed => cameraFeed;

        /// <summary>Room scan mode of the AR rig (null if the scene has no AR rig).</summary>
        public RoomScanner RoomScanner => roomScanner;

        bool IsCameraFeedActive => Mode == ToolkitMode.Desktop && cameraFeed != null && cameraFeed.IsRunning;

        public Camera ActiveCamera
        {
            get
            {
                switch (Mode)
                {
                    case ToolkitMode.AR: return arCamera;
                    case ToolkitMode.Desktop: return desktopCamera;
                    default: return null;
                }
            }
        }

        public IPlacementSurface ActiveSurface
        {
            get
            {
                switch (Mode)
                {
                    case ToolkitMode.AR: return arSurface;
                    case ToolkitMode.Desktop: return desktopSurface;
                    default: return null;
                }
            }
        }

        int PlaneCount
        {
            get
            {
                if (Mode != ToolkitMode.AR || planeManager == null)
                {
                    return 0;
                }

                return roomScanner != null && roomScanner.HasModel
                    ? roomScanner.Progress.SurfaceCount
                    : planeManager.trackables.count;
            }
        }

        bool IsScanning => Mode == ToolkitMode.AR && roomScanner != null && roomScanner.IsScanning;

        void Awake()
        {
            if (config == null)
            {
                Debug.LogError("[Erasmus AR Toolkit] No config assigned to ARToolkitManager. Using an empty config.", this);
                config = ScriptableObject.CreateInstance<ARToolkitConfig>();
            }

            LogConfigIssues();
            Registry = new PlacedObjectRegistry(config.MaxPlacedObjects);
            PlanesVisible = config.ShowPlanesOnStart;
            FindDesktopParts();
            EnsureAnchorManager();
            EnsurePlaneSurfaces();

            // Scenes made by older toolkit versions get the occlusion manager here, while the AR rig is still off.
            occlusionManager = RealWorldOcclusion.Configure(arCamera != null ? arCamera.gameObject : null,
                config.RealWorldOcclusion);
            EnsureRoomScanner();

            // Both rigs start off; Start() turns on the one that matches the platform.
            SetActive(arSessionObject, false);
            SetActive(arOriginObject, false);
            SetActive(desktopRigObject, false);
        }

        void OnEnable()
        {
            if (cameraFeed != null)
            {
                cameraFeed.StateChanged += OnCameraFeedChanged;
            }
        }

        void OnDisable()
        {
            if (cameraFeed != null)
            {
                cameraFeed.StateChanged -= OnCameraFeedChanged;
            }
        }

        void FindDesktopParts()
        {
            if (desktopRigObject == null)
            {
                return;
            }

            if (desktopGround == null)
            {
                desktopGround = desktopRigObject.GetComponentInChildren<VirtualGround>(true);
            }

            if (cameraFeed == null)
            {
                cameraFeed = desktopRigObject.GetComponentInChildren<WebcamBackground>(true);
            }
        }

        /// <summary>Anchors need an ARAnchorManager; scenes made by older toolkit versions may not have one.</summary>
        void EnsureAnchorManager()
        {
            if (arOriginObject != null && arOriginObject.GetComponent<ARAnchorManager>() == null
                && arOriginObject.GetComponent<XROrigin>() != null)
            {
                arOriginObject.AddComponent<ARAnchorManager>();
            }
        }

        /// <summary>
        /// Overlapping planes are merged into surfaces by <see cref="ARPlaneSurfaces"/>, which also shows
        /// and hides them. Scenes made by older toolkit versions may not have one.
        /// </summary>
        void EnsurePlaneSurfaces()
        {
            if (planeManager == null)
            {
                return;
            }

            planeSurfaces = planeManager.GetComponent<ARPlaneSurfaces>();
            if (planeSurfaces == null)
            {
                planeSurfaces = planeManager.gameObject.AddComponent<ARPlaneSurfaces>();
            }

            planeSurfaces.SetVisible(PlanesVisible);
        }

        /// <summary>Room scan mode (<see cref="RoomScanner"/>). Scenes made by older toolkit versions may not have one.</summary>
        void EnsureRoomScanner()
        {
            if (planeManager == null)
            {
                return;
            }

            roomScanner = planeManager.GetComponent<RoomScanner>();
            if (roomScanner == null)
            {
                roomScanner = planeManager.gameObject.AddComponent<RoomScanner>();
            }

            roomScanner.Initialize(arCamera, theme != null ? theme.OccluderMaterial : null,
                theme != null ? theme.SurfaceDepthMaterial : null);
            roomScanner.SetVisible(PlanesVisible);
        }

        void OnCameraFeedChanged()
        {
            if (desktopGround != null)
            {
                desktopGround.SetOverCamera(cameraFeed.IsRunning);
            }

            RefreshStatus();
        }

        IEnumerator Start()
        {
            RefreshStatus();
            var isMobile = Application.isMobilePlatform;
            var modeOverride = config.ModeOverride;

            if (PlatformModeResolver.NeedsAvailabilityCheck(modeOverride, isMobile))
            {
                yield return ARSession.CheckAvailability();
            }

            var mode = PlatformModeResolver.Resolve(modeOverride, isMobile, ARSession.state);
            if (mode == ToolkitMode.Undetermined)
            {
                mode = ToolkitMode.Desktop;
            }

            if (mode == ToolkitMode.AR && !isMobile)
            {
                yield return ARSession.CheckAvailability();
                if (ARSession.state == ARSessionState.Unsupported)
                {
                    Debug.LogWarning("[Erasmus AR Toolkit] Mode is forced to AR but no AR provider is active. " +
                                     "Enable XR Simulation under Project Settings > XR Plug-in Management (PC tab) " +
                                     "or set the mode back to Auto.");
                }
            }

            IsFallback = PlatformModeResolver.IsFallback(modeOverride, isMobile, mode);
            ApplyMode(mode);
        }

        void Update()
        {
            if (Time.unscaledTime >= nextStatusRefresh)
            {
                nextStatusRefresh = Time.unscaledTime + StatusRefreshSeconds;
                RefreshStatus();
            }
        }

        /// <summary>Shows or hides the AR planes (AR mode) or the virtual grid (Desktop mode).</summary>
        public void SetPlanesVisible(bool visible)
        {
            PlanesVisible = visible;
            if (desktopGround != null)
            {
                desktopGround.SetVisible(visible);
            }

            if (planeSurfaces != null)
            {
                planeSurfaces.SetVisible(visible);
            }

            if (roomScanner != null)
            {
                roomScanner.SetVisible(visible);
            }
        }

        public void ClearPlacedObjects()
        {
            Registry.Clear();
            RefreshStatus();
        }

        /// <summary>
        /// Parent for a newly placed object. In AR mode this is a new AR anchor at the pose, which keeps
        /// the object fixed to the real world when the camera looks away and back. Otherwise the
        /// placed-objects root. <paramref name="anchorHolder"/> is the anchor object (or null).
        /// </summary>
        public Transform CreatePlacementParent(Pose pose, out GameObject anchorHolder)
        {
            anchorHolder = null;
            if (Mode != ToolkitMode.AR || arOriginObject == null)
            {
                return placedObjectsRoot;
            }

            var origin = arOriginObject.GetComponent<XROrigin>();
            var trackables = origin != null && origin.TrackablesParent != null
                ? origin.TrackablesParent
                : arOriginObject.transform;

            anchorHolder = new GameObject("Anchor");
            anchorHolder.transform.SetPositionAndRotation(pose.position, pose.rotation);
            anchorHolder.transform.SetParent(trackables, true);
            anchorHolder.AddComponent<ARAnchor>();
            return anchorHolder.transform;
        }

        void ApplyMode(ToolkitMode mode)
        {
            Mode = mode;
            var isAR = mode == ToolkitMode.AR;
            SetActive(arSessionObject, isAR);
            SetActive(arOriginObject, isAR);
            SetActive(desktopRigObject, !isAR);
            if (isAR && occlusionManager != null && occlusionManager.enabled)
            {
                StartCoroutine(RealWorldOcclusion.TurnOffIfUnsupported(occlusionManager));
            }

            if (desktopGround != null)
            {
                desktopGround.SetVisible(PlanesVisible);
            }

            Debug.Log($"[Erasmus AR Toolkit] Running in {mode} mode{(IsFallback ? " (AR not supported on this device)" : string.Empty)}.");
            ModeChanged?.Invoke(mode);
            RefreshStatus();
        }

        void RefreshStatus()
        {
            var context = new StatusContext(
                Mode,
                IsFallback,
                ARSession.state == ARSessionState.SessionTracking,
                PlaneCount,
                Registry != null ? Registry.Count : 0,
                config != null && config.Placeables.Count > 0,
                IsCameraFeedActive,
                IsScanning);

            var message = StatusMessageResolver.Resolve(context);
            if (message != Status)
            {
                Status = message;
                StatusChanged?.Invoke(message);
            }
        }

        void LogConfigIssues()
        {
            foreach (var issue in ConfigValidator.Validate(config))
            {
                var message = "[Erasmus AR Toolkit] " + issue.Message;
                if (issue.Severity == IssueSeverity.Error)
                {
                    Debug.LogError(message, config);
                }
                else
                {
                    Debug.LogWarning(message, config);
                }
            }
        }

        static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        internal void Initialize(ARToolkitConfig toolkitConfig, ToolkitTheme toolkitTheme, Transform placedRoot,
            GameObject session, GameObject origin, Camera originCamera, ARPlacementSurface originSurface,
            ARPlaneManager planes, GameObject desktopRig, Camera rigCamera, DesktopPlacementSurface rigSurface)
        {
            config = toolkitConfig;
            theme = toolkitTheme;
            placedObjectsRoot = placedRoot;
            arSessionObject = session;
            arOriginObject = origin;
            arCamera = originCamera;
            arSurface = originSurface;
            planeManager = planes;
            desktopRigObject = desktopRig;
            desktopCamera = rigCamera;
            desktopSurface = rigSurface;
        }

        internal void SetDesktopParts(VirtualGround ground, WebcamBackground feed)
        {
            desktopGround = ground;
            cameraFeed = feed;
        }

        internal void SetConfig(ARToolkitConfig toolkitConfig)
        {
            config = toolkitConfig;
        }
    }
}
