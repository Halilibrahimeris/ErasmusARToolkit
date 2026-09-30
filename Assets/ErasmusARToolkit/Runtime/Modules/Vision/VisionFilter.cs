using System;
using System.Collections.Generic;
using UnityEngine;
#if ERASMUS_HAS_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Erasmus.ARToolkit
{
    /// <summary>Ways of seeing the vision simulator can show.</summary>
    public enum VisionFilterKind
    {
        Normal,
        Protanopia,
        Deuteranopia,
        Tritanopia,
        Achromatopsia,
        LowVision,
        TunnelVision,
        Cataract,
    }

    /// <summary>What a filter does to the picture: a color matrix, blur, a tunnel and haze.</summary>
    public readonly struct VisionFilterSettings
    {
        public VisionFilterSettings(Vector3 row0, Vector3 row1, Vector3 row2, float blur = 0f, float tunnel = 0f, float haze = 0f)
        {
            Row0 = row0;
            Row1 = row1;
            Row2 = row2;
            Blur = blur;
            Tunnel = tunnel;
            Haze = haze;
        }

        public Vector3 Row0 { get; }
        public Vector3 Row1 { get; }
        public Vector3 Row2 { get; }

        /// <summary>Blur radius as a fraction of the screen width.</summary>
        public float Blur { get; }

        /// <summary>Radius of the seen circle (fraction of the screen height); 0 = no tunnel.</summary>
        public float Tunnel { get; }

        /// <summary>How much the picture fades to a pale haze (0..1).</summary>
        public float Haze { get; }
    }

    /// <summary>
    /// The filters of the vision simulator. Colour vision deficiencies use the matrices of Machado, Oliveira
    /// and Fernandes (2009) at full strength.
    /// </summary>
    public static class VisionFilter
    {
        static readonly Vector3 Red = new Vector3(1f, 0f, 0f);
        static readonly Vector3 Green = new Vector3(0f, 1f, 0f);
        static readonly Vector3 Blue = new Vector3(0f, 0f, 1f);
        static readonly Vector3 Luminance = new Vector3(0.2126f, 0.7152f, 0.0722f);

        public static IReadOnlyList<VisionFilterKind> All { get; } = new[]
        {
            VisionFilterKind.Protanopia, VisionFilterKind.Deuteranopia, VisionFilterKind.Tritanopia,
            VisionFilterKind.Achromatopsia, VisionFilterKind.LowVision, VisionFilterKind.TunnelVision,
            VisionFilterKind.Cataract,
        };

        public static VisionFilterSettings Settings(VisionFilterKind kind)
        {
            switch (kind)
            {
                case VisionFilterKind.Protanopia:
                    return new VisionFilterSettings(new Vector3(0.152286f, 1.052583f, -0.204868f),
                        new Vector3(0.114503f, 0.786281f, 0.099216f), new Vector3(-0.003882f, -0.048116f, 1.051998f));
                case VisionFilterKind.Deuteranopia:
                    return new VisionFilterSettings(new Vector3(0.367322f, 0.860646f, -0.227968f),
                        new Vector3(0.280085f, 0.672501f, 0.047413f), new Vector3(-0.011820f, 0.042940f, 0.968881f));
                case VisionFilterKind.Tritanopia:
                    return new VisionFilterSettings(new Vector3(1.255528f, -0.076749f, -0.178779f),
                        new Vector3(-0.078411f, 0.930809f, 0.147602f), new Vector3(0.004733f, 0.691367f, 0.303900f));
                case VisionFilterKind.Achromatopsia:
                    return new VisionFilterSettings(Luminance, Luminance, Luminance);
                case VisionFilterKind.LowVision:
                    return new VisionFilterSettings(Red, Green, Blue, blur: 0.012f);
                case VisionFilterKind.TunnelVision:
                    return new VisionFilterSettings(Red, Green, Blue, tunnel: 0.18f);
                case VisionFilterKind.Cataract:
                    return new VisionFilterSettings(Red, Green, Blue, blur: 0.004f, haze: 0.45f);
                default:
                    return new VisionFilterSettings(Red, Green, Blue);
            }
        }

        public static string Name(VisionFilterKind kind)
        {
            switch (kind)
            {
                case VisionFilterKind.Protanopia: return "Red-blind";
                case VisionFilterKind.Deuteranopia: return "Green-blind";
                case VisionFilterKind.Tritanopia: return "Blue-blind";
                case VisionFilterKind.Achromatopsia: return "No colour";
                case VisionFilterKind.LowVision: return "Low vision";
                case VisionFilterKind.TunnelVision: return "Tunnel vision";
                case VisionFilterKind.Cataract: return "Cataract";
                default: return "Normal";
            }
        }

        public static string Description(VisionFilterKind kind)
        {
            switch (kind)
            {
                case VisionFilterKind.Protanopia:
                    return "Protanopia: red looks dark and like green. About 1 in 100 men.";
                case VisionFilterKind.Deuteranopia:
                    return "Deuteranopia: red and green look alike. The most common colour blindness.";
                case VisionFilterKind.Tritanopia:
                    return "Tritanopia: blue and green, yellow and pink look alike. Rare.";
                case VisionFilterKind.Achromatopsia:
                    return "Achromatopsia: no colours at all, only grey.";
                case VisionFilterKind.LowVision:
                    return "Low vision: everything is blurred, small text cannot be read.";
                case VisionFilterKind.TunnelVision:
                    return "Tunnel vision (glaucoma, retinitis pigmentosa): only the middle is seen.";
                case VisionFilterKind.Cataract:
                    return "Cataract: the view is hazy, pale and a little blurred.";
                default:
                    return string.Empty;
            }
        }

        /// <summary>The filter after <paramref name="current"/>: Normal, then each offered one, then Normal again.</summary>
        public static VisionFilterKind Next(IReadOnlyList<VisionFilterKind> offered, VisionFilterKind current)
        {
            if (offered == null || offered.Count == 0)
            {
                return VisionFilterKind.Normal;
            }

            if (current == VisionFilterKind.Normal)
            {
                return offered[0];
            }

            for (var i = 0; i < offered.Count; i++)
            {
                if (offered[i] == current)
                {
                    return i + 1 < offered.Count ? offered[i + 1] : VisionFilterKind.Normal;
                }
            }

            return VisionFilterKind.Normal;
        }
    }

    /// <summary>One filter the simulator offers, with the text shown for it.</summary>
    [Serializable]
    public sealed class VisionFilterEntry
    {
        [SerializeField] VisionFilterKind kind;
        [SerializeField] bool offered = true;
        [SerializeField, TextArea(1, 3), Tooltip("Shown under the status line. Empty: the toolkit's text.")]
        string description = string.Empty;

        public VisionFilterEntry()
        {
        }

        public VisionFilterEntry(VisionFilterKind kind, bool offered = true, string description = "")
        {
            this.kind = kind;
            this.offered = offered;
            this.description = description ?? string.Empty;
        }

        public VisionFilterKind Kind => kind;
        public bool Offered => offered && kind != VisionFilterKind.Normal;

        public string Description => string.IsNullOrWhiteSpace(description)
            ? VisionFilter.Description(kind)
            : description.Trim();

        public static List<VisionFilterEntry> Defaults()
        {
            var entries = new List<VisionFilterEntry>();
            foreach (var kind in VisionFilter.All)
            {
                entries.Add(new VisionFilterEntry(kind));
            }

            return entries;
        }
    }

    /// <summary>
    /// A full-screen quad in front of a camera that draws the camera's picture through a vision filter.
    /// While the filter is Normal it is off and the camera makes no copy of its picture.
    /// </summary>
    public sealed class VisionFilterView : MonoBehaviour
    {
        static readonly int Row0Id = Shader.PropertyToID("_Row0");
        static readonly int Row1Id = Shader.PropertyToID("_Row1");
        static readonly int Row2Id = Shader.PropertyToID("_Row2");
        static readonly int BlurId = Shader.PropertyToID("_Blur");
        static readonly int TunnelId = Shader.PropertyToID("_Tunnel");
        static readonly int HazeId = Shader.PropertyToID("_Haze");

        Camera target;
        MeshRenderer quadRenderer;
        Material material;

        public VisionFilterKind Kind { get; private set; }

        public bool IsDrawing => quadRenderer != null && quadRenderer.enabled;

        /// <param name="source">The theme's vision material (it carries the shader into builds).</param>
        public static VisionFilterView Create(Camera camera, Material source)
        {
            if (camera == null || source == null)
            {
                return null;
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Vision Filter";
            ObjectUtility.SafeDestroy(quad.GetComponent<Collider>());

            // In front of the camera so it is never culled; the shader stretches it over the screen.
            quad.transform.SetParent(camera.transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, Mathf.Max(camera.nearClipPlane * 2f, 0.5f));
            quad.transform.localScale = Vector3.one * 0.2f;

            var view = quad.AddComponent<VisionFilterView>();
            view.target = camera;
            view.material = new Material(source) { name = "Vision Filter" };
            view.quadRenderer = quad.GetComponent<MeshRenderer>();
            view.quadRenderer.sharedMaterial = view.material;
            view.quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view.quadRenderer.receiveShadows = false;
            view.Apply(VisionFilterKind.Normal);
            return view;
        }

        public void Apply(VisionFilterKind kind)
        {
            Kind = kind;
            var settings = VisionFilter.Settings(kind);
            material.SetVector(Row0Id, settings.Row0);
            material.SetVector(Row1Id, settings.Row1);
            material.SetVector(Row2Id, settings.Row2);
            material.SetFloat(BlurId, settings.Blur);
            material.SetFloat(TunnelId, settings.Tunnel);
            material.SetFloat(HazeId, settings.Haze);
            var on = kind != VisionFilterKind.Normal;
            quadRenderer.enabled = on;
            RequestCameraPicture(on);
        }

        void OnDestroy()
        {
            RequestCameraPicture(false);
            ObjectUtility.SafeDestroy(material);
        }

        /// <summary>The filter reads URP's opaque texture: the camera makes it only while a filter is on.</summary>
        void RequestCameraPicture(bool on)
        {
#if ERASMUS_HAS_URP
            if (target == null)
            {
                return;
            }

            var data = target.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.requiresColorOption = on ? CameraOverrideOption.On : CameraOverrideOption.UsePipelineSettings;
            }
#endif
        }
    }
}
