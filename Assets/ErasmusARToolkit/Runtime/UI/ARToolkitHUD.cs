using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// On-screen controls built with UI Toolkit (ARToolkitHUD.uxml / .uss): object picker,
    /// room scan, planes/grid toggle, camera switch, clear, screenshot, help, the edit panel of the
    /// selected object, the status line and short notifications.
    /// </summary>
    [RequireComponent(typeof(UIDocument), typeof(ScreenshotService))]
    public sealed class ARToolkitHUD : MonoBehaviour
    {
        const long ToastMilliseconds = 2500;

        [SerializeField] ARToolkitManager manager;
        [SerializeField] InteractionController interaction;

        readonly List<Button> placeableButtons = new List<Button>();
        readonly ModuleHud modules = new ModuleHud();
        UIDocument document;
        ScreenshotService screenshots;
        HudView view;
        EditPanel editPanel;
        ScanPanel scanPanel;
        IVisualElementScheduledItem toastHide;
        Rect lastSafeArea;
        Vector2Int lastScreenSize;

        internal void Initialize(ARToolkitManager toolkitManager, InteractionController interactionController)
        {
            manager = toolkitManager;
            interaction = interactionController;
        }

        /// <summary>The module parts of the HUD (buttons, info line, subtitle).</summary>
        internal ModuleHud Modules => modules;

        void Awake()
        {
            if (manager != null)
            {
                manager.RegisterHud(this);
            }
        }

        void Start()
        {
            document = GetComponent<UIDocument>();
            screenshots = GetComponent<ScreenshotService>();
            if (manager == null || interaction == null)
            {
                Debug.LogError("[Erasmus AR Toolkit] HUD is missing its manager or interaction controller.", this);
                enabled = false;
                return;
            }

            view = HudView.TryBind(document.rootVisualElement);
            if (view == null)
            {
                enabled = false;
                return;
            }

            // Modules may have asked for buttons already (on PC the mode is known before the HUD starts).
            modules.Bind(view.Root);

            try
            {
                editPanel = new EditPanel(view.Root, manager, interaction);
            }
            catch (System.InvalidOperationException exception)
            {
                Debug.LogError("[Erasmus AR Toolkit] " + exception.Message, this);
            }

            BindRoomScan();
            UIPointerGuard.Register(document);
            WireButtons();
            BuildPlaceableButtons();

            manager.StatusChanged += SetStatus;
            manager.ModeChanged += OnModeChanged;
            interaction.Notification += ShowToast;
            interaction.SelectionChanged += OnSelectionChanged;
            if (manager.CameraFeed != null)
            {
                manager.CameraFeed.Notification += ShowToast;
            }

            SetStatus(manager.Status);
            OnModeChanged(manager.Mode);
            OnSelectionChanged(interaction.Selected);
            view.Root.RegisterCallback<GeometryChangedEvent>(_ => ApplySafeArea());
        }

        void OnDestroy()
        {
            UIPointerGuard.Unregister(document);
            if (manager != null)
            {
                manager.StatusChanged -= SetStatus;
                manager.ModeChanged -= OnModeChanged;
                if (manager.CameraFeed != null)
                {
                    manager.CameraFeed.Notification -= ShowToast;
                }
            }

            if (interaction != null)
            {
                interaction.Notification -= ShowToast;
                interaction.SelectionChanged -= OnSelectionChanged;
            }

            scanPanel?.Dispose();
            if (manager != null && manager.RoomScanner != null)
            {
                manager.RoomScanner.Notification -= ShowToast;
            }
        }

        void Update()
        {
            var screenSize = new Vector2Int(Screen.width, Screen.height);
            if (Screen.safeArea != lastSafeArea || screenSize != lastScreenSize)
            {
                ApplySafeArea();
            }

            if (editPanel != null && editPanel.IsVisible)
            {
                editPanel.Refresh();
            }
        }

        public void ShowToast(string message)
        {
            if (view == null || string.IsNullOrEmpty(message))
            {
                return;
            }

            view.Toast.text = message;
            view.Toast.AddToClassList(HudView.VisibleClass);
            toastHide?.Pause();
            toastHide = view.Toast.schedule.Execute(() => view.Toast.RemoveFromClassList(HudView.VisibleClass))
                .StartingIn(ToastMilliseconds);
        }

        /// <summary>CSS class of a module button that is switched on (for example "View: Virtual").</summary>
        public const string ActiveButtonClass = "active";

        /// <summary>
        /// Adds a button to the module strip above the object bar. Works before the HUD has started too.
        /// Remove it with <see cref="RemoveModuleButton"/>.
        /// </summary>
        public Button AddModuleButton(string text, System.Action onClick)
        {
            return modules.AddButton(text, onClick);
        }

        /// <summary>Adds any element (a slider, a label) to the module strip.</summary>
        public void AddModuleElement(VisualElement element)
        {
            modules.Add(element);
        }

        public void RemoveModuleButton(VisualElement button)
        {
            modules.Remove(button);
        }

        /// <summary>
        /// A line under the status line ("Route 2 / 6", "Vision: Protanopia"). One line per
        /// <paramref name="owner"/>; an empty text removes it.
        /// </summary>
        public void SetInfoLine(object owner, string text)
        {
            modules.SetInfoLine(owner, text);
        }

        /// <summary>A subtitle above the object bar ("Ayse: Hello!"). An empty text removes it.</summary>
        public void SetSubtitle(object owner, string text)
        {
            modules.SetSubtitle(owner, text);
        }

        /// <summary>The HUD layout has the module strip (layouts from older toolkit versions do not).</summary>
        public bool HasModuleParts => modules.HasParts;

        void WireButtons()
        {
            view.PlanesButton.clicked += TogglePlanes;
            view.CameraButton.clicked += () =>
            {
                if (manager.CameraFeed != null)
                {
                    manager.CameraFeed.CycleCamera();
                }
            };
            view.ClearButton.clicked += () =>
            {
                manager.ClearPlacedObjects();
                ShowToast("All placed objects removed.");
            };
            view.PhotoButton.clicked += () => screenshots.Capture(SetHudVisible,
                path => ShowToast("Photo saved: " + System.IO.Path.GetFileName(path)),
                _ => ShowToast("Could not save the photo."));
            view.HelpButton.clicked += () => HudView.SetShown(view.HelpPanel, true);
            view.HelpClose.clicked += () => HudView.SetShown(view.HelpPanel, false);
        }

        void BindRoomScan()
        {
            var scanner = manager.RoomScanner;
            if (scanner == null)
            {
                return;
            }

            try
            {
                scanPanel = new ScanPanel(view.Root, scanner, ShowPlanesForScan, ShowsScanDiagnostics());
            }
            catch (System.InvalidOperationException exception)
            {
                Debug.LogError("[Erasmus AR Toolkit] " + exception.Message, this);
                return;
            }

            scanner.Notification += ShowToast;
        }

        /// <summary>Asked for in the config, or a development build on a phone.</summary>
        bool ShowsScanDiagnostics()
        {
            return (manager.Config != null && manager.Config.ShowScanDiagnostics)
                   || (Debug.isDebugBuild && !Application.isEditor);
        }

        /// <summary>A scan shows what it finds, so hidden planes are shown again.</summary>
        void ShowPlanesForScan()
        {
            if (!manager.PlanesVisible)
            {
                manager.SetPlanesVisible(true);
                UpdatePlanesLabel();
            }
        }

        void BuildPlaceableButtons()
        {
            var placeables = manager.Config.Placeables;
            HudView.SetShown(view.Placeables, placeables.Count > 0);
            for (var i = 0; i < placeables.Count; i++)
            {
                var item = placeables[i];
                if (item == null)
                {
                    continue;
                }

                var index = i;
                var button = new Button(() => SelectPlaceable(index)) { name = "placeable-" + i };
                button.AddToClassList(HudView.PlaceableButtonClass);
                if (item.Icon != null)
                {
                    var icon = new VisualElement();
                    icon.AddToClassList("placeable-icon");
                    icon.style.backgroundImage = new StyleBackground(Background.FromSprite(item.Icon));
                    icon.pickingMode = PickingMode.Ignore;
                    button.Add(icon);
                }

                var label = new Label(item.ResolvedName) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("placeable-label");
                button.Add(label);
                view.Placeables.Add(button);
                placeableButtons.Add(button);
            }

            SelectPlaceable(interaction.SelectedPlaceableIndex);
        }

        void SelectPlaceable(int index)
        {
            interaction.SelectPlaceable(index);
            for (var i = 0; i < placeableButtons.Count; i++)
            {
                placeableButtons[i].EnableInClassList(HudView.SelectedClass, i == interaction.SelectedPlaceableIndex);
            }
        }

        void TogglePlanes()
        {
            manager.SetPlanesVisible(!manager.PlanesVisible);
            UpdatePlanesLabel();
        }

        void UpdatePlanesLabel()
        {
            var surface = manager.Mode == ToolkitMode.AR ? "Planes" : "Grid";
            view.PlanesButton.text = (manager.PlanesVisible ? "Hide " : "Show ") + surface;
        }

        void SetHudVisible(bool visible)
        {
            view.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void SetStatus(string status)
        {
            if (view != null)
            {
                view.Status.text = status;
            }
        }

        void OnModeChanged(ToolkitMode mode)
        {
            var decided = mode != ToolkitMode.Undetermined;
            HudView.SetShown(view.PlanesButton, decided);
            scanPanel?.SetAvailable(mode == ToolkitMode.AR);
            var feed = manager.CameraFeed;
            HudView.SetShown(view.CameraButton, mode == ToolkitMode.Desktop && feed != null && feed.IsEnabledInConfig);
            UpdatePlanesLabel();
            view.HelpText.text = HelpTexts.For(mode, Application.isMobilePlatform);
        }

        void OnSelectionChanged(ToolkitObject selected)
        {
            editPanel?.Show(selected);
        }

        /// <summary>Pads the HUD so it stays clear of notches and rounded corners.</summary>
        void ApplySafeArea()
        {
            var panel = view?.Root.panel;
            if (panel == null)
            {
                return;
            }

            lastSafeArea = Screen.safeArea;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            var topLeft = RuntimePanelUtils.ScreenToPanel(panel,
                new Vector2(lastSafeArea.xMin, Screen.height - lastSafeArea.yMax));
            var bottomRight = RuntimePanelUtils.ScreenToPanel(panel,
                new Vector2(lastSafeArea.xMax, Screen.height - lastSafeArea.yMin));
            var panelSize = view.Root.layout.size;
            if (float.IsNaN(panelSize.x) || panelSize.x <= 0f)
            {
                return;
            }

            var style = view.SafeArea.style;
            style.paddingLeft = Mathf.Max(0f, topLeft.x);
            style.paddingTop = Mathf.Max(0f, topLeft.y);
            style.paddingRight = Mathf.Max(0f, panelSize.x - bottomRight.x);
            style.paddingBottom = Mathf.Max(0f, panelSize.y - bottomRight.y);
        }
    }

    public static class HelpTexts
    {
        /// <summary>Help for real AR, a phone without ARCore, or a PC.</summary>
        public static string For(ToolkitMode mode, bool isMobile)
        {
            if (mode == ToolkitMode.AR)
            {
                return Touch;
            }

            return isMobile ? TouchFallback : Desktop;
        }

        public const string TouchFallback =
            "• This phone has no ARCore, so objects stand on a virtual floor over the camera image.\n" +
            "• Pick an object in the bottom bar and tap the floor to place it.\n" +
            "• Drag to move, pinch to scale, twist to rotate.\n" +
            "• Tap an object to open its panel: info, scale, rotate, raise, delete.\n" +
            "• Camera switches between the phone cameras or turns the camera off.\n" +
            "• Photo saves a picture without the buttons.";

        public const string Touch =
            "• Pick an object in the bottom bar.\n" +
            "• Move the phone until the floor shows a grid; the ring shows where objects can go.\n" +
            "• Tap the grid to place the object. It stays there when you look away.\n" +
            "• Surfaces spill past table edges? Tap Scan Room, walk slowly and look at the floor and\n" +
            "  every table top, then tap Finish Scan. The grid then follows the real edges.\n" +
            "• Drag an object to move it. Pinch to scale, twist to rotate.\n" +
            "• Tap an object to open its panel: info, scale, rotate, raise, delete.\n" +
            "• Point the camera at an image target to see its content (a frame shows it was found).\n" +
            "• Photo saves a picture without the buttons.";

        public const string Desktop =
            "• Pick an object in the bottom bar.\n" +
            "• Left-click the grid to place it.\n" +
            "• Left-drag an object to move it.\n" +
            "• Q / E rotate, Shift + scroll scales, Delete removes.\n" +
            "• Click an object to open its panel: info, scale, rotate, raise, delete.\n" +
            "• Right-drag orbits, middle-drag pans, scroll zooms.\n" +
            "• W A S D moves the view, F resets it.\n" +
            "• Camera switches between webcams or turns the webcam off.\n" +
            "• Hold a printed image target up to the webcam to see its content on it\n" +
            "  (with the webcam off, image targets lie on the grid instead).";
    }
}
