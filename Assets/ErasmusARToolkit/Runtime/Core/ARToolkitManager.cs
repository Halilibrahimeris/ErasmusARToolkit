using System;
using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

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

        /// <summary>The screen stays on this long (seconds) after the last touch in AR mode.</summary>
        const float KeepAwakeSeconds = 180f;

        /// <summary>After tracking was lost for this long (seconds), the scanned room is checked again.</summary>
        const float TrackingLossToVerify = 2f;

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
        ARToolkitHUD hud;
        InteractionController interaction;
        ContactDepthView contactDepth;
        bool contactDepthDirty;
        float nextStatusRefresh;
        float lastUserActivity;
        bool keepingAwake;
        bool themeChecked;
        bool adoptObjects;
        float trackingLostAt = -1f;

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

        /// <summary>The XR Origin object of the AR rig (null if the scene has no AR rig).</summary>
        internal GameObject OriginObject => arOriginObject;

        internal Camera ARCamera => arCamera;

        /// <summary>The PC webcam / phone camera shown in Desktop mode (null if the scene has none).</summary>
        public WebcamBackground CameraFeed => cameraFeed;

        /// <summary>Room scan mode of the AR rig (null if the scene has no AR rig).</summary>
        public RoomScanner RoomScanner => roomScanner;

        /// <summary>The on-screen controls (null in scenes without a HUD). Project modules add their buttons here.</summary>
        public ARToolkitHUD Hud
        {
            get
            {
                if (hud == null)
                {
                    hud = GetComponentInChildren<ARToolkitHUD>(true);
                }

                return hud;
            }
        }

        /// <summary>Turns touches and clicks into toolkit actions (null in scenes without one).</summary>
        public InteractionController Interaction
        {
            get
            {
                if (interaction == null)
                {
                    interaction = GetComponent<InteractionController>();
                }

                return interaction;
            }
        }

        /// <summary>Hides the camera picture behind a sky color and shows it again (virtual views, portals).</summary>
        public CameraImageSwitch CameraImage { get; private set; }

        internal ARPlaneManager PlaneManager => planeManager;

        internal VirtualGround DesktopGround => desktopGround;

        internal Camera DesktopCamera => desktopCamera;

        internal AROcclusionManager OcclusionManager => occlusionManager;

        /// <summary>Called by the HUD when it wakes up.</summary>
        internal void RegisterHud(ARToolkitHUD value)
        {
            hud = value;
        }

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
                    : CountHorizontalPlanes();
            }
        }

        /// <summary>Planes to place things on: walls do not count.</summary>
        int CountHorizontalPlanes()
        {
            var count = 0;
            foreach (var plane in planeManager.trackables)
            {
                if (plane.alignment.IsHorizontal())
                {
                    count++;
                }
            }

            return count;
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
            Registry.Changed += OnPlacedObjectsChanged;
            PlanesVisible = config.ShowPlanesOnStart;
            FindDesktopParts();
            EnsureOriginSettings();
            EnsureAnchorManager();
            EnsurePlaneSurfaces();

            // Scenes made by older toolkit versions get the occlusion manager here, while the AR rig is still off.
            occlusionManager = RealWorldOcclusion.Configure(arCamera != null ? arCamera.gameObject : null,
                config.RealWorldOcclusion);
            EnsureRoomScanner();
            CameraImage = new CameraImageSwitch(this);

            // Both rigs start off; Start() turns on the one that matches the platform.
            SetActive(arSessionObject, false);
            SetActive(arOriginObject, false);
            SetActive(desktopRigObject, false);
        }

        void OnEnable()
        {
            ARSession.stateChanged += OnSessionStateChanged;
            if (cameraFeed != null)
            {
                cameraFeed.StateChanged += OnCameraFeedChanged;
            }
        }

        void OnDisable()
        {
            ARSession.stateChanged -= OnSessionStateChanged;
            SetKeepAwake(false);
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

        /// <summary>
        /// Phone AR has no floor-level tracking origin, so the XR Origin's camera height offset would only
        /// shift every world height by a constant (1.1176 m in scenes made by older toolkit versions). The
        /// camera image has to be drawn before the scene, because the toolkit's surface marks and depth
        /// corrections are depth-tested against the real-world depth it writes.
        /// </summary>
        void EnsureOriginSettings()
        {
            var origin = arOriginObject != null ? arOriginObject.GetComponent<XROrigin>() : null;
            if (origin != null && origin.CameraYOffset != 0f)
            {
                origin.CameraYOffset = 0f;
            }

            var cameraManager = arCamera != null ? arCamera.GetComponent<ARCameraManager>() : null;
            if (cameraManager != null
                && cameraManager.requestedBackgroundRenderingMode != CameraBackgroundRenderingMode.BeforeOpaques)
            {
                cameraManager.requestedBackgroundRenderingMode = CameraBackgroundRenderingMode.BeforeOpaques;
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
            planeSurfaces.SetGridCellSize(config.GridCellSize);

            // Vertical planes are the walls of the room scan, and walls for pictures (wall gallery).
            planeManager.requestedDetectionMode = config.DetectWalls || config.HasWallPlaceables
                ? PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical
                : PlaneDetectionMode.Horizontal;
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

            roomScanner.Initialize(arCamera, theme);
            roomScanner.GridCellSize = config.GridCellSize;
            roomScanner.DetectWalls = config.DetectWalls;
            roomScanner.SetVisible(PlanesVisible);
            roomScanner.ScanningChanged += OnScanningChanged;
        }

        void OnDestroy()
        {
            if (roomScanner != null)
            {
                roomScanner.ScanningChanged -= OnScanningChanged;
            }

            if (Registry != null)
            {
                Registry.Changed -= OnPlacedObjectsChanged;
            }

            contactDepth?.Dispose();
            contactDepth = null;
        }

        void OnPlacedObjectsChanged()
        {
            contactDepthDirty = true;
        }

        /// <summary>The depth patches under placed objects (AR with occlusion only), or null.</summary>
        internal ContactDepthView ContactDepth => contactDepth;

        /// <summary>
        /// Under objects that stand where the room scan steadies no depth (before a scan, beside it), the
        /// depth of the real floor is steadied by patches of their own (see <see cref="ContactDepthView"/>).
        /// </summary>
        void RefreshContactDepth()
        {
            contactDepthDirty = false;
            var wanted = Mode == ToolkitMode.AR && occlusionManager != null && occlusionManager.enabled
                         && theme != null && theme.SurfaceDepthMaterial != null;
            if (!wanted)
            {
                contactDepth?.Clear();
                return;
            }

            if (contactDepth == null)
            {
                contactDepth = new ContactDepthView(theme.SurfaceDepthMaterial);
            }

            contactDepth.Refresh(Registry.Objects, IsCoveredByScan);
        }

        bool IsCoveredByScan(ToolkitObject placed)
        {
            // Pictures on walls stand on no floor: they get no depth patch under them.
            return placed.IsOnWall || (roomScanner != null && roomScanner.CoversWithDepth(placed));
        }

        void OnScanningChanged(bool isScanning)
        {
            // Once the scan has drawn its last surfaces, the objects on them hang from the scan.
            adoptObjects = !isScanning;
        }

        /// <summary>
        /// Objects that were placed on ARCore planes before the room was scanned stand on the scanned
        /// surfaces from now on, so they stay on the grid that is drawn under them.
        /// </summary>
        void AdoptPlacedObjects()
        {
            adoptObjects = false;
            if (roomScanner == null || !roomScanner.HasModel || Registry == null)
            {
                return;
            }

            foreach (var placed in Registry.Objects)
            {
                roomScanner.Adopt(placed);
            }
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
            if (adoptObjects)
            {
                AdoptPlacedObjects();
            }

            if (contactDepthDirty)
            {
                RefreshContactDepth();
            }

            if (Time.unscaledTime >= nextStatusRefresh)
            {
                nextStatusRefresh = Time.unscaledTime + StatusRefreshSeconds;
                RefreshStatus();
                RefreshContactDepth();
                SetKeepAwake(Mode == ToolkitMode.AR
                             && (IsScanning || Time.unscaledTime - lastUserActivity < KeepAwakeSeconds));
            }
        }

        /// <summary>The phone is kept from dimming and locking right now.</summary>
        internal bool KeepsScreenOn => keepingAwake;

        /// <summary>Called on every touch: the screen stays on while the app is in use.</summary>
        internal void NoteUserActivity()
        {
            lastUserActivity = Time.unscaledTime;
        }

        /// <summary>
        /// Keeps the phone from dimming and locking during a room scan or while objects are placed (the AR
        /// session would pause). An app left alone goes back to the phone's own screen timeout.
        /// </summary>
        void SetKeepAwake(bool value)
        {
            if (keepingAwake == value)
            {
                return;
            }

            keepingAwake = value;
            Screen.sleepTimeout = value ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
        }

        void OnSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            if (Mode != ToolkitMode.AR)
            {
                return;
            }

            var reason = args.state == ARSessionState.SessionTracking ? string.Empty : ", reason: " + ARSession.notTrackingReason;
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this,
                "[Erasmus AR Toolkit] AR session: {0}{1}", args.state, reason);
            NoteTracking(args.state == ARSessionState.SessionTracking, Time.unscaledTime);
        }

        /// <summary>
        /// When ARCore finds its way again after a while, it may be somewhere else than before: the scanned
        /// room is checked against what the camera sees.
        /// </summary>
        internal void NoteTracking(bool isTracking, float now)
        {
            if (!isTracking)
            {
                if (trackingLostAt < 0f)
                {
                    trackingLostAt = now;
                }

                return;
            }

            var wasLostFor = trackingLostAt >= 0f ? now - trackingLostAt : 0f;
            trackingLostAt = -1f;
            if (wasLostFor >= TrackingLossToVerify && roomScanner != null)
            {
                roomScanner.Reverify();
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

            if (desktopSurface != null && desktopSurface.Wall != null)
            {
                desktopSurface.Wall.SetVisible(visible);
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
        /// the object fixed to the real world when the camera looks away and back; after a room scan it
        /// is a point of the scanned surface, which keeps the object on the grid drawn under it.
        /// Otherwise the placed-objects root. <paramref name="anchorHolder"/> is the anchor object (or null).
        /// </summary>
        public Transform CreatePlacementParent(Pose pose, out GameObject anchorHolder)
        {
            return CreatePlacementParent(pose, -1, out anchorHolder);
        }

        /// <param name="levelId">The scanned surface the pose lies on, or -1 when that is not known.</param>
        internal Transform CreatePlacementParent(Pose pose, int levelId, out GameObject anchorHolder)
        {
            anchorHolder = null;
            if (Mode != ToolkitMode.AR || arOriginObject == null)
            {
                return placedObjectsRoot;
            }

            if (roomScanner != null && roomScanner.HasModel)
            {
                anchorHolder = roomScanner.CreateContentHolder(pose, levelId).gameObject;
                return anchorHolder.transform;
            }

            anchorHolder = CreateAnchor(pose);
            return anchorHolder.transform;
        }

        /// <summary>
        /// Parent for an object that hangs on a wall: an AR anchor at the pose in AR mode (never a point of
        /// the scanned floor), else the placed-objects root.
        /// </summary>
        public Transform CreateWallPlacementParent(Pose pose, out GameObject anchorHolder)
        {
            anchorHolder = null;
            if (Mode != ToolkitMode.AR || arOriginObject == null)
            {
                return placedObjectsRoot;
            }

            anchorHolder = CreateAnchor(pose);
            return anchorHolder.transform;
        }

        GameObject CreateAnchor(Pose pose)
        {
            var origin = arOriginObject.GetComponent<XROrigin>();
            var trackables = origin != null && origin.TrackablesParent != null
                ? origin.TrackablesParent
                : arOriginObject.transform;

            var anchor = new GameObject("Anchor");
            anchor.transform.SetPositionAndRotation(pose.position, pose.rotation);
            anchor.transform.SetParent(trackables, true);
            anchor.AddComponent<ARAnchor>();
            return anchor;
        }

        void ApplyMode(ToolkitMode mode)
        {
            Mode = mode;
            var isAR = mode == ToolkitMode.AR;
            SetActive(arSessionObject, isAR);
            SetActive(arOriginObject, isAR);
            SetActive(desktopRigObject, !isAR);
            if (isAR)
            {
                lastUserActivity = Time.unscaledTime;
                CheckRoomScanAssets();
            }

            if (isAR && occlusionManager != null && occlusionManager.enabled)
            {
                StartCoroutine(RealWorldOcclusion.TurnOffIfUnsupported(occlusionManager));
            }

            if (desktopGround != null)
            {
                desktopGround.SetVisible(PlanesVisible);
            }

            if (!isAR && config.HasWallPlaceables && desktopSurface != null && desktopSurface.Wall == null)
            {
                desktopSurface.SetWall(VirtualWall.Create(desktopRigObject != null ? desktopRigObject.transform : transform,
                    theme, desktopGround != null ? desktopGround.Height : 0f));
                desktopSurface.Wall.SetVisible(PlanesVisible);
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

        /// <summary>
        /// A theme without the room scan materials means a build without their shaders: no table occluders
        /// and no steady surface depth. Said once, where it matters (AR mode; an error on the phone).
        /// </summary>
        void CheckRoomScanAssets()
        {
            if (themeChecked || (theme != null && theme.HasRoomScanAssets))
            {
                return;
            }

            themeChecked = true;
            const string message = "[Erasmus AR Toolkit] The theme has no room scan materials. " +
                                   "Run Tools > Erasmus AR Toolkit > Run Setup and build again.";
            if (Application.isEditor)
            {
                Debug.LogWarning(message, this);
            }
            else
            {
                Debug.LogError(message, this);
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

        internal void SetTheme(ToolkitTheme toolkitTheme)
        {
            theme = toolkitTheme;
        }
    }
}
