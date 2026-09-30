using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;

namespace Erasmus.ARToolkit
{
    /// <summary>How a project module finds its place in the real world.</summary>
    public enum PlacementMode
    {
        /// <summary>
        /// Where the app was started: the start point (or the object itself) comes to where the phone is,
        /// on the floor, looking the way the phone looks. In Desktop mode it stays where it is in the scene.
        /// </summary>
        AtAppStart,

        /// <summary>The user taps the floor; the object appears there with its front (+Z) to the user.</summary>
        TapToPlace,
    }

    /// <summary>
    /// Puts a project module (a design, a portal, a story ...) into the real world and keeps it there: in
    /// AR it follows an AR anchor, so it stays put when the user walks around. A "Move" button on the HUD
    /// puts it somewhere else. The object's children are hidden until it is placed.
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("Erasmus AR Toolkit/Module Placement")]
    public sealed class ModulePlacement : ToolkitModule, ITapHandler
    {
        const float FloorRetrySeconds = 0.5f;

        [SerializeField] PlacementMode placementMode = PlacementMode.AtAppStart;
        [SerializeField, Tooltip("At App Start: this point comes to where the phone is, looking the way the phone " +
                                 "looks. Empty: the object itself.")]
        Transform startPoint;
        [SerializeField, Min(0.3f), Tooltip("How high the phone is held above the floor (meters). Used until the " +
                                            "real floor is found.")]
        float phoneHeight = 1.5f;
        [SerializeField, Tooltip("Tap To Place: the object turns its front (+Z, the blue arrow) to the user.")]
        bool faceUser = true;
        [SerializeField, Tooltip("A HUD button to put the object somewhere else.")]
        bool showMoveButton = true;
        [SerializeField, Tooltip("Hide the object's children until it is placed.")]
        bool hideUntilPlaced = true;
        [SerializeField, Tooltip("Name in messages ('Tap the floor to place ...'). Empty: the object name.")]
        string displayName = string.Empty;

        readonly List<GameObject> hiddenChildren = new List<GameObject>();
        GameObject holder;
        bool followsHolder;
        Pose holderOffset;
        Pose startLocal = new Pose(Vector3.zero, Quaternion.identity);
        Pose placedPose;
        bool waitingForTracking;
        bool awaitingTap;
        float nextFloorCheck;
        float heightOffset;
        Button moveButton;

        /// <summary>Raised after the object was placed (first time, floor found, moved). The children are shown by then.</summary>
        public event Action<Pose> Placed;

        public PlacementMode PlacementMode
        {
            get => placementMode;
            set => placementMode = value;
        }

        public Transform StartPoint
        {
            get => startPoint;
            set => startPoint = value;
        }

        public bool IsPlaced { get; private set; }

        public Pose PlacedPose => placedPose;

        /// <summary>Where the floor height of the placement came from.</summary>
        public FloorSource FloorSource { get; private set; } = FloorSource.Fallback;

        public bool IsAwaitingTap => awaitingTap;

        /// <summary>
        /// Lifts the placed object by this many meters (negative lowers it). A design lifted by 0.3 m looks
        /// as it would from eyes 0.3 m lower (someone in a wheelchair).
        /// </summary>
        public float HeightOffset
        {
            get => heightOffset;
            set
            {
                heightOffset = value;
                if (IsPlaced)
                {
                    ApplyPose(placedPose);
                }
            }
        }

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();

        bool ITapHandler.WantsTap => awaitingTap;

        internal void Configure(PlacementMode mode, Transform start, bool hideChildren)
        {
            placementMode = mode;
            startPoint = start;
            hideUntilPlaced = hideChildren;
        }

        void Awake()
        {
            startLocal = ModulePlacementMath.Relative(transform, startPoint);
            if (hideUntilPlaced && Application.isPlaying)
            {
                foreach (Transform child in transform)
                {
                    if (child.gameObject.activeSelf)
                    {
                        hiddenChildren.Add(child.gameObject);
                        child.gameObject.SetActive(false);
                    }
                }
            }
        }

        protected override void OnModeReady(ToolkitMode mode)
        {
            var movable = mode == ToolkitMode.AR || placementMode == PlacementMode.TapToPlace;
            if (showMoveButton && movable)
            {
                moveButton = AddHudButton(MoveText(), OnMoveClicked);
            }

            if (IsPlaced)
            {
                return;
            }

            if (placementMode == PlacementMode.TapToPlace)
            {
                BeginTapToPlace();
            }
            else if (mode == ToolkitMode.Desktop)
            {
                // The PC shows the design where it is in the scene, on the virtual ground.
                FloorSource = FloorSource.VirtualGround;
                Place(new Pose(transform.position, transform.rotation));
            }
            else
            {
                waitingForTracking = true;
            }
        }

        protected override void OnDisable()
        {
            CancelTapToPlace();
            moveButton = null;
            base.OnDisable();
        }

        void OnDestroy()
        {
            if (holder != null)
            {
                ObjectUtility.SafeDestroy(holder);
            }
        }

        void Update()
        {
            if (Manager == null)
            {
                return;
            }

            if (waitingForTracking && IsTracking())
            {
                waitingForTracking = false;
                PlaceAtCamera();
            }

            if (IsPlaced && FloorSource == FloorSource.Fallback && placementMode == PlacementMode.AtAppStart
                && Time.unscaledTime >= nextFloorCheck)
            {
                nextFloorCheck = Time.unscaledTime + FloorRetrySeconds;
                var floor = FloorFinder.Find(Manager, phoneHeight);
                if (floor.IsMeasured)
                {
                    FloorSource = floor.Source;
                    Place(ModulePlacementMath.WithHeight(placedPose, floor.Height));
                }
            }
        }

        void LateUpdate()
        {
            if (!followsHolder)
            {
                return;
            }

            if (holder == null)
            {
                // AR Foundation removed the anchor (the session was reset): stay where it was.
                followsHolder = false;
                return;
            }

            var pose = ModulePlacementMath.Follow(new Pose(holder.transform.position, holder.transform.rotation),
                holderOffset);
            placedPose = pose;
            ApplyPose(pose);
        }

        void ApplyPose(Pose pose)
        {
            transform.SetPositionAndRotation(pose.position + Vector3.up * heightOffset, pose.rotation);
        }

        /// <summary>Puts the start point where the phone is now (AR), on the floor.</summary>
        public void PlaceAtCamera()
        {
            var camera = ViewCamera;
            if (camera == null)
            {
                return;
            }

            var floor = FloorFinder.Find(Manager, phoneHeight);
            FloorSource = floor.Source;
            nextFloorCheck = Time.unscaledTime + FloorRetrySeconds;
            Place(ModulePlacementMath.AtAppStart(camera.transform.position, camera.transform.rotation, floor.Height,
                startLocal));
        }

        /// <summary>Waits for a tap on the floor, then places the object there.</summary>
        public void BeginTapToPlace()
        {
            awaitingTap = true;
            if (Interaction != null)
            {
                Interaction.PushTapHandler(this);
            }

            var message = PlacementText.TapToPlace(DisplayName);
            SetInfoLine(message);
            Toast(message);
            RefreshMoveButton();
        }

        public void CancelTapToPlace()
        {
            if (!awaitingTap)
            {
                return;
            }

            awaitingTap = false;
            if (Interaction != null)
            {
                Interaction.RemoveTapHandler(this);
            }

            SetInfoLine(null);
            RefreshMoveButton();
        }

        /// <summary>Puts the object at a world pose (on the floor). In AR it hangs from a new anchor.</summary>
        /// <param name="levelId">The scanned surface under the pose, or -1.</param>
        public void Place(Pose worldPose, int levelId = -1)
        {
            var previous = holder;
            holder = null;
            followsHolder = false;
            if (Manager != null && Manager.Mode == ToolkitMode.AR)
            {
                Manager.CreatePlacementParent(worldPose, levelId, out var newHolder);
                if (newHolder != null)
                {
                    holder = newHolder;
                    holder.name = "Anchor - " + DisplayName;
                    holderOffset = ModulePlacementMath.LocalOffset(
                        new Pose(holder.transform.position, holder.transform.rotation), worldPose);
                    followsHolder = true;
                }
            }

            ApplyPose(worldPose);
            if (previous != null && previous != holder)
            {
                ObjectUtility.SafeDestroy(previous);
            }

            placedPose = worldPose;
            IsPlaced = true;
            ShowChildren();
            Placed?.Invoke(worldPose);
        }

        void ITapHandler.OnTap(Vector2 screenPosition)
        {
            if (!TryGetFloor(screenPosition, out var pose, out var levelId, out var refusal))
            {
                Toast(refusal ?? PlacementText.TapTheFloor);
                return;
            }

            var camera = ViewCamera;
            var cameraPosition = camera != null ? camera.transform.position : pose.position + Vector3.back;
            FloorSource = Manager.Mode == ToolkitMode.Desktop
                ? FloorSource.VirtualGround
                : levelId >= 0 ? FloorSource.Scan : FloorSource.Plane;
            CancelTapToPlace();
            Place(ModulePlacementMath.TapToPlace(pose.position, cameraPosition, faceUser), levelId);
        }

        bool TryGetFloor(Vector2 screenPosition, out Pose pose, out int levelId, out string refusal)
        {
            levelId = -1;
            refusal = null;
            if (Interaction != null)
            {
                return Interaction.TryGetSurface(screenPosition, out pose, out levelId, out refusal);
            }

            pose = default;
            var surface = Manager != null ? Manager.ActiveSurface : null;
            return surface != null && surface.TryGetPose(screenPosition, out pose);
        }

        bool IsTracking()
        {
            return Manager.Mode == ToolkitMode.Desktop || ARSession.state == ARSessionState.SessionTracking;
        }

        void OnMoveClicked()
        {
            if (awaitingTap)
            {
                CancelTapToPlace();
            }
            else if (placementMode == PlacementMode.TapToPlace)
            {
                BeginTapToPlace();
            }
            else
            {
                PlaceAtCamera();
                Toast(PlacementText.MovedHere(DisplayName));
            }
        }

        void RefreshMoveButton()
        {
            if (moveButton != null)
            {
                moveButton.text = MoveText();
            }
        }

        string MoveText()
        {
            return awaitingTap ? PlacementText.Cancel : PlacementText.MoveButton(DisplayName, placementMode);
        }

        void ShowChildren()
        {
            foreach (var child in hiddenChildren)
            {
                if (child != null)
                {
                    child.SetActive(true);
                }
            }

            hiddenChildren.Clear();
        }
    }

    /// <summary>Texts of <see cref="ModulePlacement"/>.</summary>
    public static class PlacementText
    {
        public const string Cancel = "Cancel";
        public const string TapTheFloor = "Tap the floor.";

        public static string TapToPlace(string name) => $"Tap the floor to place {name}.";

        public static string MovedHere(string name) => $"{name} moved to where you stand.";

        public static string MoveButton(string name, PlacementMode mode)
        {
            return mode == PlacementMode.AtAppStart ? $"{name}: Move Here" : $"Move {name}";
        }
    }
}
