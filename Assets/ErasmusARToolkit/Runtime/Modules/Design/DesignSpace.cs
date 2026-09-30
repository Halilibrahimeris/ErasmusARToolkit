using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit
{
    /// <summary>What the phone shows around a design.</summary>
    public enum DesignView
    {
        /// <summary>The real room through the camera, with the design in it.</summary>
        AR,

        /// <summary>Only the design: the camera picture is hidden and the room shell is shown.</summary>
        Virtual,
    }

    /// <summary>
    /// A space designed in Unity (a classroom, a garden, a museum) that the user walks through with the
    /// phone. The design is put where the app starts: its Start Camera comes to where the phone is. The
    /// HUD's View button switches between the real room (AR) and the design alone (Virtual); holding it
    /// down in the Virtual view shows the real room while the finger is down. On the PC the camera walks
    /// through the design (W A S D, right-drag to look around).
    /// </summary>
    [RequireComponent(typeof(ModulePlacement))]
    [DisallowMultipleComponent, AddComponentMenu("Erasmus AR Toolkit/Design Space")]
    public sealed class DesignSpace : ToolkitModule
    {
        const long PeekDelayMilliseconds = 350;

        [SerializeField, Tooltip("Width (x), height (y) and length (z) of the designed space in meters. Drawn as a " +
                                 "grid in the Scene view.")]
        Vector3 size = new Vector3(8f, 3f, 6f);
        [SerializeField, Tooltip("Shown instead of the camera picture in the Virtual view.")]
        Color skyColor = new Color(0.62f, 0.78f, 0.93f);
        [SerializeField, Tooltip("Where the phone starts, looking the way it looks.")]
        DesignStartPoint startPoint;
        [SerializeField, Tooltip("Shown in the Virtual view only: the room shell (floor, walls, ceiling). Put " +
                                 "things here that would hide the real room.")]
        GameObject virtualOnly;
        [SerializeField] DesignView startView = DesignView.AR;
        [SerializeField, Tooltip("A View button (AR / Virtual) on the HUD.")]
        bool showViewButton = true;

        ModulePlacement placement;
        Button viewButton;
        IVisualElementScheduledItem peekStart;
        DesktopCameraController walkingCamera;
        bool warned;
        bool pressing;
        bool peeking;
        bool suppressClick;

        public DesignView View { get; private set; }

        /// <summary>Raised when the view switches between AR and Virtual.</summary>
        public event Action<DesignView> ViewChanged;

        public Vector3 Size => size;

        public Color SkyColor => skyColor;

        public DesignStartPoint StartPoint => startPoint;

        public GameObject VirtualOnly => virtualOnly;

        /// <summary>True while the real room is shown for a moment in the Virtual view (View held down).</summary>
        public bool IsPeeking => peeking;

        public ModulePlacement Placement => placement != null ? placement : placement = GetComponent<ModulePlacement>();

        internal void Configure(Vector3 designSize, Color sky, DesignStartPoint start, GameObject virtualParts)
        {
            size = designSize;
            skyColor = sky;
            startPoint = start;
            virtualOnly = virtualParts;
        }

        void Awake()
        {
            View = startView;
            Placement.Placed += OnPlaced;
        }

        void OnDestroy()
        {
            if (placement != null)
            {
                placement.Placed -= OnPlaced;
            }
        }

        protected override void OnModeReady(ToolkitMode mode)
        {
            if (showViewButton)
            {
                viewButton = AddHudButton(DesignText.ViewButton(View), OnViewClicked);
                viewButton.RegisterCallback<PointerDownEvent>(_ => BeginPress(), TrickleDown.TrickleDown);
                viewButton.RegisterCallback<PointerUpEvent>(_ => EndPress(), TrickleDown.TrickleDown);
                viewButton.RegisterCallback<PointerCaptureOutEvent>(_ => EndPress());
            }

            if (mode == ToolkitMode.Desktop)
            {
                EnterWalkMode();
            }

            ApplyView();
        }

        protected override void OnDisable()
        {
            EndPress();
            if (Manager != null && Manager.CameraImage != null && View == DesignView.Virtual)
            {
                Manager.CameraImage.Show();
            }

            if (walkingCamera != null)
            {
                walkingCamera.ExitWalkMode();
                walkingCamera = null;
            }

            viewButton = null;
            base.OnDisable();
        }

        /// <summary>Shows the real room with the design (AR) or the design alone (Virtual).</summary>
        public void SetView(DesignView view)
        {
            View = view;
            if (view == DesignView.Virtual && !warned && IsAR)
            {
                warned = true;
                Toast(DesignText.VirtualWarning);
            }

            ApplyView();
            ViewChanged?.Invoke(view);
        }

        public void ToggleView()
        {
            SetView(View == DesignView.AR ? DesignView.Virtual : DesignView.AR);
        }

        /// <summary>The Virtual view shows the real room while <paramref name="value"/> is true.</summary>
        public void SetPeeking(bool value)
        {
            peeking = value && View == DesignView.Virtual;
            ApplyView();
        }

        void OnPlaced(Pose pose)
        {
            // The placement shows every child once it is placed: the room shell belongs to one view only.
            ApplyView();
        }

        void ApplyView()
        {
            var isVirtual = View == DesignView.Virtual;
            if (virtualOnly != null && Placement.IsPlaced)
            {
                virtualOnly.SetActive(isVirtual);
            }

            if (IsModeReady && Manager != null && Manager.CameraImage != null)
            {
                if (isVirtual && !peeking)
                {
                    Manager.CameraImage.Hide(skyColor);
                }
                else
                {
                    Manager.CameraImage.Show();
                }
            }

            if (viewButton != null)
            {
                viewButton.text = peeking ? DesignText.Peeking : DesignText.ViewButton(View);
                viewButton.EnableInClassList(ARToolkitHUD.ActiveButtonClass, isVirtual);
            }
        }

        void EnterWalkMode()
        {
            var camera = ViewCamera;
            walkingCamera = camera != null ? camera.GetComponent<DesktopCameraController>() : null;
            if (walkingCamera == null)
            {
                return;
            }

            var start = startPoint != null ? startPoint.transform : transform;
            walkingCamera.EnterWalkMode(start.position, start.eulerAngles.y);
        }

        void OnViewClicked()
        {
            if (suppressClick)
            {
                suppressClick = false;
                return;
            }

            ToggleView();
        }

        void BeginPress()
        {
            pressing = true;
            suppressClick = false;
            if (View == DesignView.Virtual && viewButton != null)
            {
                peekStart?.Pause();
                peekStart = viewButton.schedule.Execute(StartPeek).StartingIn(PeekDelayMilliseconds);
            }
        }

        void StartPeek()
        {
            if (!pressing)
            {
                return;
            }

            suppressClick = true;
            SetPeeking(true);
        }

        void EndPress()
        {
            pressing = false;
            peekStart?.Pause();
            peekStart = null;
            if (peeking)
            {
                SetPeeking(false);
            }
        }
    }

    /// <summary>Texts of the design walkthrough.</summary>
    public static class DesignText
    {
        public const string VirtualWarning =
            "Virtual view: you cannot see the room now. Walk slowly. Hold the View button to see the room.";

        public const string Peeking = "Showing the room";

        public static string ViewButton(DesignView view) => view == DesignView.AR ? "View: AR" : "View: Virtual";
    }
}
