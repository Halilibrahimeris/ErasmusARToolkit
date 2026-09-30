using System;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Turns touch/mouse input into toolkit actions: place, select, move, rotate, scale,
    /// delete and toggle info labels. Works the same in AR and Desktop mode.
    /// </summary>
    [RequireComponent(typeof(ARToolkitManager))]
    public sealed class InteractionController : MonoBehaviour
    {
        static readonly RaycastHit[] PickHits = new RaycastHit[16];

        [SerializeField, Tooltip("A touch that moves less than this (pixels) counts as a tap.")]
        float tapMaxMovePixels = 25f;
        [SerializeField] float tapMaxSeconds = 0.4f;
        [SerializeField, Tooltip("Keyboard rotation speed (Q/E) in degrees per second.")]
        float keyboardRotateSpeed = 120f;
        [SerializeField, Tooltip("Scale change per mouse wheel notch (Shift+scroll).")]
        float scrollScaleStep = 0.1f;
        [SerializeField] float pickDistance = 50f;

        readonly GestureReader reader = new GestureReader();
        readonly TapHandlerStack tapHandlers = new TapHandlerStack();
        ARToolkitManager manager;
        ITapHandler gestureHandler;

        bool gestureActive;
        bool gestureOverUI;
        bool gestureUsedTwoFingers;
        bool dragging;
        Vector2 pressPosition;
        float pressTime;
        ToolkitObject pressedObject;
        bool hadTwoFingersLastFrame;
        Vector2 previousA;
        Vector2 previousB;

        public event Action<ToolkitObject> SelectionChanged;
        public event Action<string> Notification;

        public int SelectedPlaceableIndex { get; private set; }
        public ToolkitObject Selected { get; private set; }

        void Awake()
        {
            manager = GetComponent<ARToolkitManager>();
        }

        void Start()
        {
            PlacementReticle.Create(manager);
        }

        /// <summary>Selects an object and opens its edit panel (null closes it).</summary>
        public void Select(ToolkitObject target)
        {
            SetSelection(target);
        }

        public void ClearSelection()
        {
            SetSelection(null);
        }

        /// <summary>
        /// Gives taps on the scene to <paramref name="handler"/> first, while it wants them (see
        /// <see cref="ITapHandler.WantsTap"/>). The last handler added comes first. A tap it takes does not
        /// select or place anything.
        /// </summary>
        public void PushTapHandler(ITapHandler handler)
        {
            tapHandlers.Push(handler);
        }

        public void RemoveTapHandler(ITapHandler handler)
        {
            tapHandlers.Remove(handler);
        }

        /// <summary>True when the next tap would go to a tap handler instead of selecting or placing.</summary>
        public bool IsTapCaptured => tapHandlers.TopWanting() != null;

        void OnEnable()
        {
            reader.Enable();
        }

        void OnDisable()
        {
            reader.Disable();
        }

        public void SelectPlaceable(int index)
        {
            var placeables = manager.Config.Placeables;
            if (index >= 0 && index < placeables.Count)
            {
                SelectedPlaceableIndex = index;
            }
        }

        /// <summary>Deletes the selected object. Image target content cannot be deleted (it follows its image).</summary>
        public void DeleteSelected()
        {
            if (Selected == null || !Selected.IsPlaced)
            {
                return;
            }

            var target = Selected;
            SetSelection(null);
            manager.Registry.Remove(target);
        }

        void Update()
        {
            if (manager.Mode == ToolkitMode.Undetermined || manager.ActiveCamera == null)
            {
                return;
            }

            var frame = reader.Read();
            HandleKeyboard(frame);

            if (frame.PointerCount >= 2)
            {
                HandleTwoFingers(frame);
                return;
            }

            hadTwoFingersLastFrame = false;
            if (frame.PressedThisFrame)
            {
                BeginGesture(frame.Primary);
            }
            else if (gestureActive && frame.IsHeld)
            {
                ContinueGesture(frame.Primary);
            }

            if (frame.ReleasedThisFrame && gestureActive)
            {
                EndGesture(frame.Primary);
            }
        }

        void BeginGesture(Vector2 position)
        {
            manager.NoteUserActivity();
            gestureActive = true;
            gestureUsedTwoFingers = false;
            dragging = false;
            pressPosition = position;
            pressTime = Time.unscaledTime;
            gestureOverUI = UIPointerGuard.IsOverUI(position);

            // A module that waits for a tap (placing itself, a story going on) gets it; nothing is selected.
            gestureHandler = gestureOverUI ? null : tapHandlers.TopWanting();
            pressedObject = gestureOverUI || gestureHandler != null ? null : PickObject(position);
            if (pressedObject != null)
            {
                SetSelection(pressedObject);
            }
        }

        void ContinueGesture(Vector2 position)
        {
            if (gestureOverUI || gestureUsedTwoFingers || pressedObject == null || !pressedObject.IsManipulatable
                || !pressedObject.Manipulator.CanMove)
            {
                return;
            }

            if (!dragging && (position - pressPosition).sqrMagnitude > tapMaxMovePixels * tapMaxMovePixels)
            {
                dragging = true;
            }

            if (dragging && pressedObject.IsOnWall)
            {
                if (TryGetWall(position, out var wallPose, out _))
                {
                    MoveOnWall(pressedObject, wallPose);
                }
            }
            else if (dragging && TryGetSurface(position, out var pose, out var levelId))
            {
                Move(pressedObject, pose, levelId);
            }
        }

        /// <summary>
        /// The surface under a screen position. After a room scan it is a scanned surface, and
        /// <paramref name="levelId"/> says which one; else it is -1.
        /// </summary>
        bool TryGetSurface(Vector2 position, out Pose pose, out int levelId)
        {
            // (The other overload tells why a spot is refused.)
            return TryGetSurface(position, out pose, out levelId, out _);
        }

        /// <param name="refusal">Why nothing can be placed at that spot, when something is in the way; else null.</param>
        internal bool TryGetSurface(Vector2 position, out Pose pose, out int levelId, out string refusal)
        {
            levelId = -1;
            refusal = null;
            var scanner = manager.Mode == ToolkitMode.AR ? manager.RoomScanner : null;
            if (scanner != null && scanner.HasModel)
            {
                var found = scanner.TryGetHit(position, out var hit);
                var blocked = found && hit.Blocked;
                if (blocked)
                {
                    refusal = hit.BehindWall ? RoomScanText.WallThere : RoomScanText.ObstacleThere;
                }

                pose = hit.Pose;
                levelId = found && !blocked ? hit.LevelId : -1;
                return found && !blocked;
            }

            return manager.ActiveSurface.TryGetPose(position, out pose);
        }

        /// <summary>The wall under a screen position (for things that hang on walls).</summary>
        bool TryGetWall(Vector2 position, out Pose pose, out string refusal)
        {
            refusal = null;
            if (manager.ActiveSurface is IWallPlacementSurface walls && walls.TryGetWallPose(position, out pose))
            {
                return true;
            }

            pose = default;
            refusal = manager.Mode == ToolkitMode.AR ? WallText.NoWallAR : WallText.NoWallDesktop;
            return false;
        }

        /// <summary>A picture slides along the wall and turns to face out of the wall it is dragged onto.</summary>
        static void MoveOnWall(ToolkitObject target, Pose wallPose)
        {
            var roll = Quaternion.Inverse(target.WallRotation) * target.transform.rotation;
            target.Manipulator.MoveTo(wallPose.position);
            target.transform.rotation = wallPose.rotation * roll;
            target.SetOnWall(wallPose.rotation);
        }

        /// <summary>An object on a scanned surface moves with what it hangs from, onto the surface under the pointer.</summary>
        void Move(ToolkitObject target, Pose pose, int levelId)
        {
            var scanner = manager.RoomScanner;
            if (levelId >= 0 && scanner != null && target.SurfaceHolder != null)
            {
                scanner.MoveBound(target, new SurfaceHit(pose, levelId));
                target.StandOnHolder();
                return;
            }

            target.Manipulator.MoveTo(pose.position);
        }

        void EndGesture(Vector2 position)
        {
            gestureActive = false;
            var handler = gestureHandler;
            gestureHandler = null;
            if (gestureOverUI || gestureUsedTwoFingers)
            {
                return;
            }

            var isTap = TapClassifier.IsTap(pressPosition, position, Time.unscaledTime - pressTime,
                tapMaxMovePixels, tapMaxSeconds);
            if (!isTap)
            {
                return;
            }

            if (handler != null)
            {
                handler.OnTap(position);
                return;
            }

            if (pressedObject != null)
            {
                // The object was selected on press; its edit panel is open.
                return;
            }

            if (Selected != null)
            {
                // First tap on empty space just closes the edit panel.
                ClearSelection();
                return;
            }

            TryPlace(position);
        }

        void HandleTwoFingers(GestureFrame frame)
        {
            gestureUsedTwoFingers = true;
            var target = Selected;
            if (target == null || !target.IsManipulatable)
            {
                hadTwoFingersLastFrame = false;
                return;
            }

            if (hadTwoFingersLastFrame)
            {
                var twist = ManipulationMath.TwistDeltaDegrees(previousA, previousB, frame.Primary, frame.Secondary);
                target.Manipulator.RotateYaw(-twist);

                var manipulator = target.Manipulator;
                var scale = ManipulationMath.PinchScale(manipulator.ScaleMultiplier,
                    Vector2.Distance(previousA, previousB), Vector2.Distance(frame.Primary, frame.Secondary),
                    manager.Config.ScaleLimits);
                manipulator.SetScaleMultiplier(scale);
            }

            previousA = frame.Primary;
            previousB = frame.Secondary;
            hadTwoFingersLastFrame = true;
        }

        void HandleKeyboard(GestureFrame frame)
        {
            if (frame.DeletePressed)
            {
                DeleteSelected();
                return;
            }

            var target = Selected;
            if (target == null || !target.IsManipulatable)
            {
                return;
            }

            if (Mathf.Abs(frame.RotateAxis) > 0f)
            {
                target.Manipulator.RotateYaw(frame.RotateAxis * keyboardRotateSpeed * Time.deltaTime);
            }

            if (frame.ModifierHeld && Mathf.Abs(frame.ScrollSteps) > 0f)
            {
                var manipulator = target.Manipulator;
                manipulator.SetScaleMultiplier(ManipulationMath.StepScale(manipulator.ScaleMultiplier,
                    frame.ScrollSteps, scrollScaleStep, manager.Config.ScaleLimits));
            }
        }

        void TryPlace(Vector2 position)
        {
            var placeables = manager.Config.Placeables;
            if (placeables.Count == 0)
            {
                return;
            }

            var item = placeables[Mathf.Clamp(SelectedPlaceableIndex, 0, placeables.Count - 1)];
            if (item == null || item.Prefab == null)
            {
                Notify("This object has no prefab. Check the Content tab.");
                return;
            }

            if (!manager.Registry.CanPlace)
            {
                Notify($"Limit reached ({manager.Registry.MaxObjects} objects). Delete or clear objects first.");
                return;
            }

            if (item.PlaceOn == PlaceOn.Wall)
            {
                TryPlaceOnWall(item, position);
                return;
            }

            if (!TryGetSurface(position, out var pose, out var levelId, out string refusal))
            {
                Notify(refusal ?? NoSurfaceMessage());
                return;
            }

            var facing = ManipulationMath.YawTowards(pose.position, manager.ActiveCamera.transform.position);
            var parent = manager.CreatePlacementParent(pose, levelId, out var anchorHolder);
            var placed = ToolkitObjectFactory.SpawnPlaceable(item, new Pose(pose.position, facing),
                parent, manager.Config.ScaleLimits, manager.Theme);
            if (anchorHolder != null)
            {
                placed.SetAnchorHolder(anchorHolder);
            }

            manager.Registry.Add(placed);
        }

        void TryPlaceOnWall(PlaceableItem item, Vector2 position)
        {
            if (!TryGetWall(position, out var pose, out var refusal))
            {
                Notify(refusal);
                return;
            }

            var parent = manager.CreateWallPlacementParent(pose, out var anchorHolder);
            var placed = ToolkitObjectFactory.SpawnPlaceable(item, pose, parent, manager.Config.ScaleLimits, manager.Theme);
            placed.SetOnWall(pose.rotation);
            if (anchorHolder != null)
            {
                placed.SetAnchorHolder(anchorHolder);
            }

            manager.Registry.Add(placed);
        }

        /// <summary>The placeable object picked in the object bar (null when there is none).</summary>
        public PlaceableItem SelectedPlaceable
        {
            get
            {
                var placeables = manager != null && manager.Config != null ? manager.Config.Placeables : null;
                return placeables != null && placeables.Count > 0
                    ? placeables[Mathf.Clamp(SelectedPlaceableIndex, 0, placeables.Count - 1)]
                    : null;
            }
        }

        string NoSurfaceMessage()
        {
            if (manager.Mode != ToolkitMode.AR)
            {
                return "Click on the grid.";
            }

            var scanner = manager.RoomScanner;
            return scanner != null && scanner.HasModel ? RoomScanText.NoScannedSurface : "No surface there yet. Keep scanning.";
        }

        /// <summary>The nearest placed object under the pointer. Other colliders (AR planes) are skipped.</summary>
        ToolkitObject PickObject(Vector2 position)
        {
            var ray = manager.ActiveCamera.ScreenPointToRay(position);
            var count = Physics.RaycastNonAlloc(ray, PickHits, pickDistance);
            ToolkitObject nearest = null;
            var nearestDistance = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var candidate = PickHits[i].collider.GetComponentInParent<ToolkitObject>();
                if (candidate != null && PickHits[i].distance < nearestDistance)
                {
                    nearest = candidate;
                    nearestDistance = PickHits[i].distance;
                }
            }

            return nearest;
        }

        void SetSelection(ToolkitObject target)
        {
            if (Selected == target)
            {
                return;
            }

            if (Selected != null)
            {
                Selected.SetSelected(false);
            }

            Selected = target;
            if (Selected != null)
            {
                Selected.SetSelected(true);
            }

            SelectionChanged?.Invoke(Selected);
        }

        void Notify(string message)
        {
            Notification?.Invoke(message);
        }
    }
}
