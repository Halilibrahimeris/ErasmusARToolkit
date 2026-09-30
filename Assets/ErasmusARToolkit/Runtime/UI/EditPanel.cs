using UnityEngine.UIElements;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// The HUD panel shown for the selected object: its info text, live transform values and
    /// buttons to scale, rotate, raise/lower, reset, toggle the 3D info card and delete.
    /// </summary>
    internal sealed class EditPanel
    {
        const float RotateStepDegrees = 15f;
        const float RaiseStepMeters = 0.02f;

        readonly ARToolkitManager manager;
        readonly InteractionController interaction;
        readonly VisualElement root;
        readonly Label title;
        readonly Label info;
        readonly Label values;
        readonly Slider scale;
        readonly Button labelButton;
        readonly Button deleteButton;
        readonly VisualElement rotateRow;
        readonly VisualElement heightRow;
        readonly Button resetButton;
        readonly Button mediaButton;
        ToolkitObject target;
        VideoSurface media;

        public EditPanel(VisualElement hudRoot, ARToolkitManager toolkitManager, InteractionController controller)
        {
            manager = toolkitManager;
            interaction = controller;
            root = Require<VisualElement>(hudRoot, "edit-panel");
            title = Require<Label>(hudRoot, "edit-title");
            info = Require<Label>(hudRoot, "edit-info");
            values = Require<Label>(hudRoot, "edit-values");
            scale = Require<Slider>(hudRoot, "edit-scale");
            labelButton = Require<Button>(hudRoot, "edit-label");
            deleteButton = Require<Button>(hudRoot, "edit-delete");

            Require<Button>(hudRoot, "edit-close").clicked += interaction.ClearSelection;
            var rotateLeft = Require<Button>(hudRoot, "edit-rotate-left");
            rotateLeft.clicked += () => Edit(m => m.RotateYaw(-RotateStepDegrees));
            Require<Button>(hudRoot, "edit-rotate-right").clicked += () => Edit(m => m.RotateYaw(RotateStepDegrees));
            var lower = Require<Button>(hudRoot, "edit-lower");
            lower.clicked += () => Edit(m => m.Raise(-RaiseStepMeters));
            Require<Button>(hudRoot, "edit-raise").clicked += () => Edit(m => m.Raise(RaiseStepMeters));
            resetButton = Require<Button>(hudRoot, "edit-reset");
            resetButton.clicked += () => Edit(m => m.ResetPose());
            rotateRow = rotateLeft.parent;
            heightRow = lower.parent;

            // Optional: layouts made for older toolkit versions have no media button.
            mediaButton = hudRoot.Q<Button>("edit-media");
            if (mediaButton != null)
            {
                mediaButton.clicked += ToggleMedia;
            }
            labelButton.clicked += ToggleLabel;
            deleteButton.clicked += interaction.DeleteSelected;
            scale.RegisterValueChangedCallback(evt => Edit(m => m.SetScaleMultiplier(evt.newValue)));
        }

        public bool IsVisible => !root.ClassListContains(HudView.HiddenClass);

        public void Show(ToolkitObject selected)
        {
            target = selected;
            HudView.SetShown(root, selected != null);
            if (selected == null)
            {
                return;
            }

            title.text = string.IsNullOrWhiteSpace(selected.Info.Title) ? selected.DisplayName : selected.Info.Title;
            info.text = selected.Info.Body;
            HudView.SetShown(info, !string.IsNullOrWhiteSpace(selected.Info.Body));
            labelButton.SetEnabled(selected.HasLabel);
            HudView.SetShown(deleteButton, selected.IsPlaced);

            // Objects with an info card only (parts of a design, a portal world, story characters) are
            // read-only: their panel shows the card, nothing to scale, turn or move.
            var editable = selected.IsManipulatable;
            HudView.SetShown(values, editable);
            HudView.SetShown(scale, editable);
            HudView.SetShown(rotateRow, editable);
            HudView.SetShown(heightRow, editable);
            HudView.SetShown(resetButton, editable);

            // Living posters and video exhibits can be paused.
            media = selected.GetComponentInChildren<VideoSurface>(true);
            if (mediaButton != null)
            {
                HudView.SetShown(mediaButton, media != null);
            }

            var limits = manager.Config.ScaleLimits;
            scale.lowValue = limits.x;
            scale.highValue = limits.y;
            Refresh();
        }

        /// <summary>Keeps the values in sync with gestures (pinch, twist, drag). Called every frame while open.</summary>
        public void Refresh()
        {
            if (target == null)
            {
                // The object was deleted (Clear All, Delete key) while the panel was open.
                HudView.SetShown(root, false);
                return;
            }

            var manipulator = target.Manipulator;
            var yaw = manipulator != null ? manipulator.YawDegrees : target.transform.eulerAngles.y;
            var scaleValue = manipulator != null ? manipulator.ScaleMultiplier : 1f;
            values.text = target.HasSurface
                ? TransformFormatter.Format(target.transform.position, target.HeightAboveSurface, yaw, scaleValue)
                : TransformFormatter.Format(target.transform.position, yaw, scaleValue);
            scale.SetValueWithoutNotify(scaleValue);
            scale.SetEnabled(manipulator != null);
            labelButton.text = target.IsLabelVisible ? "Hide Card" : "Show Card";
            if (mediaButton != null && media != null)
            {
                mediaButton.text = media.WantsPlaying ? "Pause" : "Play";
            }
        }

        void ToggleMedia()
        {
            if (media != null)
            {
                media.TogglePlay();
                Refresh();
            }
        }

        void Edit(System.Action<ObjectManipulator> change)
        {
            if (target != null && target.Manipulator != null)
            {
                change(target.Manipulator);
                Refresh();
            }
        }

        void ToggleLabel()
        {
            if (target != null)
            {
                target.ToggleLabel();
                Refresh();
            }
        }

        static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            return root.Q<T>(name) ?? throw new System.InvalidOperationException(
                $"ARToolkitHUD.uxml is missing {typeof(T).Name} '{name}'.");
        }
    }
}
