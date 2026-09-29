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
        ToolkitObject target;

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
            Require<Button>(hudRoot, "edit-rotate-left").clicked += () => Edit(m => m.RotateYaw(-RotateStepDegrees));
            Require<Button>(hudRoot, "edit-rotate-right").clicked += () => Edit(m => m.RotateYaw(RotateStepDegrees));
            Require<Button>(hudRoot, "edit-lower").clicked += () => Edit(m => m.Raise(-RaiseStepMeters));
            Require<Button>(hudRoot, "edit-raise").clicked += () => Edit(m => m.Raise(RaiseStepMeters));
            Require<Button>(hudRoot, "edit-reset").clicked += () => Edit(m => m.ResetPose());
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
