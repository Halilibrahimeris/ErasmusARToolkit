using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit
{
    /// <summary>Eye heights the vision simulator offers for a design walkthrough.</summary>
    public enum EyeHeight
    {
        Standing,
        Wheelchair,
        Child,
    }

    /// <summary>
    /// "See through other eyes": a HUD button switches the camera view between normal sight and the offered
    /// filters (colour blindness, low vision, tunnel vision, cataract); the line under the status says what
    /// the filter shows. With a design walkthrough in the scene, an Eye button shows the design (Virtual
    /// view) from the eye height of someone in a wheelchair or of a child.
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("Erasmus AR Toolkit/Vision Simulator")]
    public sealed class VisionSimulator : ToolkitModule
    {
        [SerializeField] List<VisionFilterEntry> filters = VisionFilterEntry.Defaults();
        [SerializeField, Tooltip("An Eye button for a design walkthrough in the scene (Virtual view).")]
        bool offerEyeHeights = true;
        [SerializeField, Min(0.3f)] float standingEyeHeight = 1.5f;
        [SerializeField, Min(0.3f)] float wheelchairEyeHeight = 1.2f;
        [SerializeField, Min(0.3f)] float childEyeHeight = 1.0f;

        VisionFilterView view;
        Button visionButton;
        Button eyeButton;
        DesignSpace design;

        public VisionFilterKind Current { get; private set; }

        public EyeHeight Eye { get; private set; }

        public VisionFilterView View => view;

        /// <summary>The filters offered, in order.</summary>
        public List<VisionFilterKind> OfferedFilters
        {
            get
            {
                var offered = new List<VisionFilterKind>();
                foreach (var entry in filters)
                {
                    if (entry != null && entry.Offered && !offered.Contains(entry.Kind))
                    {
                        offered.Add(entry.Kind);
                    }
                }

                return offered;
            }
        }

        internal void SetFilters(IEnumerable<VisionFilterEntry> entries)
        {
            filters = new List<VisionFilterEntry>(entries);
        }

        protected override void OnModeReady(ToolkitMode mode)
        {
            var theme = Manager.Theme;
            var source = theme != null ? theme.VisionMaterial : null;
            if (source == null && Shader.Find("Erasmus AR Toolkit/Vision Filter") is Shader shader)
            {
                source = new Material(shader);
            }

            view = VisionFilterView.Create(ViewCamera, source);
            if (view == null)
            {
                Debug.LogWarning("[Erasmus AR Toolkit] The theme has no vision filter material. Run Setup.", this);
            }

            visionButton = AddHudButton(VisionText.Button(VisionFilterKind.Normal), NextFilter);
            design = offerEyeHeights ? FindAnyObjectByType<DesignSpace>() : null;
            if (design != null)
            {
                eyeButton = AddHudButton(VisionText.EyeButton(Eye), NextEye);
                design.ViewChanged += OnDesignViewChanged;
            }

            SetFilter(Current);
        }

        protected override void OnDisable()
        {
            if (view != null)
            {
                Destroy(view.gameObject);
                view = null;
            }

            if (design != null)
            {
                design.ViewChanged -= OnDesignViewChanged;
                design.Placement.HeightOffset = 0f;
                design = null;
            }

            visionButton = null;
            eyeButton = null;
            base.OnDisable();
        }

        public void NextFilter()
        {
            SetFilter(VisionFilter.Next(OfferedFilters, Current));
        }

        public void SetFilter(VisionFilterKind kind)
        {
            Current = kind;
            if (view != null)
            {
                view.Apply(kind);
            }

            SetInfoLine(kind == VisionFilterKind.Normal ? null : VisionText.Line(kind, DescriptionOf(kind)));
            if (visionButton != null)
            {
                visionButton.text = VisionText.Button(kind);
                visionButton.EnableInClassList(ARToolkitHUD.ActiveButtonClass, kind != VisionFilterKind.Normal);
            }
        }

        public void NextEye()
        {
            SetEye((EyeHeight)(((int)Eye + 1) % 3));
        }

        public void SetEye(EyeHeight eye)
        {
            Eye = eye;
            ApplyEye();
            if (design != null && design.View != DesignView.Virtual && eye != EyeHeight.Standing)
            {
                Toast(VisionText.EyeNeedsVirtual);
            }
        }

        /// <summary>How far the design is lifted: a lower eye sees the world higher up.</summary>
        public float EyeOffset(EyeHeight eye)
        {
            return EyeHeights.Offset(standingEyeHeight, eye == EyeHeight.Wheelchair ? wheelchairEyeHeight
                : eye == EyeHeight.Child ? childEyeHeight : standingEyeHeight);
        }

        void OnDesignViewChanged(DesignView view)
        {
            ApplyEye();
        }

        void ApplyEye()
        {
            if (eyeButton != null)
            {
                eyeButton.text = VisionText.EyeButton(Eye);
                eyeButton.EnableInClassList(ARToolkitHUD.ActiveButtonClass, Eye != EyeHeight.Standing);
            }

            if (design != null)
            {
                // In the AR view the design lies in the real room: it cannot move away from it.
                design.Placement.HeightOffset = design.View == DesignView.Virtual ? EyeOffset(Eye) : 0f;
            }
        }

        string DescriptionOf(VisionFilterKind kind)
        {
            foreach (var entry in filters)
            {
                if (entry != null && entry.Kind == kind)
                {
                    return entry.Description;
                }
            }

            return VisionFilter.Description(kind);
        }
    }

    public static class EyeHeights
    {
        /// <summary>How much the world rises for an eye at <paramref name="eye"/> instead of <paramref name="standing"/>.</summary>
        public static float Offset(float standing, float eye) => standing - eye;
    }

    /// <summary>Texts of the vision simulator.</summary>
    public static class VisionText
    {
        public const string EyeNeedsVirtual = "Eye heights change the design in its Virtual view. Tap View first.";

        public static string Button(VisionFilterKind kind) => "Vision: " + VisionFilter.Name(kind);

        public static string EyeButton(EyeHeight eye) => "Eye: " + eye;

        public static string Line(VisionFilterKind kind, string description)
        {
            return string.IsNullOrWhiteSpace(description) ? VisionFilter.Name(kind) : description;
        }
    }
}
