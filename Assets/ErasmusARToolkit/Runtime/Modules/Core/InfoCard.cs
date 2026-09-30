using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// An info card for any object in the scene: a part of a design, a thing in a portal world, a story
    /// character. On the phone, tapping the object opens the card above it and a read-only panel with its
    /// text. Put it on the object that has the mesh (a chair), not on a group of objects: the object gets
    /// a box collider around its meshes if it has no collider, and a big box would catch the taps meant
    /// for the objects inside it.
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("Erasmus AR Toolkit/Info Card")]
    public sealed class InfoCard : MonoBehaviour
    {
        [SerializeField] InfoLabelData info = new InfoLabelData();

        public InfoLabelData Info => info ?? new InfoLabelData();

        /// <summary>The toolkit object made for the card at start (null before Start).</summary>
        public ToolkitObject ToolkitObject { get; private set; }

        /// <summary>Sets the text. Takes effect when set before the object starts (in the Editor or on Awake).</summary>
        public void SetInfo(string title, string body, bool showOnStart = false)
        {
            info = new InfoLabelData(title, body, showOnStart);
        }

        void Start()
        {
            Attach(null);
        }

        /// <summary>Makes the object tappable with its card (once).</summary>
        internal ToolkitObject Attach(ToolkitTheme theme)
        {
            if (ToolkitObject != null)
            {
                return ToolkitObject;
            }

            var existing = GetComponent<ToolkitObject>();
            if (existing != null)
            {
                ToolkitObject = existing;
                return existing;
            }

            if (theme == null)
            {
                var manager = FindAnyObjectByType<ARToolkitManager>();
                theme = manager != null ? manager.Theme : null;
            }

            ToolkitObject = ToolkitObjectFactory.AttachInfoCard(gameObject, InfoCardText.Title(Info.Title, name), Info, theme);
            return ToolkitObject;
        }
    }

    internal static class InfoCardText
    {
        /// <summary>The card's title, or the object's name when the card has none.</summary>
        public static string Title(string title, string objectName)
        {
            return string.IsNullOrWhiteSpace(title) ? (objectName ?? string.Empty).Trim() : title.Trim();
        }
    }
}
