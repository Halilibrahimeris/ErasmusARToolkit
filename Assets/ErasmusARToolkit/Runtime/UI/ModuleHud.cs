using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// The HUD parts of project modules: a strip of buttons above the object bar, an info line under the
    /// status line and a subtitle line above the object bar. All three are optional in the UXML (layouts
    /// made for older toolkit versions have none). Requests made before the HUD is bound wait and are
    /// attached when it is.
    /// </summary>
    internal sealed class ModuleHud
    {
        public const string StripName = "module-buttons";
        public const string InfoLineName = "info-line";
        public const string SubtitleName = "subtitle";
        public const string ButtonClass = "module-button";

        readonly List<VisualElement> items = new List<VisualElement>();
        readonly List<KeyValuePair<object, string>> infoLines = new List<KeyValuePair<object, string>>();
        readonly List<KeyValuePair<object, string>> subtitles = new List<KeyValuePair<object, string>>();
        VisualElement strip;
        Label infoLine;
        Label subtitle;
        bool bound;
        bool warned;

        /// <summary>The layout has a strip for module buttons.</summary>
        public bool HasParts => strip != null;

        public bool IsBound => bound;

        /// <summary>Everything that was asked for (buttons and other elements), in order.</summary>
        public IReadOnlyList<VisualElement> Items => items;

        public string InfoText => infoLine != null ? infoLine.text : Join(infoLines);

        public string SubtitleText => subtitle != null ? subtitle.text : Last(subtitles);

        public void Bind(VisualElement root)
        {
            bound = true;
            strip = root?.Q<VisualElement>(StripName);
            infoLine = root?.Q<Label>(InfoLineName);
            subtitle = root?.Q<Label>(SubtitleName);
            if (strip != null)
            {
                foreach (var item in items)
                {
                    strip.Add(item);
                }
            }
            else if (items.Count > 0)
            {
                WarnOnce();
            }

            RefreshStrip();
            RefreshTexts();
        }

        public Button AddButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList(ButtonClass);
            Add(button);
            return button;
        }

        public void Add(VisualElement element)
        {
            if (element == null || items.Contains(element))
            {
                return;
            }

            items.Add(element);
            if (strip != null)
            {
                strip.Add(element);
            }
            else if (bound)
            {
                WarnOnce();
            }

            RefreshStrip();
        }

        public void Remove(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            items.Remove(element);
            element.RemoveFromHierarchy();
            RefreshStrip();
        }

        /// <summary>One line per owner; an empty text removes the owner's line.</summary>
        public void SetInfoLine(object owner, string text)
        {
            Set(infoLines, owner, text);
            RefreshTexts();
        }

        /// <summary>The latest subtitle is shown; an empty text removes the owner's subtitle.</summary>
        public void SetSubtitle(object owner, string text)
        {
            Set(subtitles, owner, text);
            RefreshTexts();
        }

        void RefreshStrip()
        {
            if (strip != null)
            {
                HudView.SetShown(strip, items.Count > 0);
            }
        }

        void RefreshTexts()
        {
            if (infoLine != null)
            {
                infoLine.text = Join(infoLines);
                HudView.SetShown(infoLine, infoLine.text.Length > 0);
            }

            if (subtitle != null)
            {
                subtitle.text = Last(subtitles);
                HudView.SetShown(subtitle, subtitle.text.Length > 0);
            }
        }

        void WarnOnce()
        {
            if (warned)
            {
                return;
            }

            warned = true;
            Debug.LogWarning("[Erasmus AR Toolkit] ARToolkitHUD.uxml has no module parts (module-buttons, info-line, " +
                             "subtitle), so project modules run without their HUD buttons. Re-import the toolkit package.");
        }

        static void Set(List<KeyValuePair<object, string>> lines, object owner, string text)
        {
            owner = owner ?? lines;
            lines.RemoveAll(pair => ReferenceEquals(pair.Key, owner));
            if (!string.IsNullOrWhiteSpace(text))
            {
                lines.Add(new KeyValuePair<object, string>(owner, text.Trim()));
            }
        }

        static string Join(List<KeyValuePair<object, string>> lines)
        {
            if (lines.Count == 0)
            {
                return string.Empty;
            }

            var texts = new string[lines.Count];
            for (var i = 0; i < lines.Count; i++)
            {
                texts[i] = lines[i].Value;
            }

            return string.Join("\n", texts);
        }

        static string Last(List<KeyValuePair<object, string>> lines)
        {
            return lines.Count > 0 ? lines[lines.Count - 1].Value : string.Empty;
        }
    }
}
