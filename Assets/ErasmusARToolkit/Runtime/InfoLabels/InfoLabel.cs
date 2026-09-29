using TMPro;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Floating info card above an object: one TextMeshPro text on a transparent background.
    /// Follows its target without inheriting its scale and turns (around Y only) to face the camera.
    /// </summary>
    public sealed class InfoLabel : MonoBehaviour
    {
        const float WidthMeters = 0.3f;
        const float FontSize = 0.022f;          // world units, about 2 cm tall text
        const float PaddingMeters = 0.015f;
        const float VerticalGap = 0.05f;

        Transform target;

        public bool IsVisible => gameObject.activeSelf;

        public static InfoLabel Create(Transform target, InfoLabelData data, ToolkitTheme theme)
        {
            var root = new GameObject("Info Label - " + target.name);
            var label = root.AddComponent<InfoLabel>();
            label.target = target;

            var text = CreateText(root.transform, data, theme);
            CreateBackground(root.transform, text, theme);

            label.FollowTarget();
            label.SetVisible(data.ShowOnStart);
            return label;
        }

        /// <summary>Rich text for the card: a bold, colored title line followed by the body.</summary>
        public static string FormatText(InfoLabelData data, Color titleColor)
        {
            var title = string.IsNullOrWhiteSpace(data.Title)
                ? string.Empty
                : $"<b><color=#{ColorUtility.ToHtmlStringRGB(titleColor)}><size=125%>{data.Title.Trim()}</size></color></b>";
            var body = string.IsNullOrWhiteSpace(data.Body) ? string.Empty : data.Body.Trim();
            if (title.Length == 0)
            {
                return body;
            }

            return body.Length == 0 ? title : title + "\n" + body;
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
            if (visible)
            {
                FollowTarget();
            }
        }

        void LateUpdate()
        {
            if (target == null)
            {
                Destroy(gameObject);
                return;
            }

            FollowTarget();
        }

        void FollowTarget()
        {
            if (target == null)
            {
                return;
            }

            var bounds = ObjectUtility.GetRendererBounds(target.gameObject);
            transform.position = new Vector3(bounds.center.x, bounds.max.y + VerticalGap, bounds.center.z);

            var viewer = Camera.main;
            if (viewer != null)
            {
                // Text reads correctly when the label's forward points away from the viewer.
                transform.rotation = ManipulationMath.YawTowards(viewer.transform.position, transform.position);
            }
        }

        static TextMeshPro CreateText(Transform parent, InfoLabelData data, ToolkitTheme theme)
        {
            var textObject = new GameObject("Text");
            textObject.transform.SetParent(parent, false);
            var text = textObject.AddComponent<TextMeshPro>();
            if (theme != null && theme.LabelFont != null)
            {
                text.font = theme.LabelFont;
            }

            text.richText = true;
            text.text = FormatText(data, theme != null ? theme.LabelTitleColor : Color.yellow);
            text.fontSize = FontSize * 10f;   // TMP 3D font size 1 = 0.1 world units
            text.color = theme != null ? theme.LabelTextColor : Color.white;
            text.alignment = TextAlignmentOptions.BottomLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.rectTransform.pivot = new Vector2(0.5f, 0f);
            text.rectTransform.sizeDelta = new Vector2(WidthMeters, 0f);
            text.ForceMeshUpdate();

            // Grow the rect to the text height so the card hugs its content.
            var height = Mathf.Max(text.preferredHeight, FontSize);
            text.rectTransform.sizeDelta = new Vector2(WidthMeters, height);
            text.rectTransform.localPosition = new Vector3(0f, PaddingMeters, 0f);
            return text;
        }

        static void CreateBackground(Transform parent, TextMeshPro text, ToolkitTheme theme)
        {
            var material = theme != null ? theme.LabelBackgroundMaterial : null;
            if (material == null)
            {
                return;
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Background";
            ObjectUtility.SafeDestroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);

            var size = text.rectTransform.sizeDelta;
            var height = size.y + PaddingMeters * 2f;
            quad.transform.localScale = new Vector3(size.x + PaddingMeters * 2f, height, 1f);
            // Slightly behind the text (text faces -Z), bottom edge at the label origin.
            quad.transform.localPosition = new Vector3(0f, height * 0.5f, 0.002f);

            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
