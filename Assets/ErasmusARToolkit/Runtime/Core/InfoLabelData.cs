using System;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Text shown on a floating info card above an object.</summary>
    [Serializable]
    public sealed class InfoLabelData
    {
        [SerializeField] string title = string.Empty;
        [SerializeField, TextArea(2, 6)] string body = string.Empty;
        [SerializeField, Tooltip("Show the label as soon as the object appears.")]
        bool showOnStart;

        public InfoLabelData()
        {
        }

        public InfoLabelData(string title, string body, bool showOnStart = false)
        {
            this.title = title ?? string.Empty;
            this.body = body ?? string.Empty;
            this.showOnStart = showOnStart;
        }

        public string Title => title ?? string.Empty;
        public string Body => body ?? string.Empty;
        public bool ShowOnStart => showOnStart;

        public bool HasContent => !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(body);
    }
}
